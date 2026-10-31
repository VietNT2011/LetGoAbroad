// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, it, expect, vi } from "vitest";
import { RecordingClipPlayer } from "../src/pages/RecordingClipPlayer";
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
let root: Root, container: HTMLDivElement, paused: boolean, nextFrame: number;
let frames: Map<number, FrameRequestCallback>;
const clip = { id: "segment", order: 0, startMs: 2000, endMs: 5000 };
beforeEach(() => {
  paused = true;
  nextFrame = 0;
  frames = new Map();
  vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => {
    paused = true;
  });
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
  vi.stubGlobal(
    "requestAnimationFrame",
    vi.fn((callback: FrameRequestCallback) => {
      frames.set(++nextFrame, callback);
      return nextFrame;
    }),
  );
  vi.stubGlobal(
    "cancelAnimationFrame",
    vi.fn((id: number) => frames.delete(id)),
  );
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});
async function mount() {
  await act(async () =>
    root.render(
      <RecordingClipPlayer assetId="authorized-sample" clip={clip} />,
    ),
  );
  const audio = container.querySelector("audio")!;
  Object.defineProperty(audio, "duration", { configurable: true, value: 8 });
  Object.defineProperty(audio, "paused", {
    configurable: true,
    get: () => paused,
  });
  await act(async () => audio.dispatchEvent(new Event("loadedmetadata")));
  return audio;
}
async function play(audio: HTMLAudioElement) {
  paused = false;
  await act(async () => audio.dispatchEvent(new Event("play")));
}
async function frame() {
  const entry = frames.entries().next().value!;
  frames.delete(entry[0]);
  await act(async () => entry[1](0));
}
it("seeks without autoplay, supports speed, bounds native seeking and restarts only after explicit play", async () => {
  const audio = await mount();
  expect(audio.currentTime).toBe(2);
  expect(audio.autoplay).toBe(false);
  expect(audio.play).not.toHaveBeenCalled();
  const speed = container.querySelector("select")!;
  speed.value = "1.25";
  await act(async () =>
    speed.dispatchEvent(new Event("change", { bubbles: true })),
  );
  expect(audio.playbackRate).toBe(1.25);
  audio.currentTime = 1;
  await act(async () => audio.dispatchEvent(new Event("seeking")));
  expect(audio.currentTime).toBe(2);
  expect(paused).toBe(true);
  audio.currentTime = 5;
  await play(audio);
  expect(audio.currentTime).toBe(2);
  audio.currentTime = 6;
  await act(async () => audio.dispatchEvent(new Event("timeupdate")));
  expect(audio.currentTime).toBe(5);
  expect(paused).toBe(true);
});
it("stops at the clip boundary on animation frames and cancels playback work when removed", async () => {
  const audio = await mount();
  await play(audio);
  audio.currentTime = 4.99;
  await frame();
  expect(paused).toBe(false);
  audio.currentTime = 5.03;
  await frame();
  expect(paused).toBe(true);
  expect(audio.currentTime).toBe(5);
  expect(frames.size).toBe(0);
  await play(audio);
  expect(audio.currentTime).toBe(2);
  expect(frames.size).toBe(1);
  await act(async () => root.render(<p>Source removed</p>));
  expect(paused).toBe(true);
  expect(frames.size).toBe(0);
});
it("retains position after an expired-grant retry without autoplay and refuses incompatible audio duration", async () => {
  const audio = await mount();
  audio.currentTime = 3.2;
  await act(async () => audio.dispatchEvent(new Event("timeupdate")));
  await act(async () => audio.dispatchEvent(new Event("error")));
  const oldSource = audio.getAttribute("src");
  await act(async () => container.querySelector("button")!.click());
  expect(audio.getAttribute("src")).not.toBe(oldSource);
  audio.currentTime = 0;
  await act(async () => audio.dispatchEvent(new Event("loadedmetadata")));
  expect(audio.currentTime).toBe(3.2);
  expect(audio.play).not.toHaveBeenCalled();
  Object.defineProperty(audio, "duration", { configurable: true, value: 4 });
  await act(async () => audio.dispatchEvent(new Event("loadedmetadata")));
  expect(container.textContent).toContain("không khớp thời lượng");
  await play(audio);
  expect(paused).toBe(true);
  expect(frames.size).toBe(0);
});

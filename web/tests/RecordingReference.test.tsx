// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { MemoryRouter, useNavigate } from "react-router-dom";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { api } from "../src/api";
import {
  RecordingReference,
  pauseOtherRecordingAudio,
} from "../src/pages/RecordingReference";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
const one = "11111111-1111-4111-8111-111111111111";
const two = "22222222-2222-4222-8222-222222222222";
const sample = (
  id = one,
  mediaAssetId: string | null = "authorized-audio",
) => ({
  id,
  mediaAssetId,
  title: "Original synthetic sample",
  version: 1,
  archived: false,
  attribution: "Original text / synthetic voice",
});
let root: Root;
let container: HTMLDivElement;
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  });
};
beforeEach(() => {
  vi.clearAllMocks();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});
async function render(path: string) {
  await act(async () =>
    root.render(
      <MemoryRouter initialEntries={[path]}>
        <RecordingReference />
      </MemoryRouter>,
    ),
  );
  await flush();
}
describe("recording sample comparison", () => {
  it("does not fetch, upload or create practice when no sample is chosen", async () => {
    await render("/recordings");
    expect(request).not.toHaveBeenCalled();
    expect(container.querySelector("audio")).toBeNull();
  });
  it("rejects malformed lesson links without interpreting a media URL or starting requests", async () => {
    await render("/recordings?lesson=https%3A%2F%2Fexample.com%2Fprivate.mp3");
    expect(request).not.toHaveBeenCalled();
    expect(container.textContent).toContain("chưa hợp lệ");
  });
  it("uses the authenticated lesson response for the sample player and return link without autoplay", async () => {
    request.mockResolvedValue(sample());
    await render(`/recordings?lesson=${one}&media=untrusted`);
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toBe(`/lessons/${one}`);
    expect(container.querySelector("audio")?.getAttribute("src")).toContain(
      "/media/authorized-audio/content",
    );
    expect(container.querySelector("audio")?.autoplay).toBe(false);
    expect(container.querySelector("a")?.getAttribute("href")).toBe(
      `/learn/${one}`,
    );
    expect(container.textContent).toContain("synthetic voice");
  });
  it("retains the server access error and never invents a fallback player", async () => {
    request.mockRejectedValue(new Error("Sample is unavailable"));
    await render(`/recordings?lesson=${one}`);
    expect(container.textContent).toContain("Sample is unavailable");
    expect(container.querySelector("audio")).toBeNull();
  });
  it("removes old audio immediately on source changes and aborts the obsolete request", async () => {
    let pendingResolve!: (value: unknown) => void;
    request.mockImplementation((path) =>
      path === `/lessons/${one}`
        ? Promise.resolve(sample())
        : new Promise((resolve) => {
            pendingResolve = resolve;
          }),
    );
    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button onClick={() => navigate(`/recordings?lesson=${two}`)}>
            Change source
          </button>
          <RecordingReference />
        </>
      );
    }
    await act(async () =>
      root.render(
        <MemoryRouter initialEntries={[`/recordings?lesson=${one}`]}>
          <Harness />
        </MemoryRouter>,
      ),
    );
    await flush();
    expect(container.querySelector("audio")).not.toBeNull();
    const signal = request.mock.calls[0][1]?.signal;
    await act(async () => container.querySelector("button")!.click());
    await flush();
    expect(signal?.aborted).toBe(true);
    expect(container.querySelector("audio")).toBeNull();
    await act(async () => pendingResolve(sample(two, null)));
    await flush();
    expect(container.textContent).toContain("chưa có audio mẫu");
    expect(container.querySelector("audio")).toBeNull();
  });
  it("pauses only other active audio in the comparison page without rewinding or touching outside players", () => {
    const active = document.createElement("audio");
    const other = document.createElement("audio");
    const idle = document.createElement("audio");
    const outside = document.createElement("audio");
    container.append(active, other, idle);
    document.body.append(outside);
    Object.defineProperty(other, "paused", { value: false });
    Object.defineProperty(outside, "paused", { value: false });
    other.currentTime = 1.5;
    const activePause = vi.spyOn(active, "pause").mockImplementation(() => {});
    const otherPause = vi.spyOn(other, "pause").mockImplementation(() => {});
    const idlePause = vi.spyOn(idle, "pause").mockImplementation(() => {});
    const outsidePause = vi
      .spyOn(outside, "pause")
      .mockImplementation(() => {});
    pauseOtherRecordingAudio(container, active);
    expect(otherPause).toHaveBeenCalledTimes(1);
    expect(other.currentTime).toBe(1.5);
    expect(activePause).not.toHaveBeenCalled();
    expect(idlePause).not.toHaveBeenCalled();
    expect(outsidePause).not.toHaveBeenCalled();
    outside.remove();
  });
});

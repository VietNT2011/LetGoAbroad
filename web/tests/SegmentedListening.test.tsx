// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { api, post } from "../src/api";
import {
  SegmentedListening,
  ListeningLauncher,
  ListeningPractice,
  type ListeningData,
} from "../src/pages/SegmentedListening";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
  post: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
const write = vi.mocked(post);
let root: Root;
let container: HTMLDivElement;
const reload = vi.fn();
const data = (): ListeningData => ({
  id: "session-1",
  contentVersionId: "lesson-1",
  mediaAssetId: "audio-1",
  durationMs: 10000,
  title: "Original listening",
  expiresAt: new Date(Date.now() + 86400000).toISOString(),
  serverNow: new Date().toISOString(),
  isCurrent: true,
  resume: { sessionId: "session-1", positionMs: 0, etag: '"first"' },
  answeredSegments: 0,
  firstUnaidedCount: 0,
  firstAssistedCount: 0,
  firstCorrectUnaided: 0,
  firstCorrectAssisted: 0,
  segments: [
    {
      id: "segment-1",
      order: 0,
      startMs: 0,
      endMs: 1000,
      textEn: null,
      textVi: null,
      isDisclosed: false,
      latest: null,
    },
    {
      id: "segment-2",
      order: 1,
      startMs: 2000,
      endMs: 3000,
      textEn: null,
      textVi: null,
      isDisclosed: false,
      latest: null,
    },
  ],
});
const answered = () => {
  const result = data();
  result.segments[0] = {
    ...result.segments[0],
    isDisclosed: true,
    textEn: "I am ready.",
    textVi: "Tôi đã sẵn sàng.",
    latest: {
      id: "answer-1",
      segmentId: "segment-1",
      number: 1,
      answerText: "I am ready.",
      isCorrect: true,
      isAssisted: false,
      expectedText: "I am ready.",
      tokens: [{ kind: "equal", expected: "I", actual: "I" }],
    },
  };
  result.answeredSegments = 1;
  result.firstUnaidedCount = 1;
  result.firstCorrectUnaided = 1;
  return result;
};
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 6; i++) await Promise.resolve();
  });
};
const render = async (node: React.ReactNode) => {
  await act(async () => root.render(node));
  await flush();
};
async function click(text: string) {
  const button = [...container.querySelectorAll("button")].find(
    (x) => x.textContent === text,
  )!;
  expect(button).toBeTruthy();
  await act(async () => button.click());
  await flush();
}
async function submit() {
  await act(async () =>
    container
      .querySelector("form")!
      .dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })),
  );
  await flush();
}
beforeEach(() => {
  request.mockReset();
  write.mockReset();
  reload.mockReset();
  request.mockImplementation(async (path, options) =>
    options?.method === "PUT"
      ? {
          sessionId: "session-1",
          positionMs: JSON.parse(options.body as string).positionMs,
          etag: '"second"',
        }
      : path.startsWith("/lessons/")
        ? { sessionId: null, resumeEtag: null, canStart: true, segmentCount: 2 }
        : data(),
  );
  write.mockResolvedValue(data());
  vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => {});
  vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue();
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.useRealTimers();
});
describe("segmented listening", () => {
  it("keeps a closed panel lazy and opening read-only until an explicit start", async () => {
    await render(<SegmentedListening versionId="lesson-1" />);
    expect(request).not.toHaveBeenCalled();
    const details = container.querySelector("details")!;
    await act(async () => {
      details.open = true;
      details.dispatchEvent(new Event("toggle"));
    });
    await flush();
    expect(request.mock.calls[0][0]).toBe("/lessons/lesson-1/listening");
    expect(write).not.toHaveBeenCalled();
  });
  it("retries an unknown session start with the same command before loading that session", async () => {
    write
      .mockRejectedValueOnce(new TypeError("Offline start"))
      .mockResolvedValueOnce(data());
    await render(<ListeningLauncher versionId="lesson-1" />);
    await click("Tạo phiên luyện mới");
    await click("Gửi lại yêu cầu tạo phiên");
    expect(write.mock.calls[1]).toEqual(write.mock.calls[0]);
    expect(
      container.querySelector('[aria-label="Luyện nghe theo đoạn"]'),
    ).toBeTruthy();
  });
  it("keeps transcript hidden until the server accepts a check and separates first-attempt counts", async () => {
    request.mockResolvedValue(answered());
    write.mockResolvedValue(answered().segments[0].latest);
    await render(<ListeningPractice initial={data()} reload={reload} />);
    expect(
      container.querySelector('[aria-label="Transcript đã mở"]'),
    ).toBeNull();
    const field = container.querySelector("textarea")!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(
        HTMLTextAreaElement.prototype,
        "value",
      )!.set!.call(field, " I  AM ready. ");
      field.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await submit();
    expect(write.mock.calls[0][1]).toMatchObject({
      answerText: " I  AM ready. ",
    });
    expect(
      container.querySelector('[aria-label="Transcript đã mở"]')?.textContent,
    ).toContain("Tôi đã sẵn sàng.");
    expect(container.textContent).toContain("1/1");
  });
  it("freezes an unknown answer for exact retry instead of creating a second event", async () => {
    write
      .mockRejectedValueOnce(new TypeError("Lost ACK"))
      .mockResolvedValueOnce(answered().segments[0].latest);
    request.mockResolvedValue(answered());
    await render(<ListeningPractice initial={data()} reload={reload} />);
    await submit();
    expect(container.querySelector("textarea")!.disabled).toBe(true);
    await click("Gửi lại lần kiểm tra");
    expect(write.mock.calls[1]).toEqual(write.mock.calls[0]);
    expect(container.textContent).toContain("Đúng theo đáp án biên soạn");
  });
  it("retries only the read when grading succeeded but fetching its saved result failed", async () => {
    write.mockResolvedValue(answered().segments[0].latest);
    request
      .mockRejectedValueOnce(new TypeError("Result read offline"))
      .mockResolvedValueOnce(answered());
    await render(<ListeningPractice initial={data()} reload={reload} />);
    await submit();
    expect(container.textContent).toContain("Lệnh đã được nhận");
    expect(write).toHaveBeenCalledTimes(1);
    await click("Thử lại");
    expect(write).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain("Đúng theo đáp án biên soạn");
  });
  it("reveals authored bilingual text without pretending that a segment was answered", async () => {
    const revealed = data();
    revealed.segments[0] = {
      ...revealed.segments[0],
      isDisclosed: true,
      textEn: "I am ready.",
      textVi: "Tôi đã sẵn sàng.",
    };
    request.mockResolvedValue(revealed);
    await render(<ListeningPractice initial={data()} reload={reload} />);
    await click("Mở transcript & bản dịch");
    expect(write.mock.calls[0][0]).toContain("/reveal");
    expect(container.textContent).toContain("Tôi đã sẵn sàng.");
    expect(container.textContent).toContain("0/2 đoạn đã kiểm tra");
  });
  it("restores verified resume time and sends a fenced saved position on pause", async () => {
    const initial = data();
    initial.resume.positionMs = 1000;
    await render(<ListeningPractice initial={initial} reload={reload} />);
    const audio = container.querySelector("audio")!;
    Object.defineProperty(audio, "duration", { value: 10, configurable: true });
    await act(async () => audio.dispatchEvent(new Event("loadedmetadata")));
    expect(audio.currentTime).toBe(1);
    audio.currentTime = 2.5;
    await act(async () => audio.dispatchEvent(new Event("pause")));
    await flush();
    expect(JSON.parse(request.mock.calls[0][1]!.body as string)).toMatchObject({
      positionMs: 2500,
      expectedEtag: '"first"',
    });
  });
  it("stops or loops at the selected segment boundary without autoplay on mount", async () => {
    await render(<ListeningPractice initial={data()} reload={reload} />);
    expect(HTMLMediaElement.prototype.play).not.toHaveBeenCalled();
    const audio = container.querySelector("audio")!;
    await click("Nghe đoạn 1");
    audio.currentTime = 1.1;
    await act(async () => audio.dispatchEvent(new Event("timeupdate")));
    expect(audio.currentTime).toBe(1);
    expect(HTMLMediaElement.prototype.pause).toHaveBeenCalledTimes(1);
    await act(async () =>
      container
        .querySelector<HTMLInputElement>('input[type="checkbox"]')!
        .click(),
    );
    await click("Nghe đoạn 1");
    audio.currentTime = 1.1;
    await act(async () => audio.dispatchEvent(new Event("timeupdate")));
    expect(audio.currentTime).toBe(0);
    expect(HTMLMediaElement.prototype.pause).toHaveBeenCalledTimes(1);
  });
  it("expires against the server clock while allowing an unknown accepted command to be retried", async () => {
    vi.useFakeTimers();
    const initial = data();
    initial.expiresAt = new Date(Date.now() + 1000).toISOString();
    write.mockRejectedValueOnce(new TypeError("Lost ACK"));
    await render(<ListeningPractice initial={initial} reload={reload} />);
    await submit();
    await act(async () => vi.advanceTimersByTimeAsync(1001));
    expect(container.textContent).toContain("Phiên này đã hết hạn");
    const retry = [...container.querySelectorAll("button")].find(
      (x) => x.textContent === "Gửi lại lần kiểm tra",
    )!;
    expect(retry.disabled).toBe(false);
  });
});

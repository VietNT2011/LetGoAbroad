// @vitest-environment jsdom
import { act } from "react";
import { MemoryRouter } from "react-router-dom";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { api } from "../src/api";
import { audioUpload } from "../src/audioUpload";
import { useAudioRecorder } from "../src/useAudioRecorder";
import RecordingsPage from "../src/pages/Recordings";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
vi.mock("../src/audioUpload", () => ({ audioUpload: vi.fn() }));
vi.mock("../src/useAudioRecorder", () => ({ useAudioRecorder: vi.fn() }));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
const upload = vi.mocked(audioUpload);
const record = vi.mocked(useAudioRecorder);
let root: Root;
let container: HTMLDivElement;
const start = vi.fn();
const stop = vi.fn();
const discard = vi.fn();
const send = vi.fn();
const revoked = vi.fn();
const rows = () => ({
  items: [
    {
      id: "ready-1",
      fileName: "My ready voice.wav",
      status: "ready",
      failureCode: null,
      durationMs: 1000,
      attribution: "My voice",
      createdAt: "2026-11-18T10:00:00Z",
    },
    {
      id: "pending-1",
      fileName: "My pending voice.wav",
      status: "processing",
      failureCode: null,
      durationMs: null,
      attribution: "My voice",
      createdAt: "2026-11-18T10:00:00Z",
    },
  ],
  nextOffset: null,
});
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  });
};
const render = async () => {
  await act(async () =>
    root.render(
      <MemoryRouter>
        <RecordingsPage />
      </MemoryRouter>,
    ),
  );
  await flush();
};
async function click(text: string) {
  const target = [...container.querySelectorAll("button")].find(
    (x) => x.textContent === text,
  )!;
  expect(target).toBeTruthy();
  await act(async () => target.click());
  await flush();
}
beforeEach(() => {
  request.mockReset();
  request.mockImplementation(async (path, options) =>
    options?.method === "DELETE" ? undefined : rows(),
  );
  upload.mockReset();
  record.mockReset();
  start.mockReset();
  stop.mockReset();
  discard.mockReset();
  send.mockReset().mockResolvedValue("new-1");
  upload.mockReturnValue({ send });
  record.mockReturnValue({
    phase: "idle",
    file: undefined,
    error: undefined,
    start,
    stop,
    discard,
  });
  revoked.mockReset();
  Object.defineProperty(URL, "createObjectURL", {
    value: vi.fn(() => "blob:local-draft"),
    configurable: true,
  });
  Object.defineProperty(URL, "revokeObjectURL", {
    value: revoked,
    configurable: true,
  });
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});
describe("private recording page", () => {
  it("only reads metadata on mount and requests the microphone only after explicit activation", async () => {
    await render();
    expect(start).not.toHaveBeenCalled();
    expect(upload).not.toHaveBeenCalled();
    expect(request.mock.calls[0][0]).toBe("/me/recordings?offset=0");
    await click("Bắt đầu ghi âm");
    expect(start).toHaveBeenCalledTimes(1);
  });
  it("plays only ready remote assets while processing remains a status", async () => {
    await render();
    const players = [...container.querySelectorAll("audio")];
    expect(players).toHaveLength(1);
    expect(players[0].getAttribute("src")).toContain("/media/ready-1/content");
    expect(container.textContent).toContain("processing");
    expect(upload).not.toHaveBeenCalled();
  });
  it("keeps a captured draft local until source and explicit save, then reports queued acceptance", async () => {
    const draft = new File(["private voice"], "draft.webm", {
      type: "audio/webm",
    });
    record.mockReturnValue({
      phase: "ready",
      file: draft,
      error: undefined,
      start,
      stop,
      discard,
    });
    await render();
    expect(
      container
        .querySelector('audio[aria-label="Nghe bản nháp"]')
        ?.getAttribute("src"),
    ).toBe("blob:local-draft");
    expect(upload).not.toHaveBeenCalled();
    const field = container.querySelector(
      'input[maxlength="2000"]',
    ) as HTMLInputElement;
    await act(async () => {
      Object.getOwnPropertyDescriptor(
        HTMLInputElement.prototype,
        "value",
      )!.set!.call(field, "My own voice");
      field.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await flush();
    await click("Lưu riêng bản thu");
    expect(upload.mock.calls[0].slice(0, 3)).toEqual([
      draft,
      "recording",
      "My own voice",
    ]);
    expect(send).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain("Kho lưu trữ đang xử lý");
    expect(discard).toHaveBeenCalled();
  });
  it("requires explicit deletion confirmation and accepts the queued empty acknowledgment", async () => {
    await render();
    await click("Xóa bản thu");
    expect(request.mock.calls.some((x) => x[1]?.method === "DELETE")).toBe(
      false,
    );
    await click("Xác nhận xóa bản thu");
    expect(
      request.mock.calls.some(
        (x) => x[0] === "/media/ready-1" && x[1]?.method === "DELETE",
      ),
    ).toBe(true);
    expect(container.textContent).toContain("Quyền phát mới bị chặn ngay");
  });
  it("revokes a local preview on unmount without uploading it", async () => {
    record.mockReturnValue({
      phase: "ready",
      file: new File(["draft"], "draft.webm", { type: "audio/webm" }),
      error: undefined,
      start,
      stop,
      discard,
    });
    await render();
    await act(async () => root.render(null));
    expect(revoked).toHaveBeenCalledWith("blob:local-draft");
    expect(upload).not.toHaveBeenCalled();
  });
});

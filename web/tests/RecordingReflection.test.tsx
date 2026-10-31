// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { api, ApiError } from "../src/api";
import {
  RecordingReflectionEditor,
  RecordingJournal,
  ReflectionForm,
  type ReflectionView,
} from "../src/pages/RecordingReflection";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
let root: Root;
let container: HTMLDivElement;
const view = (patch: Partial<ReflectionView> = {}): ReflectionView => ({
  assetId: "own-recording",
  fileName: "My original recording.wav",
  status: "ready",
  etag: null,
  notes: "",
  clarity: null,
  rhythm: null,
  updatedAt: null,
  ...patch,
});
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 6; i++) await Promise.resolve();
  });
};
const button = (label: string) =>
  [...container.querySelectorAll("button")].find(
    (x) => x.textContent === label,
  )!;
const writeCalls = () =>
  request.mock.calls.filter(([, options]) => options?.method === "PUT");
async function click(label: string) {
  await act(async () => button(label).click());
  await flush();
}
async function type(value: string) {
  await act(async () => {
    const input = container.querySelector("textarea")!;
    Object.getOwnPropertyDescriptor(
      HTMLTextAreaElement.prototype,
      "value",
    )!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
async function renderForm() {
  await act(async () =>
    root.render(<ReflectionForm assetId="own-recording" />),
  );
  await flush();
}
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
describe("private recording reflection", () => {
  it("opens lazily without creating feedback or touching storage", async () => {
    request.mockResolvedValue(view());
    await act(async () =>
      root.render(<RecordingReflectionEditor assetId="own-recording" />),
    );
    expect(request).not.toHaveBeenCalled();
    await act(async () => {
      const details = container.querySelector("details")!;
      details.open = true;
      details.dispatchEvent(new Event("toggle"));
    });
    await flush();
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toBe("/media/own-recording/reflection");
    expect(writeCalls()).toHaveLength(0);
  });
  it("saves explicit unrated text with the initial ETag and uses the returned acknowledgment", async () => {
    request.mockImplementation(async (_path, options) =>
      options?.method === "PUT"
        ? view({ etag: '"new"', notes: "  My private notes  " })
        : view(),
    );
    await renderForm();
    await type("  My private notes  ");
    await click("Lưu tự đánh giá riêng");
    const payload = JSON.parse(writeCalls()[0][1]!.body as string);
    expect(payload.expectedEtag).toBeNull();
    expect(payload.notes).toBe("  My private notes  ");
    expect(payload.clarity).toBeNull();
    expect(payload.rhythm).toBeNull();
    expect(payload.clientRequestId).toBeTruthy();
    expect(container.textContent).toContain("Đã lưu ghi chú riêng");
    expect(request).toHaveBeenCalledTimes(2);
  });
  it("freezes and retries exactly the same command after an unknown write outcome", async () => {
    let attempts = 0;
    request.mockImplementation(async (_path, options) => {
      if (options?.method !== "PUT")
        return view({ etag: '"base"', notes: "Original" });
      if (++attempts === 1) throw new Error("Network lost");
      return view({ etag: '"saved"', notes: "My draft" });
    });
    await renderForm();
    await type("My draft");
    await click("Lưu tự đánh giá riêng");
    expect(container.querySelector("textarea")?.matches(":disabled")).toBe(
      true,
    );
    expect(container.querySelector("textarea")?.value).toBe("My draft");
    await click("Gửi lại lệnh chưa xác nhận");
    expect(writeCalls()[0][1]?.body).toBe(writeCalls()[1][1]?.body);
    expect(request).toHaveBeenCalledTimes(3);
    expect(container.querySelector("textarea")?.matches(":disabled")).toBe(
      false,
    );
  });
  it("keeps a stale draft and updates its baseline only after explicit comparison acceptance", async () => {
    let reads = 0;
    let writes = 0;
    request.mockImplementation(async (_path, options) => {
      if (options?.method === "PUT") {
        if (++writes === 1)
          throw new ApiError(412, "stale_version", "Newer note exists");
        return view({ etag: '"final"', notes: "Keep my text" });
      }
      return ++reads === 1
        ? view({ etag: '"old"', notes: "Original" })
        : view({ etag: '"server"', notes: "Peer text", clarity: 5 });
    });
    await renderForm();
    await type("Keep my text");
    await click("Lưu tự đánh giá riêng");
    expect(button("Lưu tự đánh giá riêng").disabled).toBe(true);
    await click("Đọc bản mới để so sánh");
    expect(container.textContent).toContain("Peer text");
    expect(container.querySelector("textarea")?.value).toBe("Keep my text");
    expect(writeCalls()).toHaveLength(1);
    await click("Giữ nháp của tôi và dùng phiên bản mới để lưu");
    expect(container.querySelector("textarea")?.value).toBe("Keep my text");
    await click("Lưu tự đánh giá riêng");
    const payload = JSON.parse(writeCalls()[1][1]!.body as string);
    expect(payload.expectedEtag).toBe('"server"');
    expect(payload.notes).toBe("Keep my text");
    expect(payload.clientRequestId).not.toBe(
      JSON.parse(writeCalls()[0][1]!.body as string).clientRequestId,
    );
  });
  it("rejects an unready first reflection but keeps existing deleted-recording notes editable", async () => {
    request.mockResolvedValue(view({ status: "processing" }));
    await renderForm();
    expect(container.querySelector("textarea")).toBeNull();
    expect(container.textContent).toContain("Chờ audio Ready");
    await act(async () => root.render(null));
    request.mockResolvedValue(view({ status: "deleted" }));
    await renderForm();
    expect(container.textContent).toContain("không còn khả dụng");
    expect(container.querySelector("textarea")).toBeNull();
    await act(async () => root.render(null));
    request.mockResolvedValue(
      view({ status: "deleted", etag: '"kept"', notes: "Retained note" }),
    );
    await renderForm();
    expect(container.querySelector("textarea")?.value).toBe("Retained note");
    expect(container.querySelector("textarea")?.matches(":disabled")).toBe(
      false,
    );
    expect(container.textContent).toContain("File audio đã xóa");
    expect(container.querySelector("audio")).toBeNull();
  });
  it("shows a private retained journal only on opening and does not grant playback for deleted files", async () => {
    request.mockResolvedValue({
      items: [
        view({ status: "deleted", etag: '"kept"', notes: "Only my old notes" }),
      ],
      nextOffset: null,
    });
    await act(async () => root.render(<RecordingJournal />));
    expect(request).not.toHaveBeenCalled();
    await act(async () => {
      const details = container.querySelector("details")!;
      details.open = true;
      details.dispatchEvent(new Event("toggle"));
    });
    await flush();
    expect(request.mock.calls[0][0]).toBe("/me/recording-journal?offset=0");
    expect(container.textContent).toContain("Only my old notes");
    expect(container.textContent).toContain("Audio đã xóa");
    expect(container.querySelector("audio")).toBeNull();
    expect(writeCalls()).toHaveLength(0);
  });
});

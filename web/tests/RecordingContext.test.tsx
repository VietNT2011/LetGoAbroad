// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { api } from "../src/api";
import {
  ReflectionForm,
  type ReflectionView,
} from "../src/pages/RecordingReflection";
import {
  RecordingHistory,
  RecordingSourceSummary,
  type RecordingSourceView,
} from "../src/pages/RecordingHistory";
import { recordingSourceSuggestion } from "../src/recordingSource";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
const one = "11111111-1111-4111-8111-111111111111";
const two = "22222222-2222-4222-8222-222222222222";
const segment = "33333333-3333-4333-8333-333333333333";
const source = (
  patch: Partial<RecordingSourceView> = {},
): RecordingSourceView => ({
  versionId: one,
  title: "Frozen original sample",
  version: 1,
  mediaAssetId: "sample-audio",
  audioAvailable: true,
  archived: false,
  segmentId: segment,
  segmentOrder: 0,
  startMs: 1000,
  endMs: 3000,
  ...patch,
});
const metadata = (patch: Partial<ReflectionView> = {}): ReflectionView => ({
  assetId: "my-recording",
  fileName: "My voice.wav",
  status: "ready",
  etag: '"base"',
  notes: "Stored notes",
  clarity: null,
  rhythm: null,
  updatedAt: "2026-11-20T03:00:00Z",
  revision: 1,
  ...patch,
});
let root: Root;
let container: HTMLDivElement;
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 6; i++) await Promise.resolve();
  });
};
async function render(node: React.ReactNode) {
  await act(async () => root.render(<MemoryRouter>{node}</MemoryRouter>));
  await flush();
}
async function click(label: string) {
  const button = [...container.querySelectorAll("button")].find(
    (x) => x.textContent === label,
  )!;
  expect(button).toBeTruthy();
  await act(async () => button.click());
  await flush();
}
async function type(value: string) {
  const input = container.querySelector("textarea")!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(
      HTMLTextAreaElement.prototype,
      "value",
    )!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
const writes = () =>
  request.mock.calls.filter(([, options]) => options?.method === "PUT");
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
describe("pinned recording samples and history", () => {
  it("binds a version/segment explicitly without saving or replacing the unsaved note draft", async () => {
    request.mockImplementation(async (path, options) =>
      options?.method !== "PUT"
        ? metadata()
        : path.endsWith("/source")
          ? metadata({ etag: '"bound"', source: source(), revision: 2 })
          : metadata({
              etag: '"saved"',
              source: source(),
              revision: 3,
              notes: "Unsaved private draft",
            }),
    );
    await render(
      <ReflectionForm
        assetId="my-recording"
        suggestedSource={{ versionId: one, segmentId: segment }}
      />,
    );
    expect(writes()).toHaveLength(0);
    await type("Unsaved private draft");
    await click("Liên kết bài mẫu đang mở");
    expect(container.querySelector("textarea")?.value).toBe(
      "Unsaved private draft",
    );
    expect(writes()[0][0]).toBe("/media/my-recording/reflection/source");
    const binding = JSON.parse(writes()[0][1]!.body as string);
    expect(binding.sourceContentVersionId).toBe(one);
    expect(binding.sourceSegmentId).toBe(segment);
    expect(binding).not.toHaveProperty("notes");
    expect(container.textContent).toContain("Frozen original sample");
    expect(container.textContent).toContain("Đoạn 1");
    await click("Lưu tự đánh giá riêng");
    const note = JSON.parse(writes()[1][1]!.body as string);
    expect(note.expectedEtag).toBe('"bound"');
    expect(note.notes).toBe("Unsaved private draft");
  });

  it("retries an unknown source command on its original route even when the suggested URL changes", async () => {
    let attempts = 0;
    request.mockImplementation(async (_path, options) => {
      if (options?.method !== "PUT") return metadata();
      if (++attempts === 1) throw new Error("Lost reply");
      return metadata({ source: source(), etag: '"bound"', revision: 2 });
    });
    await render(
      <ReflectionForm
        assetId="my-recording"
        suggestedSource={{ versionId: one, segmentId: segment }}
      />,
    );
    await type("Local draft");
    await click("Liên kết bài mẫu đang mở");
    await render(
      <ReflectionForm
        assetId="my-recording"
        suggestedSource={{ versionId: two, segmentId: null }}
      />,
    );
    expect(container.querySelector("textarea")?.matches(":disabled")).toBe(
      true,
    );
    await click("Gửi lại lệnh chưa xác nhận");
    expect(writes()[1][0]).toBe(writes()[0][0]);
    expect(writes()[1][1]?.body).toBe(writes()[0][1]?.body);
    expect(container.querySelector("textarea")?.value).toBe("Local draft");
  });

  it("clears the current link explicitly while retaining an unsaved note on a deleted recording", async () => {
    request.mockImplementation(async (_path, options) =>
      options?.method === "PUT"
        ? metadata({
            status: "deleted",
            etag: '"cleared"',
            revision: 3,
            source: null,
          })
        : metadata({
            status: "deleted",
            source: source({ audioAvailable: false }),
          }),
    );
    await render(<ReflectionForm assetId="my-recording" />);
    await type("Keep this draft");
    expect(container.textContent).toContain("không còn khả dụng");
    await click("Bỏ liên kết mẫu hiện tại");
    const body = JSON.parse(writes()[0][1]!.body as string);
    expect(body.sourceContentVersionId).toBeNull();
    expect(body.sourceSegmentId).toBeNull();
    expect(container.querySelector("textarea")?.value).toBe("Keep this draft");
    expect(container.querySelector("audio")).toBeNull();
  });

  it("loads immutable history lazily, labels legacy captures and follows the server cursor without writes or players", async () => {
    const item = {
      number: 1,
      kind: "edit",
      notes: "First actual edit",
      clarity: null,
      rhythm: null,
      updatedAt: "2026-11-20T03:00:00Z",
      capturedAt: "2026-11-20T03:00:00Z",
      source: source({ archived: true }),
    };
    request.mockImplementation(async (path) =>
      path.includes("before=1")
        ? {
            items: [
              {
                ...item,
                number: 0,
                kind: "legacy_snapshot",
                notes: "Old actual value",
                source: null,
              },
            ],
            nextBefore: null,
          }
        : { items: [item], nextBefore: 1 },
    );
    await render(<RecordingHistory assetId="my-recording" revision={1} />);
    expect(request).not.toHaveBeenCalled();
    const details = container.querySelector("details")!;
    await act(async () => {
      details.open = true;
      details.dispatchEvent(new Event("toggle"));
    });
    await flush();
    expect(container.textContent).toContain("First actual edit");
    expect(
      container.querySelector('a[href^="/learn/"]')?.getAttribute("href"),
    ).toBe(`/learn/${one}`);
    expect(container.querySelector("audio")).toBeNull();
    await click("Xem các bản cũ hơn");
    expect(request.mock.calls.at(-1)![0]).toBe(
      "/media/my-recording/reflection/history?before=1",
    );
    expect(container.textContent).toContain(
      "Nhận xét có sẵn trước khi theo dõi lịch sử",
    );
    expect(container.textContent).toContain("Old actual value");
    expect(writes()).toHaveLength(0);
  });

  it("refreshes an opened history at a new revision and never invents an unavailable source player", async () => {
    request.mockResolvedValue({ items: [], nextBefore: null });
    await render(<RecordingHistory assetId="my-recording" revision={1} />);
    const details = container.querySelector("details")!;
    await act(async () => {
      details.open = true;
      details.dispatchEvent(new Event("toggle"));
    });
    await flush();
    const firstReads = request.mock.calls.length;
    await render(<RecordingHistory assetId="my-recording" revision={2} />);
    expect(request.mock.calls.length).toBe(firstReads + 1);
    await render(
      <RecordingSourceSummary source={source({ audioAvailable: false })} />,
    );
    expect(container.textContent).toContain("không còn khả dụng");
    expect(container.querySelector("audio")).toBeNull();
    expect(
      [...container.querySelectorAll("a")].some((a) =>
        a.getAttribute("href")?.startsWith("/recordings"),
      ),
    ).toBe(false);
  });

  it("validates suggested version/segment IDs without accepting media URLs or dropping an invalid segment", () => {
    expect(recordingSourceSuggestion(one.toUpperCase(), segment)).toEqual({
      versionId: one,
      segmentId: segment,
    });
    expect(recordingSourceSuggestion(one, "bad")).toBeUndefined();
    expect(
      recordingSourceSuggestion("https://example.com/audio", null),
    ).toBeUndefined();
    expect(recordingSourceSuggestion(null, segment)).toBeUndefined();
  });
});

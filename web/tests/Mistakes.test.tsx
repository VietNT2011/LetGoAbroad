// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, post } from "../src/api";
import MistakesPage, { MistakeList } from "../src/pages/Mistakes";

vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
  post: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const read = vi.mocked(api);
const write = vi.mocked(post);
const item = (id = "mistake-1") => ({
  id,
  questionId: "question-1",
  prompt: "Original passage question",
  part: 7,
  skill: "reading",
  sourceAttemptId: "original-1",
  wrongCount: 3,
  lastWrongAt: "2026-11-18T08:00:00Z",
  status: "learning",
  nextReviewAt: "2026-11-18T08:00:00Z",
  correctReviewDays: 1,
  cycleTimeZoneId: "UTC",
  canReview: true,
  activeAttemptId: null as string | null,
  groupQuestionCount: 3,
});
let root: Root;
let container: HTMLDivElement;
const flush = async () => {
  await act(async () => {
    for (let n = 0; n < 6; n++) await Promise.resolve();
  });
};
const render = async (node: React.ReactNode) => {
  await act(async () =>
    root.render(
      <MemoryRouter>
        <Routes>
          <Route path="/" element={node} />
          <Route path="/attempts/:id" element={<p>Opened recall attempt</p>} />
        </Routes>
      </MemoryRouter>,
    ),
  );
  await flush();
};
async function click(text: string) {
  const button = [...container.querySelectorAll("button")].find(
    (x) => x.textContent?.replace(/\s+/g, " ").trim() === text,
  )!;
  expect(button).toBeTruthy();
  await act(async () => button.click());
  await flush();
}

describe("mistake notebook", () => {
  beforeEach(() => {
    read.mockReset();
    write.mockReset();
    read.mockResolvedValue({ items: [item()], nextOffset: null });
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });
  it("opens as a read-only page and explains contextual recall and historical import", async () => {
    await render(<MistakesPage />);
    expect(write).not.toHaveBeenCalled();
    expect(container.textContent).toContain("chỉ câu bạn chọn cập nhật");
    expect(container.textContent).toContain("3 lần chưa đúng");
    expect(container.textContent).toContain("Ôn trọn nhóm · 3 câu");
    expect(read.mock.calls[0][0]).toContain("status=learning");
  });
  it("resumes existing attempts and respects the server due flag without inventing a new attempt", async () => {
    read.mockResolvedValue({
      items: [
        { ...item(), canReview: false },
        { ...item("active"), activeAttemptId: "recall-1" },
      ],
      nextOffset: null,
    });
    await render(<MistakeList path="/me/mistakes" offset={0} page={vi.fn()} />);
    const start = [...container.querySelectorAll("button")].find((x) =>
      x.textContent?.includes("Ôn trọn nhóm"),
    )!;
    expect(start.disabled).toBe(true);
    expect(
      container.querySelector('a[href="/attempts/recall-1"]')?.textContent,
    ).toBe("Tiếp tục phiên ôn");
    expect(
      container.querySelector('a[href="/attempts/original-1/result"]'),
    ).toBeTruthy();
    expect(write).not.toHaveBeenCalled();
  });
  it("retries an unknown start with the same command then navigates to its accepted attempt", async () => {
    write
      .mockRejectedValueOnce(new TypeError("Offline"))
      .mockResolvedValueOnce({ id: "recall-1" });
    await render(<MistakeList path="/me/mistakes" offset={0} page={vi.fn()} />);
    await click("Ôn trọn nhóm · 3 câu");
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      "Offline",
    );
    await click("Ôn trọn nhóm · 3 câu");
    expect(write.mock.calls[1]).toEqual(write.mock.calls[0]);
    expect(container.textContent).toContain("Opened recall attempt");
  });
  it("imports history explicitly with immutable retry and describes remaining bounded batches", async () => {
    write
      .mockRejectedValueOnce(new TypeError("Offline import"))
      .mockResolvedValueOnce({ attemptsProcessed: 50, hasMore: true });
    await render(<MistakesPage />);
    await click("Nạp câu sai từ lịch sử");
    await click("Gửi lại yêu cầu nạp");
    expect(write.mock.calls[1]).toEqual(write.mock.calls[0]);
    expect(write.mock.calls[0][0]).toBe("/me/mistakes/import-history");
    expect(container.textContent).toContain("Còn lịch sử; bấm nạp tiếp");
    expect(container.textContent).toContain("Đã đối chiếu 50 bài");
  });
  it("resets pagination when filters change and requests the selected status", async () => {
    read.mockResolvedValue({ items: [item()], nextOffset: 30 });
    await render(<MistakesPage />);
    await click("Trang sau");
    expect(read.mock.calls.at(-1)?.[0]).toContain("offset=30");
    const select = container.querySelector("select")!;
    await act(async () => {
      select.value = "mastered";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await flush();
    expect(read.mock.calls.at(-1)?.[0]).toContain("status=mastered");
    expect(read.mock.calls.at(-1)?.[0]).toContain("offset=0");
  });
  it("separates a network failure from an empty successful notebook and supports retry", async () => {
    read
      .mockRejectedValueOnce(new Error("Notebook unavailable"))
      .mockResolvedValueOnce({ items: [], nextOffset: null });
    await render(<MistakeList path="/me/mistakes" offset={0} page={vi.fn()} />);
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      "Notebook unavailable",
    );
    expect(container.querySelector(".card-empty")).toBeNull();
    await click("Thử lại");
    expect(container.querySelector(".card-empty")?.textContent).toContain(
      "Chưa có câu phù hợp",
    );
  });
});

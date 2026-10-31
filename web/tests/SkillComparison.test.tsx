// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../src/api";
import { SkillComparison } from "../src/pages/SkillComparison";

vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
const sample = (
  observedCount = 50,
  correctCount = 40,
  accuracyPercent: number | null = 80,
) => ({
  observedCount,
  correctCount,
  accuracyPercent,
  blankCount: 5,
  parts: [{ part: 5, observedCount, correctCount }],
});
const payload = () => ({
  policyVersion: 1,
  windowSize: 50,
  minimumObservations: 5,
  groups: [
    {
      track: "practice",
      skill: "grammar",
      recent: {
        ...sample(),
        parts: [
          { part: 5, observedCount: 40, correctCount: 30 },
          { part: 6, observedCount: 10, correctCount: 10 },
        ],
      },
      previous: sample(50, 30, 60),
      differencePercentagePoints: 20 as number | null,
    },
    {
      track: "full_mock_unseen",
      skill: "reading",
      recent: sample(50, 25, 50),
      previous: sample(5, 1, 20),
      differencePercentagePoints: 30 as number | null,
    },
  ],
});
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}
let root: Root;
let container: HTMLDivElement;
const flush = async () => {
  await act(async () => {
    for (let index = 0; index < 6; index++) await Promise.resolve();
  });
};
async function open(value = true) {
  await act(async () => {
    const details = container.querySelector("details")!;
    details.open = value;
    details.dispatchEvent(new Event("toggle"));
  });
  await flush();
}
async function click(text: string) {
  const button = [...container.querySelectorAll("button")].find(
    (item) => item.textContent === text,
  )!;
  expect(button).toBeTruthy();
  await act(async () => button.click());
  await flush();
}

describe("skill sample comparison", () => {
  beforeEach(async () => {
    request.mockReset();
    request.mockResolvedValue(payload());
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () => root.render(<SkillComparison />));
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it("loads one snapshot only when expanded and keeps the two test conditions separate", async () => {
    expect(request).not.toHaveBeenCalled();
    await open();
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toBe("/learning/skill-comparison");
    const practice = container.querySelector(
      '[aria-label="So sánh bài luyện"]',
    )!;
    const full = container.querySelector(
      '[aria-label="So sánh full mock chưa lộ đáp án"]',
    )!;
    expect(practice.textContent).toContain("40/50 câu đúng");
    expect(practice.textContent).toContain("+20 điểm phần trăm");
    expect(practice.textContent).not.toContain("25/50 câu đúng");
    expect(full.textContent).toContain("25/50 câu đúng");
    expect(full.textContent).toContain("1/5 câu đúng");
    expect(container.textContent).toContain("không phải hai khoảng ngày");
  });

  it("provides captions, scoped headers and the union of Parts including a missing previous sample", async () => {
    await open();
    const table = container.querySelector("table")!;
    expect(table.caption?.textContent).toContain("Ngữ pháp");
    expect(table.querySelectorAll('thead th[scope="col"]')).toHaveLength(3);
    const part = [...table.querySelectorAll("tbody tr")].find(
      (row) => row.querySelector("th")?.textContent === "Part 6",
    )!;
    expect(part.querySelector("th")?.getAttribute("scope")).toBe("row");
    expect(
      [...part.querySelectorAll("td")].map((cell) => cell.textContent),
    ).toEqual(["10/10", "Chưa có câu"]);
  });

  it("distinguishes insufficient previous evidence from a valid zero-percent difference", async () => {
    const sparse = payload();
    sparse.groups = [
      {
        ...sparse.groups[0],
        recent: sample(50, 0, 0),
        previous: sample(4, 0, null),
        differencePercentagePoints: null,
      },
    ];
    request.mockResolvedValueOnce(sparse);
    await open();
    expect(container.textContent).toContain("0%");
    expect(container.textContent).toContain("Chưa đủ mẫu để so sánh.");
    expect(container.textContent).not.toContain("Chênh lệch: 0 điểm");
    request.mockResolvedValueOnce({
      ...sparse,
      groups: [
        {
          ...sparse.groups[0],
          previous: sample(5, 0, 0),
          differencePercentagePoints: 0,
        },
      ],
    });
    await click("Cập nhật so sánh");
    expect(container.textContent).toContain("Chênh lệch: 0 điểm phần trăm.");
  });

  it("shows a recoverable network error instead of an empty successful comparison", async () => {
    request.mockRejectedValueOnce(new Error("Offline comparison"));
    await open();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      "Offline comparison",
    );
    expect(container.querySelector("article")).toBeNull();
    await click("Thử lại");
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(container.textContent).toContain("40/50 câu đúng");
    expect(request).toHaveBeenCalledTimes(2);
  });

  it("aborts a discarded request and cannot restore old data after closing and reopening", async () => {
    const old = deferred<unknown>();
    request.mockImplementationOnce(() => old.promise as never);
    await open();
    const signal = request.mock.calls[0][1]?.signal;
    await open(false);
    expect(signal?.aborted).toBe(true);
    const fresh = payload();
    fresh.groups = [{ ...fresh.groups[0], skill: "Fresh sample" }];
    request.mockResolvedValueOnce(fresh);
    await open();
    const stale = payload();
    stale.groups = [{ ...stale.groups[0], skill: "Old private sample" }];
    await act(async () => old.resolve(stale));
    await flush();
    expect(container.textContent).toContain("Fresh sample");
    expect(container.textContent).not.toContain("Old private sample");
  });

  it("shows explicit empty states for both tracks without inventing percentages", async () => {
    request.mockResolvedValueOnce({ ...payload(), groups: [] });
    await open();
    expect(container.querySelectorAll(".activity-empty")).toHaveLength(2);
    expect(container.querySelector("article")).toBeNull();
    expect(container.querySelector("table")).toBeNull();
  });
});

// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import {
  TranscriptEditor,
  TranscriptFields,
} from "../src/pages/TranscriptEditor";
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
let root: Root;
let container: HTMLDivElement;
const base = (segments?: unknown[]) => ({
  title: "Original lesson",
  mediaAssetId: "original-audio",
  body: "Keep this original body",
  groups: [{ part: 4, privateEditorialValue: "retain" }],
  extra: { keep: true },
  ...(segments ? { segments } : {}),
});
const segment = (startMs = 0, endMs = 2000) => ({
  startMs,
  endMs,
  textEn: "I am ready.",
  textVi: "Tôi đã sẵn sàng.",
  normalizationPolicyVersion: 1,
  ignoreTerminalPunctuation: true,
  privateEditorialValue: "keep",
});
const changed = vi.fn();
function Harness({
  initial,
  disabled = false,
}: {
  initial: string;
  disabled?: boolean;
}) {
  const [json, setJson] = useState(initial);
  return (
    <>
      <TranscriptFields
        json={json}
        disabled={disabled}
        onChange={(value) => {
          changed(value);
          setJson(value);
        }}
      />
      <pre data-json>{json}</pre>
    </>
  );
}
const draft = () =>
  JSON.parse(container.querySelector("[data-json]")!.textContent!);
const button = (text: string) =>
  [...container.querySelectorAll("button")].find(
    (b) => b.textContent === text,
  )!;
const control = (text: string) =>
  [...container.querySelectorAll("label")]
    .find((l) => l.textContent!.startsWith(text))!
    .querySelector("input,textarea") as HTMLInputElement | HTMLTextAreaElement;
async function enter(text: string, value: string) {
  const input = control(text);
  const proto =
    input instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
  await act(async () => {
    Object.getOwnPropertyDescriptor(proto, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
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
describe("controlled transcript authoring", () => {
  it("opens lazily without changing even an invalid JSON draft", async () => {
    await act(async () =>
      root.render(<TranscriptEditor json="{unfinished" onChange={changed} />),
    );
    expect(container.textContent).not.toContain("JSON hoặc cấu trúc");
    await act(async () => {
      const details = container.querySelector("details")!;
      details.open = true;
      details.dispatchEvent(new Event("toggle"));
    });
    expect(container.textContent).toContain("JSON hoặc cấu trúc");
    expect(changed).not.toHaveBeenCalled();
  });
  it("adds bilingual fields while preserving media, quiz and unknown document data", async () => {
    const original = base();
    await act(async () =>
      root.render(<Harness initial={JSON.stringify(original)} />),
    );
    await act(async () => button("Thêm câu transcript").click());
    await enter("Tiếng Anh câu 1", "Please bring a notebook.");
    await enter("Tiếng Việt câu 1", "Vui lòng mang theo sổ.");
    const value = draft();
    expect(value.body).toBe(original.body);
    expect(value.groups).toEqual(original.groups);
    expect(value.extra).toEqual(original.extra);
    expect(value.mediaAssetId).toBe(original.mediaAssetId);
    expect(value.segments).toEqual([
      {
        startMs: 0,
        endMs: 3000,
        textEn: "Please bring a notebook.",
        textVi: "Vui lòng mang theo sổ.",
        normalizationPolicyVersion: 1,
        ignoreTerminalPunctuation: true,
      },
    ]);
  });
  it("keeps raw variants and metadata and offers an explicit canonical-answer reset", async () => {
    await act(async () =>
      root.render(<Harness initial={JSON.stringify(base([segment()]))} />),
    );
    await enter("Biến thể được chấp nhận câu 1", "I am ready.\nI'm ready.\n");
    expect(draft().segments[0].acceptedAnswers).toEqual([
      "I am ready.",
      "I'm ready.",
      "",
    ]);
    expect(container.textContent).toContain("biến thể không rỗng");
    expect(draft().segments[0].privateEditorialValue).toBe("keep");
    await act(async () =>
      button("Dùng câu tiếng Anh làm đáp án câu 1").click(),
    );
    expect(draft().segments[0]).not.toHaveProperty("acceptedAnswers");
    expect(draft().segments[0].textEn).toBe("I am ready.");
  });
  it("reports overlapping and incomplete segments without silently fixing or sending them", async () => {
    const data = base([
      segment(0, 2000),
      { ...segment(1000, 1500), textVi: "" },
    ]);
    await act(async () =>
      root.render(<Harness initial={JSON.stringify(data)} />),
    );
    expect(container.textContent).toContain("thời gian chồng");
    expect(container.textContent).toContain("cả nội dung tiếng Anh");
    expect(changed).not.toHaveBeenCalled();
    await enter("Bắt đầu câu 2", "2000");
    await enter("Kết thúc câu 2", "4000");
    expect(draft().segments[1].startMs).toBe(2000);
    expect(draft().segments[1].endMs).toBe(4000);
    expect(container.textContent).not.toContain("thời gian chồng");
  });
  it("deletes only the selected local segment and starts the next one at the previous end", async () => {
    await act(async () =>
      root.render(
        <Harness
          initial={JSON.stringify(base([segment(), segment(3000, 5000)]))}
        />,
      ),
    );
    await act(async () => button("Bỏ câu 1 khỏi bản nháp").click());
    expect(draft().segments).toHaveLength(1);
    expect(draft().segments[0].startMs).toBe(3000);
    await act(async () => button("Thêm câu transcript").click());
    expect(draft().segments[1].startMs).toBe(5000);
    expect(draft().segments[1].endMs).toBe(8000);
    expect(draft().groups).toEqual(base().groups);
  });
  it("blocks unsupported shapes and policies without rewriting the source", async () => {
    for (const invalid of [
      "[]",
      JSON.stringify({ segments: "wrong" }),
      JSON.stringify(base([{ ...segment(), normalizationPolicyVersion: 2 }])),
      JSON.stringify(base([{ ...segment(), acceptedAnswers: [null] }])),
    ]) {
      await act(async () =>
        root.render(<TranscriptFields json={invalid} onChange={changed} />),
      );
      expect(container.textContent).toContain("Sửa ở ô JSON");
      expect(container.querySelector("input")).toBeNull();
    }
    expect(changed).not.toHaveBeenCalled();
  });
  it("bounds additions by count and actual timeline and ignores empty or invalid numeric input", async () => {
    await act(async () =>
      root.render(
        <Harness initial={JSON.stringify(base([segment(3599000, 3600000)]))} />,
      ),
    );
    expect(button("Thêm câu transcript").disabled).toBe(true);
    await enter("Bắt đầu câu 1", "");
    expect(changed).not.toHaveBeenCalled();
    await enter("Kết thúc câu 1", "3600001");
    expect(changed).not.toHaveBeenCalled();
    await act(async () =>
      root.render(
        <TranscriptFields
          json={JSON.stringify(
            base(
              Array.from({ length: 100 }, (_, i) =>
                segment(i * 3000, i * 3000 + 2000),
              ),
            ),
          )}
          onChange={changed}
        />,
      ),
    );
    expect(button("Thêm câu transcript").disabled).toBe(true);
  });
  it("respects an in-flight CMS command and flags a missing media reference", async () => {
    await act(async () =>
      root.render(
        <Harness
          disabled
          initial={JSON.stringify({ ...base([segment()]), mediaAssetId: null })}
        />,
      ),
    );
    expect(container.textContent).toContain("Chưa gắn audio");
    expect(control("Tiếng Anh câu 1").matches(":disabled")).toBe(true);
    await act(async () => button("Bỏ câu 1 khỏi bản nháp").click());
    expect(changed).not.toHaveBeenCalled();
    expect(draft().segments).toHaveLength(1);
  });
});

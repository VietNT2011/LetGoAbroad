// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../src/api";
import { CardEditor, type EditableCard } from "../src/pages/CardEditor";

vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
const card: EditableCard = {
  id: "card-1",
  front: "Original front",
  back: "Original back",
  etag: '"old"',
  contentVersion: 1,
};
let root: Root;
let container: HTMLDivElement;
const saved = vi.fn();
const cancel = vi.fn();
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  });
};
async function submit() {
  await act(async () => {
    container
      .querySelector("form")!
      .dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  });
  await flush();
}
async function click(text: string) {
  const target = [...container.querySelectorAll("button")].find(
    (x) => x.textContent === text,
  )!;
  expect(target).toBeTruthy();
  await act(async () => target.click());
  await flush();
}

describe("versioned card editor", () => {
  beforeEach(async () => {
    request.mockReset();
    saved.mockReset();
    cancel.mockReset();
    request.mockResolvedValue(card);
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () =>
      root.render(<CardEditor card={card} saved={saved} cancel={cancel} />),
    );
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it("explains the schedule reset and binds saved text to the displayed etag", async () => {
    expect(container.textContent).toContain("đưa lịch ôn về bước 0");
    expect(
      container.querySelector('label[for="edit-card-front"]'),
    ).toBeTruthy();
    await submit();
    expect(request.mock.calls[0][0]).toBe("/cards/card-1");
    const input = JSON.parse(request.mock.calls[0][1]!.body as string);
    expect(input).toMatchObject({
      expectedEtag: card.etag,
      front: card.front,
      back: card.back,
    });
    expect(input.clientRequestId).toBeTruthy();
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it("keeps an unknown request immutable for exact retry and prevents premature cancellation", async () => {
    request.mockRejectedValueOnce(new TypeError("Offline"));
    await submit();
    expect(container.textContent).toContain("Chưa nhận được xác nhận");
    expect(
      [...container.querySelectorAll("textarea")].every((x) => x.disabled),
    ).toBe(true);
    await click("Hủy sửa");
    expect(cancel).not.toHaveBeenCalled();
    await click("Gửi lại bản sửa");
    expect(request.mock.calls[1][1]!.body).toBe(request.mock.calls[0][1]!.body);
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it("preserves stale drafts until explicit comparison then uses a fresh etag and command", async () => {
    request.mockRejectedValueOnce(
      new ApiError(412, "stale_version", "Changed"),
    );
    await submit();
    expect(
      (container.querySelector("textarea") as HTMLTextAreaElement).value,
    ).toBe(card.front);
    const remote = {
      ...card,
      front: "Remote front",
      back: "Remote back",
      etag: '"new"',
      contentVersion: 2,
    };
    request.mockResolvedValueOnce(remote);
    await click("Tải bản để so sánh");
    expect(
      container.querySelector('[aria-label="Nội dung hiện tại trên máy chủ"]')
        ?.textContent,
    ).toContain("Remote back");
    await click("Giữ bản nháp để lưu lại");
    await submit();
    const input = JSON.parse(request.mock.calls[2][1]!.body as string);
    expect(input).toMatchObject({
      expectedEtag: remote.etag,
      front: card.front,
      back: card.back,
    });
    expect(input.clientRequestId).not.toBe(
      JSON.parse(request.mock.calls[0][1]!.body as string).clientRequestId,
    );
  });

  it("can deliberately adopt the remote text without automatically writing", async () => {
    request.mockRejectedValueOnce(
      new ApiError(412, "stale_version", "Changed"),
    );
    await submit();
    request.mockResolvedValueOnce({
      ...card,
      front: "Remote",
      back: "New answer",
      etag: '"new"',
      contentVersion: 2,
    });
    await click("Tải bản để so sánh");
    await click("Dùng nội dung máy chủ");
    expect(
      [...container.querySelectorAll("textarea")].map((x) => x.value),
    ).toEqual(["Remote", "New answer"]);
    expect(request).toHaveBeenCalledTimes(2);
    expect(saved).not.toHaveBeenCalled();
  });

  it("keeps validation failures editable and permits an explicit cancel", async () => {
    request.mockRejectedValueOnce(
      new ApiError(409, "card_duplicate", "Trùng thẻ"),
    );
    await submit();
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      "Trùng thẻ",
    );
    expect(
      [...container.querySelectorAll("textarea")].every((x) => !x.disabled),
    ).toBe(true);
    await click("Hủy sửa");
    expect(cancel).toHaveBeenCalledTimes(1);
    expect(saved).not.toHaveBeenCalled();
  });

  it("does not accept a second submit while an edit is in flight", async () => {
    let resolve!: (value: unknown) => void;
    request.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }) as never,
    );
    await submit();
    await submit();
    expect(request).toHaveBeenCalledTimes(1);
    await act(async () => resolve(card));
    await flush();
    expect(saved).toHaveBeenCalledTimes(1);
  });
});

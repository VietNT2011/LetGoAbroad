// @vitest-environment jsdom
import { act, StrictMode } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, post } from "../src/api";
import { useStudyActivity, type StudyKind } from "../src/useStudyActivity";

vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
  post: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const start = vi.mocked(post);
const pulse = vi.mocked(api);
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}
let root: Root;
let container: HTMLDivElement;
let focused: boolean;
let visible: DocumentVisibilityState;
const receipt = (options?: RequestInit) => ({
  sequence: JSON.parse(options?.body as string).sequence,
});
const bodies = () =>
  pulse.mock.calls.map(
    ([, options]) =>
      JSON.parse(options?.body as string) as {
        sequence: number;
        isActive: boolean;
      },
  );
const flush = async () => {
  await act(async () => {
    for (let index = 0; index < 8; index++) await Promise.resolve();
  });
};
const advance = async (milliseconds: number) => {
  await act(async () => {
    vi.advanceTimersByTime(milliseconds);
  });
  await flush();
};
const event = async (target: Window | Document, name: string) => {
  await act(async () => target.dispatchEvent(new Event(name)));
  await flush();
};
function Page({
  kind = "lesson",
  owner = "private-owner",
  pageKey = "private-lesson-id",
}: {
  kind?: StudyKind | null;
  owner?: string;
  pageKey?: string;
}) {
  useStudyActivity(kind, pageKey, owner);
  return (
    <>
      <input defaultValue="Secret learner draft" />
      <audio />
    </>
  );
}
async function mount(props: Parameters<typeof Page>[0] = {}, strict = false) {
  await act(async () =>
    root.render(
      strict ? (
        <StrictMode>
          <Page {...props} />
        </StrictMode>
      ) : (
        <Page {...props} />
      ),
    ),
  );
  await flush();
}

describe("study activity browser state", () => {
  beforeEach(() => {
    vi.useFakeTimers({
      toFake: [
        "setInterval",
        "clearInterval",
        "setTimeout",
        "clearTimeout",
        "performance",
      ],
    });
    focused = true;
    visible = "visible";
    vi.spyOn(document, "hasFocus").mockImplementation(() => focused);
    vi.spyOn(document, "visibilityState", "get").mockImplementation(
      () => visible,
    );
    start.mockReset();
    pulse.mockReset();
    start.mockResolvedValue({
      id: "session-1",
      lastSequence: 0,
      isExpired: false,
    });
    pulse.mockImplementation(
      async (_path, options) => receipt(options) as never,
    );
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    await flush();
    container.remove();
    vi.clearAllTimers();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("does not start on hidden pages and starts only after visible focus", async () => {
    visible = "hidden";
    await mount();
    await advance(30_000);
    expect(start).not.toHaveBeenCalled();
    visible = "visible";
    focused = false;
    await event(document, "visibilitychange");
    expect(start).not.toHaveBeenCalled();
    focused = true;
    await event(window, "focus");
    expect(start).toHaveBeenCalledTimes(1);
    expect(bodies()).toEqual([{ sequence: 1, isActive: true }]);
  });

  it("does not measure absent private data or an unknown owner", async () => {
    await mount({ kind: null });
    await advance(30_000);
    expect(start).not.toHaveBeenCalled();
    await mount({ owner: "" });
    await advance(30_000);
    expect(start).not.toHaveBeenCalled();
  });

  it("sends ordered pulses every fifteen seconds and pauses/resumes with focus", async () => {
    await mount();
    await advance(15_000);
    focused = false;
    await event(window, "blur");
    await advance(30_000);
    expect(bodies()).toEqual([
      { sequence: 1, isActive: true },
      { sequence: 2, isActive: true },
      { sequence: 3, isActive: false },
    ]);
    focused = true;
    await event(window, "focus");
    expect(bodies().at(-1)).toEqual({ sequence: 4, isActive: true });
  });

  it("rechecks visibility after an asynchronous session start instead of sending an obsolete active flag", async () => {
    const pending = deferred<unknown>();
    start.mockImplementationOnce(() => pending.promise as never);
    await mount();
    visible = "hidden";
    await event(document, "visibilitychange");
    await act(async () =>
      pending.resolve({ id: "session-1", lastSequence: 0, isExpired: false }),
    );
    await flush();
    expect(bodies()).toEqual([{ sequence: 1, isActive: false }]);
  });

  it("goes inactive after ninety seconds and input resumes without transmitting its content", async () => {
    await mount();
    for (let index = 0; index < 6; index++) await advance(15_000);
    expect(bodies().at(-1)?.isActive).toBe(false);
    const paused = pulse.mock.calls.length;
    await advance(30_000);
    expect(pulse).toHaveBeenCalledTimes(paused);
    await act(async () =>
      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Secret learner draft" }),
      ),
    );
    await flush();
    expect(bodies().at(-1)?.isActive).toBe(true);
    const sent = JSON.stringify([...start.mock.calls, ...pulse.mock.calls]);
    expect(sent).not.toContain("Secret learner draft");
    expect(sent).not.toContain("private-owner");
    expect(sent).not.toContain("private-lesson-id");
    expect(Object.keys(start.mock.calls[0][1] as object).sort()).toEqual([
      "clientSessionId",
      "kind",
    ]);
  });

  it("playing media permits activity after idle but never overrides a hidden document", async () => {
    await mount();
    const media = container.querySelector("audio")!;
    vi.spyOn(media, "paused", "get").mockReturnValue(false);
    vi.spyOn(media, "ended", "get").mockReturnValue(false);
    for (let index = 0; index < 7; index++) await advance(15_000);
    expect(bodies().every((body) => body.isActive)).toBe(true);
    visible = "hidden";
    await event(document, "visibilitychange");
    expect(bodies().at(-1)?.isActive).toBe(false);
  });

  it("retries an unknown receipt with exactly the same payload, then reconciles the current inactive state immediately", async () => {
    await mount();
    const pending = deferred<unknown>();
    pulse.mockImplementationOnce(() => pending.promise as never);
    await advance(15_000);
    focused = false;
    await event(window, "blur");
    await advance(15_000);
    expect(pulse).toHaveBeenCalledTimes(2);
    await act(async () =>
      pending.reject(new TypeError("Network outcome unknown")),
    );
    await flush();
    expect(pulse).toHaveBeenCalledTimes(2);
    await advance(15_000);
    expect(bodies()).toEqual([
      { sequence: 1, isActive: true },
      { sequence: 2, isActive: true },
      { sequence: 2, isActive: true },
      { sequence: 3, isActive: false },
    ]);
    expect(
      pulse.mock.calls.every(([, options]) => options?.keepalive === true),
    ).toBe(true);
  });

  it("does not spin on repeated network failures even when input arrives during a request", async () => {
    await mount();
    const pending = deferred<unknown>();
    pulse.mockImplementationOnce(() => pending.promise as never);
    await advance(15_000);
    await event(document, "visibilitychange");
    await act(async () => pending.reject(new TypeError("Offline")));
    await flush();
    expect(pulse).toHaveBeenCalledTimes(2);
    await flush();
    expect(pulse).toHaveBeenCalledTimes(2);
  });

  it("uses a fresh client session after a business expiry conflict", async () => {
    await mount();
    pulse.mockRejectedValueOnce(
      new ApiError(409, "activity_session_expired", "Expired"),
    );
    await advance(15_000);
    expect(start).toHaveBeenCalledTimes(1);
    await advance(15_000);
    expect(start).toHaveBeenCalledTimes(2);
    expect(
      (start.mock.calls[1][1] as { clientSessionId: string }).clientSessionId,
    ).not.toBe(
      (start.mock.calls[0][1] as { clientSessionId: string }).clientSessionId,
    );
    expect(bodies().at(-1)).toEqual({ sequence: 1, isActive: true });
  });

  it("sends an inactive boundary on pagehide even before visibility changes and resumes once on pageshow", async () => {
    await mount();
    await event(window, "pagehide");
    expect(bodies().at(-1)).toEqual({ sequence: 2, isActive: false });
    await advance(30_000);
    expect(pulse).toHaveBeenCalledTimes(2);
    await event(window, "pageshow");
    expect(bodies().at(-1)).toEqual({ sequence: 3, isActive: true });
    await event(window, "pageshow");
    expect(pulse).toHaveBeenCalledTimes(3);
  });

  it("closes an active route and removes its timers/listeners on cleanup", async () => {
    await mount();
    await act(async () => root.render(<p>Another page</p>));
    await flush();
    expect(bodies().at(-1)).toEqual({ sequence: 2, isActive: false });
    const count = pulse.mock.calls.length;
    await advance(60_000);
    await event(window, "focus");
    await event(window, "pagehide");
    await event(window, "pageshow");
    expect(pulse).toHaveBeenCalledTimes(count);
    expect(vi.getTimerCount()).toBe(0);
  });

  it("does not send an active pulse from a disposed StrictMode session-start request", async () => {
    const old = deferred<unknown>();
    start.mockImplementationOnce(() => old.promise as never);
    await mount({}, true);
    expect(bodies()).toEqual([{ sequence: 1, isActive: true }]);
    await act(async () =>
      old.resolve({
        id: "obsolete-session",
        lastSequence: 0,
        isExpired: false,
      }),
    );
    await flush();
    expect(pulse).toHaveBeenCalledTimes(1);
    expect(pulse.mock.calls[0][0]).not.toContain("obsolete-session");
  });
});

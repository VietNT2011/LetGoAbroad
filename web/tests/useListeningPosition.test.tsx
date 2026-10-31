// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { api, ApiError } from "../src/api";
import {
  useListeningPosition,
  type PositionAck,
} from "../src/useListeningPosition";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const request = vi.mocked(api);
let root: Root;
let container: HTMLDivElement;
let current: ReturnType<typeof useListeningPosition>;
let enabled = true;
let readPosition = 4200;
const ack = (positionMs: number, etag = '"next"'): PositionAck => ({
  sessionId: "session-1",
  positionMs,
  etag,
});
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 6; i++) await Promise.resolve();
  });
};
function Host() {
  current = useListeningPosition(
    "session-1",
    ack(0, '"initial"'),
    10000,
    () => readPosition,
    enabled,
  );
  return <p>{current.saved}</p>;
}
beforeEach(async () => {
  enabled = true;
  readPosition = 4200;
  request.mockReset();
  request.mockImplementation(async (_path, options) =>
    ack(JSON.parse(options!.body as string).positionMs),
  );
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
  await act(async () => root.render(<Host />));
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});
describe("session-fenced playback queue", () => {
  it("coalesces desired positions behind an immutable in-flight command and advances its etag", async () => {
    let resolve!: (value: PositionAck) => void;
    request.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }) as never,
    );
    await act(async () => {
      current.save(1000);
      current.save(2000);
      current.save(3000);
    });
    expect(request).toHaveBeenCalledTimes(1);
    await act(async () => resolve(ack(1000, '"second"')));
    await flush();
    expect(request).toHaveBeenCalledTimes(2);
    expect(JSON.parse(request.mock.calls[1][1]!.body as string)).toMatchObject({
      expectedEtag: '"second"',
      positionMs: 3000,
    });
    expect(current.saved).toBe(3000);
  });
  it("retries an unknown outcome with the identical event and payload", async () => {
    request.mockRejectedValueOnce(new TypeError("Offline"));
    await act(async () => current.save(1000));
    await flush();
    await act(async () => current.retry());
    await flush();
    expect(request.mock.calls[1][1]!.body).toBe(request.mock.calls[0][1]!.body);
    expect(current.saved).toBe(1000);
  });
  it("stops on another current session instead of silently reclaiming its resume head", async () => {
    request.mockRejectedValueOnce(
      new ApiError(409, "listening_session_changed", "Other tab"),
    );
    await act(async () => current.save(1000));
    await flush();
    expect(current.conflict).toBe(true);
    await act(async () => {
      current.save(5000);
      current.retry();
    });
    expect(request).toHaveBeenCalledTimes(1);
  });
  it("aborts on unmount and ignores a late success without draining the next position", async () => {
    let resolve!: (value: PositionAck) => void;
    request.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }) as never,
    );
    await act(async () => {
      current.save(1000);
      current.save(2000);
    });
    const signal = request.mock.calls[0][1]!.signal;
    await act(async () => root.render(null));
    expect(signal?.aborted).toBe(true);
    await act(async () => resolve(ack(1000)));
    await flush();
    expect(request).toHaveBeenCalledTimes(1);
  });
  it("uses a small keepalive command at pagehide and clamps to verified duration", async () => {
    readPosition = 14000;
    await act(async () => window.dispatchEvent(new Event("pagehide")));
    await flush();
    expect(request.mock.calls[0][1]?.keepalive).toBe(true);
    expect(
      JSON.parse(request.mock.calls[0][1]!.body as string).positionMs,
    ).toBe(10000);
  });
  it("never saves from a view that is no longer current", async () => {
    enabled = false;
    await act(async () => root.render(<Host />));
    await act(async () => {
      current.save(1000);
      window.dispatchEvent(new Event("pagehide"));
    });
    expect(request).not.toHaveBeenCalled();
  });
  it("rejects non-finite times and preserves zero as a valid saved position", async () => {
    await act(async () => {
      current.save(NaN);
      current.save(Infinity);
    });
    expect(request).not.toHaveBeenCalled();
    await act(async () => current.save(0));
    await flush();
    expect(
      JSON.parse(request.mock.calls[0][1]!.body as string).positionMs,
    ).toBe(0);
  });
});

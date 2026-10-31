// @vitest-environment jsdom
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { api, ApiError } from "../src/api";
import { audioUpload, audioBlockBytes } from "../src/audioUpload";
import { confirmSession, suspendSession } from "../src/authSession";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
const call = vi.mocked(api);
const fetchMock = vi.fn<typeof fetch>();
const ok = () => new Response(null, { status: 201 });
const file = (size = 3) =>
  new File([new Uint8Array(size)], "original.wav", { type: "audio/wav" });
const signal = () => new AbortController().signal;
const grant = () => ({
  assetId: "asset-1",
  uploadUrl: "https://storage.example.test/staging/asset?sig=fixture-only",
  expiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
});
beforeEach(() => {
  confirmSession("owner");
  call.mockReset();
  call.mockImplementation(async (path) =>
    path === "/media/uploads" ? grant() : undefined,
  );
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(ok());
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  vi.unstubAllGlobals();
});
describe("resumable audio blocks", () => {
  it("uses fixed-size unique block ids, ordered commit and a queued completion request", async () => {
    const progress = vi.fn();
    const task = audioUpload(
      file(audioBlockBytes + 3),
      "lesson_audio",
      "Original authored audio",
      progress,
    );
    expect(await task.send(signal())).toBe("asset-1");
    expect(fetchMock).toHaveBeenCalledTimes(3);
    const ids = fetchMock.mock.calls
      .slice(0, 2)
      .map((c) => new URL(String(c[0])).searchParams.get("blockid")!);
    expect(ids[0].length).toBe(ids[1].length);
    expect(ids[0]).not.toBe(ids[1]);
    expect((fetchMock.mock.calls[0][1]!.body as Blob).size).toBe(
      audioBlockBytes,
    );
    expect((fetchMock.mock.calls[1][1]!.body as Blob).size).toBe(3);
    expect(fetchMock.mock.calls[0][1]).toMatchObject({
      credentials: "omit",
      referrerPolicy: "no-referrer",
    });
    expect(fetchMock.mock.calls[2][1]!.body).toContain(
      `<Latest>${ids[0]}</Latest><Latest>${ids[1]}</Latest>`,
    );
    expect(call.mock.calls.at(-1)?.[0]).toBe("/media/asset-1/complete");
    expect(progress).toHaveBeenLastCalledWith(
      audioBlockBytes + 3,
      audioBlockBytes + 3,
    );
  });
  it("resumes only the first unacknowledged block after an HTTP failure", async () => {
    fetchMock
      .mockResolvedValueOnce(ok())
      .mockResolvedValueOnce(new Response(null, { status: 503 }));
    const task = audioUpload(
      file(audioBlockBytes + 2),
      "recording",
      "My voice",
    );
    await expect(task.send(signal())).rejects.toThrow("HTTP 503");
    const failed = String(fetchMock.mock.calls[1][0]);
    await task.send(signal());
    expect(String(fetchMock.mock.calls[2][0])).toBe(failed);
    expect(
      call.mock.calls.filter((x) => x[0] === "/media/uploads"),
    ).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });
  it("retries a lost block response with the exact same block identity and bytes", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("Lost ACK"));
    const task = audioUpload(file(), "recording", "My voice");
    await expect(task.send(signal())).rejects.toThrow("Lost ACK");
    await task.send(signal());
    expect(String(fetchMock.mock.calls[1][0])).toBe(
      String(fetchMock.mock.calls[0][0]),
    );
    expect((fetchMock.mock.calls[1][1]!.body as Blob).size).toBe(
      (fetchMock.mock.calls[0][1]!.body as Blob).size,
    );
  });
  it("retries a lost commit using Latest blocks without retransmitting acknowledged chunks", async () => {
    fetchMock
      .mockResolvedValueOnce(ok())
      .mockRejectedValueOnce(new TypeError("Lost commit ACK"));
    const task = audioUpload(file(), "recording", "My voice");
    await expect(task.send(signal())).rejects.toThrow("Lost commit ACK");
    await task.send(signal());
    expect(String(fetchMock.mock.calls[2][0])).toBe(
      String(fetchMock.mock.calls[1][0]),
    );
    expect(fetchMock.mock.calls[2][1]!.body).toBe(
      fetchMock.mock.calls[1][1]!.body,
    );
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });
  it("retries only Complete after its unknown outcome", async () => {
    let completeCalls = 0;
    call.mockImplementation(async (path) => {
      if (path === "/media/uploads") return grant();
      if (++completeCalls === 1) throw new TypeError("Lost completion ACK");
    });
    const task = audioUpload(file(), "recording", "My voice");
    await expect(task.send(signal())).rejects.toThrow("Lost completion ACK");
    await task.send(signal());
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(completeCalls).toBe(2);
  });
  it("pins the actor across raw SAS requests and cannot complete under a changed login", async () => {
    fetchMock.mockImplementationOnce(async () => {
      confirmSession("different-owner");
      return ok();
    });
    const task = audioUpload(
      file(audioBlockBytes + 2),
      "recording",
      "My voice",
    );
    await expect(task.send(signal())).rejects.toBeInstanceOf(ApiError);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(call.mock.calls.some((x) => x[0].endsWith("/complete"))).toBe(false);
  });
  it("refuses to start while the identity is being verified", async () => {
    suspendSession();
    const task = audioUpload(file(), "recording", "My voice");
    await expect(task.send(signal())).rejects.toBeInstanceOf(ApiError);
    expect(call).not.toHaveBeenCalled();
  });
  it("honors abort even when the transport returns a late successful block", async () => {
    const controller = new AbortController();
    fetchMock.mockImplementationOnce(async () => {
      controller.abort();
      return ok();
    });
    const progress = vi.fn();
    const task = audioUpload(file(), "recording", "My voice", progress);
    await expect(task.send(controller.signal)).rejects.toMatchObject({
      name: "AbortError",
    });
    expect(progress).not.toHaveBeenCalled();
    expect(call.mock.calls.some((x) => x[0].endsWith("/complete"))).toBe(false);
  });
  it("rejects empty, oversized or unsupported files before reserving storage", async () => {
    const oversized = file();
    Object.defineProperty(oversized, "size", { value: 21 * 1024 * 1024 });
    for (const value of [
      file(0),
      oversized,
      new File(["text"], "file.txt", { type: "text/plain" }),
    ])
      await expect(
        audioUpload(value, "recording", "My voice").send(signal()),
      ).rejects.toThrow("audio hợp lệ");
    expect(call).not.toHaveBeenCalled();
  });
  it("does not upload or enqueue again after a confirmed successful send", async () => {
    const task = audioUpload(file(), "recording", "My voice");
    await task.send(signal());
    const count = call.mock.calls.length;
    await task.send(signal());
    expect(call).toHaveBeenCalledTimes(count);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
  it("rejects overlapping sends on the same task", async () => {
    let resolve!: (value: Response) => void;
    fetchMock.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    const task = audioUpload(file(), "recording", "My voice");
    const first = task.send(signal());
    await Promise.resolve();
    await Promise.resolve();
    await expect(task.send(signal())).rejects.toThrow("đang chạy");
    resolve(ok());
    await first;
  });
  it("keeps an expired grant from sending blocks or silently creating a new reservation", async () => {
    call.mockResolvedValueOnce({
      ...grant(),
      expiresAt: new Date(Date.now() - 1).toISOString(),
    });
    const task = audioUpload(file(), "recording", "My voice");
    await expect(task.send(signal())).rejects.toThrow("hết hạn");
    await expect(task.send(signal())).rejects.toThrow("hết hạn");
    expect(call).toHaveBeenCalledTimes(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });
  it("aborts before requesting a grant and normalizes recorded codec MIME parameters", async () => {
    const c = new AbortController();
    c.abort();
    await expect(
      audioUpload(file(), "recording", "My voice").send(c.signal),
    ).rejects.toMatchObject({ name: "AbortError" });
    expect(call).not.toHaveBeenCalled();
    await audioUpload(
      new File(["bytes"], "voice.webm", { type: "audio/webm;codecs=opus" }),
      "recording",
      "My voice",
    ).send(signal());
    expect(JSON.parse(call.mock.calls[0][1]!.body as string).contentType).toBe(
      "audio/webm",
    );
  });
});

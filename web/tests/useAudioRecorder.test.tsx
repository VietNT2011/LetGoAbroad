// @vitest-environment jsdom
import { act, StrictMode } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, it, expect, vi } from "vitest";
import { useAudioRecorder, recordingLimitMs } from "../src/useAudioRecorder";
import { confirmSession, suspendSession } from "../src/authSession";
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
class Recorder {
  static instances: Recorder[] = [];
  static failStart = false;
  static isTypeSupported = vi.fn(() => true);
  state = "inactive";
  mimeType: string;
  stream: MediaStream;
  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  onerror: (() => void) | null = null;
  constructor(stream: MediaStream, options: { mimeType: string }) {
    this.stream = stream;
    this.mimeType = options.mimeType;
    Recorder.instances.push(this);
  }
  start() {
    if (Recorder.failStart) throw new Error("Encoder failed");
    this.state = "recording";
  }
  stop() {
    this.state = "inactive";
    queueMicrotask(() => {
      this.emit(new Blob(["final"]));
      this.onstop?.();
    });
  }
  emit(data: Blob) {
    this.ondataavailable?.({ data });
  }
}
let root: Root;
let container: HTMLDivElement;
let current: ReturnType<typeof useAudioRecorder>;
let stopped: ReturnType<typeof vi.fn>;
let stream: MediaStream;
const getUserMedia = vi.fn();
function Host() {
  current = useAudioRecorder();
  return <p>{current.phase}</p>;
}
const flush = async () => {
  await act(async () => {
    for (let i = 0; i < 5; i++) await Promise.resolve();
  });
};
beforeEach(async () => {
  confirmSession("owner");
  Object.defineProperty(document, "hidden", {
    value: false,
    configurable: true,
  });
  Recorder.instances = [];
  Recorder.failStart = false;
  Recorder.isTypeSupported.mockReset().mockReturnValue(true);
  stopped = vi.fn();
  stream = { getTracks: () => [{ stop: stopped }] } as unknown as MediaStream;
  getUserMedia.mockReset().mockResolvedValue(stream);
  Object.defineProperty(navigator, "mediaDevices", {
    value: { getUserMedia },
    configurable: true,
  });
  vi.stubGlobal("MediaRecorder", Recorder);
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
  await act(async () =>
    root.render(
      <StrictMode>
        <Host />
      </StrictMode>,
    ),
  );
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});
describe("explicit audio capture", () => {
  it("never opens a microphone on mount and produces only a local file after explicit stop", async () => {
    expect(getUserMedia).not.toHaveBeenCalled();
    await act(async () => current.start());
    expect(getUserMedia).toHaveBeenCalledWith({ audio: true, video: false });
    expect(current.phase).toBe("recording");
    await act(async () => Recorder.instances[0].emit(new Blob(["first"])));
    await act(async () => current.stop());
    await flush();
    expect(current.phase).toBe("ready");
    expect(current.file?.type).toBe("audio/webm");
    expect(current.file?.size).toBe(10);
    expect(stopped).toHaveBeenCalled();
  });
  it("closes active tracks when unmounted and discards late recorder data", async () => {
    await act(async () => current.start());
    await act(async () => root.render(null));
    await flush();
    expect(stopped).toHaveBeenCalled();
    expect(Recorder.instances[0].state).toBe("inactive");
  });
  it("closes a permission grant that arrives after unmount without constructing a recorder", async () => {
    let resolve!: (stream: MediaStream) => void;
    getUserMedia.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    let start!: Promise<void>;
    await act(async () => {
      start = current.start();
    });
    await act(async () => root.render(null));
    await act(async () => {
      resolve(stream);
      await start;
    });
    expect(stopped).toHaveBeenCalled();
    expect(Recorder.instances).toHaveLength(0);
  });
  it("cancels a pending microphone request and disposes its eventual stream", async () => {
    let resolve!: (stream: MediaStream) => void;
    getUserMedia.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    let start!: Promise<void>;
    await act(async () => {
      start = current.start();
    });
    await act(async () => current.discard());
    await act(async () => {
      resolve(stream);
      await start;
    });
    expect(stopped).toHaveBeenCalled();
    expect(current.phase).toBe("idle");
    expect(Recorder.instances).toHaveLength(0);
  });
  it("keeps denied permissions recoverable without automatically requesting again", async () => {
    getUserMedia.mockRejectedValueOnce(
      new DOMException("Permission denied", "NotAllowedError"),
    );
    await act(async () => current.start());
    expect(current.phase).toBe("error");
    expect(getUserMedia).toHaveBeenCalledTimes(1);
    await act(async () => current.start());
    expect(current.phase).toBe("recording");
  });
  it("supports a file-upload fallback when capture APIs are absent", async () => {
    vi.stubGlobal("MediaRecorder", undefined);
    await act(async () => current.start());
    expect(current.error?.message).toContain("chọn file audio");
    expect(getUserMedia).not.toHaveBeenCalled();
  });
  it("stops at the capture limit and clears all tracks", async () => {
    vi.useFakeTimers();
    await act(async () => current.start());
    await act(async () => vi.advanceTimersByTimeAsync(recordingLimitMs));
    expect(current.phase).toBe("ready");
    expect(stopped).toHaveBeenCalled();
  });
  it("stops on hidden pages while retaining the local draft for the same actor", async () => {
    await act(async () => current.start());
    Object.defineProperty(document, "hidden", {
      value: true,
      configurable: true,
    });
    await act(async () =>
      document.dispatchEvent(new Event("visibilitychange")),
    );
    await flush();
    expect(stopped).toHaveBeenCalled();
    expect(current.phase).toBe("ready");
    expect(current.file).toBeTruthy();
  });
  it("rejects a late permission grant after the signed-in actor changes", async () => {
    let resolve!: (stream: MediaStream) => void;
    getUserMedia.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    let start!: Promise<void>;
    await act(async () => {
      start = current.start();
    });
    confirmSession("other");
    await act(async () => {
      resolve(stream);
      await start;
    });
    expect(stopped).toHaveBeenCalled();
    expect(Recorder.instances).toHaveLength(0);
    expect(current.file).toBeUndefined();
  });
  it("stops and discards active capture on an actor change before accepting another chunk", async () => {
    await act(async () => current.start());
    confirmSession("other");
    await act(async () => Recorder.instances[0].emit(new Blob(["private"])));
    await flush();
    expect(stopped).toHaveBeenCalled();
    expect(current.file).toBeUndefined();
    expect(current.phase).toBe("idle");
  });
  it("releases permission-granted tracks if encoder startup fails", async () => {
    Recorder.failStart = true;
    await act(async () => current.start());
    expect(stopped).toHaveBeenCalled();
    expect(current.phase).toBe("error");
    expect(current.file).toBeUndefined();
  });
  it("discards over-limit data instead of uploading or returning a large file", async () => {
    await act(async () => current.start());
    const huge = new Blob();
    Object.defineProperty(huge, "size", { value: 21 * 1024 * 1024 });
    await act(async () => Recorder.instances[0].emit(huge));
    await flush();
    expect(stopped).toHaveBeenCalled();
    expect(current.phase).toBe("error");
    expect(current.file).toBeUndefined();
  });
  it("prevents duplicate requests while permission is pending", async () => {
    let resolve!: (stream: MediaStream) => void;
    getUserMedia.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    let first!: Promise<void>;
    await act(async () => {
      first = current.start();
      void current.start();
    });
    expect(getUserMedia).toHaveBeenCalledTimes(1);
    await act(async () => {
      resolve(stream);
      await first;
    });
  });
  it("uses supported MP4 capture when WebM and Ogg are unavailable", async () => {
    Recorder.isTypeSupported.mockImplementation((type) => type === "audio/mp4");
    await act(async () => current.start());
    await act(async () => current.stop());
    await flush();
    expect(current.file?.type).toBe("audio/mp4");
    expect(current.file?.name).toMatch(/\.m4a$/);
  });
  it("stops an encoder error and never restores that partial draft", async () => {
    await act(async () => current.start());
    await act(async () => Recorder.instances[0].onerror?.());
    await flush();
    expect(stopped).toHaveBeenCalled();
    expect(current.phase).toBe("error");
    expect(current.file).toBeUndefined();
    suspendSession();
  });
});

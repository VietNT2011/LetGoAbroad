import { useEffect, useRef, useState } from "react";
import { sessionBinding } from "./authSession";

export const recordingLimitMs = 170000;
/** VI: Mic chỉ mở sau click, mọi đường dừng/unmount đều đóng track; draft chưa tự upload. EN: Explicitly acquired microphones are released on every exit path; capture never uploads automatically. */
export function useAudioRecorder() {
  const [phase, setPhase] = useState<
    "idle" | "requesting" | "recording" | "stopping" | "ready" | "error"
  >("idle");
  const [file, setFile] = useState<File>();
  const [error, setError] = useState<Error>();
  const alive = useRef(false);
  const generation = useRef(0);
  const pending = useRef(false);
  const capture = useRef<
    | {
        recorder: MediaRecorder;
        stream: MediaStream;
        timer: ReturnType<typeof setTimeout>;
        discard: boolean;
      }
    | undefined
  >(undefined);
  const stop = () => {
    const active = capture.current;
    if (!active) return;
    clearTimeout(active.timer);
    if (active.recorder.state !== "inactive") {
      setPhase("stopping");
      active.recorder.stop();
    }
    active.stream.getTracks().forEach((track) => track.stop());
  };
  const discard = () => {
    generation.current++;
    pending.current = false;
    const active = capture.current;
    if (active) {
      active.discard = true;
      clearTimeout(active.timer);
      if (active.recorder.state !== "inactive") active.recorder.stop();
      active.stream.getTracks().forEach((track) => track.stop());
      capture.current = undefined;
    }
    setFile(undefined);
    setError(undefined);
    setPhase("idle");
  };
  useEffect(() => {
    alive.current = true;
    const hide = () => {
      if (document.hidden) stop();
    };
    const leave = () => stop();
    document.addEventListener("visibilitychange", hide);
    window.addEventListener("pagehide", leave);
    return () => {
      alive.current = false;
      generation.current++;
      pending.current = false;
      document.removeEventListener("visibilitychange", hide);
      window.removeEventListener("pagehide", leave);
      const active = capture.current;
      if (active) {
        active.discard = true;
        clearTimeout(active.timer);
        if (active.recorder.state !== "inactive") active.recorder.stop();
        active.stream.getTracks().forEach((track) => track.stop());
        capture.current = undefined;
      }
    };
  }, []);
  const start = async () => {
    if (pending.current || capture.current) return;
    const epoch = ++generation.current;
    const actor = sessionBinding();
    const current = () => {
      const next = sessionBinding();
      return (
        alive.current &&
        generation.current === epoch &&
        !!actor.actor &&
        !actor.checking &&
        !next.checking &&
        next.actor === actor.actor &&
        next.epoch === actor.epoch
      );
    };
    pending.current = true;
    setFile(undefined);
    setError(undefined);
    setPhase("requesting");
    let stream: MediaStream | undefined;
    try {
      if (
        !navigator.mediaDevices?.getUserMedia ||
        typeof MediaRecorder === "undefined"
      )
        throw new Error(
          "Trình duyệt không hỗ trợ ghi âm. Bạn có thể chọn file audio có sẵn.",
        );
      if (!current() || document.hidden)
        throw new Error(
          "Chỉ ghi âm khi trang đang hiện và tài khoản đã được xác minh.",
        );
      const mime = [
        "audio/webm;codecs=opus",
        "audio/ogg;codecs=opus",
        "audio/mp4",
      ].find((type) => MediaRecorder.isTypeSupported(type));
      if (!mime)
        throw new Error(
          "Trình duyệt không có định dạng ghi âm phù hợp. Hãy chọn file audio có sẵn.",
        );
      stream = await navigator.mediaDevices.getUserMedia({
        audio: true,
        video: false,
      });
      if (!current() || document.hidden) {
        stream.getTracks().forEach((track) => track.stop());
        if (alive.current && generation.current === epoch) setPhase("idle");
        return;
      }
      const recorder = new MediaRecorder(stream, {
        mimeType: mime,
        audioBitsPerSecond: 96000,
      });
      const chunks: Blob[] = [];
      let bytes = 0;
      let failed = false;
      const active = {
        recorder,
        stream,
        timer: setTimeout(() => stop(), recordingLimitMs),
        discard: false,
      };
      capture.current = active;
      recorder.ondataavailable = (event) => {
        if (!current()) {
          active.discard = true;
          stop();
          return;
        }
        if (event.data.size) {
          chunks.push(event.data);
          bytes += event.data.size;
        }
        if (bytes > 20 * 1024 * 1024) {
          failed = true;
          setError(new Error("Ghi âm vượt20 MiB. Hãy ghi đoạn ngắn hơn."));
          stop();
        }
      };
      recorder.onerror = () => {
        failed = true;
        if (alive.current)
          setError(
            new Error(
              "Trình duyệt không thể tiếp tục ghi âm. Bản lỗi không được lưu tự động.",
            ),
          );
        stop();
      };
      recorder.onstop = () => {
        clearTimeout(active.timer);
        active.stream.getTracks().forEach((track) => track.stop());
        if (capture.current === active) capture.current = undefined;
        if (!alive.current || generation.current !== epoch) return;
        if (active.discard || !current()) {
          setPhase("idle");
          return;
        }
        if (failed || !bytes) {
          setPhase("error");
          if (!failed)
            setError(new Error("Chưa thu được âm thanh. Hãy thử lại."));
          return;
        }
        const type = (recorder.mimeType || mime).split(";")[0];
        const extension =
          type === "audio/mp4" ? "m4a" : type === "audio/ogg" ? "ogg" : "webm";
        setFile(
          new File(
            chunks,
            `recording-${new Date().toISOString().replaceAll(":", "-")}.${extension}`,
            { type },
          ),
        );
        setPhase("ready");
      };
      recorder.start(1000);
      setPhase("recording");
    } catch (failure) {
      stream?.getTracks().forEach((track) => track.stop());
      const active = capture.current;
      if (active) {
        clearTimeout(active.timer);
        active.discard = true;
        capture.current = undefined;
      }
      if (alive.current && generation.current === epoch) {
        setError(failure as Error);
        setPhase("error");
      }
    } finally {
      if (generation.current === epoch) pending.current = false;
    }
  };
  return { phase, file, error, start, stop, discard };
}

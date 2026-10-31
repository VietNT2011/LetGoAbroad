import { useEffect } from "react";
import { api, ApiError, post } from "./api";

type Session = { id: string; lastSequence: number; isExpired: boolean };
type Pulse = { sequence: number; isActive: boolean };
type Ack = { sequence: number };
export type StudyKind = "lesson" | "assessment" | "cards";

/** VI: Chỉ gửi trạng thái hoạt động, không gửi nội dung phím, URL hay vị trí con trỏ. EN: Send activity state only, never typed text, route URLs or pointer coordinates. */
export function useStudyActivity(
  kind: StudyKind | null,
  pageKey: string,
  ownerId?: string,
) {
  useEffect(() => {
    if (!kind || !ownerId) return;
    let session: Session | undefined;
    let pending: Pulse | undefined;
    let clientSessionId = crypto.randomUUID();
    let sequence = 0;
    let lastInput = performance.now();
    let lastActive = false;
    let busy = false;
    let dirty = false;
    let stopped = false;
    const isActive = () =>
      document.visibilityState === "visible" &&
      document.hasFocus() &&
      (performance.now() - lastInput < 90_000 ||
        Array.from(
          document.querySelectorAll<HTMLMediaElement>("audio,video"),
        ).some((media) => !media.paused && !media.ended));
    const pulse = (body: Pulse) =>
      api<Ack>(`/learning/study-sessions/${session!.id}/heartbeat`, {
        method: "POST",
        body: JSON.stringify(body),
        keepalive: true,
      });
    const close = () => {
      if (session && lastActive) {
        lastActive = false;
        // VI: Best-effort khi rời trang; không bù thời gian nếu request không tới server. EN: Departure is best-effort and missing delivery never backfills time.
        void pulse({ sequence: sequence + 1, isActive: false }).catch(() => {});
      }
    };
    const tick = async () => {
      if (stopped) return;
      if (busy) {
        dirty = true;
        return;
      }
      const active = isActive();
      if (!pending && !active && !lastActive) return;
      busy = true;
      dirty = false;
      let acknowledged = false;
      try {
        if (!session) {
          session = await post<Session>("/learning/study-sessions", {
            clientSessionId,
            kind,
          });
          if (stopped) return;
          if (session.isExpired) {
            session = undefined;
            clientSessionId = crypto.randomUUID();
            return;
          }
          sequence = session.lastSequence;
        }
        // VI: Chỉ một request đang gửi; lỗi mạng giữ nguyên sequence/payload để retry. EN: One in-flight request retains the exact pending sequence and payload after an unknown network outcome.
        pending ??= { sequence: sequence + 1, isActive: active };
        const sent = pending;
        const ack = await pulse(sent);
        sequence = ack.sequence;
        lastActive = sent.isActive;
        pending = undefined;
        acknowledged = true;
      } catch (error) {
        if (
          error instanceof ApiError &&
          [404, 409, 422].includes(error.status)
        ) {
          session = undefined;
          pending = undefined;
          lastActive = false;
          sequence = 0;
          clientSessionId = crypto.randomUUID();
        }
        // VI: Số đo không chặn học; lần timer sau thử lại, không có queue offline vô hạn. EN: Measurement failures do not block studying; the next timer retries without an unbounded offline queue.
      } finally {
        busy = false;
        if (stopped) close();
        else if (acknowledged && dirty && !pending) void tick();
      }
    };
    const input = () => {
      lastInput = performance.now();
      if (!lastActive) void tick();
    };
    const stateChanged = () => {
      void tick();
    };
    window.addEventListener("pointerdown", input, { passive: true });
    window.addEventListener("keydown", input, { passive: true });
    window.addEventListener("focus", stateChanged);
    window.addEventListener("blur", stateChanged);
    document.addEventListener("visibilitychange", stateChanged);
    const timer = window.setInterval(stateChanged, 15_000);
    void tick();
    return () => {
      stopped = true;
      window.clearInterval(timer);
      window.removeEventListener("pointerdown", input);
      window.removeEventListener("keydown", input);
      window.removeEventListener("focus", stateChanged);
      window.removeEventListener("blur", stateChanged);
      document.removeEventListener("visibilitychange", stateChanged);
      if (!busy) close();
    };
  }, [kind, pageKey, ownerId]);
}

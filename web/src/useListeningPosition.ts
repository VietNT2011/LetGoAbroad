import { useEffect, useRef, useState } from "react";
import { api, ApiError } from "./api";

export type PositionAck = {
  sessionId: string;
  positionMs: number;
  etag: string;
};
type Pending = {
  clientRequestId: string;
  expectedEtag: string;
  positionMs: number;
};

/** VI: Xếp hàng vị trí mới nhất nhưng retry giữ nguyên request chưa có ACK; session cũ không tự chiếm head mới. EN: Coalesce desired positions while preserving unknown requests and never reclaim a different current session. */
export function useListeningPosition(
  id: string,
  initial: PositionAck,
  duration: number,
  getPosition: () => number,
  enabled: boolean,
) {
  const state = useRef({
    etag: initial.etag,
    pending: undefined as Pending | undefined,
    desired: undefined as number | undefined,
    running: false,
    conflict: false,
  });
  const life = useRef<AbortController | undefined>(undefined);
  const allowed = useRef(enabled);
  allowed.current = enabled;
  const read = useRef(getPosition);
  read.current = getPosition;
  const [saved, setSaved] = useState(initial.positionMs);
  const [error, setError] = useState<Error>();
  const [sending, setSending] = useState(false);
  const pump = async () => {
    const queue = state.current;
    const controller = life.current;
    if (!controller || queue.running || queue.conflict || !allowed.current)
      return;
    queue.running = true;
    setSending(true);
    setError(undefined);
    try {
      while (
        (queue.pending || queue.desired !== undefined) &&
        !controller.signal.aborted &&
        allowed.current
      ) {
        if (!queue.pending) {
          queue.pending = {
            clientRequestId: crypto.randomUUID(),
            expectedEtag: queue.etag,
            positionMs: queue.desired!,
          };
          queue.desired = undefined;
        }
        const ack = await api<PositionAck>(`/listening/${id}/position`, {
          method: "PUT",
          body: JSON.stringify(queue.pending),
          signal: controller.signal,
          keepalive: true,
        });
        if (controller.signal.aborted || life.current !== controller) return;
        queue.etag = ack.etag;
        queue.pending = undefined;
        setSaved(ack.positionMs);
        if (queue.desired === ack.positionMs) queue.desired = undefined;
      }
    } catch (failure) {
      if (controller.signal.aborted || life.current !== controller) return;
      setError(failure as Error);
      if (
        failure instanceof ApiError &&
        (failure.status === 412 ||
          ["listening_session_changed", "listening_expired"].includes(
            failure.code,
          ))
      )
        queue.conflict = true;
    } finally {
      queue.running = false;
      if (!controller.signal.aborted && life.current === controller)
        setSending(false);
    }
  };
  const save = (position = read.current()) => {
    if (
      !Number.isFinite(position) ||
      !allowed.current ||
      state.current.conflict
    )
      return;
    state.current.desired = Math.max(
      0,
      Math.min(duration, Math.round(position)),
    );
    void pump();
  };
  const retry = () => {
    void pump();
  };
  useEffect(() => {
    const controller = new AbortController();
    life.current = controller;
    const leave = () => save(read.current());
    window.addEventListener("pagehide", leave);
    return () => {
      window.removeEventListener("pagehide", leave);
      controller.abort();
      if (life.current === controller) life.current = undefined;
    };
  }, [id]);
  return {
    save,
    retry,
    saved,
    error,
    sending,
    conflict: state.current.conflict,
  };
}

export type SessionBinding = {
  actor: string | null | undefined;
  epoch: number;
  checking: boolean;
};
let binding: SessionBinding = { actor: undefined, epoch: 0, checking: true };
const listeners = new Set<() => void>();
export const sessionBinding = () => binding;
/** VI: Đổi danh tính hủy cả response cũ, kể cả khi đăng nhập lại cùng ID. EN: Explicit identity changes invalidate old responses even when signing back into the same ID. */
export function suspendSession(clear = false) {
  binding = {
    actor: clear ? undefined : binding.actor,
    epoch: binding.epoch + (clear ? 1 : 0),
    checking: true,
  };
}
export function confirmSession(actor: string | null) {
  binding = {
    actor,
    epoch: binding.epoch + (binding.actor !== actor ? 1 : 0),
    checking: false,
  };
}
export function invalidateSession(expected: SessionBinding) {
  if (binding.epoch !== expected.epoch || binding.actor !== expected.actor)
    return;
  suspendSession(true);
  for (const listener of listeners) listener();
}
export function onSessionInvalidated(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export const authSignalKey = "englishhub.auth.changed.v1";
/** VI: Chỉ gửi nonce, không gửi tài khoản/token; focus là fallback khi browser chặn kênh. EN: Broadcast only a nonce, never identity or tokens; focus revalidation also works when channels are unavailable. */
export function authSignals(onChange: () => void) {
  const seen = new Set<string>();
  const remember = (nonce: string) => {
    seen.add(nonce);
    if (seen.size > 64) seen.delete(seen.values().next().value!);
  };
  let channel: BroadcastChannel | undefined;
  const consume = (packet: unknown) => {
    if (!packet || typeof packet !== "object") return;
    const value = packet as { version?: unknown; nonce?: unknown };
    if (
      value.version !== 1 ||
      typeof value.nonce !== "string" ||
      value.nonce.length > 80 ||
      seen.has(value.nonce)
    )
      return;
    remember(value.nonce);
    onChange();
  };
  try {
    channel = new BroadcastChannel(authSignalKey);
    channel.onmessage = (event) => consume(event.data);
  } catch {
    /* VI: Storage/focus vẫn dùng được. EN: Storage/focus remain available. */
  }
  const storage = (event: StorageEvent) => {
    if (event.key !== authSignalKey || !event.newValue) return;
    try {
      consume(JSON.parse(event.newValue));
    } catch {
      /* VI: Bỏ dữ liệu ngoài không hợp lệ. EN: Ignore malformed external values. */
    }
  };
  window.addEventListener("storage", storage);
  return {
    publish() {
      const packet = { version: 1, nonce: crypto.randomUUID() };
      remember(packet.nonce);
      try {
        channel?.postMessage(packet);
      } catch {
        /* VI: Thử storage bên dưới. EN: Storage fallback below. */
      }
      try {
        localStorage.setItem(authSignalKey, JSON.stringify(packet));
      } catch {
        /* VI: Vẫn xác minh lúc focus. EN: Focus fallback remains. */
      }
    },
    dispose() {
      channel?.close();
      window.removeEventListener("storage", storage);
    },
  };
}

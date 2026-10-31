import {
  invalidateSession,
  sessionBinding,
  type SessionBinding,
} from "./authSession";

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
  ) {
    super(message);
  }
}
let csrf: Promise<string> | undefined;
// VI: Token nằm trong bộ nhớ, đổi sau login/logout. EN: Refresh the in-memory, identity-bound token after authentication changes.
export function resetCsrf() {
  csrf = undefined;
}
async function csrfToken(): Promise<string> {
  if (csrf) return csrf;
  const pending = fetch("/api/v1/auth/csrf", { credentials: "same-origin" })
    .then(async (r) => {
      if (!r.ok)
        throw new ApiError(
          r.status,
          "csrf_unavailable",
          "Không thể kết nối máy chủ.",
        );
      return (await r.json()).requestToken as string;
    })
    .catch((error) => {
      if (csrf === pending) csrf = undefined;
      throw error;
    });
  csrf = pending;
  return pending;
}
const unboundPaths = new Set([
  "/auth/csrf",
  "/auth/login",
  "/auth/register",
  "/auth/verify-email",
  "/auth/forgot-password",
  "/auth/reset-password",
  "/status",
]);
const sessionError = (code = "session_changed") =>
  new ApiError(
    0,
    code,
    "Phiên đăng nhập đang được kiểm tra. Vui lòng thử lại khi trang sẵn sàng.",
  );
function assertSession(expected: SessionBinding, beforeSend: boolean) {
  const current = sessionBinding();
  if (
    expected.actor === undefined ||
    current.epoch !== expected.epoch ||
    current.actor !== expected.actor
  )
    throw sessionError();
  if (beforeSend && current.checking)
    throw sessionError("session_revalidating");
}
/** VI: Ghim actor trước mọi await và giữ nguyên qua CSRF retry. EN: Freeze the confirmed actor before any await and retain it across token retries. */
async function request<T>(
  path: string,
  options: RequestInit = {},
  identityProbe = false,
): Promise<T> {
  const guarded =
    !identityProbe && !unboundPaths.has(path.split("?")[0].replace(/\/$/, ""));
  const expected = sessionBinding();
  const method = (options.method ?? "GET").toUpperCase();
  for (let attempt = 0; attempt < 2; attempt++) {
    if (guarded) assertSession(expected, true);
    const headers = new Headers(options.headers);
    if (guarded)
      headers.set("X-EnglishHub-Actor", expected.actor ?? "anonymous");
    if (options.body && !(options.body instanceof FormData))
      headers.set("Content-Type", "application/json");
    if (!["GET", "HEAD"].includes(method))
      headers.set("X-CSRF-TOKEN", await csrfToken());
    if (guarded) assertSession(expected, true);
    const response = await fetch(`/api/v1${path}`, {
      ...options,
      method,
      headers,
      credentials: "same-origin",
      cache: "no-store",
    });
    const payload =
      response.status === 204
        ? undefined
        : await response.json().catch(() => undefined);
    if (guarded) assertSession(expected, false);
    if (!response.ok) {
      if (
        guarded &&
        (response.status === 401 || payload?.code === "session_changed")
      ) {
        invalidateSession(expected);
        throw sessionError();
      }
      if (payload?.code === "invalid_csrf" && attempt === 0) {
        resetCsrf();
        continue;
      }
      throw new ApiError(
        response.status,
        payload?.code ?? "request_failed",
        payload?.detail ?? "Không thể thực hiện. Vui lòng thử lại.",
      );
    }
    if (response.status !== 204 && payload === undefined)
      throw new ApiError(
        response.status,
        "invalid_response",
        "Phản hồi máy chủ không hợp lệ.",
      );
    return payload as T;
  }
  throw new Error("Unreachable request retry.");
}
export const api = <T>(path: string, options: RequestInit = {}) =>
  request<T>(path, options);
// VI: Chỉ AuthProvider dùng probe không gắn actor; màn hình so sánh hồ sơ vẫn gọi api có guard. EN: Only the provider probes identity without an actor; ordinary profile comparisons remain guarded.
export const currentUser = (signal: AbortSignal) =>
  request<User>("/auth/me", { signal }, true);
export const post = <T>(path: string, body: unknown = {}) =>
  api<T>(path, { method: "POST", body: JSON.stringify(body) });
export type User = {
  id: string;
  email: string;
  displayName: string;
  timeZoneId: string;
  targetScore: number;
  minutesPerDay: number;
  studyDays: string;
  profileEtag: string;
  roles: string[];
};
export type Content = {
  id: string;
  rootId: string;
  slug: string;
  kind: "lesson" | "assessment";
  title: string;
  summary: string;
  skill: string;
  minutes: number;
  durationSeconds: number;
  isFixture: boolean;
  version: number;
  questionCount: number;
};

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message); }
}
let csrf: Promise<string> | undefined;
// VI: Token nằm trong bộ nhớ, đổi sau login/logout. EN: Refresh the in-memory, identity-bound token after authentication changes.
export function resetCsrf() { csrf = undefined; }
async function csrfToken(): Promise<string> {
  csrf ??= fetch('/api/v1/auth/csrf', { credentials: 'same-origin' }).then(async r => {
    if (!r.ok) throw new ApiError(r.status, 'csrf_unavailable', 'Không thể kết nối máy chủ.');
    return (await r.json()).requestToken as string;
  }).catch(error => { csrf = undefined; throw error; });
  return csrf;
}
/** VI: Chỉ báo thành công sau ACK. EN: Resolve only after server acknowledgement. */
export async function api<T>(path: string, options: RequestInit = {}, retryCsrf = true): Promise<T> {
  const method = options.method ?? 'GET'; const headers = new Headers(options.headers);
  if (options.body && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  if (!['GET', 'HEAD'].includes(method)) headers.set('X-CSRF-TOKEN', await csrfToken());
  const response = await fetch(`/api/v1${path}`, { ...options, method, headers, credentials: 'same-origin' });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    if (error.code === 'invalid_csrf' && retryCsrf) { resetCsrf(); return api(path, options, false); }
    throw new ApiError(response.status, error.code ?? 'request_failed', error.detail ?? 'Không thể thực hiện. Vui lòng thử lại.');
  }
  return response.status === 204 ? undefined as T : response.json();
}
export const post = <T>(path: string, body: unknown = {}) => api<T>(path, { method: 'POST', body: JSON.stringify(body) });
export type User = { id: string; email: string; displayName: string; timeZoneId: string; targetScore: number; minutesPerDay: number; studyDays: string; roles: string[] };
export type Content = { id: string; rootId: string; slug: string; kind: 'lesson' | 'assessment'; title: string; summary: string; skill: string; minutes: number; durationSeconds: number; isFixture: boolean; version: number; questionCount: number };

# Session consistency / Đồng bộ tài khoản giữa các tab

VI: Khi một tab đăng nhập/đăng xuất, các tab khác khóa thao tác và bỏ giao diện riêng cũ trước khi xác minh tài khoản hiện tại. Khi quay lại cửa sổ/trang đang ẩn hoặc khôi phục từ back-forward cache, app cũng xác minh lại. Nếu vẫn cùng tài khoản/quyền, bản nháp được giữ; nếu tài khoản hoặc tập quyền thay đổi, cây React riêng được tạo lại. Không lưu bản nháp riêng vào localStorage để chuyển giữa tài khoản.

## Requests / Điều kiện request

The shared API transport captures the confirmed actor and identity epoch **before** awaiting CSRF. Every ordinary request includes `X-EnglishHub-Actor` with that UUID (or `anonymous`); retries keep the same actor. A request cannot begin while identity is being checked, and old responses cannot populate a later identity. Identity changes use local status0 consistency errors rather than business409 conflicts, so review/answer retry payloads are not discarded as if the server rejected their business event.

The server compares the supplied actor with the authenticated cookie principal **before** CSRF validation and endpoint execution. Mismatch returns409 `session_changed`; malformed/multiple values return400 `invalid_actor_precondition`. The header never authenticates anyone, chooses an owner or bypasses roles, CSRF, ETags or receipt policies. If A's old CSRF fails after the cookie changes to B, the original A payload cannot be retried under B merely by obtaining B's token.

Browser writes identified by `Origin` or `Sec-Fetch-Mode` must include the precondition; an older JS client receives428 `identity_precondition_required` and must reload. Unmarked HTTP API clients retain their existing cookie/CSRF contract. Native media GETs and direct API reads without custom headers remain possible; this compatibility detection is not an authentication boundary. The new JS attaches and enforces the precondition for ordinary reads as well as writes.

Login/register/email/password flows and CSRF/status discovery are unbound. Provider bootstrap may read `/auth/me` without the actor, while an ordinary profile-comparison read supplies the actor and is checked. Logout is guarded so an old A tab cannot sign out B. API responses that reach the middleware use `Cache-Control: no-store`; this does not revoke already-issued media grants or clear another browser's memory.

## Tabs and failures / Kênh và lỗi mạng

[BroadcastChannel](https://developer.mozilla.org/en-US/docs/Web/API/BroadcastChannel) and storage-event fallback send only a version and random nonce, with bounded replay deduplication. No account ID, email, password or token is broadcast/stored. Profile refresh does not announce a login/logout. Focus, visibility and persisted-pageshow revalidation also work without either messaging mechanism; there is no polling or promise of instant detection in a suspended browser.

Probes have AbortController plus generation checks on success/failure, including fetch implementations that ignore cancellation. An obsolete bootstrap cannot restore an old identity. On routine checks, the private subtree is hidden and inert while retaining same-owner drafts. Network failure keeps it hidden with a retry action; it is not treated as evidence of logout. An explicit remote change immediately removes the private subtree. Transport is unlocked in a layout effect only after React commits the verified subtree. This does not provide offline private-page reading while verification is unavailable.

## Verification / Kiểm chứng

Nine API cases cover matching actors, unchanged ownership/roles/CSRF, old-cookie versus fresh/stale tokens without writes, legacy-browser reload, malformed values, guarded `/me` and logout, and native GET compatibility. Initial fixture failures omitted required card request IDs, assumed an empty HttpClient header was sent, and assumed media was configured in the non-storage profile; fixtures were corrected before passing.

Twenty frontend tests use [React `act`](https://react.dev/reference/react/act), JSDOM and simulated peer messages. They cover token-await and retry races, stale success/failure, request suspension,401/409 revalidation, logout binding, immediate private-state removal, same-owner draft preservation, role changes, network retry, storage fallback/deduplication, nonce-only announcements and StrictMode cleanup. CI now runs frontend tests before the production build. These are simulated DOM/transport cases, not native multi-browser/keyboard/mobile acceptance; Computer Use remains stopped after the user's Escape.

No schema migration, learner-progress backfill or external service is introduced. See [verification](verification.md) for complete-suite results and [status](status.md) for remaining product work.

VI: Khi xác minh lại cùng tài khoản, app trả focus về ô đang viết sau khi cây riêng hiện lại, giữ selection của control đang tồn tại và không cuộn trang. Chỉ giữ tham chiếu DOM trong bộ nhớ; không sao chép nội dung nhập. Node cũ đã tháo khi đổi tài khoản/quyền không được focus lại. Hai kiểm thử DOM bổ sung xác nhận focus/selection cùng chủ và không chuyển focus vào editor của tài khoản khác. JSDOM mô phỏng blur do ẩn cây; hành vi focus native trên thiết bị vẫn cần nghiệm thu.

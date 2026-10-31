# Profile concurrency / Xung đột cấu hình cá nhân

Settings keeps the version of the form initially opened. A later authentication refresh does not silently replace that baseline while the learner is editing.

`GET /api/v1/auth/me` returns an opaque `profileEtag`. `PUT /api/v1/profile` requires it as `expectedEtag` in the JSON command. Missing versions return **428**. A stale command that would change persisted preferences returns **412** with `profile_conflict`. The successful response is **200** with `{ "etag": "..." }` for the acknowledged version.

Version comparison, the Identity update and any goal revision/outbox changes use the same `learning:<user>` transaction. A rejected stale edit cannot partially change the display name, timezone, study days, target or time budget. Identity concurrency failures also become 412 and roll back the transaction. Existing tasks and results remain unchanged.

An exact retry whose complete normalized preferences already match the server is a no-op and receives the current version, even if its supplied version is older. Normalization trims the display name, canonicalizes Windows timezone IDs and sorts/deduplicates study days. This exception acknowledges a lost successful response without appending another revision; it never applies a different stale draft.

## Browser recovery

After 412/428, the form retains its fields and disables submission. **Tải cấu hình để so sánh** reads the current owner profile and shows it beside the retained form. **Dùng phiên bản mới, giữ bản đang nhập** explicitly adopts that compared version. The learner can then adjust the retained fields and press Save. If another write wins during comparison, the next save can conflict again; the UI never retries a changed draft automatically.

Comparison is read-only. No action occurs until the learner submits. Drafts survive request failures within the current mounted page; reloading or leaving the page does not persist the unsaved draft.

The token derives from Identity's concurrency stamp, not its security stamp or password hash. It does not authorize another account and does not replace authentication or CSRF. Other Identity updates can invalidate a form version and require comparison.

This protects profile editing across tabs. Effective-date timezone/study-day revision history and changes to a pinned path calendar remain separate work; the existing goal revision policy still covers target score and minutes only.

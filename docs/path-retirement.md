# Template admission lifecycle / Ngừng và mở lại đăng ký mẫu

VI: Admin chọn **Ngừng đăng ký mẫu mới** trong CMS để ẩn toàn bộ template khỏi danh sách nhận đăng ký. Cả enrollment mới chọn version trực tiếp và thao tác áp dụng mẫu cho enrollment legacy đều kiểm tra trạng thái trên server. Người đã ghim một version vẫn đọc lộ trình, nhận nhiệm vụ, làm quiz/practice và giữ tiến độ như trước.

EN: `PathTemplate.RetiredAt` is admission metadata outside the immutable published payload. Retirement keeps the current publication pointer, versions, node IDs, grading sources and enrollments intact. Restoring the template reopens discovery/admissions for its current published version. The original publication's ETag and graph do not change.

## Commands and consistency

- `GET /api/v1/admin/path-versions/{id}` returns separate `etag` (version) and `templateEtag` (admission metadata), plus `retiredAt`. The CMS version list identifies retired templates.
- `POST /api/v1/admin/paths/{templateId}/retire` and `/restore` accept `clientRequestId` and `expectedEtag` for the template. Only Admin with authenticated cookie and CSRF can change admission state.
- A stale template ETag returns 412. Payload-bound receipts return the original acknowledgement for exact retries. Retrying an old retirement after restoration returns that old acknowledgement without retiring the template again. Reusing its command ID for another action/input returns 409.
- State, audit entry and receipt commit atomically. A current-version request for an already-matching state is a no-op with an acknowledgement. It does not duplicate audit effects.
- New enrollments/adoptions acquire the `path-catalog` transaction lock after their `learning:<user>` lock. Retirement/publication acquires `path-catalog` only. An admission racing retirement either pins before closure or is rejected after closure; an existing pinned enrollment remains valid.
- Draft creation/editing/validation remain available. Publishing another version requires restoring admissions first. Publication then follows the normal DAG/content checks and preserves older pins.

VI: CMS giữ nguyên JSON đang soạn khi đổi trạng thái nhận đăng ký. Sau ACK, giao diện chỉ đọc lại metadata mới nhất để tránh coi một receipt cũ là trạng thái hiện tại. Xung đột giữ bản đang soạn và yêu cầu người dùng tải version muốn xem; không tự ghi đè draft.

EN: This lifecycle manages future admissions. It preserves access for existing pinned learners; content takedown, deleting histories and switching an enrollment to a different template require separate policies. When no matching active template exists, the existing optional legacy/catalog enrollment fallback remains; no retired template is silently pinned.

Relational tests cover discovery/direct-ID exclusion, legacy adoption, continued learning on an existing pin, immutable graph/ETag, restore and replay after restore, publication guards, Admin/CSRF enforcement, competing writes and enrollment/retirement races.

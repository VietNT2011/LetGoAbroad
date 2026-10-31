# Scheduled path choices / Chuyển mẫu và lịch sử lựa chọn

VI: Người đã ghim mẫu có thể chọn mẫu/version đang nhận đăng ký và ghi lý do chuyển tại trang Lộ trình. Lựa chọn mới có hiệu lực ở nửa đêm hợp lệ tiếp theo theo múi giờ hồ sơ tại yêu cầu đầu tiên. Lịch hôm nay, task, câu trả lời, deadline, mục tiêu và ngân sách phút đang có được giữ nguyên.

EN: Each choice appends a `PathChoiceRevision` containing the exact version, level, original placement ID, selection/user reason, calendar/start snapshot, observation time and effective instant. The original placement is retained after a manual override. New enrollments record their initial choice; legacy adoption records an immediate transition from an observed baseline. Existing databases receive a baseline at the actual observation time, without fabricating a past choice timestamp or backfilling old task pins.

## Boundary, replacement and cancellation

- The first pending request fixes an effective UTC instant and its scheduling timezone. Later choices before that boundary reuse it, even when profile timezone preferences change. Earlier choices at the boundary remain in history as superseded.
- Choosing the latest requested version again is a no-op. It does not append another reason/choice. A different choice requires the currently observed history token.
- **Giữ lộ trình đang học** appends a cancellation snapshot at the reserved boundary. It copies the active calendar/start unchanged. Cancelling a pending switch does not restart the current path, including when its template has since retired admissions.
- At or after the boundary, the next dashboard, path plan, history or regeneration command activates the latest due revision under the owner lock. A single observed activation timestamp and outbox event record activation. The operation uses one observation instant across its boundary decisions.
- A first activation of a target version starts its suggested study-day calendar at the scheduled date. Returning to a previously activated version resumes its stored start/timezone/weekdays. A reservation that was cancelled before activation cannot count as a previously used calendar.
- Revision payloads are retained. `ActivatedAt` is one-time lifecycle metadata, recording when activation was observed, rather than an invented historical interaction.

## Evidence and existing tasks

VI: Tiến độ quiz dùng đúng `ContentVersion` tiếp tục áp dụng cho lesson node cùng phiên bản. Practice cần bằng chứng cho đúng node và task đã ghim; kết quả của node ở mẫu cũ không tự hoàn thành node khác ở mẫu mới. Khi quay lại cùng version, các node đã có bằng chứng vẫn giữ tiến độ. Không tạo một kết quả mới để mô phỏng việc chuyển mẫu.

EN: Existing tasks keep their path/node/content/timezone/goal snapshots. A live old attempt crossing midnight remains on the dashboard with its original deadline; completion is credited to its old source. It can temporarily delay new assignments under the existing carryover/budget policy. Unfinished lessons without a live assessment still follow the existing missed-task rollover policy. This feature does not persist an unsent lesson quiz draft or provide offline synchronization.

## Admission, concurrency and replay

- `GET /api/v1/learning/path-revisions`: private owner history, up to twenty newest rows plus the active row when older. Includes `expectedEtag`, effective dates, reasons and original placement reference.
- `POST /api/v1/learning/enrollment/switch-path`: `clientRequestId`, `pathVersionId`, `expectedEtag`, nonblank `reason` of at most 500 UTF-16 units. Authentication and CSRF are required.
- Choices use `learning:<user>` then `path-catalog` locks, matching admission lock order. The target must be published and its template open when reserved. Retirement after a successful reservation preserves it; new reservations to that retired template are rejected.
- The token binds owner enrollment, active revision and latest choice. Concurrent different choices from one snapshot have one winner; a stale command returns 412. The UI retains its target/reason, loads history for comparison and requires explicit acceptance before another write.
- Exact payload-bound retries return their original acknowledgement, including after activation or subsequent choices. They cannot reinstate an old superseded request. Reusing a command ID with another payload returns 409. The UI refreshes history after acknowledgement to show current state.

VI: Đây là workflow chuyển mẫu; lịch tuần/múi giờ của một path đang ghim vẫn cần calendar revision riêng. Core/Exam, phân bổ review/weak-skill, nội dung nghe và các tiêu chí P0 còn lại được theo dõi trong [status](status.md).

EN: Relational tests cover unchanged daily tasks/budgets, exact boundaries, cancellation without resetting calendars, lesson carryover and separate practice evidence, live attempts over midnight, stale tabs, payload replay, owner/FK isolation, publication/retirement reservations, legacy observation/adoption, original placement history, return to an activated calendar and cancelled reservations.

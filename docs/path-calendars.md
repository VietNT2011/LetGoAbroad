# Effective path calendars / Lịch riêng có ngày hiệu lực

VI: Trên trang Lộ trình, người đã ghim mẫu có thể đặt múi giờ, ngày trong tuần và lý do cho lịch riêng. Trước khi bật lịch riêng, nhiệm vụ vẫn dùng lịch Hồ sơ như trước; ngày học gợi ý của mẫu giữ calendar ban đầu. Sau khi áp dụng lịch riêng, cả bộ chọn nhiệm vụ và ngày học gợi ý dùng calendar đã chọn. Thay đổi Hồ sơ không âm thầm ghi đè lịch riêng, và lệnh calendar không sửa hồ sơ Identity.

EN: This is an explicit calendar for a pinned path. Legacy unpinned enrollments must adopt a version first. Profile preferences remain available for initial selections. Existing published template graphs, content versions, goals, assessment deadlines and results are untouched.

## Boundary and study-day prefix

- The first calendar request uses the next valid midnight in the **currently pinned path timezone**, handling skipped/ambiguous midnights through the existing boundary helper. The scheduling timezone differs from the requested new timezone.
- Replacement requests reuse the reserved UTC instant and scheduling timezone. They cannot keep postponing activation by choosing another timezone.
- A `calendar_change` choice stores the exact timezone/weekdays, original path start, new `CalendarAnchorOn` and `CalendarElapsedDays`. The prefix counts suggested study dates before the boundary under the old calendar; only dates from the new anchor use the new weekdays. A later edit preserves that accumulated prefix. These are suggested study-day ordinals, not completed lessons, attendance, streaks or score evidence.
- Activation at the next dashboard/path/history/regeneration read changes the enrollment pointer once. Late observation still counts the new suffix from its scheduled anchor, rather than restarting the calendar at login.
- **Giữ lịch đang áp dụng** appends `calendar_cancel` at the same reserved instant, copying the active calendar, prefix and mode. It retains the history of the replaced request. After activation, another change applies at a future boundary.

## Existing assignments and timezone changes

VI: Nhiệm vụ hôm nay không đổi trước boundary. Mỗi nhiệm vụ mới của path ghim `PathChoiceRevisionId`; FK ghép với EnrollmentId ngăn dùng revision của người khác. Task cũ trước migration giữ giá trị null, không được gán lịch sử giả.

EN: Pending assignments remain visible even when a timezone change makes their original local date later than the new current date. They block duplicate core-plan allocation and retain their source timezone/deadline; missed tasks still use the existing rollover rules. Completion records use the task's original timezone. Existing per-enrollment/date slot and budget rules remain conservative when a local date repeats: changing timezone does not reset an existing day's plan.

## Path switching, history and replay

- Calendar choices reuse the owner-scoped path-choice history and token. Original placement IDs, reasons and activated calendars remain available when returning to an earlier version.
- A pending switch to another path must first be cancelled before editing the current calendar. A pending calendar change must first be cancelled before switching paths. The API returns 409 for incompatible pending intents instead of silently dropping either request; UI explains the required action.
- A cancellation with no actual pending calendar is a no-op. Repeated normalized settings are a no-op. Weekdays are deduplicated and sorted; supported Windows timezone IDs are canonicalized to IANA IDs.
- Retiring admissions does not block calendar edits for an existing pin. No new template admission is performed.
- `POST /api/v1/learning/enrollment/calendar`: `clientRequestId`, `expectedEtag`, `timeZoneId`, `studyDays` (1–7 values from 0 to 6), nonblank `reason` up to 500 UTF-16 units, optional `cancelPending`. Cancellation ignores timezone/weekdays and uses the server's active snapshot.
- Authentication, CSRF and the learning owner lock are required. A stale token returns 412. The browser retains both calendar and template drafts, loads fresh history for comparison and requires explicit acceptance before another write.
- Payload-bound receipt scope `path-calendar` retains exact acknowledgements across activation and later edits; a changed payload with the same UUID returns 409. Retrying an old request cannot restore a superseded calendar.

VI: Các kiểm thử có clock bao phủ prefix nhiều lần đổi lịch, áp dụng muộn, thay/hủy, cạnh tranh hai tab, ownership/CSRF, canonical timezone, pin đã retirement, ưu tiên lịch riêng, quay lại version và task còn chạy khi ngày địa phương lùi lại. Phạm vi chưa gồm đồng bộ offline, calendar riêng cho enrollment chưa ghim, SRS hoặc thước đo streak/activity; xem [status](status.md).

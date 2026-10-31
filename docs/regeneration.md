# Daily plan regeneration / Lập lại nhiệm vụ hôm nay

VI: Dashboard có thao tác **Lập lại bài chưa bắt đầu**. Người học giữ nguyên nhiệm vụ đã hoàn thành, nhiệm vụ đã mở từ dashboard và nhiệm vụ có attempt bắt đầu từ lúc được giao. Việc mở không ghi hoàn thành; quiz/kết quả hợp lệ vẫn là nguồn bằng chứng duy nhất.

EN: Assignment starts, assessment starts, lesson completion, dashboard generation and regeneration share the `learning:<user>` transaction lock. Starting an assessment from outside the dashboard also marks matching pending assignments started. This prevents a new attempt from racing with replacement. Legacy attempts without a task-start timestamp are detected from persisted attempt history.

Regeneration reads fresh profile preferences after acquiring that lock. A user entity tracked earlier by cookie validation cannot hide a calendar change committed by another request; the old plan token is rejected instead of applying the cached calendar.

## Selection and preservation

- Reconcile durable completions and midnight carryover before comparing the loaded plan version. A stale request returns **412** and rolls back incidental reconciliation; the learner refreshes before deliberately requesting again.
- Reserve protected assignments' time and slots first, including a live carryover from an earlier date. Select at most three total assignments, using the same path/prerequisite/whole-content selection as the initial dashboard plan.
- New selections must fit the remaining active minute budget and exclude protected content. Already-started carryover is preserved even if it consumes more than a newly reduced budget; no extra work is added in that case. Regeneration does not apply a future goal revision early or extend attempt deadlines.
- When selection changes, unstarted rows become `superseded`. New rows receive fresh IDs and increasing slot keys within the day. The unique enrollment/date/slot constraint remains; history is never deleted or rewritten into a different content assignment.
- If the complete proposed selection matches the unstarted assignments, retain their IDs, timestamps and pins. Empty content, a non-study day and a full protected budget do not produce invented assignments.
- At most **five actual plan changes in a rolling 24 hours** are allowed. Identical no-ops do not consume this allowance. A rejected sixth change rolls back every task/outbox modification.

The initial implementation protects an assigned lesson after its explicit dashboard start, or after valid quiz evidence. Merely reading a library lesson in another tab is optional self-study and does not persist an assignment start or an unsaved quiz draft. This feature does not provide offline draft storage or change the existing midnight missed-task policy.

## Commands and replay

- `GET /api/v1/learning/dashboard` exposes `planEtag` and each task's nullable `startedAt`.
- `POST /api/v1/learning/tasks/{taskId}/start` records the first owner start once and returns its timestamp/status. Foreign IDs return 404; superseded or closed assignments return 409. Starting alone never completes a task.
- `POST /api/v1/learning/plan/regenerate` accepts `clientRequestId`, `localDate`, `expectedEtag`. All writes require the authenticated cookie and CSRF.
- The plan token binds the owner enrollment, local date/timezone, study weekdays, pinned path, active goal revision, relevant task IDs/rowversions and practice aggregates/reinforcement rule. A start, completion or changed practice evidence invalidates an older snapshot, even when the new result comes from unassigned library content.
- A payload-bound receipt returns the original acknowledgement for the same command, including after midnight. The acknowledgement names retained, superseded and new task IDs and the resulting plan token. Reusing a command ID with different input returns 409.
- An actual change and its outbox event commit with the receipt and task updates. The pending-node unique index is released before inserting replacements, inside that same transaction.

VI: Giao diện đợi ACK bắt đầu rồi mới mở bài. Khi xung đột, giao diện yêu cầu tải lại lịch và không tự gửi lại lệnh thay đổi. Khi mất phản hồi, lệnh giữ cùng UUID/payload để retry an toàn. Đã có [chuyển mẫu](path-switching.md), [calendar có ngày hiệu lực](path-calendars.md) và [ưu tiên củng cố](weak-skill-planning.md); phân bổ due-review/SRS vẫn chưa triển khai, xem [status](status.md).

EN: Relational tests cover explicit/implicit starts, ownership, no completion from opening, unchanged selection, new prerequisites, immutable protected rows, budgeting, simultaneous starts/regenerations, exact replay across midnight, quota rollback, missing content, legacy enrollments and a profile tracked before the transaction lock.

# Due review in daily plans / Ôn đến hạn trong lịch ngày

VI: Planner chọn một **batch ôn thẻ đến hạn** trước nội dung học mới, sau khi dành ngân sách cho việc phải giữ. Batch là một nhiệm vụ lõi; tổng batch và lesson/practice không vượt ba nhiệm vụ. Không tạo ContentVersion giả để đại diện thẻ.

## Allocation policy v1 / Quy tắc phân bổ

Each due card reserves one **estimated** minute. Review is capped at `floor(MinutesPerDay ×30/100)` from the active goal revision, further bounded by remaining minutes and available core slots. Thus daily budgets20/30/45 allow at most6/9/13 review minutes at initial selection. These are product planning estimates, not measured active duration or validated recall-time predictions. Free review outside the assigned batch remains available separately.

Candidates are active, owned cards with `DueAt <= now`, ordered by DueAt then ID. Future/archived/foreign cards are excluded. The batch pins card IDs, their due instants at selection, assignment timezone/local date, goal revision, estimated minutes, policy version and explanation. Card front/back are immutable in the current custom-card feature. Completed/started content tasks, active attempts and started/completed review batches reserve their minutes/slots first. Protected older assignments retain their original estimate even when a later goal/calendar has a smaller budget.

VI: Refresh giữ nguyên lịch đã lưu; thẻ mới đến hạn hoặc bị cất chỉ làm đổi plan ETag. **Lập lại bài chưa bắt đầu** mới chọn lại phần được phép thay. Token ràng buộc cả nguồn thẻ đến hạn và rowversion batch; command dùng cùng khóa learning với tạo thẻ, review, archive và start. No-op giữ ID/nguồn/lý do. Batch thay thế được đánh dấu superseded trước khi thêm batch mới, trong cùng transaction. Rate limit và receipt retry qua nửa đêm giữ nguyên chính sách regenerate hiện có; ACK task-ID arrays gồm cả batch IDs.

## Sources and rollover / Nguồn hoàn thành và qua ngày

`DailyReviewBatch`/`DailyReviewItem` are persisted review assignments beside existing content tasks. Composite foreign keys enforce enrollment/goal ownership, card ownership and an item's matching CardReview source. A filtered unique index allows one pending batch per enrollment. A CardReview source can satisfy only one item.

Opening a batch records StartedAt but never completes it. An accepted review of an assigned card also starts the batch, including when reviewed from the free due queue. Each accepted rating (Again/Hard/Good/Easy) satisfies that card's activity once. This records participation, not objectively correct recall. Every item must reference a real accepted CardReview before the batch completes; completion and one `learning.review_batch_completed` outbox record commit with that final review. Replaying the review receipt creates no additional effects. Historical reviews before assignment are never backfilled as new completion.

An unstarted batch becomes missed after its pinned local day; current due cards may be selected into a new batch, with only one pending batch retained. A started/partially completed batch survives midnight and reserves its original minutes/slot during regeneration. Archiving an unstarted assigned card permits explicit regeneration to replace the batch. An archived card in a protected started batch must be restored to continue; the system does not invent a successful review or silently discard partial evidence.

Lesson/attempt statistics, path-node completion and the existing completion-activity chart remain separate from self-rated card review. No score, XP or streak is awarded by this implementation.

## API and UI

Dashboard adds `reviewBatches` with saved DUE_REVIEW explanations and completed/total card counts. The existing `tasks` array keeps content tasks and their ContentVersion IDs. Core count/budget applies to both arrays together.

- `GET /api/v1/review-batches/{id}` returns only the owner's pinned card queue and completion flags.
- `POST /api/v1/review-batches/{id}/start` acknowledges start/resume; closed batches cannot start.
- Dashboard opens `/vocabulary?batch={id}`. The card page shows the pinned checklist, reveals each card before rating, and advances after acknowledged review. Archived cards are identified with an explicit restore path.

No card/review/batch is seeded into demo accounts. Existing plans change only through ordinary future-day planning or explicit regeneration. Native browser input was stopped with Escape during the preceding card checkpoint, so this turn uses production builds and packaged API checks; new UI visual/accessibility/mobile acceptance remains unverified.

Twelve relational cases cover the three supported budgets, owned/due filters, three-slot protection, stale/no-op/replayed regeneration, archival replacement, real-source completion/outbox, started/missed midnight behavior, grading/regeneration races and ownership/CSRF/FK boundaries. See [verification](verification.md) for completed whole-suite results and API probes.

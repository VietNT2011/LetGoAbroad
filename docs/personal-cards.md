# Personal cards and SRS v1 / Thẻ riêng và lịch ôn

VI: Trang **Từ vựng & ôn tập** cho phép tự viết hai mặt thẻ và ngữ cảnh, xem thẻ đến hạn, mở mặt sau rồi tự đánh giá, đọc lịch sử và cất/khôi phục thẻ. Không tự suy diễn nghĩa, không gọi AI và không dùng mức tự đánh giá để thay điểm bài thi.

## Storage and ownership / Lưu trữ và quyền riêng tư

`PersonalCardReview` adds `UserCards`, `CardSourceContexts` and append-only `CardReviews`. Existing content, enrollments, attempts and progress are not rewritten. No demo cards or reviews are seeded. All endpoints are authenticated and owner-scoped, including Admin requests; writes use the existing CSRF boundary.

Front/back are plain text, limited to 500/2000 UTF-16 characters; each source context is at most 2000. Text is trimmed and normalized to Unicode NFC. The SHA256 key of the exact normalized two-sided text deduplicates cards per owner, with no case folding or inferred dictionary identity. Re-saving the same text adds a distinct context without losing earlier ones (at most 20 contexts). Different back text is a different custom card. An archived matching card must be explicitly restored, preserving its old schedule/history.

The database enforces owner/key uniqueness, context-key uniqueness and valid interval/rating bounds. Rowversion protects scheduling, archival and added contexts. API pages contain at most 30 items and use bounded offsets. Due queue ordering is DueAt then ID; pages reflect current state and are not a frozen multi-page session.

## Schedule v1 / Quy tắc lịch ôn

| Rating | IntervalDays / DueAt | SuccessStep |
|---|---|---|
| Again | Reset interval to 0; now +10 minutes | Reset to 0 |
| Hard | max(1, round(interval ×1.2)) days; new cards get 1 | Unchanged |
| Good | Steps 0/1/2 get 1/3/7 days; later max(1, interval ×2) | +1 |
| Easy | max(4, round(interval ×2.5)) days | +1 |

Intervals cap at 180 days and positive halves round AwayFromZero. UTC elapsed days/minutes determine DueAt, independently of local midnight, DST and path assignment calendars. A card is due at the exact `DueAt <= now` instant. The server returns `canReview`; refreshing the card rechecks the due instant. Future or archived cards cannot accept new reviews. This is a deterministic product policy, not a validated memory model.

VI: Review cần `ReviewEventId`, `ExpectedEtag` và rating. Khóa `learning:{owner}` thống nhất với planner; receipt được tìm trước kiểm tra hạn/ETag. Cùng event/payload trả ACK cũ, dù lịch đã đổi hoặc thẻ đã cất. Cùng event nhưng đổi thẻ/rating/ETag trả 409. Hai thiết bị gửi event khác nhau cho cùng ETag: một lệnh thắng, lệnh kia nhận 412; không âm thầm coi đó là hai lần nhớ liên tiếp. Review, trạng thái trước/sau, receipt và outbox cùng transaction.

The client retains an unacknowledged review event for explicit retry. A stale response requires fetching current state and choosing a rating again. Each accepted review stores before/after due instants, intervals, steps and scheduler version; archive/restore never removes that evidence. Personal recall ratings do not create Attempt, LessonProgress or TaskCompletion rows and do not enter skill accuracy or the existing activity chart.

## API

- `GET /api/v1/cards/?dueOnly=false&archived=false&offset=0`
- `POST /api/v1/cards/` — ClientRequestId, Front, Back, optional SourceContext
- `GET /api/v1/cards/{id}`
- `GET /api/v1/cards/{id}/reviews?offset=0`
- `POST /api/v1/cards/{id}/reviews` — ReviewEventId, ExpectedEtag, Rating (`again`, `hard`, `good`, `easy`)
- `POST /api/v1/cards/{id}/archive` — ClientRequestId, ExpectedEtag, Archived

## Scope and verification / Phạm vi và kiểm chứng

Twenty-two relational/policy cases cover all schedule steps, rounding/cap/reset, normalized creation/context merging, receipt replay after later reviews/archival, exact due ticks, concurrent event/version races, owner/Admin/CSRF boundaries, reversible archival, invalid text/rating, cross-card event reuse, bounded pages and unchanged learning evidence. Both SQLite and SQL Server profiles run these cases. Actual full-suite and packaged-browser results are recorded in [verification](verification.md).

Manual custom cards now supply the [daily planner's due-review budget](daily-review.md). [Versioned card editing](card-editing.md) preserves historical review text and restarts scheduling only when text changes. Dictionary senses/pronunciation, shared versioned decks, transcript anchors and offline queues remain open; this does not close all M06 acceptance.

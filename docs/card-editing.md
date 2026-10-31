# Versioned personal-card editing / Sửa nội dung thẻ

VI: Trang Từ vựng có **Sửa nội dung thẻ**, lịch sử nội dung và nội dung chính xác của mỗi lượt ôn cũ. Hai mặt đổi thực sự tạo phiên bản mới, đưa lịch về bước0/interval0/đến hạn ngay. ID, ngữ cảnh, ngày tạo và trạng thái đã cất được giữ. Bản lưu chỉ khác khoảng trắng ngoài/NFC giữ nguyên lịch, ETag và phiên bản. Đây là chính sách khi đổi nội dung, không phải kết luận người học đã quên.

## API and invariants / API và ràng buộc

- `PUT /api/v1/cards/{id}`: ClientRequestId, ExpectedEtag, Front, Back.
- `GET /api/v1/cards/{id}/versions?offset=0`: newest first, at most30, nextOffset; offset0–100000.
- Existing card views add ContentVersion. Review history adds ContentVersion and the historical Front/Back.

All reads/writes are authenticated and owner-scoped, including Admin. Writes retain CSRF and actor preconditions. Both faces follow existing plain-text limits and NFC/trim rules. Editing to another owned card's exact canonical two-sided text returns409, including archived targets; histories and schedules are never silently merged. This is current-text deduplication: editing frees the old custom key, while its previous versions remain historical evidence.

Edit, create, archive and review share the owner's transactional learning lock. ETag changes invalidate stale review screens before they can rate text that was edited elsewhere. Receipt lookup precedes ETag checks; identical retries return the original accepted card view after later edits/reviews/archival. Changed payload/card with the same command ID returns409. A real edit creates one version, one outbox event and one receipt atomically. A normalized no-op saves only its receipt.

`UserCardVersions` has a composite card/number key, bounded text and a positive-number constraint. Each CardReview has a composite FK to that card's exact content version. The migration copies previously immutable card text to version1 and binds existing reviews to1, preserving schedules, dates, contexts, reviews and planner completion sources. No new review, attempt or lesson completion is backfilled. Versions are append-only through the application; there is no API for rewriting or deleting them.

Daily review batches allocate card identities, not immutable learning material. Unfinished items can be reviewed on the current text version; completed items retain their original CardReview source/version. Editing does not complete or reopen a batch, change assessment scores, or create skill/activity evidence.

## Client reconciliation / Xử lý xung đột

The editor displays the reset policy before saving. While it is open the selected card's review/archive controls are disabled. An unknown network outcome freezes fields and retains the exact command for retry.412 keeps the draft, offers a separate current-server read and requires an explicit choice: adopt server text, or retain the draft against the newly displayed ETag. Neither choice automatically writes. Cancel is unavailable while a request is in flight or has an unknown outcome. The existing private-tree actor reset applies; draft persistence across navigation/offline is not promised.

Relational cases cover preserved review text and identity, schedule reset/no-op, later-state receipt replay, active/archived collision, competing edits and edit-versus-review, owner/Admin/CSRF/validation, duplicate command replay, canonical creation and bounded history. Frontend cases cover immutable retry, explicit stale reconciliation/adoption, duplicate validation, cancellation and pending-submit guards. Complete-suite results are in [verification](verification.md). Native visual/mobile acceptance remains unverified; Computer Use is not used without an explicit user request.

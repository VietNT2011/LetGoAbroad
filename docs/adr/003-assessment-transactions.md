# Assessment snapshots and command receipts

VI: Mỗi attempt lưu bản chụp prompt/options và bảng GradingKey riêng. Autosave và finalization dùng cùng SQL application lock thuộc transaction. Giờ được đọc sau khóa; AcceptedAt >= DeadlineAt bị từ chối. Toàn batch được kiểm tra ETag và ownership trước khi đổi dữ liệu. Composite FK ngăn option thuộc câu khác. Unique AttemptResult PK và outbox cùng transaction bảo đảm một kết quả logic.

EN: Idempotency receipts bind user, command scope, request ID and SHA-256 payload hash. Same-key retries replay the original acknowledgement; changed payload returns 409. SQL rowversion yields 412 for stale writes. Published source versions remain pinned. No client-supplied correctness or numeric TOEIC conversion is accepted.

Coverage: INV-01…06/19; start retry, no key disclosure, stale/atomic batches, cross-question options, deadline boundary, concurrent submit and private ownership. Integration suite runs against SQL Server via EH_TEST_SQL; default SQLite coverage is supplemental.

# Lesson notes and highlights / Ghi chú và tô sáng bài học

VI: Chọn một đoạn trong nội dung bài học rồi bấm **Ghi chú đoạn đã chọn**, nhập ghi chú tùy ý và lưu. Có thể chỉ tô sáng, hoặc bỏ tô sáng và chỉ giữ ghi chú. Với bàn phím, mở **Chọn đoạn bằng văn bản hoặc bàn phím**, nhập nguyên văn đoạn cần tìm và chọn đúng vị trí nếu đoạn đó xuất hiện nhiều lần. Các ghi chú đã lưu xuất hiện trong bài và ở **Câu đã lưu & ghi chú → Đoạn trích trong bài học**.

EN: Each note pins a `ContentVersionId`, `BlockKey=body`, start-inclusive/end-exclusive UTF-16 offsets and the exact original quote. The body is rendered as source text with preserved newlines, not HTML. Overlapping highlights are merged for display without changing the original text coordinates. React escapes both quotes and notes. Publishing a new lesson version never moves or rewrites old anchors.

## Privacy and historical content / Riêng tư và nguồn lịch sử

VI: Owner luôn lấy từ session. Các endpoint không nhận UserId do client gửi; Admin cũng không đọc được ghi chú riêng của người khác. Draft, assessment và version không tồn tại không được dùng làm nguồn ghi chú bài học. Một bài đã archive chỉ được mở lại qua API người học nếu chính người đó từng có ghi chú trên version ấy; giao diện ghi rõ archived và ẩn quiz hoàn thành. Ghi chú vẫn sửa/xóa/khôi phục được. Xóa ghi chú không sửa nguồn bài học, attempt hoặc tiến độ.

EN: The database enforces user/version foreign keys, bounded anchor offsets and rowversion concurrency. The service additionally verifies source kind/publication, exact quote equality and Unicode surrogate boundaries. Quotes and notes each permit at most 4000 UTF-16 code units. An owner may retain 100 active notes and 500 historical rows per lesson version; restoration respects the active limit.

## Concurrency and removal / Xung đột và xóa

VI: Client tạo UUID ghi chú và giữ CommandId khi retry cùng payload. Receipt được lưu trong transaction khóa owner/version, trả ACK gốc ngay cả khi ghi chú đã đổi sau đó. Cùng CommandId nhưng đổi payload trả 409; ETag cũ trả 412. Khi sửa từ hai tab, browser giữ nguyên bản đang gõ, cho xem bản server rồi người học chủ động dùng phiên bản mới để lưu lại.

EN: Removal is a reversible tombstone, with a separate payload-bound receipt. **Hoàn tác xóa ghi chú** restores the same ID using its current ETag. Retrying an earlier removal returns its original acknowledgement and cannot delete a later restoration. Owner-only single-note reads expose tombstones to support comparison/recovery; lists omit them. Unsaved editor drafts remain in the current tab during conflicts/network errors; they are not persisted across closing/reloading the tab in this release.

## API

- `GET /api/v1/lessons/{version}/annotations/`: owner list for a readable lesson version.
- `GET /api/v1/lessons/{version}/annotations/{id}`: owner read, including a removed row.
- `PUT /api/v1/lessons/{version}/annotations/{id}`: create/update/restore with `ClientRequestId`, exact anchor/quote, text, highlight setting and `ExpectedEtag` (null on create).
- `POST /api/v1/lessons/{version}/annotations/{id}/remove`: reversible remove with command ID and current ETag.
- `GET /api/v1/me/lesson-annotations?offset=0`: owner list, 30 rows per page and `nextOffset`.

VI: Test gồm Unicode, quyền owner/Admin, CSRF, quote sai nguồn, version chưa publish, xung đột đồng thời, receipt replay, đổi version/archive, quota 100, phân trang và xóa/khôi phục. Browser đã kiểm tra chọn đoạn, reload, hai tab, so sánh bản mới, hoàn tác và liên kết về đúng version. Ghi chú cho transcript/segment và anchor trên structured ContentBlocks khác vẫn thuộc phần tiếp theo.

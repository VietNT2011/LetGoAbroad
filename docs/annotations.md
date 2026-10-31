# Bookmark và ghi chú riêng / Private question annotations

VI: Có thể đánh dấu câu trong khi làm bài, viết/xóa nội dung ghi chú ở trang chữa bài và tìm lại tại **Câu đã lưu & ghi chú**. Mỗi người có một annotation trên một QuestionVersion; các attempt cùng version dùng chung annotation đó. Xóa nội dung ghi chú giữ nguyên bookmark. Bỏ bookmark giữ nguyên ghi chú. Khi cả hai rỗng, hàng không hiện trong danh sách đã lưu nhưng token concurrency vẫn tồn tại.

EN: Every read and write derives the owner from the authenticated session. An administrator cannot use these endpoints to read another learner's notes. The source attempt item must belong to that owner and the requested attempt. A unique `(UserId, QuestionId)` index and a composite `(SourceAttemptItemId, QuestionId)` foreign key protect identity and source consistency.

## Anchor và version / Anchors and versions

VI: `AnchorBlock` nhận `prompt` hoặc `stimulus` của snapshot câu hỏi. Khoảng là offset UTF-16, đầu bao gồm/cuối không bao gồm, nằm trong đúng block và không được cắt đôi surrogate pair. UI hiện gắn toàn bộ prompt; API hỗ trợ khoảng con. Annotation giữ ContentVersion thông qua FK tới snapshot attempt bất biến. Publish version mới tạo QuestionId mới và không chuyển ghi chú lịch sử sang câu mới.

EN: Text-selection highlights and lesson-body notes are still pending. This feature is question bookmarking and private notes; it is not the P1 mistake scheduler, vocabulary cards or spaced repetition.

## API

- `GET /api/v1/attempts/{id}/annotations`: one owner-scoped dictionary keyed by the current attempt's item IDs, avoiding one request per question.
- `PUT /api/v1/attempts/{id}/items/{itemId}/annotation`: save a bookmark and/or note. New annotations require `expectedEtag: null`; existing ones require the exact current ETag. Limit: 4000 UTF-16 code units for note text.
- `GET /api/v1/me/annotations?bookmarkedOnly=false&offset=0`: owner list, at most 30 rows, stable timestamp/ID ordering and `nextOffset` when another page exists.

```json
{
  "clientRequestId": "<uuid>",
  "isBookmarked": true,
  "noteText": "Nhớ kiểm tra chủ ngữ trước khi chia động từ.",
  "expectedEtag": null,
  "anchorBlock": "prompt",
  "anchorStart": 0,
  "anchorEnd": null
}
```

VI: `anchorEnd: null` lấy chiều dài block từ snapshot. Các lệnh serialize trên khóa owner/question, dùng payload-bound receipt và cập nhật trong transaction. Cùng CommandId/payload trả ACK gốc; đổi payload trả 409. ETag cũ hoặc tạo lặp khác CommandId trả 412. Browser giữ nội dung đang gõ khi gặp 412, cho tải bản mới để so sánh rồi người dùng quyết định lưu.

EN: Annotation writes never change AttemptItem answers, ETags or AttemptResult. Tests check owner isolation (including Admin), exact replay, two-attempt create races, stale updates, Unicode anchor bounds, publication separation, note clearing and pagination.

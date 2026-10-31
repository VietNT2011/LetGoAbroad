# Điền đáp án / Typed answers

`QuestionType` dùng `single_choice` hoặc `fill_blank` ở API. Mini test/practice và quiz bài học hỗ trợ cả hai. Full LR blueprint 200 câu chỉ nhận SingleChoice; bài gõ đáp án có nhãn riêng trong danh mục.

## Policy v1

VI: Chấm ở server bằng các biến thể do biên tập viên soạn, sau các bước theo đúng thứ tự:

1. Unicode NFC.
2. Đổi dấu nháy `‘`, `’`, `ʼ`, `＇` thành `'`.
3. Trim và gộp các khoảng trắng Unicode thành một dấu cách, kể cả tab, xuống dòng và NBSP.
4. Đổi hoa/thường bằng quy tắc invariant.
5. Nếu `ignoreTerminalPunctuation` bật, bỏ chuỗi `. ! ? 。 ！ ？` cuối câu rồi trim phần cuối.

EN: The server compares exact normalized strings against authored variants. Version one preserves accents, internal punctuation, word order and spelling. It does not use fuzzy matching. An empty normalized answer is never correct. Whitespace-only input counts as blank; punctuation-only input is an incorrect nonblank answer.

Ví dụ: `DON’T` khớp `don't`; ` I\tam READY. ` khớp `I am ready` khi cho phép bỏ dấu câu cuối. `cafe` không tự khớp `café`, và `redy` không khớp `ready`. Muốn chấp nhận hai cách viết khác nhau phải biên soạn cả hai biến thể.

## CMS và snapshot / Authoring and snapshots

```json
{
  "prompt": "Write the full form of: I'm ready.",
  "skill": "grammar",
  "explanation": "I'm là dạng rút gọn của I am.",
  "options": [],
  "type": "fill_blank",
  "acceptedAnswerVariants": ["I am ready"],
  "normalizationPolicyVersion": 1,
  "ignoreTerminalPunctuation": true
}
```

VI: Publish/import kiểm tra 1–20 biến thể, tối đa 2000 UTF-16 code units mỗi biến thể, không rỗng/trùng sau chuẩn hóa. Câu FillBlank không có options; SingleChoice không nhận biến thể văn bản. Policy chưa hỗ trợ bị từ chối, không tự thay thế bằng policy mới.

EN: Start pins the type, variants, punctuation setting and policy version into the attempt's private grading snapshot. Publishing a newer content version cannot change an existing attempt. The active DTO exposes the prompt, type, learner's saved text and ETag; variants and explanations appear only in the owner-scoped result after durable finalization.

## Autosave và chữa bài / Saving and review

```json
{
  "clientRequestId": "<uuid>",
  "answers": [{
    "attemptItemId": "<uuid>",
    "selectedOptionId": null,
    "answerText": " I am READY. ",
    "expectedEtag": "<etag>"
  }]
}
```

VI: Giữ nguyên văn bản gốc để chữa bài; chỉ chuẩn hóa khi chấm. AnswerText tối đa 2000 UTF-16 code units. Toàn batch được xác thực trước khi ghi; receipt giữ cả payload và ACK của văn bản. ETag, deadline, khóa section và quyền owner áp dụng giống trắc nghiệm. Chọn option cho FillBlank hoặc gửi text cho SingleChoice trả 422.

EN: The browser uses the existing 500 ms autosave queue, stable retry IDs and local draft recovery. Submit waits for acknowledgement. Review shows the original text, accepted variants and the pinned policy. Tests cover normalization boundaries, malformed CMS variants, atomic rejection, replay, deadline enforcement, publication changes and quiz-backed completion.

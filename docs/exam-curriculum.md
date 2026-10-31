# Exam Practice starter / Chương trình luyện đọc theo ngữ cảnh

VI: Exam Practice v1 có **6 bài, 12 câu quiz và 13 câu luyện mới**, tổng cộng 25 câu nguyên gốc. Chương trình tập trung đọc/ngữ pháp với nhóm hoàn thành đoạn văn và đối chiếu tài liệu công sở hư cấu. Đây là starter có lịch, prerequisite và bằng chứng hoàn thành; audio/listening, full mock được biên soạn và thẩm định trình độ vẫn còn trong backlog.

EN: All passages, options and explanations were authored for EnglishHub under the seed's CC0 attribution. No ETS test questions were copied. The official [TOEIC Listening and Reading overview](https://www.ets.org/toeic/about/listening-reading.html) identifies Text Completion and Reading Comprehension; the [official score user guide](https://www.ets.org/pdfs/toeic/toeic-listening-reading-score-user-guide.pdf) describes the four-question text and five-question multiple-passage formats used as structural references here. These references do not validate this starter's difficulty, teaching outcomes or scores. There is no conversion to 990 or calibrated CEFR claim.

| Suggested study day / Ngày học gợi ý | Lesson / Bài | Checkpoint / Mốc |
|---|---|---|
| 1 | Word form: slot, role and meaning | 2-question quiz |
| 2 | Connectors and paragraph logic | Quiz, then two context groups of 4 questions |
| 3 | Purpose and bounded inference | Requires the context checkpoint; 2-question quiz |
| 4 | Cross-document requirements | 2-question quiz |
| 5 | Exceptions and combined conditions | 2-question quiz |
| 6 | Paraphrase scope and evidence | Quiz, then one three-document group with 5 questions |

## Completion and scheduling / Hoàn thành và lập lịch

Every lesson requires both quiz answers correct. Both assessment nodes use `assessment_all_raw`: one newly graded `mini` or `practice` attempt must cover **every authored question ID** for the pinned version and reach at least 60%. The context checkpoint therefore needs **5/8**, and the document checkpoint needs **3/5**. Results from different attempts are not combined. A perfect 4/4 context subset cannot complete the eight-question checkpoint. Requesting one question keeps a whole group: four context questions or all five document questions.

VI: Mỗi bài/mốc có ngân sách gợi ý 10 phút. Lịch mặc định 30 phút hoặc ngân sách tối thiểu 20 phút đủ chỗ cho một bài và mốc sau khi người học chủ động lập lại phần chưa bắt đầu. Refresh không tự chèn thêm task. Ngày nghỉ, prerequisite và việc học lại có thể làm lịch dài hơn sáu ngày; mở bài hay xem preview không ghi hoàn thành.

EN: The template has slug `exam-practice-six-study-days`, level `exam_practice`, three stages and eight nodes. It can be selected explicitly. Under the existing demo placement policy, a complete 15–20/20 result now pins this published template instead of using the legacy unpinned plan. Placement remains a demonstration policy with listening coverage and calibration still open. Existing Foundation/Core enrollments and path-choice histories retain their pins.

The context bank records six grammar and two reading questions rather than assigning one skill to every item in a mixed group. The three-document bank records reading evidence. This metadata feeds existing practice statistics; the small authored bank is not a proficiency assessment.

## Publication and maintenance / Phát hành và bảo trì

The source manifest is [seed/exam-curriculum.json](../seed/exam-curriculum.json). `AuthoredCurriculumSeed` now shares the additive atomic publisher with Core: validate authored content, save content versions and publication pointers, validate the pinned graph, then commit the template and path pointer in the same catalog transaction. Core's authored schedule and attribution remain unchanged. No schema migration is needed.

VI: Chỉ chạy demo seed ngoài Production khi bật `Seed:Demo`. Template đã tồn tại làm seed bỏ qua; không ghi đè payload, bản CMS mới, retirement hoặc enrollment. Nếu chưa có template nhưng slug nội dung đã bị chiếm, toàn bộ seed dừng với `curriculum_seed_collision` để kiểm tra, thay vì nhận nhầm nội dung. Sửa manifest không cập nhật nội dung đã publish trong database; chỉnh sửa biên tập cần version CMS mới.

## Verification and limits / Kiểm thử và giới hạn

Eight relational cases cover corpus shape and key nondisclosure; unchanged Foundation/Core references and learner pins; byte-stable content on repeated seed with retirement preserved; reserved-slug collision without partial publication; both Exam placement boundaries; the full six-day journey with failed and successful checkpoints; rejected perfect partial context evidence; and the indivisible three-document snapshot. The same suite runs on SQLite and SQL Server; media tests continue to exercise Azurite and FFmpeg independently.

Packaged browser checks explicitly enrolled the previously unused `learner2@englishhub.local` demo account in Exam, leaving it at **0/8**. Opening the first lesson and selecting one answer kept the two-question completion button disabled; no quiz was submitted. Practice previews with a target of one question returned four context questions or five document questions, without starting attempts. At a 320px viewport the document width and scroll width were both 305px; all eight titles and prerequisite captions remained present. Native JPEG screenshots were saved in the handover outputs. The viewport and main learner session were restored; Foundation remained 1/18 with 1 completed lesson, 3 attempts and 4/29 observed answers, and the Core demo remained 0/10. Fresh databases do not auto-enroll these secondary accounts.

The content has been manually checked for answer/explanation consistency and obvious ambiguity. External language-teacher review, learner trials, wider vocabulary/distractor diversity, listening material, measured difficulty and learning outcomes remain open. The complete P0 gate is still open; this starter does not by itself meet every criterion in the original handover.

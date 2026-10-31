# Core starter curriculum / Chương trình Core demo

Core v1 has **8 lessons, 16 lesson-quiz questions and 12 new assessment questions**. All text, examples, passages and explanations were authored for EnglishHub. The seed attribution is CC0; it is not ETS material, an external exam bank, a calibrated CEFR level or a TOEIC score predictor. Locations, travel options and schedules in reading exercises are fictional.

| Suggested study day | Lesson | Checkpoint |
|---|---|---|
| 1 | Present perfect versus a finished past | Two-question lesson quiz |
| 2 | Past continuous background and completed events | Two-question lesson quiz |
| 3 | Passive processes | Two-question lesson quiz |
| 4 | A realistic future condition | Two-question lesson quiz |
| 5 | Defining relative clauses | Two-question lesson quiz |
| 6 | Required, optional and prohibited | Lesson quiz, then six-question grammar assessment |
| 7 | Revised instructions and separate deadlines | Requires the grammar checkpoint; two-question lesson quiz |
| 8 | Combining a timetable with an arrival condition | Lesson quiz, then two reading passages with three questions each |

VI: Mỗi bài dự kiến 10 phút; quiz cần đúng cả hai câu. Hai checkpoint dùng một **lần làm mới đủ toàn bộ sáu câu**, đúng ít nhất 60% (tức ít nhất 4/6). Không cộng câu từ nhiều lần làm. Mốc ngày chỉ là gợi ý: ngân sách, prerequisite, học lại và ngày nghỉ có thể kéo dài lịch. Sau khi hoàn thành bài trong ngày, người học có thể lập lại phần chưa bắt đầu để nhận checkpoint nếu còn ngân sách. Việc mở bài không ghi hoàn thành.

EN: The new `assessment_all_raw` node rule requires a newly graded `mini` or `practice` attempt for the exact pinned content version. The result count must match that version's authored question count, and every authored question ID must be present in the attempt snapshot. A perfect three-question reading subset does not complete the six-question checkpoint. The original `assessment_raw` rule retains its existing subset/raw-score semantics; published Foundation paths are not reinterpreted. CMS can opt new assessment nodes into the new rule, but cannot apply it to lesson nodes. The UI states that all questions are required.

## Admission, publication and existing databases

The template slug is `core-eight-study-days`, with three stages and ten nodes. New learners can select it explicitly. With no manual override, the existing demo placement policy's Core band (8–14 correct out of 20) now pins this published Core template instead of falling back to an unpinned catalog plan. This fills a missing template; it does not validate the current placement test as a broad proficiency assessment. Exam Practice still requires its own authored curriculum, and placement listening coverage remains an acceptance gap.

VI: Core chỉ seed khi `Seed:Demo` bật ngoài Production. Nội dung và graph mới dùng chung transaction/khóa catalog; lỗi không để lại bản publish nửa chừng. Validator nội dung CMS kiểm tra câu hỏi trước khi lưu. Slug bị chiếm khi chưa có template làm seed dừng rõ ràng thay vì ghi đè hoặc ghim nhầm nội dung. Khi template đã tồn tại, chạy lại không sửa payload, con trỏ phiên bản, retirement hay enrollment. Foundation vẫn giữ version/nội dung/tiến độ cũ. Không có migration schema mới.

The manifest is [seed/core-curriculum.json](../seed/core-curriculum.json). Future editorial revisions should use new CMS content/path versions rather than editing already published database payloads or expecting reseeding to overwrite them. Content has been manually checked for answer/explanation consistency and obvious ambiguity, with structural validators and relational journey tests. External language-teacher review, learner trials, listening/audio, calibrated difficulty and educational-outcome validation remain open.

## Verification

Relational cases cover corpus shape and key nondisclosure, unchanged Foundation references, exact reseed/no-op payloads and preserved retirement, automatic Core admission at both placement-band boundaries, all eight lessons plus two checkpoints with insufficient/sufficient attempts, prerequisite blocking, one source per completion, whole-group preview and partial-attempt rejection, and excluded fixture/review modes. Existing path tests retain coverage of older rule behavior, owners, stale commands, publication and calendar boundaries.

Browser checks used the packaged local app: the previously unenrolled `learner3@englishhub.local` demo account explicitly selected Core and remained at **0/10** after opening a lesson and selecting one answer without submitting. The incomplete two-question quiz kept its completion button disabled. Core captions showed full-coverage requirements. A 320px check found that old mobile CSS hid prerequisite explanations; summaries/prerequisites now remain visible, with 0 overflowing path nodes and a 305px document width. The viewport was reset afterwards. The browser returned to the main learner, retaining Foundation **1/18**, one lesson completed, three submitted attempts and 4/29 observed answers. No profile/calendar edits or quiz submissions were made in this UI check. On a fresh database, the secondary demo account still chooses its own path; the code does not silently enroll it.

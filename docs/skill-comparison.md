# Comparing recent samples / So sánh hai mẫu luyện

VI: Dashboard có mục **So sánh hai mẫu luyện gần đây**. Mỗi kỹ năng/nhóm làm bài lấy tối đa50 câu khác nhau mới nhất và50 câu liền trước, luôn hiện số đúng/số quan sát và câu trống. Mẫu mới nhất khớp quy tắc của [dữ liệu kỹ năng hiện có](skill-evidence.md). Đây là so sánh hai nhóm câu theo thứ tự lần làm hợp lệ gần nhất, không phải hai cửa sổ ngày hay hai snapshot đã lưu trong quá khứ.

## Selection policy v1 / Chọn mẫu

`GET /api/v1/learning/skill-comparison` authenticates the owner and returns policyVersion, windowSize50, minimumObservations5 and groups. Each group has track, skill, recent, previous and nullable differencePercentagePoints. Both samples include the same safe count/Part fields as the existing skill-evidence DTO. There are no submitted answers, question text/IDs, grading variants, answer keys or internal ranks in this response.

Both endpoints use **one shared eligibility query**. For each track/skill/question-version, only the most recent eligible answer is retained, ordered by finalization time and attempt ID descending. The distinct versions are then ordered by finalization, attempt ID and item order, descending. Ranks1–50 form the recent sample;51–100 form the previous sample. Older ranks are omitted. A question version cannot occupy both samples in the same track/skill. Repeating it replaces its answer and can move it between the samples; this is not an immutable historical performance ledger.

Practice includes mini/practice/placement. Full-mock items use the existing server-disclosure and conservative legacy rules; an entire attempt is not assumed to be unseen just because its mode is full_mock. Technical fixtures, mistake-review mode and ungraded attempts are excluded; archiving content afterward retains eligible historical evidence. No sum combines practice and full-mock conditions. FillBlank uses the pinned normalization policy, and blanks remain in the denominator. The same calculated outcome feeds totals and Part counts.

The SQL window sorts narrow IDs/skills before joining private answer payloads, and reads at most100 selected rows per track/skill. The endpoint performs a single query, not one query per sample or day. This extends the existing50-row read only for the comparison endpoint; the planner/current-evidence endpoint still selects50. No migration, new ledger, source rewrite, learner backfill or score/XP/streak change is introduced.

## Display and interpretation / Cách đọc

Fewer than5 observations in a sample yields null accuracy and `insufficient_data`. Difference is null unless both samples qualify. Otherwise each accuracy uses the existing integer rounding (halves away from zero), and difference subtracts those **displayed percentages**, in percentage points. For example49/50 is98%,1/8 is13%, and the displayed difference is+85 points. Zero and negative differences remain valid values.

VI: Hai nhóm có thể khác câu và độ khó; chúng không phải một bài kiểm tra chuẩn hóa trước/sau. Chênh lệch mô tả các mẫu đã làm, không chứng minh mức tiến bộ hoặc dự đoán điểm TOEIC. Các nhóm có ít mẫu vẫn hiện số đếm; không thêm dữ liệu demo để làm đẹp biểu đồ. Lịch sử mở lời giải chỉ phản ánh những lần server ghi nhận, không biết kiến thức người học có từ nơi khác.

The panel mounts its data view only when opened and discards it on close. One snapshot supplies both samples. It separates the two tracks, labels the sample counts/empty states, and provides a captioned HTML Part table with scoped headers; decorative bars are hidden from accessibility APIs. Errors have retry controls. Closing cancels the request; obsolete data cannot restore the discarded view. Existing actor-bound transport and private-tree resets apply without adding a shared user-data cache.

## Verification / Kiểm chứng

Ten new relational cases cover empty/read-only behavior,100-version bounds and repeat replacement,54/55 observation thresholds, difference-of-rounded-values, typed normalization/blanks/Parts, disclosure/legacy eligibility, mode/fixture/unfinished/archive boundaries, equal-time deterministic ordering and owner/Admin/anonymous/key isolation. The original five skill-evidence cases also pass after query reuse. Six frontend cases cover lazy loading and separated tracks, table semantics/Part union, null versus zero, network retry, aborted stale responses and empty samples. See [verification](verification.md) for complete-suite results.

Packaged read-only probes checked all three existing demo accounts: recent samples match the old endpoint exactly, sparse/empty samples remain sparse/empty, no private answer payload appears, and anonymous access returns401. Native verification stopped during navigation because Computer Use could not determine the current browser URL confidently enough to enforce policy; no further app input was issued. Native visual/keyboard/mobile acceptance remains open; DOM tests do not verify actual browser layout or screen-reader behavior. No runtime capacity benchmark or calibrated educational validity is claimed. Date-based performance histories and further taxonomy analysis remain separate work.

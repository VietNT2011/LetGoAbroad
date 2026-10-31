# Whole placement evidence / Bằng chứng đủ bài đánh giá đầu vào

VI: Gợi ý Foundation/Core/Exam Practice của demo được định nghĩa trên **20 câu**, không phải trên tỷ lệ đúng của một phần đề. Trước bản sửa, lời gọi API trực tiếp có thể rút placement xuống một câu; kết quả 1/1 có thể bị đưa qua ngưỡng phần trăm như một bài placement đầy đủ. Guard mới chặn đầu vào đó và kiểm tra lại snapshot trước khi tạo enrollment.

## Start and admission

- A new `mode: placement` start must reference the nonfixture `placement-demo` assessment version with exactly 20 authored questions. `part` must be absent; `questionCount` may be absent or exactly 20. A shortened count or Part filter returns 422 `placement_whole_required` without creating an attempt/start receipt. Other banks are rejected too; existing technical-fixture guards still apply.
- Enrollment from placement still requires the authenticated owner's graded attempt. It now checks the result total, valid correct-count range, exactly 20 distinct attempt question IDs and equality with the complete question set of the exact pinned content version. Counts alone cannot establish coverage.
- Blank answers remain in the denominator. Full coverage means all 20 questions were in the graded snapshot; it does not require every answer to be correct or nonblank. A completed full test may still recommend Foundation.
- A graded `practice` attempt does not become placement evidence, even when it used the same whole bank. Optional filtered practice remains available under its own mode. Explicitly choosing a path or starting from basics remains available; that choice is recorded as a learner/manual choice rather than a placement recommendation.

EN: Both checks use `PlacementPolicy` for the recognized blueprint. The existing 0–7 / 8–14 / 15–20 demo thresholds and stored `placement_demo_v1` reason stay unchanged. The change enforces their intended denominator; it does not introduce a new proficiency estimator.

## Historical evidence and replay

VI: Attempt cũ không bị xóa hoặc chấm lại. Một receipt khởi động đã được chấp nhận trước đây vẫn trả đúng attempt gốc khi retry, kể cả attempt rút gọn; nhưng attempt đó không tạo được enrollment mới dựa trên placement. Enrollment đã tồn tại cũng không tự đổi lộ trình/lịch sử lý do. Không có migration sửa tiến độ.

A complete historical placement remains eligible after a newer version is published or its original content version is archived: coverage is checked against the attempt's pinned version, not the current publication pointer. Existing results, private grading keys and deadlines are not rewritten. The admission/start/grade commands continue using the owner learning transaction lock, so a recommendation cannot race a partially committed grade.

## Verification and scope

Relational API cases cover shortened/Part-filtered starts with no side effects, wrong-bank labels, default/explicit twenty-question starts and replay, legacy partial/misreported/mismatched snapshots, an accepted old partial-start receipt, ordinary practice exclusion, and a complete historical version after republishing/archiving. Existing Core placement-boundary and path-history tests remain part of the full regression suite.

This closes an input/evidence-integrity gap only. The current demo placement content still needs broader authored listening/reading coverage and external editorial review. Completing 20 demo questions is not a CEFR certificate, an official TOEIC score or evidence that the entire P0 acceptance gate is satisfied.

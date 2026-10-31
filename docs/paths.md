# Versioned learning paths / Lộ trình có phiên bản

## Content graph / Graph nội dung

VI: `PathTemplate → PathVersion → PathStage → PathNode`. Node ghim một `ContentVersion`, ngày học gợi ý, quy tắc hoàn thành và các node prerequisite. CMS tạo/sửa draft JSON, kiểm tra graph rồi Admin publish. Chỉ draft được sửa; kể cả thay graph mà không sửa tiêu đề cũng tăng ETag. Tạo version mới không thay đổi enrollment đang có.

EN: Publication validates unique keys, bounded node/edge counts, existing references, a deterministic topological order, no cycles and no prerequisite scheduled after its dependent. Composite foreign keys keep edges/stages in one path version and require a task's content to match its node. A published path cannot reference fixtures, unpublished content or a lesson without a quiz. Content pinned by a published path cannot be archived through the general content endpoint.

```json
{
  "title": "A two-day start",
  "summary": "Lịch học tự biên soạn.",
  "attribution": "Original project author; describe usage rights.",
  "stages": [{
    "key": "getting-started", "title": "Bước đầu",
    "nodes": [{
      "key": "introductions", "contentVersionId": "<published-lesson-uuid>",
      "studyDay": 1, "completionRule": "lesson_quiz",
      "minimumCorrectPercent": 100, "prerequisites": []
    }, {
      "key": "practice", "contentVersionId": "<published-assessment-uuid>",
      "studyDay": 2, "completionRule": "assessment_raw",
      "minimumCorrectPercent": 50, "prerequisites": ["introductions"]
    }]
  }]
}
```

## Enrollment and evidence / Ghim mẫu và bằng chứng

VI: Enrollment mới chọn bản publish phù hợp level, hoặc version được người học chọn. Lưu lý do lựa chọn và placement thuộc chính người học nếu có. Seed Foundation gồm **3 stage, 14 bài/quiz và 4 mốc practice**, trải trên 14 ngày học gợi ý. Thời lượng từng bài, ngân sách, prerequisite và ngày bỏ lỡ có thể kéo dài thời gian hoàn thành. Đây là lịch demo tự biên soạn, không phải chứng nhận trình độ hoặc điểm TOEIC.

EN: Lesson nodes use persisted quiz-backed `LessonProgress` for the exact pinned content version. Practice nodes require a newly started, graded, nonfixture attempt meeting the configured raw-score percentage. One attempt can source only one daily completion; repeated practice nodes require separate attempts. Opening a page or submitting an insufficient score cannot complete a node. Prerequisites affect the path and daily scheduling; the general library remains available for optional self-study.

VI: Enrollment cũ sau migration có `PathVersionId = null`; không bịa lại lịch sử đã học. Người học chọn **Áp dụng mẫu lộ trình** để ghim bản publish rõ ràng. Thao tác có payload-bound receipt; task đã có trong ngày giữ nguyên. Chọn mẫu khác qua endpoint enroll đang có trả 409 thay vì âm thầm bỏ qua lựa chọn. Thay đổi một enrollment đã ghim sang template khác còn thuộc phần quản lý revision tiếp theo.

## Daily scheduling / Lập lịch ngày

EN: The enrollment pins the start date, IANA timezone and weekly calendar used for authored study-day indices. Eligible unfinished nodes must have satisfied prerequisites and a due study-day index. The scheduler first takes eligible carryover, then a lesson and a short practice, with at most three tasks, no duplicate content in one daily plan and a total within the current minute budget. Tasks pin their content, path version, node and timezone. Empty selections remain empty rather than inventing content.

VI: Task quá ngày chưa có bằng chứng được chuyển `missed`. Lần chọn lại nối `SourceTaskId` tới lần bỏ lỡ gần nhất. Unique filtered index giữ tối đa một task `pending` cho một node trong enrollment. Vì vậy lịch sử tiếp nối được giữ mà hàng đợi đang chờ không nhân bản vô hạn. Các request sinh task cùng người học serialize qua transaction lock; `EnrollmentId + LocalDate + SlotKey` giữ idempotency.

EN: A live attempt spanning midnight stays attached to its original task, appears on the next dashboard with a resume link and keeps its original server deadline. Reconciliation also handles a result committed during rollover: it credits the original eligible source, supersedes an unnecessary pending successor, and does not reuse that result for the next practice node. Completion timestamps/local dates come from the actual result/quiz evidence, using the task's pinned timezone.

VI: Refresh không thêm các task mới sau khi ngày đó đã có lịch; node vừa mở khóa xuất hiện ở lần lập lịch ngày sau. Cần workflow regenerate riêng để bổ sung/chỉnh task chưa bắt đầu. Task legacy giữ logic danh mục cũ cho tới khi người học chủ động ghim mẫu.

## API and limits / API và giới hạn

- Learner: `GET /learning/templates`, `GET /learning/path-plan`, `POST /learning/enrollment` với `pathVersionId` tùy chọn, `POST /learning/enrollment/adopt-path`.
- CMS: `GET/POST /admin/paths`, `GET/PUT /admin/path-versions/{id}`, `POST /admin/paths/{templateId}/versions`, `POST /admin/path-versions/{id}/validate` và `/publish`. Các route đều có prefix `/api/v1`; ghi browser cần CSRF; publish chỉ Admin.
- Draft limits: 20 stages, 200 nodes, 20 prerequisites/node, suggested study days 1–365. Each node points at an existing content version and a supported completion rule.
- Foundation is authored. [Goal/minute revisions](goal-revisions.md) apply to future plans and are pinned on tasks. Additional Core/Exam Practice schedules, due-review/SRS budgeting, last-50 skill evidence, effective-date calendar history, switching an already pinned template, regeneration and archive/withdrawal policy for paths remain open. Calendar changes do not rewrite the originally pinned path calendar in this release.

API/SQL tests cover cycles, malformed references, reversed days, immutable publication, graph-only ETags, unchanged old enrollments/content after republishing, owner/role checks, actual quiz/score evidence, missed-task chains, midnight/reconciliation and relational FK/source constraints.

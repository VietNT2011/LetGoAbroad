# ADR-008 — Explainable planning / Lộ trình theo quy tắc

Planner v1 chọn tối đa 3 nhiệm vụ trong ngân sách thời gian: bài nền tảng tiếp theo, practice ngắn nếu vừa thời gian, rồi bài tiếp theo nếu còn chỗ. Không bịa nội dung khi thiếu. Placement yêu cầu một attempt placement đã chấm, đúng owner và đúng bộ nội dung, hoặc lựa chọn bắt đầu từ căn bản.

Daily task có unique EnrollmentId + LocalDate + SlotKey. Ngày được suy ra từ IClock UTC và múi giờ hồ sơ. Task đã sinh giữ snapshot phiên bản nội dung. Complete yêu cầu quiz đúng hoặc attempt đã chấm sau khi task được tạo. TaskCompletion có đúng một FK nguồn: LessonProgress hoặc Attempt. Completion/outbox cùng transaction; retry không cộng tiến độ hai lần.

Thống kê đang hiển thị: bài học đã hoàn thành và raw correct/observed của attempt không phải fixture/ôn sai. Chưa công bố streak, XP, điểm 990 hoặc benchmark.

Subsequent changes implemented versioned Foundation/Core/Exam reading schedules, prerequisites, recent-skill reinforcement and carryover. [Due-review allocation v1](../daily-review.md) reserves at most30% of the active daily budget after protected work, one estimated minute per due card, in a real-card batch occupying one of three core slots. Its source rows are accepted private CardReviews; self-rated recall never becomes lesson/assessment accuracy. See [personal cards](../personal-cards.md) for schedule/replay/concurrency policy. [Estimated study time](../estimated-study-time.md) uses bounded server-time intervals and union across tabs in rolling UTC windows. Authored listening, editorial review, local-day duration charts and browser/device/offline acceptance still prevent a complete-PATH claim.

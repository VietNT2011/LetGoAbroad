# ADR-008 — Explainable planning / Lộ trình theo quy tắc

Planner v1 chọn tối đa 3 nhiệm vụ trong ngân sách thời gian: bài nền tảng tiếp theo, practice ngắn nếu vừa thời gian, rồi bài tiếp theo nếu còn chỗ. Không bịa nội dung khi thiếu. Placement yêu cầu một attempt placement đã chấm, đúng owner và đúng bộ nội dung, hoặc lựa chọn bắt đầu từ căn bản.

Daily task có unique EnrollmentId + LocalDate + SlotKey. Ngày được suy ra từ IClock UTC và múi giờ hồ sơ. Task đã sinh giữ snapshot phiên bản nội dung. Complete yêu cầu quiz đúng hoặc attempt đã chấm sau khi task được tạo. TaskCompletion có đúng một FK nguồn: LessonProgress hoặc Attempt. Completion/outbox cùng transaction; retry không cộng tiến độ hai lần.

Thống kê đang hiển thị: bài học đã hoàn thành và raw correct/observed của attempt không phải fixture/ôn sai. Chưa công bố streak, XP, điểm 990 hoặc benchmark.

Remaining acceptance: path template/version registry, 14-day authored curriculum, prerequisite graph/cycle validation, weak-skill last-50 selection, missed-task carryover and SRS review budget belong to subsequent changes. Current planner does not claim the complete PATH specification.

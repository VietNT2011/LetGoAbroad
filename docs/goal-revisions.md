# Effective goal revisions / Mục tiêu có ngày hiệu lực

VI: Mục tiêu điểm và ngân sách phút có lịch sử snapshot chỉ thêm mới. Khi đã có enrollment, đổi hai giá trị này trong Settings sẽ tạo revision có hiệu lực ở nửa đêm hợp lệ tiếp theo, tính theo múi giờ hồ sơ trước thao tác đổi. Profile lưu lựa chọn mong muốn ngay; dashboard/lập lịch vẫn dùng revision đang có hiệu lực. Task đang có không bị sửa, kể cả khi revision mới bắt đầu có hiệu lực.

EN: Profile updates, goal snapshots and the scheduled outbox event share the `learning:<user>` transaction lock. Identical retries do not append another revision. Multiple choices before the same effective boundary remain in history; the latest number wins, and earlier choices at that boundary are labelled superseded. A directly queried owner history shows up to twenty recent records plus the active record if it is older.

## Pins and baseline / Ghim và baseline

VI: Task mới ghim `GoalRevisionId`. FK ghép với `EnrollmentId` ngăn tham chiếu revision của người khác. Snapshot lưu số revision, mục tiêu, số phút, thời điểm ghi nhận, thời điểm/ngày có hiệu lực và múi giờ dùng để tính mốc đó. Snapshot không sửa lại các task, câu trả lời, kết quả hay quiz đã hoàn thành.

EN: A new enrollment starts immediately with the learner's selected preferences. An enrollment created before this feature receives an **observed baseline at the actual observation time**, preserving its known revision counter. Old task revision references remain null; unknown historical settings are not invented. Current profile preferences differing from the observed baseline are scheduled for the next boundary. This migration behaviour is covered by relational/API tests.

## API and interface / API và giao diện

- `PUT /api/v1/profile`: existing authenticated, CSRF-protected profile command; goal/time changes append a scheduled revision when needed.
- `GET /api/v1/learning/plan-revisions`: owner-scoped history with active, scheduled, past and superseded status; no parameter grants access to another learner.
- `/learning/dashboard`: active goal/minute budget and revision number; each task exposes its goal revision pin. The profile may intentionally show the newly requested values while today's dashboard retains its earlier plan.
- Settings shows effective timestamps in the timezone recorded with the request and identifies observed upgrade baselines.

VI: Đây là revision cho **mục tiêu và ngân sách phút**. Múi giờ/ngày trong tuần của hồ sơ vẫn theo luồng cấu hình hiện có; calendar đã ghim trong lộ trình không được viết lại. Lịch sử calendar có ngày hiệu lực và chuyển một enrollment đã ghim sang template khác còn cần workflow riêng. Mục tiêu 450/650/800 là ý định của người học, không phải điểm TOEIC được hệ thống dự đoán.

EN: Tests exercise next-midnight boundaries, unchanged daily task IDs/pins, delayed budget application, concurrent duplicate profile writes, supersession, owner isolation, cross-enrollment foreign-key rejection, observed legacy baselines and preferences selected before enrollment. No load or timezone-policy benchmark is claimed.

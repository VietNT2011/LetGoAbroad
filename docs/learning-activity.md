# Learning activity / Nhịp học theo ngày

VI: Dashboard có mục **Hoạt động 7 / 30 ngày**, gồm biểu đồ, tổng số bài hoàn thành/lượt đã chấm và bảng số liệu từng ngày. Chỉ tải dữ liệu khi mở mục này. Đổi khoảng hoặc bấm cập nhật không tạo activity, task, enrollment hay kết quả mới.

EN: `GET /api/v1/learning/activity?days=7` (or `30`) reads only the authenticated user's existing completion sources. The default is seven days including today; other numeric windows return HTTP422 `invalid_activity_window`. All days are returned in ascending order, including zero-activity dates. The response exposes counts and dates, with no question, answer, source-ID or grading-key payload.

## Count policy v1 / Quy tắc đếm

| Metric / Chỉ số | Source / Nguồn |
|---|---|
| Lessons completed / Bài hoàn thành | One persisted `LessonProgress` per user and lesson content version, at `CompletedAt`; fixture lessons excluded |
| Practice attempts / Lượt luyện đã chấm | Graded, nonfixture `mini`, `practice` and `placement` attempts with a nonempty result and valid raw-count bounds, at `AttemptResult.FinalizedAt` |
| Full mock attempts / Full mock đã chấm | Graded, nonfixture `full_mock` attempts under the same result bounds, counted separately |
| Active days / Ngày có hoạt động | Dates in this window with at least one included source |

VI: Quiz sai hoặc chỉ mở bài không tạo `LessonProgress`. Nộp lại cùng receipt, làm lại quiz đã đạt hoặc refresh không nhân số bản ghi đã hoàn thành. Mỗi attempt đã chấm là một lượt riêng, dù cùng đề; không đếm mỗi câu hoặc mỗi task liên quan thành một lượt. Kết quả nộp/được chấm qua nửa đêm thuộc ngày chấm, không phải ngày bắt đầu. Lượt đang làm, bỏ dở, mode `fixture`/`mistake_review`, nội dung fixture và kết quả rỗng không tham gia. Nội dung bị archive sau khi học vẫn giữ hoạt động lịch sử.

EN: These are participation counts. The full-mock count includes attempts with disclosed answers and legacy evidence policies; it does not claim unseen-test performance. [Skill evidence](skill-evidence.md) remains the separate disclosure-aware accuracy view. The technical 200-question fixture is excluded from this activity view as well. No score prediction, streak, XP or active-minutes metric is introduced.

## Dates and implementation / Ngày và triển khai

The calendar is the **current profile timezone**, stated beside the date range. Changing that setting re-buckets historical UTC completion timestamps in this view without rewriting any source timestamp. A path's separately pinned assignment calendar is not used for this personal retrospective view. This is distinct from a future immutable local-date activity ledger for streaks/awards.

VI: Server chốt `now` một lần, lọc bỏ timestamp tương lai, lấy cận UTC rộng thêm tối đa 14 giờ và chỉ giữ đúng ngày địa phương sau khi chuyển zone. Không dựng local midnight để truy vấn nên không giả định một ngày luôn có 24 giờ, một giờ chỉ xảy ra một lần hay midnight luôn tồn tại. Ngày bị bỏ qua trong lịch địa phương vẫn có một hàng 0 trong khoảng ngày đã chọn.

The read uses a narrow SQL `UNION ALL` of completion timestamps/types with owner/time filters. It does not load question text, join task-completion fan-out or write projections. Existing owner-leading indexes are retained; no schema migration or historical backfill is needed. No load/capacity benchmark is claimed. Active time still requires the separate sequence/interval/overlap policy in the original specification.

## UI and verification / Giao diện và kiểm chứng

VI: Biểu đồ có nhãn số trực tiếp, ba tổng riêng và bảng HTML với caption/header cho từng ngày. Màu chỉ hỗ trợ phân biệt; người dùng có thể đọc toàn bộ dữ liệu qua bảng bằng bàn phím hoặc trình đọc màn hình. Ngày đã được server đổi sang timezone hồ sơ nên frontend chỉ định dạng DateOnly ở UTC. Request cũ được hủy khi đổi khoảng; response 7 ngày không được gắn nhãn thành 30 ngày trong lúc chờ.

Relational cases cover empty/default/thirty-day windows without read side effects, invalid bounded inputs, owner/auth/key isolation, mode/fixture/result guards, archived content and disclosed full-mock participation, UTC and ±14/−12 date edges, exact first-midnight/now ticks, both daylight-saving transitions, Samoa's skipped 2011 date, current-profile re-bucketing without source edits, and real lesson/submit receipt replays across midnight. See [verification](verification.md) for final run counts and actual browser checks.

VI: Kiểm tra bản đóng gói ngày 18/11/2026 đã đổi nhanh 7/30 ngày, refresh, mở bảng bằng Enter và thử viewport 320px (client/scroll width cùng 305px, bảng 239px). Dữ liệu tài khoản chính là 1 bài / 3 lượt luyện / 0 full mock trong 2 ngày có hoạt động của cửa sổ 30 ngày. API ba tài khoản xác nhận tiến độ, pin và lịch sử attempt không thay đổi do các request activity. Đây là kiểm chứng chức năng cục bộ; chưa có kiểm thử với trình đọc màn hình thực hoặc đo tải production.

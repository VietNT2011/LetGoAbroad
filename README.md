# EnglishHub · LetGoAbroad

Thẻ riêng và lịch ôn SRS: [quy tắc, quyền riêng tư và API](docs/personal-cards.md).
Sửa thẻ có phiên bản và giữ nguyên nội dung lịch sử ôn: [chính sách chỉnh sửa](docs/card-editing.md).
Sổ câu sai, nhập lịch sử và ôn trọn nhóm câu: [nguồn dữ liệu và quy tắc đã nắm](docs/mistakes.md).
Gửi audio theo block, tạm dừng/gửi lại trong cùng tab: [phạm vi phục hồi upload](docs/block-uploads.md).
Mic, nghe bản nháp và thư viện ghi âm riêng: [quyền riêng tư và vòng đời ghi âm](docs/recordings.md).
Lịch ngày dành [tối đa30% ngân sách cho thẻ đến hạn](docs/daily-review.md), chung giới hạn ba nhiệm vụ với bài học/bài luyện.
Dashboard có [thời gian học ước tính](docs/estimated-study-time.md) từ heartbeat có sequence, giới hạn30 giây/lần và hợp nhất các tab trùng giờ.
Có [biểu đồ thời gian theo ngày Hồ sơ](docs/study-time-calendar.md), xử lý ngày đổi giờ và giữ phần giây lẻ trong tổng; vẫn chọn được cửa sổ UTC cũ.
Các tab [xác minh lại tài khoản và chặn request từ phiên cũ](docs/session-consistency.md); bản nháp cùng tài khoản được giữ khi kiểm tra lại.

Nền tảng tự học tiếng Anh bằng ASP.NET Core 10, EF Core 10, SQL Server 2022 và React + TypeScript. Có một hành trình học/luyện chạy được cục bộ, với dữ liệu bền vững và integration test trên database/blob/FFmpeg thật.

**Trạng thái:** learning core đang triển khai. Chưa tuyên bố hoàn thành toàn bộ P0–P3. Xem [checklist nghiệm thu](docs/status.md) và [đặc tả gốc](docs/spec.md) để phân biệt phần đã chạy với phần còn thiếu.

Giao diện dùng màu rõ theo kỹ năng, chữ tương phản cao, nhiệm vụ học nổi bật và menu mobile có bàn phím. Xem [cơ sở thiết kế và kiểm chứng UI](docs/ui-design.md), gồm nguồn nghiên cứu, các tỷ lệ tương phản và giới hạn của bằng chứng.

Thư viện bài học/bài luyện có [liên kết giữ bộ lọc](docs/catalog-filters.md): reload, Back/Forward và đăng nhập từ deep link giữ từ khóa/kỹ năng đã chọn.

## Chạy trên Windows

Mở Docker Desktop, chuyển sang Linux containers, rồi chạy trong thư mục repo:

```powershell
pwsh -NoProfile -File .\scripts\start.ps1
```

Hoặc mở `Start-EnglishHub.cmd`. Script tạo mật khẩu SQL/key Azurite ngẫu nhiên trong `deploy/.env` đã được gitignore, dựng web/API/worker, áp dụng migration và chờ readiness. Các core image đã khóa digest. Lần đầu cần tải image/dependency.
Docker tách [input build UI, API và worker](docs/container-builds.md), giữ cache publish backend khi chỉ đổi giao diện và vẫn rebuild khi đổi seed nhúng.

- Ứng dụng: [http://127.0.0.1:5188](http://127.0.0.1:5188)
- OpenAPI: [http://127.0.0.1:5188/openapi/v1.json](http://127.0.0.1:5188/openapi/v1.json)
- Liveness: `/health/live`; readiness database: `/health/ready`.

Đây là cấu hình local, bind API/SQL/Blob vào loopback. Chưa có public deployment, domain/TLS hoặc cổng gọi đã nghiệm thu.

```powershell
# Dừng stack, giữ toàn bộ dữ liệu trong volumes
pwsh -NoProfile -File .\scripts\stop.ps1
```

Không thay `deploy/.env` sau khi SQL volume đã được khởi tạo; nó chứa thông tin kết nối của chính local stack đó. Không commit file này.

## Tài khoản demo

| Vai trò | Email |
|---|---|
| Learner | `learner@englishhub.local` |
| Admin / CMS | `admin@englishhub.local` |
| ContentEditor | `editor@englishhub.local` |
| Learner bổ sung | `learner2@englishhub.local`, `learner3@englishhub.local` |

Mật khẩu local demo: `EnglishHub@2026`. Nút demo trên trang đăng nhập sử dụng Learner đầu tiên. Demo accounts chỉ được tạo khi bật `Seed:Demo` trong môi trường không phải Production.

Đăng ký mới cần xác minh email. Trong Development/Testing, `GET /api/v1/dev/mailbox?email=...` trả thư local để kiểm thử liên kết. Production không map mailbox; cấu hình `Smtp:Host/Port/User/Password/From` và `PublicOrigin` qua secret/environment nếu triển khai email thật.

## Có thể thử ngay

1. Đăng nhập demo, vào Lộ trình, chọn bắt đầu từ căn bản hoặc làm placement.
   Enrollment mới ghim bản lộ trình đã publish. Người học cũ chọn **Áp dụng mẫu lộ trình**; lịch ngày đang có được giữ. Mẫu Foundation có 3 stage, 14 bài/quiz và 4 mốc practice, mở khóa theo bằng chứng hoàn thành. CMS hỗ trợ draft JSON, kiểm tra DAG và publish version mới; xem [lộ trình có phiên bản](docs/paths.md).
   [Core demo](docs/core-curriculum.md) có 8 bài, 16 câu quiz và 12 câu luyện mới; checkpoint yêu cầu làm đủ bài. Chọn Core thủ công hoặc dùng gợi ý placement Core; không tự chuyển người đang ghim Foundation.
   [Exam Practice starter](docs/exam-curriculum.md) có 6 bài và 25 câu đọc/ngữ pháp mới trong 3 stage / 8 bước. Hai checkpoint giữ nhóm 4 và 5 câu, yêu cầu đủ toàn bộ 8 hoặc 5 câu và đúng ít nhất 60%. Chọn thủ công hoặc từ placement đầy đủ 15–20/20; listening và full mock được biên soạn vẫn còn mở.
   [Placement cần đủ 20 câu](docs/placement-integrity.md): API không cho rút câu/lọc Part dưới nhãn placement, và enrollment kiểm tra snapshot đầy đủ thay vì suy gợi ý từ một phần đề.
2. Mở bài, đọc nội dung Anh–Việt, làm quiz cuối bài, quay lại dashboard và refresh. Tiến độ lấy từ bằng chứng đã lưu.
   Chọn đoạn để [tô sáng/ghi chú riêng](docs/lesson-notes.md); bàn phím có cách tìm nguyên văn đoạn trích. Ghi chú ghim đúng version bài, xuất hiện ở mục đã lưu và có hoàn tác xóa; sửa từ nhiều tab cần ETag hiện tại.
   Settings có [lịch sử mục tiêu/ngân sách](docs/goal-revisions.md): cấu hình mới được lưu ngay nhưng chỉ áp dụng khi lập lịch cho ngày học tiếp theo, giữ nguyên các nhiệm vụ đang có.
   Khi hai tab sửa cấu hình, tab cũ giữ bản đang nhập và yêu cầu [so sánh với server](docs/profile-concurrency.md) trước khi lưu lại.
   Dashboard hiển thị [dữ liệu kỹ năng gần đây](docs/skill-evidence.md): tối đa 50 câu khác nhau, luôn có số mẫu, dưới 5 câu ghi chưa đủ dữ liệu; tách bài luyện và full mock theo lịch sử mở lời giải.
   Có thể [so sánh mẫu 50 câu mới nhất với 50 câu liền trước](docs/skill-comparison.md), xem số đúng/trống và theo Part. Chỉ hiện chênh lệch khi cả hai mẫu đủ ít nhất5 câu; đây là hai nhóm câu khác nhau, không suy điểm thi hay kết luận trình độ.
   [Hoạt động 7/30 ngày](docs/learning-activity.md) có biểu đồ và bảng theo ngày cho bài hoàn thành, lượt luyện và full mock đã chấm. Đọc theo múi giờ Hồ sơ hiện tại, chỉ tính nguồn đã lưu; mở trang hoặc refresh không cộng thêm hoạt động.
   [Ưu tiên củng cố kỹ năng](docs/weak-skill-planning.md) dùng chính mẫu luyện đó khi có ít nhất 5 câu và đúng dưới 60%, vẫn giữ prerequisite, carryover và ngân sách; mỗi task ghi số mẫu/lý do. Ngưỡng này là quy tắc sản phẩm, không phải đánh giá trình độ.
   Có thể [lập lại nhiệm vụ hôm nay](docs/regeneration.md): giữ bài đã bắt đầu/hoàn thành, thay phần chưa bắt đầu trong ngân sách còn lại; hai tab dùng snapshot cũ sẽ được yêu cầu tải lại.
   Trang Lộ trình cho [đặt lịch chuyển mẫu và hủy yêu cầu](docs/path-switching.md), giữ lịch hôm nay và lịch sử lý do/placement; mẫu mới áp dụng từ mốc nửa đêm đã ghi nhận.
   Có thể đặt [lịch riêng có ngày hiệu lực](docs/path-calendars.md) cho mẫu đang học: đổi múi giờ/ngày trong tuần, giữ ngày học đã tích lũy, hủy lịch chờ và đối chiếu xung đột. Nhiệm vụ mới ghim revision calendar; bài đang làm giữ lịch và deadline gốc.
3. Chọn mini test hoặc practice theo Part/số câu. Preview hiển thị số câu thật vì nhóm câu được giữ nguyên.
4. Chọn đáp án, xem trạng thái autosave, nộp bài và đọc lời giải. Mở hai tab có thể thử ETag conflict; submit lặp không tạo kết quả thứ hai.
   Đánh dấu câu khi làm bài, viết ghi chú riêng ở trang chữa bài và tìm lại tại **Câu đã lưu & ghi chú**. Note/bookmark gắn đúng version câu hỏi; xóa note giữ nguyên kết quả và bookmark. Xem [quy tắc ghi chú](docs/annotations.md).
5. Đăng nhập Admin, tạo/sửa draft JSON, preview, publish/archive, hoặc dry-run/import lô tối đa 20 nội dung.
   CMS lộ trình có [ngừng/mở lại đăng ký mẫu](docs/path-retirement.md); người đã ghim vẫn tiếp tục học trên version cũ.
6. Trong CMS, upload audio có nguồn/quyền sử dụng, theo dõi Uploaded → Processing → Ready hoặc Failed. Chỉ Ready `lesson_audio` được gắn vào `mediaAssetId` của một bản nội dung rồi publish.
7. Phát audio, seek/tốc độ; xem job và retry lỗi. Xóa media chưa còn tham chiếu Published sẽ chặn quyền phát mới và đưa vào hàng đợi xóa bền vững.
   Upload/xóa phân biệt [lệnh đã được nhận và xử lý đã hoàn tất](docs/media-acknowledgments.md); phản hồi202 rỗng không còn bị báo thành lỗi parse JSON.

Thư viện có 14 bài nền tảng và quiz, 20 câu grammar khác nhau, mini reading 5 email × 4 câu và bài gõ đáp án 5 câu. Câu điền đáp án có autosave, biến thể biên soạn và [policy chuẩn hóa v1](docs/answer-policy.md), áp dụng được cả trong lesson quiz. Bài có audio Ready và transcript biên soạn hỗ trợ [nghe theo đoạn, lặp câu, dictation và tiếp tục vị trí nghe](docs/segmented-listening.md); CMS nhận mảng `segments`. Nội dung tự biên soạn được ghi nguồn trong [seed](seed/README.md).

Full mock 200 câu là **dữ liệu demo kỹ thuật**, kiểm thử counts/group/timer/grading. Nó có Listening 45 phút rồi Reading 75 phút theo giờ server, nhưng không phải bộ đề TOEIC LR thực đã biên soạn/kiểm định. Fixture không tham gia thống kê thành tích. Chỉ raw score, không quy đổi điểm 990.

## Kiểm thử

```powershell
# Database quan hệ SQLite: unit, API, contract và concurrency cơ bản
pwsh -NoProfile -File .\scripts\test.ps1

# SQL Server thực
pwsh -NoProfile -File .\scripts\test.ps1 -SqlServer

# SQL Server + Azurite + ffprobe/FFmpeg thật
pwsh -NoProfile -File .\scripts\test.ps1 -SqlServer -Storage

# Frontend
npm --prefix web ci
npm --prefix web run audit:ci
npm --prefix web test
npm --prefix web run build
```

Chạy `start-infra.ps1` trước các test SQL/Storage nếu stack chưa chạy. Mỗi fixture SQL dùng database tên `EnglishHubTest_<guid>` riêng, không dùng database ứng dụng. Các test DB có FakeClock; test storage vẫn truyền file thật và kiểm tra HTTP 206/ETag/ACL thật. TRX nằm trong `artifacts/TestResults` khi dùng script.

CI có job build/SQLite/frontend và job SQL/Azurite/FFmpeg riêng. Kết quả workflow remote chỉ được coi là đã qua khi chính workflow đó chạy thành công; các lần chạy local được ghi trong tài liệu bàn giao.
Gate dependency chỉ chấp nhận [một lỗi dev-only picomatch đã ghi rõ](docs/frontend-dependencies.md); lỗi mới hoặc dependency production có cảnh báo sẽ chặn CI.

## Cấu trúc và quy tắc

```text
src/EnglishHub.Domain          # Bất biến, entity, blueprint
src/EnglishHub.Application     # Clock, planner, provider boundaries
src/EnglishHub.Infrastructure  # Identity, EF, use cases, blob, FFmpeg, jobs
src/EnglishHub.Api             # DTO endpoints, CSRF, OpenAPI, static web
src/EnglishHub.Worker          # Media, expiry, retention
web/                          # React/TypeScript, same-origin requests
tests/EnglishHub.Tests/        # Unit/API/SQL/Storage/contract scenarios
deploy/                       # Docker images and compose
docs/adr/                     # Quyết định và tradeoff
seed/                         # Nội dung nguyên gốc, attribution
```

Comment nghiệp vụ song ngữ Việt–Anh. Published payload được giữ nguyên; thay đổi nội dung tạo version mới. Cookie HttpOnly, CSRF trên browser writes, DTO đề tách khỏi GradingKey. Application locks/rowversion/unique constraints cùng bảo vệ concurrency. Blob/FFmpeg không nằm trong transaction SQL; lease token ràng buộc commit của worker.

API/Worker dùng chung use case. Outbox đã được lưu cùng transaction; realtime fanout/consumer inbox thuộc P2 và chưa bật. Redis/LiveKit compose profiles hiện chỉ chuẩn bị hạ tầng, chưa có chat/call integration. AI luôn tắt.

## Phát triển frontend

Node 22, SDK được chọn trong `global.json`; dependencies được khóa bằng lockfiles. Vite proxy mặc định gọi API port 5188:

```powershell
npm --prefix web run dev
```

Trong môi trường Windows hiện tại, Application Control từng chặn DLL mới tạo; các script test sử dụng Linux SDK container để giữ nguyên chính sách Windows. Full-stack Docker là đường chạy đã kiểm chứng.

Xem [status](docs/status.md) để tiếp tục theo đúng thứ tự P0 → P1 → P2 → P3. Chưa công bố benchmark, hiệu năng tải, chất lượng điểm dự đoán hoặc độ tin cậy cuộc gọi.

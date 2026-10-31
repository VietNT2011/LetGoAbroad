# EnglishHub · LetGoAbroad

Nền tảng tự học tiếng Anh bằng ASP.NET Core 10, EF Core 10, SQL Server 2022 và React + TypeScript. Có một hành trình học/luyện chạy được cục bộ, với dữ liệu bền vững và integration test trên database/blob/FFmpeg thật.

**Trạng thái:** learning core đang triển khai. Chưa tuyên bố hoàn thành toàn bộ P0–P3. Xem [checklist nghiệm thu](docs/status.md) và [đặc tả gốc](docs/spec.md) để phân biệt phần đã chạy với phần còn thiếu.

## Chạy trên Windows

Mở Docker Desktop, chuyển sang Linux containers, rồi chạy trong thư mục repo:

```powershell
pwsh -NoProfile -File .\scripts\start.ps1
```

Hoặc mở `Start-EnglishHub.cmd`. Script tạo mật khẩu SQL/key Azurite ngẫu nhiên trong `deploy/.env` đã được gitignore, dựng web/API/worker, áp dụng migration và chờ readiness. Các core image đã khóa digest. Lần đầu cần tải image/dependency.

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
2. Mở bài, đọc nội dung Anh–Việt, làm quiz cuối bài, quay lại dashboard và refresh. Tiến độ lấy từ bằng chứng đã lưu.
3. Chọn mini test hoặc practice theo Part/số câu. Preview hiển thị số câu thật vì nhóm câu được giữ nguyên.
4. Chọn đáp án, xem trạng thái autosave, nộp bài và đọc lời giải. Mở hai tab có thể thử ETag conflict; submit lặp không tạo kết quả thứ hai.
   Đánh dấu câu khi làm bài, viết ghi chú riêng ở trang chữa bài và tìm lại tại **Câu đã lưu & ghi chú**. Note/bookmark gắn đúng version câu hỏi; xóa note giữ nguyên kết quả và bookmark. Xem [quy tắc ghi chú](docs/annotations.md).
5. Đăng nhập Admin, tạo/sửa draft JSON, preview, publish/archive, hoặc dry-run/import lô tối đa 20 nội dung.
6. Trong CMS, upload audio có nguồn/quyền sử dụng, theo dõi Uploaded → Processing → Ready hoặc Failed. Chỉ Ready `lesson_audio` được gắn vào `mediaAssetId` của một bản nội dung rồi publish.
7. Phát audio, seek/tốc độ; xem job và retry lỗi. Xóa media chưa còn tham chiếu Published sẽ chặn quyền phát mới và đưa vào hàng đợi xóa bền vững.

Thư viện có 14 bài nền tảng và quiz, 20 câu grammar khác nhau, mini reading 5 email × 4 câu và bài gõ đáp án 5 câu. Câu điền đáp án có autosave, biến thể biên soạn và [policy chuẩn hóa v1](docs/answer-policy.md), áp dụng được cả trong lesson quiz. Nội dung tự biên soạn được ghi nguồn trong [seed](seed/README.md).

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
npm --prefix web run build
```

Chạy `start-infra.ps1` trước các test SQL/Storage nếu stack chưa chạy. Mỗi fixture SQL dùng database tên `EnglishHubTest_<guid>` riêng, không dùng database ứng dụng. Các test DB có FakeClock; test storage vẫn truyền file thật và kiểm tra HTTP 206/ETag/ACL thật. TRX nằm trong `artifacts/TestResults` khi dùng script.

CI có job build/SQLite/frontend và job SQL/Azurite/FFmpeg riêng. Kết quả workflow remote chỉ được coi là đã qua khi chính workflow đó chạy thành công; các lần chạy local được ghi trong tài liệu bàn giao.

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

# EnglishHub / LetGoAbroad

Nền tảng tự học tiếng Anh. Backend ASP.NET Core 10, EF Core 10, SQL Server 2022; frontend React + TypeScript. Không cần AI API key.

## Phát triển / Development

SDK được khóa trong `global.json`. Chạy `dotnet test EnglishHub.slnx` và `dotnet run --project src/EnglishHub.Api --urls http://localhost:5080`.

Đặc tả gốc: [docs/spec.md](docs/spec.md). Trạng thái nghiệm thu được cập nhật theo từng milestone; không đồng nhất biên dịch thành công với nghiệm thu P0–P3.

## Quy ước / Conventions

- Mỗi tính năng có commit riêng. Comment nghiệp vụ song ngữ Việt–Anh.
- Nội dung seed là dữ liệu demo do project biên soạn; không phải đề ETS.
- Cookie HttpOnly; dữ liệu học/thi bền vững trong database; không lưu đáp án đúng trong DTO đề đang làm.
- Chỉ raw score; không tự quy đổi điểm TOEIC 990.

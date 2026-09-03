# ADR-001 — Modular monolith / Kiến trúc module

API và Worker dùng chung Domain, Application và Infrastructure. EF Core là cổng database; use case sở hữu transaction. Identity, Content, Assessment, Learning, Review, Community, Media và Operations có service riêng.

SQL Server là database triển khai chuẩn. SQLite chỉ là profile demo/test thuận tiện trên máy không chạy Docker; không thay thế integration test SQL Server khi nghiệm thu race/locking.

Frontend gọi cùng origin qua Vite proxy trong phát triển và static hosting trong bản build. Không đăng ký hoặc xuất bản Sites vì yêu cầu hiện tại là triển khai cục bộ trên Desktop.

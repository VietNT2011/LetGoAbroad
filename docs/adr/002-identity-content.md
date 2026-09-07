# ADR-002/003 — Cookie auth and immutable content

VI: Cookie Identity HttpOnly cùng origin, token antiforgery trước mọi API ghi. Xác minh email/reset dùng token Identity có thời hạn; reset đổi security stamp để chống tái dùng. Hộp thư DB chỉ được map trong Development/Testing, SMTP thật trong Production. Người biên soạn tạo draft/version mới; chỉ Admin publish. Composite FK bảo đảm published pointer cùng root. DTO công khai không serialize graph đáp án.

EN: Identity owns credentials and cookies. Unsafe browser requests require antiforgery. Published content is versioned; history pins immutable versions. SQL Server rowversion drives ETags; SQLite uses random application concurrency tokens as a demo/testing compatibility profile. SQL Server remains the acceptance target for locking and concurrent commands.

Giới hạn hiện tại: seed full-mock là fixture Part 5 cho timer/grade, không đáp ứng blueprint TOEIC 7 Part; không đánh dấu EXAM full-mock hoàn tất.

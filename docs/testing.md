# Database test lifecycle / Vòng đời database kiểm thử

VI: `scripts/test.ps1 -SqlServer -Storage` dùng SQL Server/Azurite của compose nhưng mỗi `TestApp` tạo database riêng tên `EnglishHubTest_<uuid32>`. Database ứng dụng local là `EnglishHub`. Chuỗi kết nối được truyền bằng environment, không in mật khẩu trong command hay TRX.

EN: A fixture disposes its WebApplicationFactory host, clears only its own connection pool and drops only its generated database. Cleanup validates the exact fixed prefix and 32 hexadecimal UUID characters before interpolating an SQL identifier. Both xUnit async fixture disposal and directly-created `await using` hosts run the same idempotent cleanup, including after failed assertions. SQLite fixtures clear their own pool and remove their disposable database/WAL files. The health/OpenAPI tests assert that their database is gone after disposal.

VI: Tối đa hai test collections tạo schema đồng thời. Các test concurrency vẫn chủ động gửi nhiều request/job claim trong cùng test, nên không mất kiểm tra tranh chấp do giới hạn fixture parallelism.

EN: Repeated local runs previously left 115 fixture databases in the SQL container. SQL eventually reported insufficient resource-pool memory and stopped. Those verified fixture databases were removed on 10/11/2026; the fixture lifecycle now prevents that accumulation. The local application database and existing learning data remain available.

Kết quả được ghi ở `artifacts/TestResults/results.trx`; workflow SQL/media upload file này làm artifact. Khi test fail, đọc lỗi gốc và TRX; fixture database không phải nơi lưu bằng chứng lâu dài.

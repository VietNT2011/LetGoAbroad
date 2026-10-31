# Database test lifecycle / Vòng đời database kiểm thử

VI: `scripts/test.ps1 -SqlServer -Storage` dùng SQL Server/Azurite của compose nhưng mỗi `TestApp` tạo database riêng tên `EnglishHubTest_<uuid32>`. Database ứng dụng local là `EnglishHub`. Chuỗi kết nối được truyền bằng environment, không in mật khẩu trong command hay TRX.

EN: A fixture disposes its WebApplicationFactory host, clears only its own connection pool and drops only its generated database. Cleanup validates the exact fixed prefix and 32 hexadecimal UUID characters before interpolating an SQL identifier. Both xUnit async fixture disposal and directly-created `await using` hosts run the same idempotent cleanup, including after failed assertions. SQLite fixtures clear their own pool and remove their disposable database/WAL files. The health/OpenAPI tests assert that their database is gone after disposal.

VI: Các test collections tạo schema tuần tự. Các test concurrency vẫn chủ động gửi nhiều request/job claim trong cùng test, nên không mất kiểm tra tranh chấp do giới hạn fixture parallelism.

EN: Repeated local runs previously left 115 fixture databases in the SQL container. SQL eventually reported insufficient resource-pool memory and stopped. Those verified fixture databases were removed on 10/11/2026; the fixture lifecycle now prevents that accumulation. The local application database and existing learning data remain available.

Kết quả được ghi ở `artifacts/TestResults/results.trx`; workflow SQL/media upload file này làm artifact. Khi test fail, đọc lỗi gốc và TRX; fixture database không phải nơi lưu bằng chứng lâu dài.

## Local SQL memory / Bộ nhớ SQL cục bộ

Compose caps the SQL container at 3 GiB and sets `MSSQL_MEMORY_LIMIT_MB=2048` to leave headroom for components outside the engine. This follows Microsoft's [SQL Server on Linux memory guidance](https://learn.microsoft.com/en-us/sql/linux/configure/performance-best-practices-sql-server-memory?view=sql-server-ver17). It is a local development setting, not a measured production capacity recommendation.

On 16/11/2026, a repeated run exposed `RESOURCE_SEMAPHORE` waits and an API test timeout. The skill query now ranks IDs/skills before joining answer/variant text, reducing the width of its window-sort input. The internal memory limit was also made explicit. The failed run was interrupted with SIGINT; its two remaining databases were identified by exact generated names and removed before the rerun. Ordinary fixture disposal still handles completed/failed assertions; abrupt process termination can bypass that disposal. Never remove a database based on an unverified name or touch the application's `EnglishHub` database.


On 17/11/2026, a cold local SQL/media run exhausted the internal/default memory pools and the SQL process stopped (43 passed / 154 failed). Three exactly verified fixture databases left by that interruption were removed after SQL restarted. Collections now run one at a time while individual race tests retain their explicit parallel requests. Azurite also rejected a playback grant 27 seconds after its expiry: the shared test clock had remained frozen during the slow setup. The storage upload helper now refreshes that fixture clock after login so real-time SAS checks remain meaningful; the strict post-delete HTTP404 assertion and production grant lifetime are retained. No application/media production policy was changed.

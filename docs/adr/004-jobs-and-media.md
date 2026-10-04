# ADR-004/005 — Durable worker, media storage and playback

VI: API cấp SAS chỉ ghi một staging blob trong 15 phút sau khi giữ quota bằng SQL transaction. Complete HEAD blob, ghim ETag và enqueue job trong transaction. Worker đóng băng nguồn vào canonical bằng If-Match; canonical không có upload grant. FFprobe kiểm tra định dạng/thời lượng, FFmpeg decode/transcode audio trong process riêng, có timeout, giới hạn protocol và giới hạn CPU/RAM container.

EN: SQL application locks serialize queue claims across processes. Each claim receives a fresh fencing token and 90-second lease, renewed every 20 seconds. Retries use 15/60/300-second backoff, at most four claims. Database state cannot become Ready without the current, unexpired lease. Output paths include asset generation and claim token to isolate stale workers. Result/outbox commit is atomic; polling is the P0 notification fallback.

Private playback checks owner or a published lesson reference, then issues a two-minute read-only SAS. Personal recordings are not publishable as lesson audio. Blob storage handles real byte ranges. Browser playback failures can refresh authorization while retaining position.

Verified integration cases: valid WAV → real FFmpeg MP3 → Ready; private ACL; HTTP 206 byte range; staging rewrite after complete → Failed; fake WAV → decode failure; two workers claiming one job; stale lease cannot finalize after reclaim.

Remaining work: durable deletion/orphan cleanup, manual retry UI, longer media corpus with authored segments, video/HLS, resumable block uploads, per-purpose quota policies and reconciliation reporting. No P0/P1 gate is declared complete until its remaining acceptance criteria pass.

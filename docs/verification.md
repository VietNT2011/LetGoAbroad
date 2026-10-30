# Verification record / Báo cáo kiểm thử

Verified locally on **09/11/2026**. Functional source commit: `eb0e46eca6fc841ad78568a10669fa7eabac661c`. Later documentation-only commits do not change the tested implementation.

| Check | Observed result |
|---|---|
| `scripts/test.ps1 -SqlServer -Storage` | **29 passed, 0 failed, 0 skipped**; real SQL Server, Azurite, ffprobe and FFmpeg |
| `scripts/test.ps1` | **23 passed, 0 failed, 0 skipped**; the six Storage-category cases are excluded by the selected profile |
| `dotnet restore EnglishHub.slnx --locked-mode` | Passed in the pinned Linux SDK |
| `npm --prefix web run build` | Passed; TypeScript and Vite production output |
| `npm audit --omit=dev` | 0 production dependency advisories after updating React Router to 7.18.5 |
| Docker API/web + worker build | Passed; non-root runtime, pinned base images |
| `/health/ready` and `/api/v1/auth/csrf` | HTTP 200 after correcting the key-volume ownership |
| Git whitespace check | `git diff --check` passed |

SDK: 10.0.403. Runtime: 10.0.14. Core image digests and the observed FFmpeg package are recorded in `deploy/images.lock.json`.

## Browser checks actually performed

1. Demo login → mini test → select one answer → immediately submit. The client waited for save acknowledgement; result was 1/20 with the expected explanation.
2. Packaged same-origin application → create enrollment → complete the first lesson quiz → refresh dashboard. One completed lesson remained persisted.
3. Reading practice target of two questions → preview reported four questions in one complete passage group → start showed exactly four questions sharing the passage → submit gave 1/4 with matching explanations.
4. Vocabulary/community navigation is visibly marked “Sắp có”, so unfinished modules are not presented as working features.

## Coverage and limits

The automated suite includes CSRF/role enforcement, verification/reset tokens, private ownership, no active answer-key disclosure, immutable publication, atomic imports and answer batches, idempotency conflicts, stale ETags, exact deadline and section boundaries, duplicate finalization, source-backed daily progress, whole-group preview, older pinned versions, SQL job leases, real media decoding, private playback/ranges, source ETag mutation, deletion, retention and manual retry history.

These observations are not a full P0–P3 acceptance claim. Multi-device/offline browser automation, path-template graphs, FillBlank, richer listening/video, SRS, chat, calls and load benchmarks remain as listed in [status.md](status.md). Remote GitHub Actions were not executed locally and must be checked separately after an authorized push.

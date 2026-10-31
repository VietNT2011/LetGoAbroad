# Verification record / Báo cáo kiểm thử

Baseline verified locally on **09/11/2026** at `eb0e46eca6fc841ad78568a10669fa7eabac661c`. Typed answers, private question annotations, versioned learning paths and future goal revisions verified on **09–10/11/2026**, including migration of the existing local SQL database and browser recovery after restarting Docker.

| Check | Observed result |
|---|---|
| `scripts/test.ps1 -SqlServer -Storage` | **80 passed, 0 failed, 0 skipped**; real SQL Server, Azurite, ffprobe and FFmpeg |
| `scripts/test.ps1` | **74 passed, 0 failed, 0 skipped**; the six Storage-category cases are excluded by the selected profile |
| `dotnet restore EnglishHub.slnx --locked-mode` | Passed in the pinned Linux SDK |
| `npm --prefix web run build` | Passed; TypeScript and Vite production output |
| `npm audit --omit=dev` | 0 production dependency advisories after updating React Router to 7.18.5 |
| Docker API/web + worker build | Passed; non-root runtime, pinned base images |
| `/health/ready` and `/api/v1/auth/csrf` | HTTP 200 after correcting the key-volume ownership |
| Git whitespace check | `git diff --check` passed |
| Test database lifecycle | **0 remaining** `EnglishHubTest_<uuid32>` databases after the full SQL/media run; direct `await using` hosts also assert cleanup |

SDK: 10.0.403. Runtime: 10.0.14. Core image digests and the observed FFmpeg package are recorded in `deploy/images.lock.json`.

## Browser checks actually performed

1. Demo login → mini test → select one answer → immediately submit. The client waited for save acknowledgement; result was 1/20 with the expected explanation.
2. Packaged same-origin application → create enrollment → complete the first lesson quiz → refresh dashboard. One completed lesson remained persisted.
3. Reading practice target of two questions → preview reported four questions in one complete passage group → start showed exactly four questions sharing the passage → submit gave 1/4 with matching explanations.
4. Vocabulary/community navigation is visibly marked “Sắp có”, so unfinished modules are not presented as working features.
5. Typed-answer mini test → type ` SEND ` → reload retained the raw text and saved state → enter ` I   AM ready. ` → immediately submit. Result was **2/5 with three blanks**, showing the original text, accepted variants and policy v1. Active payload did not disclose variants; API tests also cover rejected mixed answer types, length, exact deadline, stable receipts and old/new publication variants.
6. On that persisted result, bookmark a question and save a private note. A second browser tab updated the note; saving from the stale first tab returned 412 while preserving its draft. Reload-for-comparison showed the newer server text next to the draft. Explicitly saving the reconciled note then opening **Câu đã lưu & ghi chú** showed the bookmark, correct source title and note. The result remained 2/5.
7. Migrated the existing local learner to the published Foundation path explicitly. The UI retained the previously completed lesson, showed **1/18 steps**, the pinned v1 and future-day/prerequisite locks. Signing out, logging in again and reopening the path retained this state.
8. In the browser CMS, saved a one-node technical draft with a self prerequisite. Validation returned the cycle error. Changed only the graph to remove that edge, saved it, validated the source/DAG and published successfully. The editor then disabled draft editing/publication. A clearly labelled technical demonstration template remains in the local demo database.
9. In Settings, requested 800/20 minutes. History showed the new revision scheduled for **00:00 11/11/2026, Asia/Ho_Chi_Minh**, while the path still showed the active 650/30 plan. Restored the original demo preferences 650/30; the earlier pending choice was labelled superseded and its history remained visible. Automated clock tests verify activation at the boundary and immutable task revision pins.

## Coverage and limits

The automated suite includes CSRF/role enforcement, verification/reset tokens, private ownership, no active answer-key disclosure, immutable publication, atomic imports and answer batches, idempotency conflicts, stale ETags, exact deadline and section boundaries, duplicate finalization, source-backed daily progress, whole-group preview, older pinned versions, SQL job leases, real media decoding, private playback/ranges, source ETag mutation, deletion, retention and manual retry history.

Path coverage also includes relational rejection of cross-version edges/mismatched task content, graph-only ETags, old path/content pins after republishing, actual quiz/threshold evidence, distinct attempts for repeated practice, carryover chains, midnight continuation and recovery of a result committed during rollover. See [paths.md](paths.md).

These observations are not a full P0–P3 acceptance claim. Broader multi-device/offline browser automation, effective-date calendar revisions, lesson-body notes/highlights, richer listening/video, SRS, chat, calls and load benchmarks remain as listed in [status.md](status.md). Both remote GitHub Actions jobs passed for [baseline `8f7a642`](https://github.com/VietNT2011/LetGoAbroad/actions/runs/42649802535), [typed answers `f09dba6`](https://github.com/VietNT2011/LetGoAbroad/actions/runs/42726293233), [fixture disposal `596cde1`](https://github.com/VietNT2011/LetGoAbroad/actions/runs/42727668195), [private notes `e2fce48`](https://github.com/VietNT2011/LetGoAbroad/actions/runs/42728331978) and [versioned paths `f0a57ac`](https://github.com/VietNT2011/LetGoAbroad/actions/runs/42733786173); later pushes require separate checks.

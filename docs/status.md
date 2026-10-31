# Trạng thái triển khai / Implementation status

This is a working local learning-core implementation, not a claim that the full P0–P3 specification has passed acceptance. The original handover remains in [spec.md](spec.md).

| Milestone | Implemented and checked | Remaining acceptance |
|---|---|---|
| M00 | Modular .NET solution, React, pinned core image digests, Compose, API/worker processes, health, SQL migrations, lockfiles, CI workflows; both remote jobs passed for `8f7a642` | Each later push needs its own CI result; no load benchmark claimed |
| M01 | Identity cookie, CSRF, role checks, verification/reset, local mailbox/SMTP adapter, profile, version registry, drafts/edit/preview/publish/archive, atomic JSON import and dry-run | Broader CMS authoring UX, detailed taxonomy and complete source metadata schema |
| M02 | Pinned public/private snapshots, atomic autosave, payload-bound receipts, exact owner/option checks, deadline and section boundaries, single result/outbox, practice whole-group preview, result explanations; FillBlank variants and versioned normalization in assessments/lesson quizzes; private question bookmarks/notes with versioned anchors and saved list | Text-selection highlights and lesson-body notes, broad multi-tab/offline browser automation and a reviewed full LR content bank |
| M03 | Enrollment, deterministic budget planner, local dates, reason codes, quiz-backed lesson progress, source-backed daily completion and statistics | Versioned path-template graph, prerequisites/cycle validation, authored 14-day task schedule, missed-task carryover, last-50 skill evidence and goal revisions |
| M04 | Azurite/private blob, quota reservation, scoped staging grants, ETag freeze, ffprobe/FFmpeg, fenced leases, retry history, HTTP range playback, durable deletion and age-buffer cleanup | Resumable block uploads, a full authored listening corpus, broader crash/fault injection, operational reconciliation reports |
| P0 gate | Login → path → lesson quiz → persisted progress; practice → autosave → grade → explanations; real media integration tested | **Open** until every remaining P0 criterion is satisfied |
| P1 | Personal-recording upload purpose/API boundary exists | Dictation/segments, video/HLS/subtitles, IPA, dictionary, cards/SRS, mistake notebook, recording UI and analytics are not implemented |
| P2 | Outbox records persist; compose has an optional Redis profile | Groups, memberships, invites, durable chat, SignalR, presence, shared playback, notifications and XP are not implemented |
| P3 | Optional LiveKit infrastructure profile is retained for future work | Provider adapter, admissions, tokens/webhooks, reconnect, host controls and two-network call acceptance are not implemented |
| P4 | Disabled | Deferred as requested; no LLM/STT calls or AI API keys |

The vocabulary/community navigation is marked “Sắp có”. Infrastructure profiles alone do not imply that the corresponding product integration works.

## Next implementation order

1. Close the remaining P0 acceptance gaps, especially path templates, lesson-body notes/highlights and listening content. See [typed answers](answer-policy.md), [question annotations](annotations.md) and [database test lifecycle](testing.md).
2. Complete the P0 gate with repeatable browser/offline scenarios, then implement M05/M06.
3. Implement M07/M08 and verify membership revocation/catch-up before enabling Community.
4. Implement M09 and real two-network calls before enabling Calls.

## Verification evidence

- SQL Server + Azurite + actual FFmpeg integration suite covers owner isolation, key nondisclosure, CSRF, email lifecycle, concurrency, receipt replay/conflicts, atomic import/answer batches, deadlines, sections, task completion, blob immutability, decode rejection, byte ranges, lease fencing, deletion, retention and retry history.
- SQLite is a supplemental CI/demo profile. UTC tick conversion preserves timestamp precision so replay responses remain stable. Existing SQLite development databases from an older codec are not an upgrade target; use a new disposable SQLite database or SQL migrations.
- Browser checks completed: demo login; mini test, immediate save-then-submit and explanations; packaged app enrollment, lesson quiz, dashboard refresh retaining progress.
- Production npm dependencies were audited and the reported React Router advisory was patched. No measured p95, concurrent-user capacity, call reliability or educational-score validity is claimed.

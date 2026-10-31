# Recent skill evidence / Dữ liệu kỹ năng gần đây

`GET /api/v1/learning/skill-evidence` returns aggregates for the signed-in learner. The dashboard shows the sample size beside every result. It does not predict a TOEIC score or label a learner's ability from a small sample.

## Window and correctness

- Each track/skill uses at most **50 distinct question versions** from durable graded attempts. Repeating the same version replaces its older eligible answer; it does not add another observation.
- Order is result finalization time, attempt ID, then item order, descending. A SQL window query ranks narrow item IDs and skill labels first, then joins answer/variant text only for the selected rows; it does not sort the full answer payload. The learner ID is parameterized from the authenticated session.
- Fewer than **5 observations** returns `insufficient_data` and `accuracyPercent: null`. Counts remain visible. Otherwise, percentage is correct/observed rounded to the nearest integer, with halves rounded away from zero.
- Blank answers remain in the denominator. Part counts describe that same bounded sample. Multiple-choice correctness and FillBlank normalization use the attempt's pinned private grading policy.
- The response exposes counts, skill, Part and percentage only. It contains no question text, submitted answers, accepted variants or correct option IDs.

## Separate conditions

The `practice` track includes mini tests, practice and placement. `full_mock_unseen` includes full-mock items only when no other attempt disclosed that question version's answer at or before this attempt started. A full mock can therefore contribute fewer eligible observations than its total question count. Its own result disclosure does not remove its evidence.

The first successful owner request for an attempt's result records `FirstDisclosedAt` under the attempt transaction lock. Later requests preserve that timestamp. An outsider cannot set it. Disclosure after another attempt has already started does not change that earlier starting condition. This measures server-recorded answer disclosure; it cannot detect knowledge obtained outside the application.

Technical fixtures, the `mistake_review` mode and ungraded/abandoned attempts are excluded. A repetition with no prior recorded disclosure contributes at most one observation for that question version in a track. There is no cross-track sum presented as a single performance estimate.

## Existing databases

Migration `AssessmentEvidenceDisclosure` adds nullable disclosure time and an evidence policy version. New attempts use version 1. Existing attempts remain version 0 because their historical first disclosure time is unknown; the migration does not invent one.

Legacy full mocks are excluded from the unseen track. A legacy graded occurrence also conservatively excludes the same question version from a later full mock when the legacy attempt began at or before it. Legacy practice can still contribute to practice counts. This favors an explicit missing sample over claiming an unknown history was unseen.

## Verification and remaining work

Relational API tests exercise the 50-item cap, replacement of repeated questions, sparse/blank samples, typed normalization, fixture/review exclusions, disclosure timing, legacy history, owner isolation and answer-key nondisclosure on SQL Server and SQLite.

This closes the recent-skill-window portion of PATH-04. [Reinforcement allocation](weak-skill-planning.md) reuses these typed aggregates for practice-based task priorities. Activity duration, trends over time and additional taxonomy/tag breakdowns remain separate work. No calibrated educational validity or external benchmark is claimed.

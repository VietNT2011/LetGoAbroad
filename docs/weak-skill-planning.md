# Reinforcement allocation / Phân bổ củng cố kỹ năng

VI: Lịch mới và thao tác **Lập lại bài chưa bắt đầu** dùng cùng dữ liệu với [thống kê kỹ năng](skill-evidence.md). Quy tắc củng cố v1 chỉ ưu tiên kỹ năng có **ít nhất 5 câu luyện khác nhau** trong cửa sổ tối đa 50 câu và **tỷ lệ đúng dưới 60%**. Đây là ngưỡng vận hành được chọn cho sản phẩm, chưa được kiểm định như chẩn đoán năng lực hoặc phương pháp tăng điểm ngoại ngữ.

EN: Only the `practice` track (mini/practice/placement) participates. Full mocks remain a separate observation condition and never get pooled into this allocation. Technical fixtures, mistake review and ungraded attempts retain the shared evidence service's exclusions. Repeated question versions replace older answers; blanks count in the denominator and typed answers use their pinned normalization policy. No second eligibility query or alternate correctness calculation is maintained in the planner.

## Ordering and constraints

1. Reserve assignments that are completed, started, already attempted or live across a date/calendar boundary. Their time and slots remain protected by regeneration.
2. For a pinned path, discard completed/pending/reserved nodes, future study days and unsatisfied prerequisites **before** ranking. An observation cannot unlock content or credit progress.
3. Keep eligible missed-task carryover first, in its original deterministic order and with its source link/reason.
4. Within the remaining eligible candidates, choose a lesson, then a practice, then fill remaining slots. Rank skills by the lowest exact correct/observed fraction, breaking equal fractions by ordinal skill key. Within a skill, retain authored study day/order/ID. Skills without qualifying evidence follow the normal order. An unaffordable candidate does not displace an affordable candidate.
5. Keep at most three assignments within the remaining minute budget, without duplicate content. Entire content versions remain intact. Legacy enrollments use the same evidence/ranking within their existing catalog-based selection, until they explicitly adopt a path.

The comparison uses the raw fraction: 3/5 is exactly 60% and does not qualify. Rounded display percentages never decide eligibility. Ranking uses `ContentVersion.Skill`; a mixed-skill assessment's authored primary skill is its allocation label, while question-level observations still come from grading snapshots. This is not topic/tag-level personalization.

## Saved explanations and stale tabs

Fresh prioritized tasks have `reasonCode: WEAK_SKILL`. Their saved explanation names the skill, observed correct/total count, window, threshold and reinforcement rule version; a pinned path also explains its authored stage/day/prerequisite eligibility. Carryover keeps `PATH_CARRYOVER` and the original source date. A refresh never rewrites the explanation or silently replaces existing tasks. If regeneration chooses identical assignments, its existing no-op rule retains their original IDs and explanations even if evidence changed.

EN: The plan ETag now includes the ordered owner practice aggregates and reinforcement policy version, in addition to calendar, goal, path and task versions. Grading an unassigned library assessment can therefore invalidate a loaded plan. Regeneration returns 412 and asks for a fresh snapshot. It still requires explicit learner action and keeps payload-bound acknowledgements stable on retry, including after later evidence changes.

VI: Chấm bài thủ công và worker hết giờ đều lấy khóa transaction `learning:<user>` **trước** `attempt:<id>`. Lập lịch giữ khóa learning trong khi đọc mẫu, chọn task và tạo ETag, nên kết quả không chen giữa các bước này. Autosave và mở lời giải chỉ cần khóa attempt và không lấy learning sau đó; thứ tự khóa không tạo vòng chờ. Không đổi deadline hay điều kiện chấm bài.

## Limits and verification

This does not allocate due reviews, create SRS cards, repeat completed path nodes, change a pinned path version, revise existing task history or infer learning gains. With insufficient evidence, unavailable matching content, locked/future nodes or exhausted budget, normal scheduling/fallback applies. No new migration is needed.

Relational tests cover minimum/strict thresholds, exact-fraction ties, sparse evidence, separate full mocks, fixture/review exclusions, due/prerequisite filtering, affordable candidates, carryover priority, lesson/practice budgeting, immutable task reasons, stale snapshots after unassigned grading, concurrent grading/regeneration, retained started tasks, exact receipt replay, legacy selection and owner isolation. The existing evidence tests cover the 50-distinct window and typed normalization; existing regeneration/assessment suites cover completion, quotas, midnight, autosave/expiry and submit replay.

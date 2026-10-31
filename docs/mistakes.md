# Mistake notebook / Sổ câu sai

VI: **Sổ câu sai** ở `/review/mistakes` có lọc trạng thái, đến hạn, Part và kỹ năng, phân trang30 câu, lời giải gốc và nút ôn trọn nhóm. Các câu sai/bỏ trống từ bài luyện, mini, placement và full mock đã chấm được ghi nhận. Fixture kỹ thuật và phiên ôn tự tạo không có target không tạo câu sai mới.

## Sources and import / Nguồn và nhập lịch sử

New grading updates the notebook atomically with its original result, receipt and outbox transaction, under the same owner learning lock. Each question version has one owner-scoped MistakeItem. WrongCount includes wrong/blank original answers and targeted wrong recall answers, capped at Int32.MaxValue. Original AttemptItems/GradingKeys/results remain unchanged. A processed-attempt marker prevents counting the same finalization again.

Existing historical results are not rewritten or silently imported by a GET. **Nạp câu sai từ lịch sử** explicitly processes at most50 previously unprocessed eligible graded attempts, oldest first; HasMore invites another bounded command. Live grading and import share markers and the learning lock. Identical retries replay the original ACK, even after later batches; a fresh command advances to the next batch. Older imported wrong answers add to the historical count but cannot reset a newer recall cycle. Imported timestamps come from saved FinalizedAt; the cycle's timezone uses the current profile because historical profile zones were not recorded. No practice activity, new assessment answer, grade or lesson completion is synthesized during import.

## Contextual review / Ôn có ngữ cảnh

`POST /api/v1/me/mistakes/{id}/review` accepts ClientRequestId. Only a due learning item can start; one unfinalized target attempt per mistake is allowed. Identical start commands return the same attempt after later completion. The client can resume an active attempt. Expired attempts use the existing worker/manual deadline-finalization path before another target attempt can start.

The new attempt clones the **entire group from the saved source attempt**, including stimulus, prompts, options and pinned private grading rules. Archived source versions remain usable. A legacy snapshot missing group members, or a group over20 questions, is rejected before creating an attempt or receipt. The attempt has mode mistake_review and a20-minute deadline. Its active payload contains no answer key or accepted variants. Neighbor questions preserve context, but only the explicitly selected target updates notebook state; wrong neighbors do not add/recount mistakes. The result page labels recall scores separately.

Recall attempts never enter placement admission, ordinary lesson/path completion, skill accuracy/sample comparisons or ordinary activity statistics. They remain visible as separate attempts and retain their own scores and original answer evidence. Opening a result retains the existing first-disclosure policy for subsequent unseen-full-mock evidence.

## Mastery policy v1 / Quy tắc đã nắm

An original wrong answer starts/reopens a learning cycle, due immediately. Wrong targeted recall increments WrongCount, resets correct days and schedules10 elapsed UTC minutes. Correct targeted recall counts at most once per strictly later local date. The first qualifying day schedules24 elapsed UTC hours; the second distinct qualifying day marks mastered with no next due date. A25-hour local day may require another review because24 hours can still land on the same local date. Mastered is a product recall status, not proof of lasting retention.

Each cycle pins the profile timezone from its last wrong answer; changing profile timezone cannot manufacture an additional correct day. A new wrong answer resets the cycle using the then-current profile zone. An ordinary correct answer does not silently mark a mistake mastered. All saved wrong answers, recall attempts and their results remain intact when status changes.

## API and security / API và quyền

- `GET /api/v1/me/mistakes?status=learning&dueOnly=false&part=7&skill=reading&offset=0`; status learning/mastered/all, Part1–7, skill at most60 characters, offset0–100000. Pages reflect current state rather than a frozen multi-page snapshot.
- `POST /api/v1/me/mistakes/import-history`: ClientRequestId.
- `POST /api/v1/me/mistakes/{id}/review`: ClientRequestId; returns the existing safe AttemptView.

All endpoints require the authenticated owner, including Admin. Writes use existing CSRF and actor guards; foreign IDs return404. The list reveals counts/prompts/source links, not answers/variants/explanations, and does not mark result disclosure or write learning state. Composite keys bind source/target question versions and target ownership. Invalid filters, incomplete context, not-due state, competing starts and receipt conflicts fail without partial attempt/receipt creation.

Relational tests cover automatic/replayed grading, blank/typed policy, whole frozen context and archival, selected-target-only effects, two-day mastery/reopening and exact due ticks,25-hour days, deadlines, concurrent starts/import replay, bounded history import, older-history reconciliation, bounded filters/pages, owner/Admin/CSRF/key isolation and target FKs. Frontend tests cover read-only opening, due/resume links, immutable start/import retries, navigation, filter/page resets and network-versus-empty states. Native UI and load capacity are not claimed; no Computer Use is performed without the user's explicit request.

# Segmented listening and dictation / Nghe theo đoạn và gõ lại

VI: Bài có transcript hiện panel **Nghe theo đoạn & luyện dictation**. Mở panel chỉ đọc thông tin; người học chọn tiếp tục hoặc tạo phiên mới. Nghe đoạn/loop, tốc độ0,75–1,5×, gõ câu, kiểm tra và mở bản dịch Anh–Việt đều là thao tác rõ ràng. Quiz hoàn thành bài vẫn là nguồn riêng; dictation không tự tạo lesson/assessment completion.

## Authored immutable content / Nội dung biên soạn

ContentInput optionally accepts Segments with StartMs,EndMs,TextEn,TextVi,AcceptedAnswers,NormalizationPolicyVersion and IgnoreTerminalPunctuation. Null segments are omitted when hashing import inputs, preserving legacy receipt hashes. CMS JSON editing retains the array. A lesson may contain at most100 ordered, non-overlapping segments, each within one hour and its verified Ready lesson-audio duration at publication. English/Vietnamese text and variants are bounded; the canonical English must match an accepted variant. Normalized variants must be nonempty, unique and at most200 tokens. Private recordings cannot be attached as lesson audio.

Basic invalid timing/policy/shape fails before relational constraints; overlap, missing/invalid authored answers and bounds against actual audio are checked before publish. Published content remains immutable. Draft segment replacement pins the identifying FK before tracking new rows; child-only edits force a parent ETag change so a second stale editor cannot overwrite them. Editorial collection reads are split to avoid multiplying segments by question options. Assessment/publication paths retain their previous query shape.

## Sessions and assistance / Phiên nghe và trợ giúp

An explicit start pins the owner, content version and media asset for24 hours. Current resume is a separate owner/version head. Starting a replacement session requires the displayed resume ETag; simultaneous claims cannot silently steal another tab's newer head. Start receipts replay the same resource, while subsequent reads show current saved state.

An ordinary lesson/info response exposes segment availability/counts, not transcript answers. A session exposes segment timing first; transcript/translation becomes visible only after server-recorded reveal or an accepted answer. Disclosure is shared across that owner's sessions for the exact segment version. A new session cannot reset a previously opened answer into an unaided attempt. Disclosure records what the server returned, not proof that a person read it or lacked knowledge elsewhere.

Answers are append-only and capped at20 per segment/session,2000 characters/200 normalized tokens. Exact AnswerNormalization policy1 decides correctness; token edit distance only explains equal/replaced/missing/extra tokens and never grants fuzzy correctness. Accepted alternate variants are used for feedback so a correct alternate is not highlighted as wrong. Reveal/answer ordering is serialized per owner/version, and payload-bound receipts replay accepted outcomes after expiry or later answers. First-attempt counts/denominators are separate for assisted and unaided conditions; repeated checks remain visible without replacing the first-attempt summary. These are practice counts, not a calibrated proficiency or exam score.

## Playback and concurrency / Phát và lưu vị trí

Pause/seek/periodic playback updates use a serialized queue. An unknown position command keeps its original event/body while newer desired positions coalesce behind it. Server ownership, ETag and current-session checks prevent old tabs/requests overwriting another explicitly selected session. Accepted old receipts return their original ACK without changing the newer head. Client conflicts require explicitly loading the current session rather than automatic reclamation. Pagehide is best effort; abrupt close/network failure may leave the last acknowledged position.

Clip playback seeks to authored boundaries and stops or loops; the animation-frame/time-update checks supplement browser media events. No autoplay occurs on panel mount. Unknown grade/reveal outcomes freeze the input for exact retry, including accepted commands replayed after expiry. A successful write followed by a failed result read retries only GET. Closing the panel aborts reads/position updates and discards unsubmitted client drafts; accepted answers remain persisted. Actual browser/mobile/media precision remains separate native acceptance.

Archived lessons remain readable/playable through an owned existing session while the media is available; new starts are blocked. Private recording ACLs are never broadened. Composite keys bind session/answer owner and content version, and segment/version pairs. GETs do not create sessions, disclosure, receipts or progress. All writes retain authenticated owner, actor-precondition and CSRF boundaries; Admin has no access to another learner's session.

## API

- GET/POST `/api/v1/lessons/{version}/listening`: info / ClientRequestId,ExpectedResumeEtag.
- GET `/api/v1/listening/{id}`: current saved session view.
- PUT `/api/v1/listening/{id}/position`: ClientRequestId,ExpectedEtag,PositionMs.
- POST `/api/v1/listening/{id}/segments/{segment}/reveal`: ClientRequestId.
- POST `/api/v1/listening/{id}/segments/{segment}/answers`: ClientRequestId,AnswerText.

The migration adds empty practice tables and transcript storage; it does not backfill learning activity or alter existing learner paths. Starter audio/content publication is a separate corpus step; merely adding a transcript does not manufacture Ready blobs. No LLM/STT service is enabled.

Twenty-four relational/policy cases cover hidden/read-only payloads, normalization/diffs, assistance across sessions, first-attempt counts, later-state/expiry replay, concurrent events/head claims, resume races, owner/Admin/CSRF/source-version FKs, invalid bounds, archived history/audio ACL, CMS replacement/publication and legacy hash compatibility. Seven position-queue and nine panel cases cover lazy opening, explicit start, unknown writes/read retries, clock expiry, clip stop/loop, source resume and cancellation. Full-suite records are in [verification](verification.md).

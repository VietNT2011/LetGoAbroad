# Recording sources and history / Bài mẫu và lịch sử bản thu

VI: Mở bài nghe, chọn đoạn rồi bấm liên kết sang **Ghi âm của tôi**. Liên kết chỉ đề xuất phiên bản bài/đoạn. Sau khi có bản thu Ready, mở **Ghi chú & tự đánh giá** và bấm gắn bài mẫu để lưu quan hệ riêng. Gắn hoặc bỏ mẫu không tự lưu nội dung nhận xét đang gõ. Nhật ký hiển thị đúng phiên bản và đoạn đã ghim; mở mẫu không tự phát audio.

**Lịch sử nhận xét & bài mẫu** tải khi mở và giữ từng lần lưu thật, gồm ghi chú, mức tự chọn và nguồn tại thời điểm đó. Bỏ mẫu hiện tại vẫn giữ nguồn trong lịch sử. Xóa audio giữ metadata riêng; file đã xóa không được phát lại. Các mức1–5 là ý kiến của người học, không phải điểm phát âm, completion hoặc XP.

## Commands and ownership / Lệnh và quyền riêng tư

- `PUT /api/v1/media/{id}/reflection/source`: ClientRequestId, ExpectedEtag, nullable SourceContentVersionId and optional SourceSegmentId. Setting both source IDs to null clears the current link.
- `GET /api/v1/media/{id}/reflection/history?before=...`: at most30 immutable revisions, descending revision number, with an exclusive nextBefore cursor. New edits do not shift an older page.
- Existing notes commands and journal endpoints remain available; current views include optional source metadata and a revision number.

All endpoints inherit ownership from the recording asset, including after deletion; Admin has no override. Reads use no-store. Creating metadata requires a Ready recording and an explicit nonempty note/rating or valid sample binding. GET never creates metadata. Existing metadata can be edited after the recording is deleted.

Bindings pin a nonfixture lesson version with Ready lesson_audio and an optional segment belonging to that exact version. New bindings ordinarily require Published content. Archived content requires a retained source on this recording or an owned historical listening session. Current and historical references allow only their owner to reopen the archived lesson/sample. Audio still requires Ready state and the ordinary authorized grant; a retained reference cannot restore a deleted blob. Source summaries carry title/version/timing/availability, never transcript text, accepted answers or grading keys. Audio availability and archival status describe the current source state rather than historical playback guarantees.

Source commands have a separate payload-bound receipt scope and share the owner quota lock with note changes/deletion, then acquire the catalog lock in that order. Notes and source changes compare the same ETag: competing writes have one winner and one412. No-op commands and exact receipt replay append no revision. Reusing an event ID for another payload fails. The client freezes the exact route/body after an unknown response, retains note drafts during source saves, and requires explicit adoption of a compared412 baseline.

## Migration and retention / Nâng cấp và lưu giữ

Migration `20261120035234_RecordingSourceHistory` adds nullable source IDs, revision0 and an empty history table. It does not infer links or invent prior edits. Existing metadata stays revision0 with no history until the first real change. That change captures legacy_snapshot0 with its original UpdatedAt and the actual new CapturedAt, then saves revision1. A new metadata record starts at revision1 without a fabricated earlier version. Relational checks enforce unique reflection/revision numbers, matching segment/version pairs and rubric bounds.

Old note command DTOs/hashes stay unchanged. Null source and revision0 are omitted when replaying their original typed receipts, preserving the old response shape. Historical snapshots remain private and append-only; clearing notes/source changes the current value without erasing older snapshots or infrastructure receipts. Account-wide erasure is a separate, unfinished workflow.

## Verification / Kiểm chứng

Fifteen relational cases cover legacy capture and old receipts, explicit binding/rebinding/clearing, invalid sources, source/notes races, no-op/replay, deletion, archive rights and stable cursors. One additional SQL/Azurite/FFmpeg case uploads a synthetic tone, processes real audio, binds an original starter sample, clears it to exercise history-only authorization, checks a real206 range and deletes the recording. Source archival occurs only in that isolated test database; shared starter blobs are not deleted. Six additional frontend cases cover draft retention, frozen retries, deleted entries, lazy history, source availability and URL validation. Native microphone/device acceptance is not claimed.

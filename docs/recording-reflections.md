# Private recording reflections / Ghi chú và tự đánh giá bản thu

VI: Trong **Ghi âm của tôi**, mở **Ghi chú & tự đánh giá** dưới bản thu Ready, nghe lại rồi nhập nhận xét. Có thể chọn mức1–5 cho độ rõ giọng/nhịp đọc hoặc để trống. Bấm **Lưu tự đánh giá riêng** để gửi; mở form và gõ nháp không tự lưu. Đây là ý kiến của chính người học, không phải điểm phát âm tự động và không cộng điểm thi, completion, XP hoặc tiến độ lộ trình.

**Nhật ký tự đánh giá đã lưu** liệt kê các nhận xét hiện tại của chủ tài khoản, gồm nhận xét của file audio đã xóa. Xóa audio không khôi phục quyền phát qua nhật ký. Người dùng vẫn sửa hoặc xóa trắng nội dung nhận xét đang lưu. Mỗi bản thu có một nhận xét hiện tại; lịch sử từng lần sửa và liên kết mẫu bền vững vẫn chưa được triển khai.

## Ownership and storage / Quyền sở hữu và lưu trữ

`RecordingReflection` has a unique foreign key to its media asset. Ownership is derived through that asset's current OwnerId, avoiding a second owner column that could disagree. All reads/writes require an owned purpose recording; Admin has no override. An owned lesson_audio asset is also rejected. Metadata endpoints do not resolve blob storage or issue grants, and successful responses use Cache-Control:no-store.

- `GET /api/v1/media/{id}/reflection` returns existing feedback or an empty read-only view without creating a row.
- `PUT /api/v1/media/{id}/reflection` accepts ClientRequestId, nullable ExpectedEtag, Notes, nullable Clarity and Rhythm.
- `GET /api/v1/me/recording-journal?offset=0` returns at most30 owner entries, sorted UpdatedAt descending then ID, with bounded nextOffset.

Initial creation requires a verified Ready recording and at least one nonblank note/rating. Notes preserve raw text up to2000 UTF-16 characters; ratings are integer1–5 or null, with relational checks as well as service validation. Existing metadata stays editable after file deletion; the media tombstone remains the ownership source. Migration creates an empty metadata table and does not invent old self-assessments.

## Concurrent edits and replay / Chỉnh sửa đồng thời và gửi lại

Commands share the recording owner's media-quota transaction lock with deletion. Ownership is checked before receipt lookup. An accepted command replays its exact typed response even after file deletion; reuse with a different asset/payload is rejected. A replay is an acknowledgment of the original command, not a fresh media-state read. The ordinary library/journal/current GET remain authoritative for live state.

First save requires a null ETag; edits compare the reflection's ETag. Competing creates/updates accept one version and reject the unseen one. Actual changes update time/version and audit only IDs/actions; no-op saves preserve the ETag/time. Clearing an existing note is an explicit edit. This does not erase past infrastructure receipts or claim account-wide data deletion.

The UI keeps unknown-result requests immutable and freezes their draft for exact retry. A412 keeps local text, fetches server text only on request and adopts the newer baseline only after explicit acceptance. No automatic merge/overwrite or background autosave is performed. Journal/form requests are lazy and generation/actor guarded by the shared transport; saving never starts playback or a microphone.

After first opening, collapsing a panel preserves its mounted editor and local draft. Background recording-status polls and journal refreshes retain existing row trees/players until replacement data arrives; saving one row updates its visible acknowledgment without reloading/unmounting other editors. The shared resource hook tags snapshots with their source path so a changed/disabled path cannot briefly expose the old source before effect cleanup. Explicit page changes discard the old page cache and show the next read; local drafts remain memory-only, so save them before paging, deleting a file, leaving or reloading the page. Confirmed actor changes still clear the private tree.

## Verification scope / Phạm vi kiểm chứng

Twenty-two relational cases cover read-only/privacy/CSRF/no-store, exact replay and conflicts, first/edit concurrency, no-op/clear, every unready initial state, optional rubric bounds, raw text boundaries, archival and stable bounded pages. Metadata fixtures explicitly model media state without claiming an uploaded speech file. Six DOM/transport cases cover lazy reads, explicit save, unknown-result replay, stale-draft comparison, closed/deleted states and retained private journals. Native microphone/visual acceptance is not claimed. A separate real-worker runtime probe uses only a labelled synthetic technical tone on an isolated QA account.

Four follow-up regressions verify unchanged editor/player DOM identity during collapse, sibling saves and pending/completed background refreshes, and render-time isolation of changed/disabled resource paths. The full frontend suite passes133 cases with a successful production build; backend source stays at the independently verified358/370 reflection suites.

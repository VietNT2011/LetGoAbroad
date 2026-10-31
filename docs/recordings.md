# Private recording library / Ghi âm cá nhân

VI: **Ghi âm của tôi** ở `/recordings` cho phép mở mic sau khi bấm Bắt đầu, dừng, nghe bản nháp và bấm Lưu riêng. Có thể chọn file audio thay thế. Bản nháp chỉ ở thiết bị cho đến khi gửi; không tự upload khi dừng mic. Giới hạn ghi trực tiếp170 giây, file20 MiB; server kiểm giới hạn180 giây khi xử lý. Thư viện riêng có trạng thái xử lý, phát bản Ready và xóa có xác nhận trong ứng dụng.

## Capture lifecycle / Vòng đời mic

Bài có audio liên kết đến [so giọng với bài mẫu](recording-comparison.md): nghe mẫu và bản nháp/bản đã lưu luân phiên trong cùng trang, không tự upload hoặc tự chấm phát âm. Mẫu hiện tại theo version trên liên kết; chưa tự lưu quan hệ mẫu vào metadata bản thu.

The browser requests audio only, with no video. Supported WebM/Opus, Ogg/Opus or MP4 is selected via MediaRecorder.isTypeSupported; unsupported browsers retain file selection. Streams granted after cancellation, navigation or actor changes are stopped without constructing a recorder. Explicit stop, capture limit, hidden/pagehide, encoder failure and unmount release tracks. Same-owner hide/stop may preserve a local draft; unmount discards it. Actor/epoch checks prevent late data restoring another account's draft. More than20 MiB or an encoder failure discards partial capture.

Local object URLs are revoked when previews change or unmount. Draft/transfer state is in memory, not durable offline storage. Closing/reloading loses unsaved capture. Saving uses the [actor-bound block uploader](block-uploads.md); a queued Complete acknowledgment does not imply Ready. The server retains quota, ETag freezing, actual decoding and private playback rules. Pending processing/deletion is polled through REST metadata. No microphone or browser permission was automated during development.

## Metadata and ownership / Dữ liệu và quyền

`GET /api/v1/me/recordings?offset=0` requires authentication, filters by the current owner and purpose recording, omits Deleted, and returns at most30 entries ordered by CreatedAt descending/ID. Offset0–100000 is valid. User/owner query parameters cannot change identity. Admin has no library override for another user's recordings. The response contains ID, filename, state/failure, duration, attribution and creation time; no staging/canonical keys, hashes, quota reservations or SAS grants. Reading metadata does not initialize blob storage, create learning evidence or change media state.

The existing create/complete/delete/playback endpoints remain authoritative. Deletion rejects new playback immediately while durable cleanup finishes; existing short-lived playback grants retain the documented expiry window. The original capture/library feature adds no schema migration, automatic speech scoring, AI/STT provider or assessment/lesson completion. The subsequent [private-reflection feature](recording-reflections.md) adds an empty metadata table for current notes/subjective ratings, retained after file deletion. Durable sample links and edit history remain separate work.

The [administration privacy follow-up](recording-privacy.md) also filters CMS media/job lists and job retry by current recording ownership. Admin/Editor roles retain editorial-audio operations and their own private recordings; another person's recording metadata/job does not become accessible through operational catalogs.

## Streaming duration / Thời lượng bản thu dạng stream

A live-mode WebM fixture confirms that format duration may be absent. In that case the processor decodes at most maximumSeconds+1 seconds to a temporary PCM WAV, measures its actual duration and rejects an over-limit source before publishing an MP3. It does not silently accept a truncated recording. Temporary PCM is removed on success/failure. Known-duration sources retain the existing duration and rendition-consistency checks. Two real FFmpeg cases exercise valid duration-less WebM and rejection of an overlong source, without using a person's microphone.

Four relational cases cover metadata ownership/Admin, omitted secret fields, stable bounded pages, ignored owner parameters, auth/offset checks and read-only evidence. Fifteen simulated capture cases cover permission timing, cancellation/late grants, tracks on unmount, errors, duration/size limits, hidden documents, actor changes and MIME fallback. Five page cases verify explicit capture/save/delete, Ready-only playback, queued ACK handling and preview revocation. Native device/microphone quality, codec compatibility across devices and mobile/offline acceptance remain unverified.

Reference: [W3C MediaStream Recording](https://www.w3.org/TR/mediastream-recording/) (working draft).

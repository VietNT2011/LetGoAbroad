# Starter listening corpus / Bộ bài nghe khởi đầu

The local Docker demo opts into `Seed:ListeningAudio=true` after ordinary demo seeding. Three original five-minute **practice activities** are added to the lesson library, each with a short synthetic MP3, three bilingual dictation segments and two listening-comprehension questions:

| Slug | Topic | Sentences / questions |
|---|---|---|
| `listening-office-meeting` | Meeting time, notebook and office | 3 / 2 |
| `listening-hotel-room` | Booking two nights and breakfast | 3 / 2 |
| `listening-train-platform` | Departure, platform and ticket | 3 / 2 |

Five minutes is the suggested exercise budget, not the audio duration. The committed manifest records each clip's measured milliseconds. Total MP3 storage is300,123 bytes. Scripts, translations and exercises are original CC0 text; **Flite slt synthetic US English** is labelled in every lesson and attribution. See [audio source/notice](../seed/audio/NOTICE.md). No human voice claim, ETS endorsement, AI/STT call or standardized difficulty claim is made.

VI: Chọn Thư viện → kỹ năng Nghe, mở một trong ba bài **Listen & type**, rồi mở **Nghe theo đoạn & luyện dictation**. Người học tự bắt đầu phiên, nghe/lặp và gõ trước khi xem transcript. Quiz cuối bài là thao tác hoàn thành lesson riêng; seed và dictation không tự cộng lesson completion hoặc điểm bài thi. Bài mới không được tự chèn vào lộ trình mà người học đã ghim.

## Publication safety / An toàn công bố

`DemoListeningSeed` validates the complete embedded manifest, distinct reserved identities, original quiz choices, segment/variant rules, timing, byte lengths and SHA-256 before storage writes. It requires an existing Admin owner. Audio is uploaded to asset/hash-scoped immutable canonical keys with conditional creation and matching-hash retry. **Only after all uploads succeed** does one catalog transaction create all three Ready media rows, lessons, transcript/quiz graphs and publication pointers. Blob I/O never holds that SQL transaction. A failed later upload leaves no partial Ready rows or published lessons; retry safely reuses matching uploaded bytes. Such initial orphan bytes can remain until a successful retry/operational cleanup; no global blob deletion is performed.

The catalog is rechecked under the same SQL application lock used by CMS. Concurrent first seeds create exactly one corpus. A reserved slug/identity collision or partial reserved corpus fails explicitly, without adopting unrelated CMS content. Reseeding returns without touching storage or SQL rows when all original roots/versions exist. It preserves archival, later CMS pointers, deleted media, existing learner sessions and path pins. This is additive seeding, not storage repair: missing externally deleted blobs require an explicit operational repair, and deliberate deletion is never reversed.

Both API/worker Docker build inputs include `seed/audio`; MP3s and complete notices are embedded in the Infrastructure assembly. Ordinary relational tests keep this corpus opt-in off; the seed never fabricates Ready rows for missing starter blobs. Other metadata fixtures may explicitly model media states without claiming stored audio. Storage cases use actual Azurite uploads, full hash readback, HTTP206 ranges, FFprobe duration/codec checks and full FFmpeg decoding. Browser/device audition, teacher review and a broad natural-dialogue corpus remain open.

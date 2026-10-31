# Compare a recording with a lesson sample / So giọng với bài mẫu

VI: Trong bài có audio, chọn **Tự thu âm và so với bài mẫu**. Trang ghi âm mở kèm tên/version/nguồn của bài mẫu và player. Nghe mẫu, dừng rồi ghi hoặc chọn file; nghe luân phiên mẫu và bản nháp/bản đã lưu. Phát một player sẽ dừng các player khác trong trang, giữ nguyên vị trí của chúng. Lưu bản thu vẫn là thao tác riêng, không tự upload khi nghe hoặc dừng mic.

The `lesson` query value names a lesson version. A bounded UUID check rejects malformed links before a request; any caller-supplied media URL/ID is ignored. The sample asset comes only from the authenticated lesson response, with ordinary lesson/media ACLs still authoritative. Remounting the reference on version changes aborts its old read and removes its old player immediately. Access errors, missing audio and accessible archived versions are shown explicitly. No sample autoplay, public grant, grading or lesson/assessment completion is added.

One capture-phase playback handler pauses only other currently playing audio elements within the recordings page. It neither rewinds them nor touches audio elsewhere. Native controls and the existing speed/retry behavior remain available. This is manual listening comparison, without pronunciation scoring, STT or AI.

The chosen sample travels with the current page URL; it is **not yet a durable relationship on the uploaded recording**. Recording source descriptions remain the user's explicit input. Persistent sample/context links, private self-assessment notes/rubrics/history and segment-specific comparison remain separate work. No schema/API change is included.

Six DOM/transport cases cover no-source/no-request behavior, malformed links, server-derived source/return navigation without autoplay, access failure without fallback, version-change abort/removal, and local-only pause without rewind. The five existing recording-page cases now run inside a router and still pass. The complete frontend suite passes123 cases with a successful production build. Actual microphone/device audition and visual/mobile acceptance remain unverified; no native browser input was used.

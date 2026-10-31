# Estimated study time / Thời gian học ước tính

VI: Dashboard có số đo **ước tính** trong cửa sổ7×24 hoặc30×24 giờ UTC kết thúc tại thời điểm đọc, cùng [biểu đồ theo ngày Hồ sơ](study-time-calendar.md). Hai chế độ có mốc bắt đầu khác nhau. Mở tab không tạo thành tích, completion, raw score, streak hay XP.

## Source policy v1 / Quy tắc nguồn

`EstimatedStudyTime` adds private ActivitySessions, ActivityHeartbeats and ActivityIntervals. A session has an owner, a client-generated ID, a coarse kind (`lesson`, `assessment`, `cards`), a24-hour expiry, LastSequence, LastSeenAt and the previously acknowledged active state. There is no stored lesson URL, typed text, key value, cursor position, card content or question payload. Session creation is an owner/client-ID upsert; changing the kind for an existing key conflicts.

Heartbeats carry only a positive sequence and active/inactive boolean. They are serialized by the owner's SQL activity lock. Sequence must be exactly the next value; accepted sequence/payload replays return the old ACK without moving LastSeenAt or crediting again. Changed payloads conflict. Receipts replay even after session expiry; a new heartbeat at/after expiry requires a new session.

If the **previous** acknowledged state was active, the next heartbeat closes an interval from `max(LastSeenAt, now−30seconds)` to server `now`, limited to a positive duration. The current state controls the next interval. Thus the first active heartbeat establishes a boundary and credits zero; going inactive closes the previous tail; reactivation after an acknowledged inactive state credits no idle gap. Client timestamps are never accepted. A backward server clock credits zero and cannot rewind LastSeenAt.

VI: Mỗi interval có FK tới đúng heartbeat/owner, unique session+sequence và check duration dương không quá30 giây. Heartbeat, interval và session state commit trong cùng transaction. Đây là tín hiệu tự báo bị giới hạn, không chứng minh người dùng đang chú ý. Không cộng counter riêng của từng tab thành thời gian học.

## Aggregation / Tổng hợp

The read captures one UTC `now`, selects only the owner's intervals overlapping the requested rolling window, clips their boundaries and unions overlapping/adjacent ranges across all sessions before summing exact ticks. Whole seconds are rounded down once after union. Duplicate/nested intervals and two tabs covering the same moment contribute that moment once. Changing profile/path timezones does not re-bucket this UTC elapsed window. The separate [local-day calendar](study-time-calendar.md) reuses the same UTC sources and union policy, retains fractional seconds, and partitions by the current profile timezone.

Existing completion/activity events, attempts, lessons, SRS ratings and plan revisions are not changed or backfilled. No historical study time is invented when this feature is deployed.

## Browser behavior / Hoạt động trình duyệt

The hook mounts only after the learner's lesson, attempt/result or personal-card data is available. It sends roughly every15 seconds while the document is visible, the window has focus and there has been an input event within90 seconds or an audio/video element is playing. Input handlers record only a local timestamp. Background/unfocused pages and idle periods stop active contribution; close/route changes send a best-effort inactive boundary.

One request is in flight per hook and a failed heartbeat retains its exact pending sequence/payload for retry. Expired/conflicting sessions restart without an unbounded offline queue. Measurement errors do not block learning controls. Strict-mode cleanup removes listeners/timers; opening hidden pages does not create a session until active. Missing delivery, throttling, long gaps, focus/idle heuristics and media buffering can make this estimate differ from actual engagement. It is not a validated attention/learning metric.

VI: Sau khi tạo session bất đồng bộ, hook đọc lại visibility/focus thay vì gửi cờ active đã cũ. Retry giữ nguyên payload đã gửi; khi nhận ACK, nếu trạng thái hiện tại khác ACK thì gửi mốc mới ngay, không đợi thêm15 giây. `pagehide` đi qua cùng hàng đợi sequence để gửi mốc nghỉ best-effort, vì đóng/đưa trang vào back-forward cache không đảm bảo React unmount. `pageshow` chỉ khôi phục đánh giá hoạt động sau một lần hide, không tự tạo tương tác mới. Trang bị đóng băng hoặc mạng mất trước khi gửi xong vẫn có thể thiếu mốc; keepalive không bảo đảm giao nhận.

Twelve additional frontend cases simulate hidden/focused pages, missing owner/data,15-second ordering,90-second idle, native-media state, asynchronous-start visibility changes, identical receipt retries with immediate state reconciliation, failure/no-spin, expiry restart, pagehide/pageshow, cleanup and StrictMode disposal. Payload checks reject typed text, owner IDs and private page keys in activity bodies. These JSDOM/fake-clock cases verify client decisions and request contracts; they do not establish native browser lifecycle/delivery or actual media playback behavior.

## API and verification

- `POST /api/v1/learning/study-sessions` — ClientSessionId, Kind
- `GET /api/v1/learning/study-sessions/{id}` — owner session/sequence/expiry
- `POST /api/v1/learning/study-sessions/{id}/heartbeat` — Sequence, IsActive
- `GET /api/v1/learning/study-time?days=7|30` — policy version, UTC bounds, estimated seconds

All endpoints require authentication; mutations use CSRF. Admin cannot inspect another owner's session. Fifteen policy/relational cases cover exact union/clipping/offset/tick behavior, start/upsert, bounded gaps and pause/resume, overlapping tabs, receipt ordering/collisions, concurrent sequences, expiry replay, backward clocks, rolling-window/timezone behavior, ownership/CSRF, unchanged learning evidence and database interval/FK guards.

The frontend production build is checked; actual focus/visibility/media transitions remain a browser-acceptance gap after the user stopped native Computer Use with Escape. See [verification](verification.md) for completed full-suite and live API results. High-volume capacity, retention cleanup and offline reconstruction remain open.

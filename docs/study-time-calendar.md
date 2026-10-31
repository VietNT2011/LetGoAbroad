# Daily study time / Thời gian học theo ngày

VI: Dashboard có biểu đồ **7 hoặc30 ngày lịch theo múi giờ Hồ sơ hiện tại**, gồm hôm nay và những ngày không có dữ liệu. Có thể chuyển sang cửa sổ7/30×24 giờ UTC cũ; hai chế độ có nhãn riêng và không nhất thiết có cùng tổng vì mốc bắt đầu khác nhau. Đây vẫn là thời gian **ước tính**, không chứng minh sự chú ý hay thay đổi completion, điểm, streak hoặc XP.

## Policy v1 / Quy tắc tổng hợp

The read captures server `now` once and reads only the authenticated owner's existing ActivityIntervals. It clips and unions overlapping/adjacent UTC intervals across sessions before assigning their durations to local dates. The current profile timezone is used at read time: changing it can move historical intervals between dates without modifying the source records. There is no daily counter, migration or historical backfill.

Every constant-offset timezone segment is intersected with each selected local date. The implementation does not assume a24-hour day and does not resolve an ambiguous/nonexistent local midnight. A skipped date remains a zero row; a repeated date retains all of its elapsed duration. A conservative UTC query bound includes every offset capable of mapping into the first date. Future intervals are clipped at captured `now`.

API seconds are decimals retaining source100-nanosecond tick precision. The total is the exact sum of the daily values; fractions are not discarded separately for each interval or day. `activeDays` counts dates with positive duration. The UI floors displayed whole seconds, labels positive subsecond values “Dưới1giây”, and explains that fractions remain in the total.

## Timezone source / Nguồn múi giờ

The existing OS `TimeZoneInfo` is adapted with pinned **NodaTime3.3.6** using [BclDateTimeZone.FromTimeZoneInfo](https://nodatime.org/3.3.x/api/NodaTime.TimeZones.BclDateTimeZone.html), then [GetZoneIntervals](https://nodatime.org/3.3.x/api/NodaTime.DateTimeZone.html) supplies constant-offset segments. This does not switch profiles to the embedded TZDB provider. Results depend on the host timezone data and can change after that data is updated.

The adapter does not promise bug-for-bug parity with every `TimeZoneInfo` edge case: Noda Time documents differences around some year boundaries and normalizes the Windows23:59:59.999 end-of-day representation to the following midnight. The calendar policy uses the adapter consistently for today's date and partitioning. This dependency is pinned and locked, not claimed to be the newest release.

## API and checks

`GET /api/v1/learning/study-time/calendar?days=7|30` requires authentication. Response: policyVersion, windowDays, timeZoneId, fromDate, toDate, totalEstimatedSeconds, activeDays and ordered date/estimatedSeconds rows. Other window values return422. It performs no writes. The [rolling endpoint and heartbeat policy](estimated-study-time.md) remain available.

Fourteen new cases cover empty/default windows and no side effects, ownership/Admin/anonymous isolation, invalid windows, UTC+14/−12 boundaries, future clipping, union before date splitting, fractional midnight boundaries, current-profile re-bucketing without source changes,23/25-hour DST days, a custom midnight gap, Samoa's skipped2011 date, Kwajalein's47-hour1969 repeated date, a20-second date bounce and every installed system timezone. The historical Kwajalein transition is recorded in the [IANA timezone source](https://github.com/eggert/tz/blob/main/australasia).

The UI fetches only the opened/selected mode, guards window labels against old responses and provides a native table with caption and scoped headers alongside decorative bars. TypeScript/Vite and packaged API checks supplement the relational tests. Native visual/keyboard/mobile acceptance remains unverified after the user stopped Computer Use with Escape; it has not been resumed for this feature. Capacity/retention benchmarks and educational validity are not claimed. See [verification](verification.md) for completed suite results.

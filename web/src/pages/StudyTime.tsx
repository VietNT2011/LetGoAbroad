import { useState } from "react";
import { ErrorNotice, Loading, useResource } from "../hooks";
import { ChevronDown } from "lucide-react";
import "./learning-activity.css";

type Summary = {
  windowDays: number;
  fromUtc: string;
  toUtc: string;
  estimatedSeconds: number;
};
type Calendar = {
  windowDays: number;
  timeZoneId: string;
  fromDate: string;
  toDate: string;
  totalEstimatedSeconds: number;
  activeDays: number;
  days: { date: string; estimatedSeconds: number }[];
};
const utcTime = new Intl.DateTimeFormat("vi-VN", {
  timeZone: "UTC",
  dateStyle: "short",
  timeStyle: "short",
});
const localDay = new Intl.DateTimeFormat("vi-VN", {
  timeZone: "UTC",
  day: "2-digit",
  month: "2-digit",
});
const dayLabel = (date: string) =>
  localDay.format(new Date(`${date}T00:00:00Z`));
const duration = (seconds: number) =>
  seconds > 0 && seconds < 1
    ? "Dưới 1 giây"
    : `${Math.floor(seconds / 60).toLocaleString("vi-VN")} phút ${Math.floor(seconds) % 60} giây`;

/** VI: Hai cửa sổ được gắn nhãn riêng và chỉ tải chế độ đang xem. EN: Keep local-calendar and rolling-UTC semantics explicit and fetch only the selected view. */
export function StudyTime() {
  const [open, setOpen] = useState(false);
  const [days, setDays] = useState<7 | 30>(7);
  const [mode, setMode] = useState<"calendar" | "rolling">("calendar");
  const calendar = useResource<Calendar>(
    open && mode === "calendar"
      ? `/learning/study-time/calendar?days=${days}`
      : null,
  );
  const rolling = useResource<Summary>(
    open && mode === "rolling" ? `/learning/study-time?days=${days}` : null,
  );
  const resource = mode === "calendar" ? calendar : rolling;
  // VI: Không gắn nhãn một khoảng/mode mới lên dữ liệu cũ trong lúc request được thay. EN: Do not relabel an old window or response from the other mode.
  const calendarData =
    calendar.data?.windowDays === days ? calendar.data : undefined;
  const rollingData =
    rolling.data?.windowDays === days ? rolling.data : undefined;
  const data = mode === "calendar" ? calendarData : rollingData;
  return (
    <details
      className="learning-activity"
      onToggle={(event) => setOpen(event.currentTarget.open)}
    >
      <summary>
        <span>
          <span className="eyebrow">TIME FOR YOUR NEXT CHAPTER</span>
          <span className="activity-title">Thời gian học ước tính</span>
        </span>
        <ChevronDown size={23} aria-hidden="true" />
      </summary>
      {open ? (
        <div className="activity-content">
          <div className="activity-controls">
            <div role="group" aria-label="Cách xem thời gian học">
              <button
                type="button"
                aria-pressed={mode === "calendar"}
                onClick={() => setMode("calendar")}
              >
                Theo ngày hồ sơ
              </button>
              <button
                type="button"
                aria-pressed={mode === "rolling"}
                onClick={() => setMode("rolling")}
              >
                Khoảng {days} × 24 giờ UTC
              </button>
            </div>
          </div>
          <div className="activity-controls">
            <div role="group" aria-label="Khoảng thời gian học">
              {([7, 30] as const).map((value) => (
                <button
                  type="button"
                  key={value}
                  aria-pressed={value === days}
                  onClick={() => setDays(value)}
                >
                  {value} {mode === "calendar" ? "ngày" : "× 24 giờ"}
                </button>
              ))}
            </div>
            <button
              type="button"
              className="link-button"
              onClick={resource.reload}
            >
              Cập nhật thời gian
            </button>
          </div>
          <ErrorNotice error={resource.error} retry={resource.reload} />
          {resource.loading || (!data && !resource.error) ? (
            <Loading />
          ) : mode === "calendar" && calendarData ? (
            <CalendarTime data={calendarData} />
          ) : rollingData && mode === "rolling" ? (
            <>
              <p>
                <strong>{duration(rollingData.estimatedSeconds)}</strong> ước
                tính trong cửa sổ này.
              </p>
              <p className="activity-period">
                {utcTime.format(new Date(rollingData.fromUtc))} –{" "}
                {utcTime.format(new Date(rollingData.toUtc))} UTC
              </p>
            </>
          ) : null}
          <p className="activity-policy">
            Ghi nhận khi học bài, làm/chữa bài hoặc ôn thẻ ở tab hiển thị và cửa
            sổ được chọn, với tương tác gần đây hoặc media đang phát. Mỗi khoảng
            ghi nhận tối đa 30 giây; những khoảng trùng giờ chỉ tính một lần.
            Mạng gián đoạn, tab nền và lúc nghỉ có thể khiến số đo thiếu. Đây là
            số liệu ước tính, không chứng minh mức chú ý hay cộng điểm bài
            thi/XP.
          </p>
        </div>
      ) : null}
    </details>
  );
}

function CalendarTime({ data }: { data: Calendar }) {
  const maximum = Math.max(1, ...data.days.map((day) => day.estimatedSeconds));
  return (
    <>
      <p>
        <strong>{duration(data.totalEstimatedSeconds)}</strong> ·{" "}
        {data.activeDays}/{data.windowDays} ngày có thời gian ghi nhận.
      </p>
      <p className="activity-period">
        {dayLabel(data.fromDate)} – {dayLabel(data.toDate)} · {data.timeZoneId}
      </p>
      <ol
        className={`activity-days ${data.windowDays === 30 ? "activity-days-long" : ""}`}
        aria-hidden="true"
      >
        {data.days.map((day) => (
          <li key={day.date}>
            <b>{duration(day.estimatedSeconds)}</b>
            <span className="activity-bar">
              <i
                className="activity-bar-practice"
                style={{
                  height:
                    day.estimatedSeconds > 0
                      ? `${Math.max(2, (44 * day.estimatedSeconds) / maximum)}px`
                      : "0px",
                }}
              />
            </span>
            <time dateTime={day.date}>{dayLabel(day.date)}</time>
          </li>
        ))}
      </ol>
      <details className="activity-table">
        <summary>Xem thời gian từng ngày</summary>
        <table>
          <caption>Thời gian học ước tính theo ngày hồ sơ</caption>
          <thead>
            <tr>
              <th scope="col">Ngày</th>
              <th scope="col">Thời gian ước tính</th>
            </tr>
          </thead>
          <tbody>
            {data.days.map((day) => (
              <tr key={day.date}>
                <th scope="row">
                  <time dateTime={day.date}>{dayLabel(day.date)}</time>
                </th>
                <td>{duration(day.estimatedSeconds)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
      <p className="activity-policy">
        Ngày được tính theo múi giờ hồ sơ lúc tải dữ liệu; đổi múi giờ có thể
        chuyển các khoảng cũ sang ngày khác. Thời gian được hợp nhất trước khi
        chia ngày, kể cả ngày đổi giờ. Số hiển thị làm tròn xuống giây; các
        khoảng dưới một giây vẫn được giữ trong tổng.
      </p>
    </>
  );
}

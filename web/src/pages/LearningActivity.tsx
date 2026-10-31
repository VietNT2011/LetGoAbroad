import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { ErrorNotice, Loading, useResource } from "../hooks";
import "./learning-activity.css";

type Counts = {
  lessonsCompleted: number;
  practiceAttempts: number;
  fullMockAttempts: number;
  totalActivities: number;
};
type Activity = {
  windowDays: number;
  timeZoneId: string;
  fromDate: string;
  toDate: string;
  activeDays: number;
  totals: Counts;
  days: (Counts & { date: string })[];
};
const dateFormat = new Intl.DateTimeFormat("vi-VN", {
  day: "2-digit",
  month: "2-digit",
  timeZone: "UTC",
});
// VI: Ngày đã được server đổi sang múi giờ hồ sơ; chỉ định dạng DateOnly ở UTC. EN: Format the server's local date without applying a second timezone conversion.
const dateLabel = (date: string) =>
  dateFormat.format(new Date(`${date}T00:00:00Z`));

/** VI: Chỉ tải khi mở, tách số lượt khỏi dữ liệu độ chính xác và có bảng thay thế cho biểu đồ. EN: Load on disclosure, separate participation from accuracy, and provide a native data table. */
export function LearningActivity() {
  const [open, setOpen] = useState(false);
  const [range, setRange] = useState<7 | 30>(7);
  const resource = useResource<Activity>(
    open ? `/learning/activity?days=${range}` : null,
  );
  // VI: Không gắn nhãn 30 ngày lên response 7 ngày đang chờ effect hủy. EN: Never relabel a previous window while its replacement request starts.
  const data = resource.data?.windowDays === range ? resource.data : undefined;
  const maximum = Math.max(
    1,
    ...(data?.days.map((day) => day.totalActivities) ?? []),
  );
  return (
    <details
      className="learning-activity"
      onToggle={(event) => setOpen(event.currentTarget.open)}
    >
      <summary>
        <span>
          <span className="eyebrow">SEE YOUR SMALL STEPS</span>
          <span className="activity-title">Hoạt động 7 / 30 ngày</span>
        </span>
        <ChevronDown size={23} aria-hidden="true" />
      </summary>
      {open ? (
        <div className="activity-content">
          <div className="activity-controls">
            <div role="group" aria-label="Khoảng thời gian hoạt động">
              {([7, 30] as const).map((days) => (
                <button
                  key={days}
                  type="button"
                  aria-pressed={range === days}
                  onClick={() => setRange(days)}
                >
                  {days} ngày
                </button>
              ))}
            </div>
            <button
              className="link-button"
              type="button"
              onClick={resource.reload}
            >
              Cập nhật hoạt động
            </button>
          </div>
          <ErrorNotice error={resource.error} retry={resource.reload} />
          {resource.loading || (!data && !resource.error) ? (
            <Loading />
          ) : data ? (
            <>
              <p className="activity-period">
                {dateLabel(data.fromDate)} – {dateLabel(data.toDate)} ·{" "}
                <span>{data.timeZoneId}</span>
              </p>
              <dl className="activity-totals">
                <div>
                  <dt>Bài hoàn thành</dt>
                  <dd>{data.totals.lessonsCompleted}</dd>
                </div>
                <div>
                  <dt>Lượt luyện đã chấm</dt>
                  <dd>{data.totals.practiceAttempts}</dd>
                </div>
                <div>
                  <dt>Full mock đã chấm</dt>
                  <dd>{data.totals.fullMockAttempts}</dd>
                </div>
              </dl>
              <p>
                <b>
                  {data.activeDays}/{data.windowDays}
                </b>{" "}
                ngày có bài hoàn thành hoặc lượt đã chấm.
              </p>
              {data.totals.totalActivities === 0 ? (
                <p className="activity-empty">
                  Chưa có hoạt động đã hoàn thành trong khoảng này.
                </p>
              ) : null}
              {/* VI: Đồ thị trang trí có cùng dữ liệu với bảng bên dưới, không phụ thuộc hover hoặc màu để đọc số. EN: The decorative chart shares its data with the accessible table; values need neither hover nor color interpretation. */}
              <ol
                className={`activity-days ${range === 30 ? "activity-days-long" : ""}`}
                aria-hidden="true"
              >
                {data.days.map((day) => (
                  <li key={day.date}>
                    <b>{day.totalActivities}</b>
                    <span className="activity-bar">
                      <i
                        className="activity-bar-lesson"
                        style={{
                          height: `${(44 * day.lessonsCompleted) / maximum}px`,
                        }}
                      />
                      <i
                        className="activity-bar-practice"
                        style={{
                          height: `${(44 * day.practiceAttempts) / maximum}px`,
                        }}
                      />
                      <i
                        className="activity-bar-mock"
                        style={{
                          height: `${(44 * day.fullMockAttempts) / maximum}px`,
                        }}
                      />
                    </span>
                    <time dateTime={day.date}>{dateLabel(day.date)}</time>
                  </li>
                ))}
              </ol>
              <details className="activity-table">
                <summary>Xem số liệu từng ngày</summary>
                <table>
                  <caption>Bài hoàn thành và lượt đã chấm theo ngày</caption>
                  <thead>
                    <tr>
                      <th scope="col">Ngày</th>
                      <th scope="col">Bài học</th>
                      <th scope="col">Luyện tập</th>
                      <th scope="col">Full mock</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.days.map((day) => (
                      <tr key={day.date}>
                        <th scope="row">
                          <time dateTime={day.date}>{dateLabel(day.date)}</time>
                        </th>
                        <td>{day.lessonsCompleted}</td>
                        <td>{day.practiceAttempts}</td>
                        <td>{day.fullMockAttempts}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </details>
              <p className="activity-policy">
                Ngày theo múi giờ hiện tại trong Hồ sơ. Luyện tập gồm mini test,
                practice và placement. Full mock ở đây đếm lượt đã chấm, kể cả
                đã xem lời giải; điều kiện đánh giá độ chính xác được tách ở mục
                Dữ liệu kỹ năng.
              </p>
            </>
          ) : null}
        </div>
      ) : null}
    </details>
  );
}

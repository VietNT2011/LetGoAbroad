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
const utcTime = new Intl.DateTimeFormat("vi-VN", {
  timeZone: "UTC",
  dateStyle: "short",
  timeStyle: "short",
});

export function StudyTime() {
  const [open, setOpen] = useState(false);
  const [days, setDays] = useState<7 | 30>(7);
  const resource = useResource<Summary>(
    open ? `/learning/study-time?days=${days}` : null,
  );
  const data = resource.data?.windowDays === days ? resource.data : undefined;
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
            <div role="group" aria-label="Cửa sổ thời gian học">
              {([7, 30] as const).map((value) => (
                <button
                  type="button"
                  key={value}
                  aria-pressed={value === days}
                  onClick={() => setDays(value)}
                >
                  {value} × 24 giờ
                </button>
              ))}
            </div>
            <button
              className="link-button"
              type="button"
              onClick={resource.reload}
            >
              Cập nhật thời gian
            </button>
          </div>
          <ErrorNotice error={resource.error} retry={resource.reload} />
          {resource.loading || (!data && !resource.error) ? (
            <Loading />
          ) : data ? (
            <>
              <p>
                <strong>
                  {Math.floor(data.estimatedSeconds / 60)} phút{" "}
                  {data.estimatedSeconds % 60} giây
                </strong>{" "}
                ước tính trong cửa sổ này.
              </p>
              <p className="activity-period">
                {utcTime.format(new Date(data.fromUtc))} –{" "}
                {utcTime.format(new Date(data.toUtc))} UTC
              </p>
              <p className="activity-policy">
                Ghi nhận khi học bài, làm/chữa bài hoặc ôn thẻ ở tab đang hiển
                thị và có focus, với tương tác gần đây hoặc media đang phát. Mỗi
                heartbeat đóng góp tối đa30 giây; thời gian trùng giữa các tab
                chỉ tính một lần. Mạng gián đoạn, tab nền và lúc nghỉ có thể
                khiến số đo thiếu. Số liệu này không chứng minh mức chú ý và
                không cộng điểm bài thi hay XP.
              </p>
            </>
          ) : null}
        </div>
      ) : null}
    </details>
  );
}

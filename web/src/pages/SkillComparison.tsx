import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { ErrorNotice, Loading, useResource } from "../hooks";
import "./learning-activity.css";
import "./skill-comparison.css";

type Sample = {
  observedCount: number;
  correctCount: number;
  blankCount: number;
  accuracyPercent: number | null;
  parts: { part: number; observedCount: number; correctCount: number }[];
};
type Comparison = {
  windowSize: number;
  minimumObservations: number;
  groups: {
    track: "practice" | "full_mock_unseen";
    skill: string;
    recent: Sample;
    previous: Sample;
    differencePercentagePoints: number | null;
  }[];
};
const names: Record<string, string> = {
  grammar: "Ngữ pháp",
  reading: "Đọc hiểu",
  listening: "Nghe hiểu",
  vocabulary: "Từ vựng",
};

/** VI: Mỗi lần mở tải một snapshot gồm cả hai mẫu; đóng thì bỏ dữ liệu/request cũ. EN: Mount one shared snapshot request only while expanded; closing discards the old view and request. */
export function SkillComparison() {
  const [open, setOpen] = useState(false);
  return (
    <details
      className="learning-activity"
      onToggle={(event) => setOpen(event.currentTarget.open)}
    >
      <summary>
        <span>
          <span className="eyebrow">LOOK AT YOUR PRACTICE</span>
          <span className="activity-title">So sánh hai mẫu luyện gần đây</span>
        </span>
        <ChevronDown size={23} aria-hidden="true" />
      </summary>
      {open ? <ComparisonContent /> : null}
    </details>
  );
}

function ComparisonContent() {
  const resource = useResource<Comparison>("/learning/skill-comparison");
  return (
    <div className="activity-content">
      <button type="button" className="link-button" onClick={resource.reload}>
        Cập nhật so sánh
      </button>
      <p className="activity-policy">
        Tối đa 50 câu khác nhau mới nhất và 50 câu liền trước cho mỗi kỹ năng,
        mỗi nhóm làm bài. Một câu chỉ nằm ở một mẫu; lấy đáp án hợp lệ gần nhất.
        Cần ít nhất 5 câu ở mỗi mẫu để hiển thị chênh lệch.
      </p>
      <ErrorNotice error={resource.error} retry={resource.reload} />
      {resource.loading ? (
        <Loading />
      ) : resource.data ? (
        <>
          {(["practice", "full_mock_unseen"] as const).map((track) => {
            const groups = resource.data!.groups.filter(
              (group) => group.track === track,
            );
            return (
              <section
                className="sample-track"
                key={track}
                aria-label={
                  track === "practice"
                    ? "So sánh bài luyện"
                    : "So sánh full mock chưa lộ đáp án"
                }
              >
                <h3>
                  {track === "practice"
                    ? "Bài luyện, mini test và placement"
                    : "Full mock · câu đủ điều kiện chưa lộ đáp án"}
                </h3>
                {groups.length ? (
                  <div className="sample-grid">
                    {groups.map((group) => {
                      const label = names[group.skill] ?? group.skill;
                      const parts = [
                        ...new Set(
                          [...group.recent.parts, ...group.previous.parts].map(
                            (part) => part.part,
                          ),
                        ),
                      ].sort((a, b) => a - b);
                      return (
                        <article className="sample-card" key={group.skill}>
                          <h4>{label}</h4>
                          <div className="sample-windows">
                            <SampleView
                              label="Mẫu mới nhất"
                              sample={group.recent}
                              limit={resource.data!.windowSize}
                              minimum={resource.data!.minimumObservations}
                            />
                            <SampleView
                              label="Mẫu liền trước"
                              sample={group.previous}
                              limit={resource.data!.windowSize}
                              minimum={resource.data!.minimumObservations}
                            />
                          </div>
                          {/* VI: Số 0 vẫn là chênh lệch hợp lệ, null mới là thiếu mẫu. EN: Zero is a valid difference; only null means insufficient evidence. */}
                          <p className="sample-difference">
                            {group.differencePercentagePoints == null
                              ? "Chưa đủ mẫu để so sánh."
                              : `Chênh lệch: ${group.differencePercentagePoints > 0 ? "+" : ""}${group.differencePercentagePoints} điểm phần trăm.`}
                          </p>
                          <details className="activity-table">
                            <summary>Xem số liệu theo Part · {label}</summary>
                            <table>
                              <caption>
                                {label} · số câu đúng / quan sát theo Part
                              </caption>
                              <thead>
                                <tr>
                                  <th scope="col">Part</th>
                                  <th scope="col">Mới nhất</th>
                                  <th scope="col">Liền trước</th>
                                </tr>
                              </thead>
                              <tbody>
                                {parts.map((part) => (
                                  <tr key={part}>
                                    <th scope="row">Part {part}</th>
                                    {[group.recent, group.previous].map(
                                      (sample, index) => {
                                        const value = sample.parts.find(
                                          (item) => item.part === part,
                                        );
                                        return (
                                          <td key={index}>
                                            {value
                                              ? `${value.correctCount}/${value.observedCount}`
                                              : "Chưa có câu"}
                                          </td>
                                        );
                                      },
                                    )}
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </details>
                        </article>
                      );
                    })}
                  </div>
                ) : (
                  <p className="activity-empty">
                    Chưa có câu hợp lệ trong nhóm này.
                  </p>
                )}
              </section>
            );
          })}
        </>
      ) : null}
      <p className="activity-policy">
        Hai mẫu gồm những câu khác nhau và có thể khác độ khó; chênh lệch mô tả
        mẫu đã làm, không kết luận trình độ hay dự đoán điểm thi. Đây là thứ tự
        câu gần đây, không phải hai khoảng ngày. Full mock chỉ tính từng câu đủ
        điều kiện theo lịch sử mở lời giải đã ghi nhận. Không gộp full mock với
        bài luyện, không tính fixture hoặc chế độ ôn câu sai.
      </p>
    </div>
  );
}

function SampleView({
  label,
  sample,
  limit,
  minimum,
}: {
  label: string;
  sample: Sample;
  limit: number;
  minimum: number;
}) {
  return (
    <section className="sample-window" aria-label={label}>
      <h5>{label}</h5>
      <strong>
        {sample.accuracyPercent == null
          ? `Chưa đủ ${minimum} câu`
          : `${sample.accuracyPercent}%`}
      </strong>
      <span className="sample-bar" aria-hidden="true">
        <i style={{ width: `${sample.accuracyPercent ?? 0}%` }} />
      </span>
      <p>
        {sample.correctCount}/{sample.observedCount} câu đúng ·{" "}
        {sample.blankCount} câu trống
      </p>
      <small>
        {sample.observedCount}/{limit} câu quan sát khác nhau
      </small>
    </section>
  );
}

import { useResource, ErrorNotice, Loading } from "../hooks";
import "./skill-evidence.css";
import { ChevronDown } from "lucide-react";

type Evidence = {
  policyVersion: number;
  windowSize: number;
  minimumObservations: number;
  groups: {
    track: "practice" | "full_mock_unseen";
    skill: string;
    observedCount: number;
    correctCount: number;
    blankCount: number;
    accuracyPercent: number | null;
    status: "insufficient_data" | "observed";
    parts: { part: number; observedCount: number; correctCount: number }[];
  }[];
};
const names: Record<string, string> = {
  grammar: "Ngữ pháp",
  reading: "Đọc hiểu",
  listening: "Nghe hiểu",
  vocabulary: "Từ vựng",
};

/** VI: Luôn hiện số mẫu và tách các điều kiện làm bài; không suy điểm TOEIC. EN: Show sample size and separate test conditions without estimating a TOEIC score. */
export function SkillEvidence() {
  const resource = useResource<Evidence>("/learning/skill-evidence");
  return (
    <details className="skill-evidence">
      {/* VI: Nhiệm vụ học đứng trước phân tích chi tiết; disclosure vẫn dùng bàn phím. EN: Keep learning actions ahead of detailed analytics with a native keyboard-operable disclosure. */}
      <summary>
        <span>
          <span className="eyebrow">UNDERSTAND YOUR PRACTICE</span>
          <span className="evidence-title">Dữ liệu kỹ năng gần đây</span>
        </span>
        <ChevronDown size={23} aria-hidden="true" />
      </summary>
      <div className="evidence-content">
        <button className="link-button" onClick={resource.reload}>
          Cập nhật dữ liệu kỹ năng
        </button>
        <p className="muted">
          Tối đa 50 câu khác nhau gần nhất cho mỗi kỹ năng và nhóm làm bài. Dưới
          5 câu là chưa đủ dữ liệu; phần trăm mô tả mẫu đã làm, không dự đoán
          điểm thi.
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
                <div key={track}>
                  <h3>
                    {track === "practice"
                      ? "Bài luyện, mini test và placement"
                      : "Full mock · chưa có lời giải được mở trước lần làm"}
                  </h3>
                  {groups.length ? (
                    <div className="skill-evidence-grid">
                      {groups.map((group) => (
                        <article key={group.skill}>
                          <h4>{names[group.skill] ?? group.skill}</h4>
                          <b>
                            {group.accuracyPercent == null
                              ? "Chưa đủ dữ liệu"
                              : `${group.accuracyPercent}%`}
                          </b>
                          <p>
                            {group.correctCount}/{group.observedCount} câu đúng
                            · {group.blankCount} câu trống
                          </p>
                          <small>
                            {group.observedCount}/50 câu quan sát khác nhau
                          </small>
                          <details>
                            <summary>Theo Part trong mẫu này</summary>
                            <ul>
                              {group.parts.map((part) => (
                                <li key={part.part}>
                                  Part {part.part}: {part.correctCount}/
                                  {part.observedCount} câu đúng
                                </li>
                              ))}
                            </ul>
                          </details>
                        </article>
                      ))}
                    </div>
                  ) : (
                    <p className="muted">Chưa có câu hợp lệ trong nhóm này.</p>
                  )}
                </div>
              );
            })}
            <small>
              Không tính fixture và chế độ ôn câu sai. Cùng một phiên bản câu
              hỏi chỉ lấy lần hợp lệ gần nhất trong mỗi nhóm. Lịch sử full mock
              cũ chưa theo dõi việc mở lời giải không được coi là mẫu chưa lộ
              đáp án.
            </small>
          </>
        ) : null}
      </div>
    </details>
  );
}

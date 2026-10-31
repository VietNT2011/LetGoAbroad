import { Link } from "react-router-dom";
import { CheckCircle2, LockKeyhole, ArrowRight } from "lucide-react";
import "./paths.css";

export type PathTemplateSummary = {
  id: string;
  title: string;
  level: string;
  version: number;
  nodeCount: number;
  studyDays: number;
};
export type PathPlan = {
  legacy: boolean;
  studyDay: number;
  template: null | {
    id: string;
    title: string;
    summary: string;
    version: number;
    level: string;
    pathStartedOn: string;
    pathTimeZoneId: string;
    pathStudyDays: string;
  };
  nodes: {
    id: string;
    key: string;
    contentVersionId: string;
    title: string;
    summary: string;
    skill: string;
    kind: "lesson" | "assessment";
    minutes: number;
    studyDay: number;
    stage: string;
    completed: boolean;
    unlocked: boolean;
    due: boolean;
    prerequisites: string[];
    minimumCorrectPercent: number;
    completionRule: string;
  }[];
};

/** VI: Trạng thái mở khóa lấy từ bằng chứng server, không tự suy từ index. EN: Unlocking is server-derived evidence rather than the item's position in the list. */
export function PathTimeline({ plan }: { plan: PathPlan }) {
  const titles = new Map(plan.nodes.map((node) => [node.id, node.title]));
  return (
    <>
      <p className="muted">
        Mốc ngày học là gợi ý. Bước sau mở khi hoàn thành các bước cần thiết;
        nhiệm vụ được chọn theo ngân sách. Bạn vẫn có thể học tự chọn trong thư
        viện.
      </p>
      <div className="path-list">
        {plan.nodes.map((node) => (
          <article
            className={`path-node ${node.completed ? "completed" : !node.unlocked || !node.due ? "locked" : ""}`}
            key={node.id}
          >
            <span className="path-dot">
              {node.completed ? (
                <CheckCircle2 size={22} />
              ) : !node.unlocked ? (
                <LockKeyhole size={18} />
              ) : (
                node.studyDay.toString().padStart(2, "0")
              )}
            </span>
            <div className="path-node-copy">
              <span className="eyebrow">
                {node.stage} · Ngày học {node.studyDay}
              </span>
              <h3>{node.title}</h3>
              <p>{node.summary}</p>
              <small>
                {node.kind === "lesson"
                  ? "Hoàn thành quiz bài học"
                  : `Đạt ít nhất ${node.minimumCorrectPercent}% raw score bằng một lần làm mới`}
              </small>
              {!node.unlocked ? (
                <p className="path-prerequisite">
                  Cần hoàn thành:{" "}
                  {node.prerequisites.map((id) => titles.get(id)).join("; ")}
                </p>
              ) : null}
            </div>
            <span className="task-time">{node.minutes} phút</span>
            {node.completed || (node.unlocked && node.due) ? (
              <Link
                className="back-link"
                to={
                  node.kind === "lesson"
                    ? `/learn/${node.contentVersionId}`
                    : `/tests/${node.contentVersionId}`
                }
              >
                {node.completed ? "Xem lại" : "Mở bài"}
                <ArrowRight size={15} />
              </Link>
            ) : (
              <span className="path-locked-label">
                {!node.unlocked
                  ? "Chờ bước trước"
                  : `Ngày học ${node.studyDay}`}
              </span>
            )}
          </article>
        ))}
      </div>
    </>
  );
}

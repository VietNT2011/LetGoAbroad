import { useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  CheckCircle2,
  ArrowRight,
  Route,
  Clock3,
  BookOpen,
  Target,
  CalendarDays,
} from "lucide-react";
import { api, post, type Content } from "../api";
import { ErrorNotice, Loading, useResource } from "../hooks";
import { useAuth } from "../Auth";
import "./learning.css";
import {
  PathTimeline,
  type PathPlan,
  type PathTemplateSummary,
} from "./PathTimeline";
export type DashboardData = {
  localDate: string;
  timeZoneId: string;
  enrollment: null | {
    id: string;
    level: string;
    targetScore: number;
    minutesPerDay: number;
    ruleVersion: number;
  };
  tasks: {
    id: string;
    contentVersionId: string;
    title: string;
    kind: "lesson" | "assessment";
    estimatedMinutes: number;
    status: string;
    reasonText: string;
    reasonCode: string;
    activeAttemptId: string | null;
    carriedFromPreviousDate: boolean;
    localDate: string;
  }[];
  statistics: {
    lessonsCompleted: number;
    attemptsCompleted: number;
    questionsCorrect: number;
    questionsObserved: number;
  };
};

/** VI: Số liệu và nhiệm vụ lấy từ bằng chứng server. EN: Show only server-derived evidence and acknowledged task state. */
export function TodayPanel() {
  const { data, loading, error, reload } = useResource<DashboardData>(
    "/learning/dashboard",
  );
  if (loading) return <Loading />;
  if (error) return <ErrorNotice error={error} retry={reload} />;
  if (!data) return null;
  return (
    <>
      <div className="stats-grid">
        <div>
          <BookOpen size={19} />
          <b>{data.statistics.lessonsCompleted}</b>
          <span>Bài học đã hoàn thành</span>
        </div>
        <div>
          <CheckCircle2 size={19} />
          <b>{data.statistics.attemptsCompleted}</b>
          <span>Lần luyện tập đã nộp</span>
        </div>
        <div>
          <Target size={19} />
          <b>
            {data.statistics.questionsCorrect}
            <small> / {data.statistics.questionsObserved}</small>
          </b>
          <span>Câu đúng / câu quan sát</span>
        </div>
      </div>
      <div className="section-heading">
        <div>
          <span className="eyebrow">A LITTLE PLAN, A LOT OF POSSIBILITY</span>
          <h2>Nhịp học hôm nay</h2>
        </div>
        <Link to="/paths">
          Xem lộ trình <ArrowRight size={15} />
        </Link>
      </div>
      {!data.enrollment ? (
        <div className="onboarding-card">
          <Route size={28} />
          <div>
            <h3>Hành trình của riêng bạn.</h3>
            <p>Chọn mục tiêu và tạo nhịp học phù hợp với mình.</p>
          </div>
          <Link className="primary" to="/paths">
            Tạo lộ trình <ArrowRight size={15} />
          </Link>
        </div>
      ) : data.tasks.length ? (
        <div className="daily-tasks">
          {data.tasks.map((task, i) => (
            <Link
              to={
                task.activeAttemptId
                  ? `/attempts/${task.activeAttemptId}`
                  : task.kind === "lesson"
                    ? `/learn/${task.contentVersionId}`
                    : `/tests/${task.contentVersionId}`
              }
              className={`daily-task ${task.status}`}
              key={task.id}
            >
              <span className="task-order">
                {task.status === "completed" ? (
                  <CheckCircle2 size={22} />
                ) : (
                  `0${i + 1}`
                )}
              </span>
              <div>
                <h3>
                  {task.title}
                  {task.status === "completed" ? (
                    <small className="task-completed">Đã hoàn thành</small>
                  ) : null}
                </h3>
                <p>{task.reasonText}</p>
                {task.carriedFromPreviousDate ? (
                  <small>
                    Đang tiếp tục nhiệm vụ từ {task.localDate}; thời hạn bài làm
                    vẫn do server quyết định.
                  </small>
                ) : null}
              </div>
              <span className="task-time">
                <Clock3 size={13} />
                {task.estimatedMinutes} phút
              </span>
              <ArrowRight size={17} />
            </Link>
          ))}
        </div>
      ) : (
        <div className="empty compact">
          Hôm nay chưa có nhiệm vụ phù hợp lịch học. Bạn vẫn có thể khám phá thư
          viện bài học.
        </div>
      )}
    </>
  );
}
type PathLesson = {
  id: string;
  title: string;
  summary: string;
  skill: string;
  minutes: number;
  completed: boolean;
};
export function PathsPage() {
  const dashboard = useResource<DashboardData>("/learning/dashboard");
  const plan = useResource<PathPlan>("/learning/path-plan");
  const templates = useResource<PathTemplateSummary[]>("/learning/templates");
  const path = useResource<PathLesson[]>(
    plan.data?.legacy ? "/learning/path" : null,
  );
  const catalog = useResource<Content[]>("/catalog");
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const navigate = useNavigate();
  const placementCommand = useRef(crypto.randomUUID());
  const adoptionCommand = useRef(crypto.randomUUID());
  const [selectedTemplate, setSelectedTemplate] = useState("");
  const start = async () => {
    setBusy(true);
    try {
      await post("/learning/enrollment", {
        startFromBasics: true,
        pathVersionId: selectedTemplate || null,
      });
      dashboard.reload();
      plan.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const placement = async () => {
    const exam = catalog.data?.find((x) => x.slug === "placement-demo");
    if (!exam) return;
    setBusy(true);
    try {
      const attempt = await post<{ id: string }>("/attempts/", {
        contentVersionId: exam.id,
        clientRequestId: placementCommand.current,
        mode: "placement",
      });
      navigate(`/attempts/${attempt.id}`);
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const enrollFromPlacement = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    try {
      const form = new FormData(event.currentTarget);
      await post("/learning/enrollment", {
        startFromBasics: false,
        placementAttemptId: form.get("attemptId"),
        pathVersionId: selectedTemplate || null,
      });
      dashboard.reload();
      plan.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const adopt = async () => {
    const pathVersionId =
      selectedTemplate ||
      templates.data?.find((item) => item.level === "foundation")?.id;
    if (!pathVersionId) return;
    setBusy(true);
    setError(undefined);
    try {
      await post("/learning/enrollment/adopt-path", {
        pathVersionId,
        clientRequestId: adoptionCommand.current,
      });
      plan.reload();
      dashboard.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const picker = (
    <label className="path-template-picker">
      Mẫu đã xuất bản
      <select
        value={selectedTemplate}
        onChange={(e) => {
          setSelectedTemplate(e.target.value);
          adoptionCommand.current = crypto.randomUUID();
        }}
      >
        <option value="">
          Theo gợi ý trình độ / Foundation khi bắt đầu từ căn bản
        </option>
        {templates.data?.map((item) => (
          <option key={item.id} value={item.id}>
            {item.title} · v{item.version} · {item.nodeCount} bước
          </option>
        ))}
      </select>
    </label>
  );
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">YOUR OWN WAY FORWARD</span>
          <h1>Mỗi hành trình, một nhịp riêng.</h1>
          <p>Biết mình đang ở đâu. Rõ bước tiếp theo cần làm.</p>
        </div>
        <span className="outline-tag">
          <CalendarDays size={15} /> Theo lịch học của bạn
        </span>
      </div>
      <ErrorNotice
        error={
          error ??
          dashboard.error ??
          path.error ??
          plan.error ??
          templates.error
        }
        retry={() => {
          dashboard.reload();
          path.reload();
          plan.reload();
          templates.reload();
        }}
      />
      {dashboard.loading ? (
        <Loading />
      ) : !dashboard.data?.enrollment ? (
        <section className="lesson">
          <span className="eyebrow">LET'S FIND YOUR STARTING POINT</span>
          <h2>Bạn muốn bắt đầu thế nào?</h2>
          <p className="muted">
            Bạn có thể xây nền tảng từ đầu hoặc đánh giá trình độ bằng bài mini
            test.
          </p>
          {picker}
          <div className="button-row">
            <button className="primary" disabled={busy} onClick={start}>
              Bắt đầu từ căn bản <ArrowRight size={15} />
            </button>
            <button
              className="secondary"
              disabled={busy || !catalog.data}
              onClick={placement}
            >
              Đánh giá đầu vào
            </button>
          </div>
          <details>
            <summary>Đã hoàn thành bài đánh giá?</summary>
            <form onSubmit={enrollFromPlacement} className="placement-form">
              <label>
                Mã lần làm bài
                <input
                  name="attemptId"
                  required
                  placeholder="Mã trong đường dẫn trang kết quả"
                />
              </label>
              <button className="secondary" disabled={busy}>
                Dùng kết quả này
              </button>
            </form>
          </details>
          <Link className="back-link" to="/settings">
            Điều chỉnh mục tiêu, số phút và ngày học →
          </Link>
        </section>
      ) : (
        <>
          <div className="path-banner">
            <Route size={31} />
            <div>
              <span className="eyebrow">
                {dashboard.data.enrollment.level
                  .replace("_", " ")
                  .toUpperCase()}
              </span>
              <h2>{plan.data?.template?.title ?? "Hành trình học hiện tại"}</h2>
              <p>
                Mục tiêu {dashboard.data.enrollment.targetScore} ·{" "}
                {dashboard.data.enrollment.minutesPerDay} phút/ngày ·{" "}
                {dashboard.data.timeZoneId}
                {plan.data?.template
                  ? ` · Mẫu v${plan.data.template.version} đã ghim · Ngày học ${plan.data.studyDay}`
                  : " · Chưa ghim mẫu lộ trình"}
              </p>
            </div>
            <span>
              {plan.data?.template
                ? `${plan.data.nodes.filter((node) => node.completed).length} / ${plan.data.nodes.length} bước`
                : `${path.data?.filter((x) => x.completed).length ?? 0} / ${path.data?.length ?? 0} bài`}
            </span>
          </div>
          {plan.loading ? (
            <Loading />
          ) : plan.data?.template ? (
            <PathTimeline plan={plan.data} />
          ) : (
            <>
              {plan.data?.legacy && templates.data?.length ? (
                <section className="path-adoption">
                  <h3>Chuyển sang mẫu có phiên bản</h3>
                  <p>
                    Nhiệm vụ hôm nay được giữ nguyên. Các ngày chưa có nhiệm vụ
                    sẽ dùng mẫu đã ghim; tiến độ bài đã học vẫn được giữ.
                  </p>
                  {picker}
                  <button className="primary" onClick={adopt} disabled={busy}>
                    {busy ? "Đang áp dụng…" : "Áp dụng mẫu lộ trình"}
                  </button>
                </section>
              ) : null}
              <div className="path-list">
                {path.data?.map((lesson, i) => (
                  <Link
                    to={`/learn/${lesson.id}`}
                    className={`path-node ${lesson.completed ? "completed" : ""}`}
                    key={lesson.id}
                  >
                    <span className="path-dot">
                      {lesson.completed ? (
                        <CheckCircle2 size={22} />
                      ) : (
                        String(i + 1).padStart(2, "0")
                      )}
                    </span>
                    <div>
                      <span className="eyebrow">
                        {lesson.skill.toUpperCase()}
                      </span>
                      <h3>{lesson.title}</h3>
                      <p>{lesson.summary}</p>
                    </div>
                    <span className="task-time">{lesson.minutes} phút</span>
                    <ArrowRight size={18} />
                  </Link>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </>
  );
}
export function SettingsPage() {
  const { user, refresh } = useAuth();
  const history = useResource<{
    activeRevisionId: string | null;
    revisions: {
      id: string;
      number: number;
      targetScore: number;
      minutesPerDay: number;
      effectiveAt: string;
      effectiveOn: string;
      schedulingTimeZoneId: string;
      status: "active" | "scheduled" | "past" | "superseded";
      reason: string;
    }[];
  }>("/learning/plan-revisions");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [saved, setSaved] = useState(false);
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setBusy(true);
    setSaved(false);
    setError(undefined);
    const data = new FormData(event.currentTarget);
    try {
      await api("/profile", {
        method: "PUT",
        body: JSON.stringify({
          displayName: data.get("displayName"),
          timeZoneId: data.get("timeZoneId"),
          targetScore: Number(data.get("targetScore")),
          minutesPerDay: Number(data.get("minutesPerDay")),
          studyDays: data.getAll("days").map(Number),
        }),
      });
      await refresh();
      history.reload();
      setSaved(true);
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">MAKE IT YOURS</span>
          <h1>Nhịp học của bạn.</h1>
          <p>Một kế hoạch tốt là kế hoạch vừa với cuộc sống của mình.</p>
        </div>
      </div>
      <section className="lesson settings-form">
        <ErrorNotice error={error} />
        {saved ? (
          <div className="notice success">
            <CheckCircle2 size={17} /> Đã lưu cấu hình. Thay đổi mục tiêu và
            ngân sách phút áp dụng cho ngày học tiếp theo; lịch đang có được
            giữ.
          </div>
        ) : null}
        <form onSubmit={submit}>
          <label>
            Tên hiển thị
            <input
              name="displayName"
              defaultValue={user?.displayName}
              maxLength={120}
              required
            />
          </label>
          <label>
            Mục tiêu
            <select name="targetScore" defaultValue={user?.targetScore}>
              {[450, 650, 800].map((x) => (
                <option key={x}>{x}</option>
              ))}
            </select>
          </label>
          <label>
            Thời gian mỗi ngày
            <select name="minutesPerDay" defaultValue={user?.minutesPerDay}>
              {[20, 30, 45].map((x) => (
                <option key={x} value={x}>
                  {x} phút
                </option>
              ))}
            </select>
          </label>
          <label>
            Múi giờ
            <input
              name="timeZoneId"
              defaultValue={user?.timeZoneId}
              required
              placeholder="Asia/Ho_Chi_Minh"
            />
          </label>
          <fieldset>
            <legend>Ngày có thể học</legend>
            <div className="study-days">
              {["CN", "T2", "T3", "T4", "T5", "T6", "T7"].map((day, i) => (
                <label key={day}>
                  <input
                    type="checkbox"
                    name="days"
                    value={i}
                    defaultChecked={user?.studyDays
                      .split(",")
                      .includes(String(i))}
                  />
                  {day}
                </label>
              ))}
            </div>
          </fieldset>
          <button className="primary" disabled={busy}>
            {busy ? "Đang lưu…" : "Lưu nhịp học"}
          </button>
        </form>
      </section>
      <section className="lesson goal-history">
        <h2>Lịch sử mục tiêu và thời gian học</h2>
        <p className="muted">
          Mục tiêu là ý định của bạn; số điểm này không phải điểm TOEIC được dự
          đoán. Mốc áp dụng dùng múi giờ tại lúc yêu cầu đổi mục tiêu.
        </p>
        <ErrorNotice error={history.error} retry={history.reload} />
        {history.loading ? (
          <Loading />
        ) : history.data?.revisions.length ? (
          <div className="goal-history-scroll">
            <table>
              <thead>
                <tr>
                  <th>Phiên bản</th>
                  <th>Mục tiêu</th>
                  <th>Phút/ngày</th>
                  <th>Áp dụng từ</th>
                  <th>Trạng thái</th>
                </tr>
              </thead>
              <tbody>
                {history.data.revisions.map((revision) => (
                  <tr key={revision.id}>
                    <td>v{revision.number}</td>
                    <td>{revision.targetScore}</td>
                    <td>{revision.minutesPerDay}</td>
                    <td>
                      {new Intl.DateTimeFormat("vi-VN", {
                        dateStyle: "short",
                        timeStyle: "short",
                        timeZone: revision.schedulingTimeZoneId,
                      }).format(new Date(revision.effectiveAt))}
                      <br />
                      <small>{revision.schedulingTimeZoneId}</small>
                    </td>
                    <td>
                      {
                        {
                          active: "Đang áp dụng",
                          scheduled: "Chờ ngày học tiếp theo",
                          past: "Đã áp dụng trước đây",
                          superseded: "Đã được thay thế",
                        }[revision.status]
                      }
                      {revision.reason === "observed_baseline" ? (
                        <small> · Cấu hình được ghi nhận khi nâng cấp</small>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p>Tạo lộ trình để bắt đầu ghi lại lịch sử mục tiêu.</p>
        )}
      </section>
    </>
  );
}

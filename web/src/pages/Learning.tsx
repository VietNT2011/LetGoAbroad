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
                task.kind === "lesson"
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
  const path = useResource<PathLesson[]>("/learning/path");
  const catalog = useResource<Content[]>("/catalog");
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const navigate = useNavigate();
  const placementCommand = useRef(crypto.randomUUID());
  const start = async () => {
    setBusy(true);
    try {
      await post("/learning/enrollment", { startFromBasics: true });
      dashboard.reload();
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
      });
      dashboard.reload();
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
          <span className="eyebrow">YOUR OWN WAY FORWARD</span>
          <h1>Mỗi hành trình, một nhịp riêng.</h1>
          <p>Biết mình đang ở đâu. Rõ bước tiếp theo cần làm.</p>
        </div>
        <span className="outline-tag">
          <CalendarDays size={15} /> Theo lịch học của bạn
        </span>
      </div>
      <ErrorNotice
        error={error ?? dashboard.error ?? path.error}
        retry={() => {
          dashboard.reload();
          path.reload();
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
              <h2>Từng bước xây nền tảng vững chắc</h2>
              <p>
                Mục tiêu {dashboard.data.enrollment.targetScore} ·{" "}
                {dashboard.data.enrollment.minutesPerDay} phút/ngày ·{" "}
                {dashboard.data.timeZoneId}
              </p>
            </div>
            <span>
              {path.data?.filter((x) => x.completed).length ?? 0} /{" "}
              {path.data?.length ?? 0} bài
            </span>
          </div>
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
                  <span className="eyebrow">{lesson.skill.toUpperCase()}</span>
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
  );
}
export function SettingsPage() {
  const { user, refresh } = useAuth();
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
            <CheckCircle2 size={17} /> Đã lưu cấu hình của bạn.
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
    </>
  );
}

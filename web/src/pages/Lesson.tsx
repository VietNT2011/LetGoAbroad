import { useParams, Link } from "react-router-dom";
import { useResource, Loading, ErrorNotice } from "../hooks";
import { ArrowLeft, Clock3, BookOpen } from "lucide-react";
import { useRef, useState } from "react";
import { post } from "../api";
import "./learning.css";
import { AudioPlayer } from "./Media";
type LessonData = {
  id: string;
  title: string;
  summary: string;
  body: string;
  skill: string;
  minutes: number;
  attribution: string;
  version: number;
  mediaAssetId: string | null;
  quiz: {
    id: string;
    prompt: string;
    options: { id: string; text: string }[];
  }[];
};
type QuizResult = {
  completed: boolean;
  feedback: { questionId: string; isCorrect: boolean; explanation: string }[];
};
/** VI: Chỉ server chấm quiz và ghi completion. EN: Only the server grades quiz answers and persists completion evidence. */
function LessonQuiz({ lesson }: { lesson: LessonData }) {
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [result, setResult] = useState<QuizResult>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const command = useRef(crypto.randomUUID());
  const submit = async () => {
    setBusy(true);
    setError(undefined);
    try {
      setResult(
        await post<QuizResult>(`/lessons/${lesson.id}/complete`, {
          clientRequestId: command.current,
          answers: Object.entries(answers).map(
            ([questionId, selectedOptionId]) => ({
              questionId,
              selectedOptionId,
            }),
          ),
        }),
      );
      command.current = crypto.randomUUID();
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  if (!lesson.quiz?.length) return null;
  return (
    <section className="lesson-quiz">
      <span className="eyebrow">A SMALL CHECK, A STRONGER FOUNDATION</span>
      <h2>Thử nhớ lại một chút nhé.</h2>
      <ErrorNotice error={error} />
      {lesson.quiz.map((q) => (
        <fieldset key={q.id}>
          <legend>{q.prompt}</legend>
          <div className="quiz-options">
            {q.options.map((o) => (
              <label key={o.id}>
                <input
                  type="radio"
                  name={q.id}
                  checked={answers[q.id] === o.id}
                  disabled={result?.completed || busy}
                  onChange={() => {
                    command.current = crypto.randomUUID();
                    setAnswers((a) => ({ ...a, [q.id]: o.id }));
                  }}
                />
                {o.text}
              </label>
            ))}
          </div>
          {result?.feedback.find((x) => x.questionId === q.id) ? (
            <p className="quiz-feedback">
              {result.feedback.find((x) => x.questionId === q.id)!.explanation}
            </p>
          ) : null}
        </fieldset>
      ))}
      {result?.completed ? (
        <div className="notice success">
          Bạn đã hoàn thành bài học. Tiến độ đã được lưu.
          <Link to="/dashboard">Xem nhiệm vụ hôm nay →</Link>
        </div>
      ) : (
        <button
          className="primary"
          disabled={busy || Object.keys(answers).length !== lesson.quiz.length}
          onClick={submit}
        >
          {busy ? "Đang kiểm tra…" : "Kiểm tra & hoàn thành"}
        </button>
      )}
    </section>
  );
}
export default function Lesson() {
  const { id } = useParams();
  const { data, error, loading, reload } = useResource<LessonData>(
    `/lessons/${id}`,
  );
  return (
    <>
      <Link className="back-link" to="/catalog">
        <ArrowLeft size={16} /> Trở lại bài học
      </Link>
      <ErrorNotice error={error} retry={reload} />
      {loading ? (
        <Loading />
      ) : data ? (
        <article className="lesson">
          <span className="eyebrow">
            {data.skill.toUpperCase()} · VERSION {data.version}
          </span>
          <h1>{data.title}</h1>
          <p className="lead">{data.summary}</p>
          <div className="lesson-meta">
            <Clock3 size={15} /> {data.minutes} phút <BookOpen size={15} /> Nội
            dung nền tảng
          </div>
          {data.mediaAssetId ? (
            <AudioPlayer assetId={data.mediaAssetId} />
          ) : null}
          <div className="lesson-body">
            {data.body.split("\n\n").map((paragraph, i) => (
              <p key={i}>{paragraph}</p>
            ))}
          </div>
          <LessonQuiz lesson={data} />
          <div className="source-note">
            Nguồn / Attribution: {data.attribution}
          </div>
        </article>
      ) : null}
    </>
  );
}

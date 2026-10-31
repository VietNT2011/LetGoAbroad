import { useEffect, useRef, useState } from "react";
import { useNavigate, useParams, Link } from "react-router-dom";
import {
  Clock3,
  ArrowRight,
  ArrowLeft,
  CheckCircle2,
  Circle,
  RotateCcw,
  Send,
  AlertCircle,
} from "lucide-react";
import { api, post, ApiError, type Content } from "../api";
import { useResource, Loading, ErrorNotice } from "../hooks";
import { useAuth } from "../Auth";
import "./assessment.css";
type Item = {
  type: "single_choice" | "fill_blank";
  answerText: string | null;
  part: number;
  id: string;
  order: number;
  prompt: string;
  stimulus: string;
  options: { id: string; text: string }[];
  selectedOptionId: string | null;
  etag: string;
};
type Attempt = {
  listeningDeadlineAt: string | null;
  section: string;
  id: string;
  title: string;
  status: string;
  mode: string;
  isFixture: boolean;
  serverNow: string;
  deadlineAt: string;
  items: Item[];
};
type Change = {
  attemptItemId: string;
  selectedOptionId: string | null;
  expectedEtag: string;
  answerText: string | null;
};
type Batch = { clientRequestId: string; answers: Change[] };
type Ack = {
  answers: {
    attemptItemId: string;
    selectedOptionId: string | null;
    etag: string;
    answerText: string | null;
  }[];
};

export function TestIntro() {
  const { id } = useParams();
  const {
    data: exam,
    loading,
    error,
    reload,
  } = useResource<
    Content & { parts: { part: number; questionCount: number }[] }
  >(`/tests/${id}/configuration`);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<Error>();
  const command = useRef(crypto.randomUUID());
  const navigate = useNavigate();
  const [practice, setPractice] = useState(false);
  const [part, setPart] = useState("");
  const [count, setCount] = useState(10);
  const [preview, setPreview] = useState<{
    actualQuestionCount: number;
    groupCount: number;
  }>();
  const configuration = { data: exam };
  const payload = () => ({
    contentVersionId: id,
    clientRequestId: command.current,
    mode: exam?.isFixture ? "fixture" : practice ? "practice" : "mini",
    part: practice && part ? Number(part) : null,
    questionCount: practice ? count : null,
  });
  const changed = () => {
    command.current = crypto.randomUUID();
    setPreview(undefined);
  };
  const prepare = async () => {
    setBusy(true);
    try {
      setPreview(await post("/attempts/preview", payload()));
    } catch (e) {
      setFailure(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const start = async () => {
    if (!exam) return;
    setBusy(true);
    setFailure(undefined);
    try {
      const a = await post<Attempt>("/attempts/", payload());
      navigate(`/attempts/${a.id}`);
    } catch (e) {
      setFailure(e as Error);
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <Link className="back-link" to="/tests">
        <ArrowLeft size={16} /> Quay lại luyện tập
      </Link>
      <ErrorNotice error={error ?? failure} retry={reload} />
      {loading ? (
        <Loading />
      ) : exam ? (
        <section className="lesson test-intro">
          <span className="eyebrow">MAKE SPACE FOR PROGRESS</span>
          <h1>{exam.title}</h1>
          <p className="lead">{exam.summary}</p>
          {!exam.isFixture ? (
            <div className="practice-controls">
              <label className="checkbox">
                <input
                  type="checkbox"
                  checked={practice}
                  onChange={(e) => {
                    setPractice(e.target.checked);
                    changed();
                  }}
                />{" "}
                Luyện theo Part và số câu
              </label>
              {practice ? (
                <>
                  <label>
                    Part
                    <select
                      value={part}
                      onChange={(e) => {
                        setPart(e.target.value);
                        changed();
                      }}
                    >
                      <option value="">Tất cả Part có sẵn</option>
                      {configuration.data?.parts.map((p) => (
                        <option key={p.part} value={p.part}>
                          Part {p.part} · {p.questionCount} câu
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Số câu mục tiêu
                    <input
                      type="number"
                      min={1}
                      max={exam.questionCount}
                      value={count}
                      onChange={(e) => {
                        setCount(Number(e.target.value));
                        changed();
                      }}
                    />
                  </label>
                  <p className="muted">
                    Nhóm câu cùng ngữ cảnh được giữ nguyên. Xem số câu thực tế
                    trước khi bắt đầu.
                  </p>
                  {preview ? (
                    <div className="notice success">
                      Phiên luyện gồm {preview.actualQuestionCount} câu trong{" "}
                      {preview.groupCount} nhóm nguyên vẹn.
                    </div>
                  ) : null}
                </>
              ) : null}
            </div>
          ) : null}
          <div className="test-facts">
            <div>
              <b>{exam.questionCount}</b>
              <span>Câu hỏi</span>
            </div>
            <div>
              <b>{Math.round(exam.durationSeconds / 60)}</b>
              <span>Phút làm bài</span>
            </div>
            <div>
              <b>Raw</b>
              <span>Điểm số minh bạch</span>
            </div>
          </div>
          <ul className="plain-list">
            <li>
              Thời gian do server quyết định. Tải lại trang không gia hạn bài.
            </li>
            <li>Câu trả lời chỉ được ghi nhận khi hiển thị “Đã lưu”.</li>
            <li>Khi mất mạng, bản nháp được giữ trên thiết bị này.</li>
            <li>Sau khi nộp, bạn sẽ thấy đáp án và lời giải cho từng câu.</li>
          </ul>
          {exam.isFixture ? (
            <div className="notice">
              Dữ liệu demo kỹ thuật kiểm thử timer và chấm điểm, không phải đề
              TOEIC hoàn chỉnh.
            </div>
          ) : null}
          <button
            className="primary"
            onClick={practice && !preview ? prepare : start}
            disabled={busy}
          >
            {busy
              ? "Đang xử lý…"
              : practice && !preview
                ? "Xem cấu hình phiên luyện"
                : "Sẵn sàng, bắt đầu thôi"}
            <ArrowRight size={16} />
          </button>
        </section>
      ) : (
        <div className="empty">Không tìm thấy đề.</div>
      )}
    </>
  );
}

/** VI: Queue giữ CommandId khi retry và không xóa chỉnh sửa mới hơn ACK. EN: Stable command IDs survive retries; acknowledgements never erase newer local edits. */
export function AttemptPage() {
  const { id } = useParams();
  const { user } = useAuth();
  const navigate = useNavigate();
  const [attempt, setAttempt] = useState<Attempt>();
  const current = useRef<Attempt | undefined>(undefined);
  const pending = useRef<Record<string, string | null>>({});
  const request = useRef<Batch | undefined>(undefined);
  const saving = useRef<Promise<boolean> | undefined>(undefined);
  const [choices, setChoices] = useState<Record<string, string | null>>({});
  const [saveState, setSaveState] = useState("Đã lưu");
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const [index, setIndex] = useState(0);
  const [now, setNow] = useState(Date.now());
  const offset = useRef(0);
  const autoSubmitted = useRef(false);
  const submitId = useRef(crypto.randomUUID());
  const storageKey = `englishhub:draft:v1:${user?.id}:${id}`;
  const persist = () => {
    try {
      localStorage.setItem(
        storageKey,
        JSON.stringify({ pending: pending.current, request: request.current }),
      );
    } catch {
      /* VI: Duy trì queue RAM nếu storage đầy. EN: Keep the in-memory queue if storage is unavailable. */
    }
  };
  useEffect(() => {
    let active = true;
    api<Attempt>(`/attempts/${id}`)
      .then((a) => {
        if (!active) return;
        if (a.status === "graded") {
          navigate(`/attempts/${id}/result`, { replace: true });
          return;
        }
        current.current = a;
        setAttempt(a);
        offset.current = new Date(a.serverNow).getTime() - Date.now();
        const initial = Object.fromEntries(
          a.items.map((x) => [
            x.id,
            x.type === "fill_blank" ? (x.answerText ?? "") : x.selectedOptionId,
          ]),
        );
        try {
          const draft = JSON.parse(localStorage.getItem(storageKey) ?? "{}");
          pending.current = Object.fromEntries(
            Object.entries(draft.pending ?? {}).filter(([key, value]) =>
              a.items.some(
                (x) =>
                  x.id === key &&
                  (x.type === "fill_blank"
                    ? typeof value === "string" && value.length <= 2000
                    : value === null || x.options.some((o) => o.id === value)),
              ),
            ),
          ) as Record<string, string | null>;
          request.current = draft.request;
        } catch {
          pending.current = {};
        }
        setChoices({ ...initial, ...pending.current });
        if (Object.keys(pending.current).length)
          setSaveState("Có bản nháp chưa đồng bộ");
      })
      .catch((e) => active && setError(e));
    return () => {
      active = false;
    };
  }, [id, navigate, storageKey]);

  async function flush(): Promise<boolean> {
    if (saving.current) return saving.current;
    const operation = async () => {
      if (!current.current) return false;
      try {
        while (Object.keys(pending.current).length || request.current) {
          setSaveState("Đang lưu…");
          request.current ??= {
            clientRequestId: crypto.randomUUID(),
            answers: Object.entries(pending.current)
              .slice(0, 20)
              .map(([itemId, value]) => {
                const item = current.current!.items.find(
                  (x) => x.id === itemId,
                )!;
                return {
                  attemptItemId: itemId,
                  selectedOptionId: item.type === "fill_blank" ? null : value,
                  answerText: item.type === "fill_blank" ? value : null,
                  expectedEtag: item.etag,
                };
              }),
          };
          persist();
          const sent = request.current;
          const ack = await api<Ack>(`/attempts/${id}/answers`, {
            method: "PUT",
            body: JSON.stringify(sent),
          });
          for (const answer of ack.answers) {
            const item = current.current.items.find(
              (x) => x.id === answer.attemptItemId,
            )!;
            item.etag = answer.etag;
            item.selectedOptionId = answer.selectedOptionId;
            item.answerText = answer.answerText;
            const acknowledgedValue =
              item.type === "fill_blank"
                ? (answer.answerText ?? "")
                : answer.selectedOptionId;
            if (pending.current[item.id] === acknowledgedValue)
              delete pending.current[item.id];
          }
          request.current = undefined;
          persist();
        }
        setSaveState("Đã lưu");
        setError(undefined);
        return true;
      } catch (e) {
        if (e instanceof ApiError && e.code === "section_closed") {
          try {
            const fresh = await api<Attempt>(`/attempts/${id}`);
            current.current = fresh;
            setAttempt(fresh);
            request.current = undefined;
            for (const key of Object.keys(pending.current))
              if (fresh.items.find((q) => q.id === key)!.part <= 4)
                delete pending.current[key];
            setChoices({
              ...Object.fromEntries(
                fresh.items.map((q) => [
                  q.id,
                  q.type === "fill_blank"
                    ? (q.answerText ?? "")
                    : q.selectedOptionId,
                ]),
              ),
              ...pending.current,
            });
            persist();
          } catch {
            /* VI: Giữ bản nháp khi chưa tải lại được. EN: Preserve drafts until reconciliation succeeds. */
          }
        }
        setError(e as Error);
        setSaveState(
          e instanceof ApiError && e.status === 412
            ? "Có xung đột giữa các thiết bị"
            : "Chưa đồng bộ · bản nháp được giữ",
        );
        return false;
      }
    };
    saving.current = operation();
    try {
      return await saving.current;
    } finally {
      saving.current = undefined;
    }
  }
  const choose = (item: Item, option: string) => {
    pending.current[item.id] = option;
    setChoices((v) => ({ ...v, [item.id]: option }));
    setSaveState("Chờ lưu…");
    persist();
  };
  // VI: Debounce 500ms; flush tuần tự tránh ACK đảo thứ tự. EN: Debounce edits and serialize batches to avoid out-of-order acknowledgements.
  useEffect(() => {
    const timer = setTimeout(() => {
      if (Object.keys(pending.current).length) void flush();
    }, 500);
    return () => clearTimeout(timer);
  }, [choices]);
  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 1000);
    const online = () => void flush();
    window.addEventListener("online", online);
    return () => {
      clearInterval(tick);
      window.removeEventListener("online", online);
    };
  }, [id]);
  const remaining = attempt
    ? Math.max(
        0,
        Math.ceil(
          (new Date(attempt.deadlineAt).getTime() - now - offset.current) /
            1000,
        ),
      )
    : 1;
  const submit = async (expired = false) => {
    if (busy) return;
    setBusy(true);
    try {
      if (!expired && !(await flush())) return;
      await post(`/attempts/${id}/submit`, {
        clientRequestId: submitId.current,
        answers: [],
      });
      localStorage.removeItem(storageKey);
      navigate(`/attempts/${id}/result`, { replace: true });
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  useEffect(() => {
    if (attempt && remaining === 0 && !autoSubmitted.current) {
      autoSubmitted.current = true;
      void submit(true);
    }
  }, [remaining, attempt]);
  const rebase = async () => {
    try {
      const a = await api<Attempt>(`/attempts/${id}`);
      current.current = a;
      setAttempt(a);
      request.current = undefined;
      persist();
      await flush();
    } catch (e) {
      setError(e as Error);
    }
  };
  const listeningActive =
    !!attempt?.listeningDeadlineAt &&
    now + offset.current < new Date(attempt.listeningDeadlineAt).getTime();
  const blocked = (q: Item) =>
    !!attempt?.listeningDeadlineAt &&
    (listeningActive ? q.part > 4 : q.part <= 4);
  const displayRemaining = listeningActive
    ? Math.max(
        0,
        Math.ceil(
          (new Date(attempt!.listeningDeadlineAt!).getTime() -
            now -
            offset.current) /
            1000,
        ),
      )
    : remaining;
  useEffect(() => {
    if (attempt && blocked(attempt.items[index])) {
      const next = attempt.items.findIndex((q) => !blocked(q));
      if (next >= 0) setIndex(next);
    }
  }, [listeningActive, attempt]);
  const item = attempt?.items[index];
  return (
    <>
      <ErrorNotice error={error} />
      {error instanceof ApiError && error.status === 412 ? (
        <button className="secondary" onClick={rebase}>
          <RotateCcw size={15} /> Gửi lại bản của tôi theo phiên bản mới
        </button>
      ) : error ? (
        <button
          className="secondary"
          onClick={() => (remaining === 0 ? submit(true) : flush())}
        >
          Thử đồng bộ lại
        </button>
      ) : null}
      {!attempt ? (
        !error ? (
          <Loading />
        ) : null
      ) : (
        <>
          <div className="attempt-header">
            <div>
              <span className="eyebrow">YOUR FOCUS TIME</span>
              <h2>{attempt.title}</h2>
              {attempt.listeningDeadlineAt ? (
                <p className="muted">
                  {listeningActive
                    ? "Listening · Reading mở sau khi section kết thúc"
                    : "Reading · Listening đã khóa"}
                </p>
              ) : null}
              <span
                className={`save-state ${saveState === "Đã lưu" ? "" : "pending"}`}
              >
                {saveState === "Đã lưu" ? (
                  <CheckCircle2 size={13} />
                ) : (
                  <AlertCircle size={13} />
                )}{" "}
                {saveState}
              </span>
            </div>
            <div className={`timer ${remaining < 60 ? "urgent" : ""}`}>
              <Clock3 size={18} />
              {Math.floor(displayRemaining / 60)
                .toString()
                .padStart(2, "0")}
              :{(displayRemaining % 60).toString().padStart(2, "0")}
            </div>
          </div>
          <div className="attempt-layout">
            <section className="question-panel">
              <span className="eyebrow">
                CÂU {index + 1} / {attempt.items.length}
              </span>
              {item ? (
                <>
                  <p className="stimulus">{item.stimulus}</p>
                  <h2>{item.prompt}</h2>
                  {item.type === "fill_blank" ? (
                    <label className="fill-blank">
                      Đáp án của bạn
                      <input
                        value={choices[item.id] ?? ""}
                        onChange={(e) => choose(item, e.target.value)}
                        disabled={remaining === 0 || busy || blocked(item)}
                        maxLength={2000}
                        autoComplete="off"
                        spellCheck={false}
                      />
                      <small>Gõ đáp án rồi chờ trạng thái “Đã lưu”.</small>
                    </label>
                  ) : (
                    <div className="answer-options">
                      {item.options.map((o, i) => (
                        <button
                          key={o.id}
                          disabled={remaining === 0 || busy || blocked(item)}
                          className={choices[item.id] === o.id ? "chosen" : ""}
                          onClick={() => choose(item, o.id)}
                        >
                          <span>{String.fromCharCode(65 + i)}</span>
                          {o.text}
                          {choices[item.id] === o.id ? (
                            <CheckCircle2 size={18} />
                          ) : (
                            <Circle size={18} />
                          )}
                        </button>
                      ))}
                    </div>
                  )}
                </>
              ) : null}
              <div className="question-navigation">
                <button
                  className="secondary"
                  disabled={
                    index === 0 ||
                    (!!attempt.items[index - 1] &&
                      blocked(attempt.items[index - 1]))
                  }
                  onClick={() => setIndex((v) => v - 1)}
                >
                  <ArrowLeft size={15} /> Câu trước
                </button>
                <button
                  className="primary"
                  disabled={
                    index === attempt.items.length - 1 ||
                    (!!attempt.items[index + 1] &&
                      blocked(attempt.items[index + 1]))
                  }
                  onClick={() => setIndex((v) => v + 1)}
                >
                  Câu tiếp <ArrowRight size={15} />
                </button>
              </div>
            </section>
            <aside className="question-map">
              <h3>Hành trình làm bài</h3>
              <p>
                {Object.values(choices).filter((value) => value?.trim()).length}
                /{attempt.items.length} câu đã trả lời
              </p>
              <div>
                {attempt.items.map((q, i) => (
                  <button
                    key={q.id}
                    disabled={blocked(q)}
                    className={`${choices[q.id]?.trim() ? "answered" : ""} ${i === index ? "current" : ""}`}
                    onClick={() => setIndex(i)}
                    aria-label={`Đến câu ${i + 1}`}
                  >
                    {i + 1}
                  </button>
                ))}
              </div>
              <button
                className="primary full"
                disabled={busy || listeningActive}
                onClick={() => submit(remaining === 0)}
              >
                <Send size={15} />
                {busy ? "Đang nộp…" : "Nộp bài"}
              </button>
              <small>
                Nộp bài sẽ khóa các câu trả lời. Bạn có thể xem lời giải ngay
                sau đó.
              </small>
            </aside>
          </div>
        </>
      )}
    </>
  );
}
type ResultData = {
  id: string;
  title: string;
  isFixture: boolean;
  mode: string;
  result: {
    correctCount: number;
    totalCount: number;
    blankCount: number;
    reason: string;
  };
  items: (Item & {
    questionId: string;
    correctOptionId: string | null;
    acceptedAnswerVariants: string[];
    normalizationPolicyVersion: number;
    ignoreTerminalPunctuation: boolean;
    explanation: string;
    skill: string;
    isCorrect: boolean;
  })[];
};
export function ResultPage() {
  const { id } = useParams();
  const { data, error, loading, reload } = useResource<ResultData>(
    `/attempts/${id}/result`,
  );
  const [onlyWrong, setOnlyWrong] = useState(false);
  return (
    <>
      <Link className="back-link" to="/tests">
        <ArrowLeft size={15} /> Trở lại luyện tập
      </Link>
      <ErrorNotice error={error} retry={reload} />
      {loading ? (
        <Loading />
      ) : data ? (
        <>
          <section className="result-summary">
            <span className="eyebrow">EVERY MISTAKE IS A STEP FORWARD</span>
            <h1>Bạn đã tiến thêm một bước.</h1>
            <p>{data.title}</p>
            <div className="score-row">
              <b>
                {data.result.correctCount}
                <small> / {data.result.totalCount}</small>
              </b>
              <span>
                Câu đúng · Raw score
                <br />
                {data.result.blankCount} câu bỏ trống ·{" "}
                {data.result.reason === "deadline"
                  ? "Kết thúc do hết giờ"
                  : "Đã nộp bài"}
              </span>
            </div>
            {data.isFixture ? (
              <p className="fixture">
                Dữ liệu demo kỹ thuật · Không dùng để ước lượng điểm TOEIC
              </p>
            ) : null}
          </section>
          <div className="section-heading">
            <h2>Cùng hiểu từng câu trả lời</h2>
            <label className="checkbox">
              <input
                type="checkbox"
                checked={onlyWrong}
                onChange={(e) => setOnlyWrong(e.target.checked)}
              />{" "}
              Chỉ xem câu sai
            </label>
          </div>
          <div className="result-list">
            {data.items
              .filter((x) => !onlyWrong || !x.isCorrect)
              .map((q) => (
                <article key={q.id} className="result-item">
                  <span
                    className={`result-tag ${q.isCorrect ? "correct" : "wrong"}`}
                  >
                    {q.isCorrect
                      ? "ĐÚNG"
                      : q.selectedOptionId || q.answerText?.trim()
                        ? "CHƯA ĐÚNG"
                        : "CHƯA TRẢ LỜI"}
                  </span>
                  <h3>
                    {q.order + 1}. {q.prompt}
                  </h3>
                  {q.type === "fill_blank" ? (
                    <div className="fill-blank-review">
                      <p>
                        <b>Bạn đã viết:</b>{" "}
                        <span>
                          {q.answerText?.trim() ? q.answerText : "(Bỏ trống)"}
                        </span>
                      </p>
                      <p>
                        <b>Đáp án được chấp nhận:</b>{" "}
                        {q.acceptedAnswerVariants.join(" · ")}
                      </p>
                      <small>
                        Policy v{q.normalizationPolicyVersion}: không phân biệt
                        hoa/thường; gộp khoảng trắng và chuẩn hóa dấu nháy.
                        {q.ignoreTerminalPunctuation
                          ? " Bỏ qua dấu chấm, chấm hỏi, chấm than cuối câu."
                          : " Giữ nguyên dấu câu."}
                      </small>
                    </div>
                  ) : (
                    <div className="answer-options result-options">
                      {q.options.map((o) => (
                        <div
                          className={
                            o.id === q.correctOptionId
                              ? "correct-answer"
                              : o.id === q.selectedOptionId
                                ? "wrong-answer"
                                : ""
                          }
                          key={o.id}
                        >
                          {o.text}
                          {o.id === q.correctOptionId ? (
                            <CheckCircle2 size={15} />
                          ) : null}
                        </div>
                      ))}
                    </div>
                  )}
                  <p className="explanation">
                    <b>Vì sao?</b> {q.explanation}
                  </p>
                </article>
              ))}
          </div>
        </>
      ) : null}
    </>
  );
}

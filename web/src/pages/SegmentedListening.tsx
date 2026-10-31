import { useEffect, useRef, useState } from "react";
import { api, ApiError, post } from "../api";
import { ErrorNotice, Loading, useResource } from "../hooks";
import {
  useListeningPosition,
  type PositionAck,
} from "../useListeningPosition";
import "./listening.css";

type Token = {
  kind: "equal" | "replace" | "delete" | "insert";
  expected: string | null;
  actual: string | null;
};
type Answer = {
  id: string;
  segmentId: string;
  number: number;
  answerText: string;
  isCorrect: boolean;
  isAssisted: boolean;
  expectedText: string;
  tokens: Token[];
};
type Segment = {
  id: string;
  order: number;
  startMs: number;
  endMs: number;
  textEn: string | null;
  textVi: string | null;
  isDisclosed: boolean;
  latest: Answer | null;
};
export type ListeningData = {
  id: string;
  contentVersionId: string;
  mediaAssetId: string;
  durationMs: number;
  title: string;
  expiresAt: string;
  serverNow: string;
  isCurrent: boolean;
  resume: PositionAck;
  answeredSegments: number;
  firstUnaidedCount: number;
  firstAssistedCount: number;
  firstCorrectUnaided: number;
  firstCorrectAssisted: number;
  segments: Segment[];
};
type Info = {
  sessionId: string | null;
  resumeEtag: string | null;
  canStart: boolean;
  segmentCount: number;
};
const time = (ms: number) =>
  `${Math.floor(ms / 60000)}:${String(Math.floor(ms / 1000) % 60).padStart(2, "0")}`;

/** VI: Mở panel chỉ đọc; tạo session phải có thao tác rõ ràng. EN: Opening the panel is read-only; starting practice requires an explicit command. */
export function SegmentedListening({ versionId }: { versionId: string }) {
  const [open, setOpen] = useState(false);
  return (
    <details
      className="listening-panel"
      onToggle={(e) => setOpen(e.currentTarget.open)}
    >
      <summary>Nghe theo đoạn & luyện dictation</summary>
      {open ? <ListeningLauncher versionId={versionId} /> : null}
    </details>
  );
}

export function ListeningLauncher({ versionId }: { versionId: string }) {
  const info = useResource<Info>(`/lessons/${versionId}/listening`);
  const [session, setSession] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const pending = useRef<
    { clientRequestId: string; expectedResumeEtag: string | null } | undefined
  >(undefined);
  const start = async () => {
    if (busy || !info.data) return;
    setBusy(true);
    setError(undefined);
    pending.current ??= {
      clientRequestId: crypto.randomUUID(),
      expectedResumeEtag: info.data.resumeEtag,
    };
    try {
      const data = await post<ListeningData>(
        `/lessons/${versionId}/listening`,
        pending.current,
      );
      pending.current = undefined;
      setSession(data.id);
    } catch (failure) {
      setError(failure as Error);
      if (failure instanceof ApiError && failure.status < 500) {
        pending.current = undefined;
        info.reload();
      }
    } finally {
      setBusy(false);
    }
  };
  if (session)
    return (
      <ListeningLoader
        id={session}
        back={() => {
          setSession(undefined);
          info.reload();
        }}
      />
    );
  return (
    <div className="listening-content">
      <ErrorNotice error={info.error} retry={info.reload} />
      <ErrorNotice error={error} />
      {info.loading ? (
        <Loading />
      ) : info.data ? (
        <>
          <p>
            {info.data.segmentCount} đoạn biên soạn · nghe từng câu, gõ lại và
            đối chiếu. Transcript/lời giải đã từng mở được tính là trợ giúp, kể
            cả trong phiên trước.
          </p>
          <div className="listening-actions">
            {info.data.sessionId ? (
              <button
                type="button"
                className="button primary"
                disabled={busy}
                onClick={() => setSession(info.data!.sessionId!)}
              >
                Tiếp tục phiên nghe
              </button>
            ) : null}
            <button
              type="button"
              className="secondary"
              disabled={busy || !info.data.canStart}
              onClick={start}
            >
              {busy
                ? "Đang tạo phiên…"
                : pending.current
                  ? "Gửi lại yêu cầu tạo phiên"
                  : "Tạo phiên luyện mới"}
            </button>
          </div>
          <p>
            Mỗi phiên nhận câu trả lời trong24 giờ. Lượt kiểm tra đầu mỗi đoạn
            được thống kê riêng; luyện lại và mở lời giải vẫn được lưu.
          </p>
        </>
      ) : null}
    </div>
  );
}

function ListeningLoader({ id, back }: { id: string; back: () => void }) {
  const resource = useResource<ListeningData>(`/listening/${id}`);
  return (
    <div className="listening-content">
      <ErrorNotice error={resource.error} retry={resource.reload} />
      {resource.loading ? (
        <Loading />
      ) : resource.data ? (
        <ListeningPractice
          key={resource.data.id}
          initial={resource.data}
          reload={back}
        />
      ) : null}
    </div>
  );
}

export function ListeningPractice({
  initial,
  reload,
}: {
  initial: ListeningData;
  reload: () => void;
}) {
  const [data, setData] = useState(initial);
  const [selected, setSelected] = useState(() =>
    Math.max(
      0,
      initial.segments.findIndex((x) => x.endMs > initial.resume.positionMs),
    ),
  );
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState("");
  const [uncertain, setUncertain] = useState<"answer" | "reveal">();
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const [loop, setLoop] = useState(false);
  const [rate, setRate] = useState(1);
  const [mediaError, setMediaError] = useState(false);
  const [mediaRevision, setMediaRevision] = useState(0);
  const audio = useRef<HTMLAudioElement>(null);
  const boundary = useRef<{ start: number; end: number } | undefined>(
    undefined,
  );
  const repeat = useRef(loop);
  repeat.current = loop;
  const frame = useRef<number | undefined>(undefined);
  const lastAuto = useRef(initial.resume.positionMs);
  const commands = useRef<Record<string, { key: string; id: string }>>({});
  const segment = data.segments[selected] ?? data.segments[0];
  const draft = drafts[segment.id] ?? segment.latest?.answerText ?? "";
  const position = useListeningPosition(
    data.id,
    initial.resume,
    data.durationMs,
    () => Math.round((audio.current?.currentTime ?? 0) * 1000),
    data.isCurrent,
  );
  const clockOffset = useRef(Date.parse(initial.serverNow) - Date.now());
  const [expired, setExpired] = useState(
    () => Date.parse(initial.expiresAt) <= Date.now() + clockOffset.current,
  );
  useEffect(() => {
    const left = Date.parse(data.expiresAt) - Date.now() - clockOffset.current;
    if (left <= 0) {
      setExpired(true);
      return;
    }
    const timer = setTimeout(() => setExpired(true), left);
    return () => clearTimeout(timer);
  }, [data.expiresAt]);
  const measure = () => {
    const player = audio.current;
    const range = boundary.current;
    if (player && range && player.currentTime >= range.end / 1000) {
      if (repeat.current) {
        player.currentTime = range.start / 1000;
      } else {
        player.currentTime = range.end / 1000;
        player.pause();
        boundary.current = undefined;
      }
    }
  };
  const monitor = () => {
    measure();
    if (audio.current && !audio.current.paused)
      frame.current = requestAnimationFrame(monitor);
  };
  useEffect(
    () => () => {
      if (frame.current !== undefined) cancelAnimationFrame(frame.current);
      audio.current?.pause();
    },
    [],
  );
  const listen = async () => {
    if (!audio.current) return;
    boundary.current = { start: segment.startMs, end: segment.endMs };
    audio.current.currentTime = segment.startMs / 1000;
    try {
      await audio.current.play();
    } catch {
      setMediaError(true);
    }
  };
  const refresh = async () =>
    setData(await api<ListeningData>(`/listening/${data.id}`));
  const refreshAccepted = async () => {
    if (busy) return;
    setBusy(true);
    try {
      await refresh();
      setNeedsRefresh(false);
      setError(undefined);
    } catch (failure) {
      setError(failure as Error);
    } finally {
      setBusy(false);
    }
  };
  const run = async (kind: "answer" | "reveal") => {
    if (busy || needsRefresh || (uncertain && uncertain !== kind)) return;
    setBusy(true);
    setError(undefined);
    const key = kind === "answer" ? draft : "reveal";
    const slot = `${segment.id}:${kind}`;
    commands.current[slot] =
      commands.current[slot]?.key === key
        ? commands.current[slot]
        : { key, id: crypto.randomUUID() };
    let accepted = false;
    try {
      if (kind === "answer") {
        await post<Answer>(
          `/listening/${data.id}/segments/${segment.id}/answers`,
          { clientRequestId: commands.current[slot].id, answerText: draft },
        );
        setNotice("Đã lưu lần kiểm tra và lời giải.");
      } else {
        await post(`/listening/${data.id}/segments/${segment.id}/reveal`, {
          clientRequestId: commands.current[slot].id,
        });
        setNotice(
          "Đã mở transcript; các lần trả lời tiếp theo có nhãn trợ giúp.",
        );
      }
      accepted = true;
      setUncertain(undefined);
      delete commands.current[slot];
      await refresh();
    } catch (failure) {
      setError(failure as Error);
      if (accepted) setNeedsRefresh(true);
      else if (failure instanceof ApiError && failure.status < 500) {
        delete commands.current[slot];
        setUncertain(undefined);
      } else setUncertain(kind);
    } finally {
      setBusy(false);
    }
  };
  return (
    <section aria-label="Luyện nghe theo đoạn">
      <h2>Nghe kỹ, thử gõ lại.</h2>
      <div className="listening-summary">
        <p>
          Lần đầu tự gõ đúng:{" "}
          <b>
            {data.firstCorrectUnaided}/{data.firstUnaidedCount}
          </b>
        </p>
        <p>
          Lần đầu đã có trợ giúp đúng:{" "}
          <b>
            {data.firstCorrectAssisted}/{data.firstAssistedCount}
          </b>
        </p>
        <p>
          {data.answeredSegments}/{data.segments.length} đoạn đã kiểm tra.
        </p>
      </div>
      <p>
        Đây là kết quả luyện từng đoạn, độc lập với quiz hoàn thành bài và điểm
        bài thi.
      </p>
      <audio
        ref={audio}
        controls
        preload="metadata"
        aria-label="Audio bài nghe"
        src={`/api/v1/media/${data.mediaAssetId}/content?r=${mediaRevision}`}
        onLoadedMetadata={() => {
          if (audio.current) {
            audio.current.currentTime = Math.min(
              initial.resume.positionMs / 1000,
              audio.current.duration || 0,
            );
            audio.current.playbackRate = rate;
          }
          setMediaError(false);
        }}
        onPlay={() => {
          if (frame.current !== undefined) cancelAnimationFrame(frame.current);
          frame.current = requestAnimationFrame(monitor);
        }}
        onPause={() => position.save()}
        onSeeked={() => position.save()}
        onEnded={() => position.save()}
        onTimeUpdate={() => {
          measure();
          const value = (audio.current?.currentTime ?? 0) * 1000;
          if (Math.abs(value - lastAuto.current) >= 10000) {
            lastAuto.current = value;
            position.save(value);
          }
        }}
        onError={() => setMediaError(true)}
      />
      {mediaError ? (
        <div className="notice">
          <p>
            Audio chưa phát được. Thử cấp lại đường phát hoặc quay lại bài sau.
          </p>
          <button type="button" onClick={() => setMediaRevision((v) => v + 1)}>
            Thử phát audio lại
          </button>
        </div>
      ) : null}
      <div className="listening-actions">
        <label>
          Tốc độ{" "}
          <select
            value={rate}
            onChange={(e) => {
              const value = Number(e.target.value);
              setRate(value);
              if (audio.current) audio.current.playbackRate = value;
            }}
          >
            {[0.75, 1, 1.25, 1.5].map((value) => (
              <option key={value} value={value}>
                {value}×
              </option>
            ))}
          </select>
        </label>
        <label className="checkbox">
          <input
            type="checkbox"
            checked={loop}
            onChange={(e) => setLoop(e.target.checked)}
          />{" "}
          Lặp đoạn đang nghe
        </label>
        <button type="button" className="secondary" onClick={listen}>
          Nghe đoạn {selected + 1}
        </button>
      </div>
      <p role="status">
        {position.sending
          ? "Đang lưu vị trí…"
          : `Vị trí đã lưu: ${time(position.saved)}.`}
      </p>
      <ErrorNotice
        error={position.error}
        retry={position.conflict ? undefined : position.retry}
      />
      {!data.isCurrent || position.conflict ? (
        <div className="notice">
          <p>
            Vị trí hiện hành đã thay đổi hoặc phiên đã hết hạn. Bản đang xem
            không tự ghi đè tab khác.
          </p>
          <button type="button" onClick={reload}>
            Tải phiên nghe hiện hành
          </button>
        </div>
      ) : null}
      <div
        className="listening-segments"
        role="group"
        aria-label="Chọn đoạn nghe"
      >
        {data.segments.map((part, index) => (
          <button
            key={part.id}
            type="button"
            disabled={busy || !!uncertain || needsRefresh}
            aria-pressed={selected === index}
            onClick={() => {
              audio.current?.pause();
              boundary.current = undefined;
              setSelected(index);
              setError(undefined);
            }}
          >
            {index + 1} · {time(part.startMs)}–{time(part.endMs)}
            {part.latest?.isCorrect ? " · Đúng" : ""}
          </button>
        ))}
      </div>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void run("answer");
        }}
      >
        <label htmlFor={`dictation-${data.id}`}>
          Đoạn {selected + 1} · Gõ điều bạn nghe được
          <textarea
            id={`dictation-${data.id}`}
            maxLength={2000}
            value={draft}
            spellCheck={false}
            autoComplete="off"
            disabled={busy || expired || !!uncertain || needsRefresh}
            onChange={(e) =>
              setDrafts((old) => ({ ...old, [segment.id]: e.target.value }))
            }
          />
        </label>
        <div className="listening-actions">
          <button
            className="button primary"
            disabled={
              busy ||
              needsRefresh ||
              (expired && !uncertain) ||
              uncertain === "reveal"
            }
          >
            {busy
              ? "Đang lưu…"
              : uncertain === "answer"
                ? "Gửi lại lần kiểm tra"
                : "Kiểm tra câu đã gõ"}
          </button>
          {!segment.isDisclosed ? (
            <button
              type="button"
              className="secondary"
              disabled={
                busy ||
                needsRefresh ||
                (expired && !uncertain) ||
                uncertain === "answer"
              }
              onClick={() => run("reveal")}
            >
              {uncertain === "reveal"
                ? "Gửi lại yêu cầu mở transcript"
                : "Mở transcript & bản dịch"}
            </button>
          ) : null}
        </div>
      </form>
      <ErrorNotice
        error={error}
        retry={needsRefresh ? refreshAccepted : undefined}
      />
      {uncertain ? (
        <p className="notice">
          Chưa nhận được xác nhận. Gửi lại cùng yêu cầu; bản gõ được giữ cố định
          để không ghi thành lượt mới.
        </p>
      ) : null}
      {needsRefresh ? (
        <p className="notice">
          Lệnh đã được nhận; chỉ cần tải lại kết quả đã lưu bằng nút Thử lại.
        </p>
      ) : null}
      <p role="status">{notice}</p>
      {expired ? (
        <p className="notice">
          Phiên này đã hết hạn nhận câu trả lời. Dữ liệu cũ vẫn được giữ.
        </p>
      ) : null}
      {segment.isDisclosed ? (
        <section className="listening-transcript" aria-label="Transcript đã mở">
          <h3>Transcript & bản dịch</h3>
          <p lang="en">{segment.textEn}</p>
          <p lang="vi">{segment.textVi}</p>
          <small>
            Câu này đã được mở lời giải trong lịch sử luyện của bạn.
          </small>
        </section>
      ) : null}
      {segment.latest ? (
        <section aria-label="Kết quả dictation">
          <h3>
            {segment.latest.isCorrect
              ? "Đúng theo đáp án biên soạn"
              : "Cùng đối chiếu lại nhé"}
          </h3>
          <p>
            Lần {segment.latest.number} ·{" "}
            {segment.latest.isAssisted
              ? "Đã có trợ giúp trước khi kiểm tra"
              : "Chưa mở lời giải trước khi kiểm tra"}
          </p>
          <p>
            Đáp án đối chiếu:{" "}
            <span lang="en">{segment.latest.expectedText}</span>
          </p>
          <ol className="dictation-diff">
            {segment.latest.tokens.map((token, index) => (
              <li className={`diff-${token.kind}`} key={index}>
                <b>
                  {token.kind === "equal"
                    ? "Khớp"
                    : token.kind === "delete"
                      ? "Thiếu"
                      : token.kind === "insert"
                        ? "Thừa"
                        : "Khác"}
                </b>
                :{" "}
                {token.kind === "insert"
                  ? token.actual
                  : token.kind === "replace"
                    ? `${token.expected} → ${token.actual}`
                    : token.expected}
              </li>
            ))}
          </ol>
        </section>
      ) : null}
      <button
        type="button"
        className="link-button"
        disabled={busy || !!uncertain}
        onClick={reload}
      >
        Chọn lại phiên nghe
      </button>
    </section>
  );
}

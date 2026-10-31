import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Bookmark, CheckCircle2, Save } from "lucide-react";
import { api, ApiError } from "../api";
import { useResource, ErrorNotice, Loading } from "../hooks";
import "./annotations.css";

export type Annotation = {
  id: string;
  questionId: string;
  sourceAttemptId: string;
  sourceAttemptItemId: string;
  contentVersionId: string;
  title: string;
  prompt: string;
  stimulus: string;
  isBookmarked: boolean;
  noteText: string;
  anchorBlock: "prompt" | "stimulus";
  anchorStart: number;
  anchorEnd: number;
  etag: string;
};
type AnnotationInput = {
  isBookmarked: boolean;
  noteText: string;
  expectedEtag: string | null;
  anchorBlock: "prompt" | "stimulus";
  anchorStart: number;
  anchorEnd: number | null;
};

/** VI: Một GET cho cả attempt; retry giữ CommandId/payload. EN: Fetch annotations once per attempt and retain command IDs for identical retries. */
export function useAnnotations(attemptId: string | undefined) {
  const resource = useResource<Record<string, Annotation>>(
    attemptId ? `/attempts/${attemptId}/annotations` : null,
  );
  const [updates, setUpdates] = useState<Record<string, Annotation>>({});
  const commands = useRef<Record<string, string>>({});
  useEffect(() => {
    setUpdates({});
    commands.current = {};
  }, [attemptId]);
  const values = { ...resource.data, ...updates };
  const save = async (itemId: string, input: AnnotationInput) => {
    const payload = JSON.stringify(input);
    const commandKey = `${itemId}:${payload}`;
    commands.current[commandKey] ??= crypto.randomUUID();
    const result = await api<Annotation>(
      `/attempts/${attemptId}/items/${itemId}/annotation`,
      {
        method: "PUT",
        body: JSON.stringify({
          ...input,
          clientRequestId: commands.current[commandKey],
        }),
      },
    );
    setUpdates((old) => ({ ...old, [itemId]: result }));
    delete commands.current[commandKey];
    return result;
  };
  const reload = () => {
    setUpdates({});
    resource.reload();
  };
  return {
    values,
    save,
    reload,
    loading: resource.loading,
    error: resource.error,
  };
}

type AnnotationControlProps = {
  itemId: string;
  annotation?: Annotation;
  disabled?: boolean;
  save: (itemId: string, input: AnnotationInput) => Promise<Annotation>;
  reload: () => void;
};

/** VI: Bookmark và ghi chú độc lập; xung đột không tự ghi đè bản mới. EN: Bookmark and note changes remain independent; conflicts never silently overwrite a newer version. */
export function BookmarkButton({
  itemId,
  annotation,
  disabled,
  save,
  reload,
}: AnnotationControlProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  return (
    <div className="bookmark-control">
      <button
        className="secondary"
        aria-pressed={annotation?.isBookmarked ?? false}
        disabled={disabled || busy}
        onClick={async () => {
          setBusy(true);
          setError(undefined);
          try {
            await save(itemId, {
              isBookmarked: !annotation?.isBookmarked,
              noteText: annotation?.noteText ?? "",
              expectedEtag: annotation?.etag ?? null,
              anchorBlock: annotation?.anchorBlock ?? "prompt",
              anchorStart: annotation?.anchorStart ?? 0,
              anchorEnd: annotation?.anchorEnd ?? null,
            });
          } catch (e) {
            setError(e as Error);
          } finally {
            setBusy(false);
          }
        }}
      >
        <Bookmark
          size={15}
          fill={annotation?.isBookmarked ? "currentColor" : "none"}
        />
        {busy
          ? "Đang lưu…"
          : annotation?.isBookmarked
            ? "Đã đánh dấu"
            : "Đánh dấu câu"}
      </button>
      <ErrorNotice error={error} retry={reload} />
    </div>
  );
}

export function QuestionNote({
  itemId,
  annotation,
  disabled,
  save,
  reload,
}: AnnotationControlProps) {
  const [text, setText] = useState(annotation?.noteText ?? "");
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<Error>();
  useEffect(() => {
    if (!dirty) setText(annotation?.noteText ?? "");
  }, [annotation?.etag, dirty]);
  const store = async (noteText: string) => {
    setBusy(true);
    setSaved(false);
    setError(undefined);
    try {
      const result = await save(itemId, {
        isBookmarked: annotation?.isBookmarked ?? false,
        noteText,
        expectedEtag: annotation?.etag ?? null,
        anchorBlock: annotation?.anchorBlock ?? "prompt",
        anchorStart: annotation?.anchorStart ?? 0,
        anchorEnd: annotation?.anchorEnd ?? null,
      });
      setText(result.noteText);
      setDirty(false);
      setSaved(true);
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="question-note" aria-label="Ghi chú riêng">
      <label>
        Ghi chú của riêng bạn
        <textarea
          value={text}
          rows={3}
          maxLength={4000}
          disabled={disabled || busy}
          placeholder="Điều mình muốn nhớ ở câu này…"
          onChange={(e) => {
            setText(e.target.value);
            setDirty(true);
            setSaved(false);
          }}
        />
      </label>
      <div className="note-actions">
        <button
          className="secondary"
          disabled={disabled || busy || !dirty}
          onClick={() => store(text)}
        >
          <Save size={14} />
          {busy ? "Đang lưu…" : "Lưu ghi chú"}
        </button>
        <button
          className="link-button"
          disabled={disabled || busy || (!text && !annotation?.noteText)}
          onClick={() => store("")}
        >
          Xóa nội dung ghi chú
        </button>
        {saved ? (
          <span role="status">
            <CheckCircle2 size={14} /> Đã lưu
          </span>
        ) : dirty ? (
          <small>Chưa lưu</small>
        ) : null}
      </div>
      <ErrorNotice error={error} />
      {error instanceof ApiError && error.status === 412 ? (
        <p className="notice">
          Ghi chú đã thay đổi ở nơi khác. Bản đang gõ vẫn được giữ.{" "}
          <button
            className="link-button"
            onClick={() => {
              setError(undefined);
              reload();
            }}
          >
            Tải phiên bản mới để so sánh
          </button>
        </p>
      ) : null}
      {dirty && annotation && annotation.noteText !== text ? (
        <details>
          <summary>Bản đã lưu trên server</summary>
          <p className="saved-note-text">{annotation.noteText || "(Trống)"}</p>
        </details>
      ) : null}
      <small className="muted">
        Gắn với phiên bản câu hỏi này. Xóa ghi chú không thay đổi bài làm hay
        bookmark.
      </small>
    </section>
  );
}

export default function SavedQuestionsPage() {
  const [bookmarkedOnly, setBookmarkedOnly] = useState(false);
  const [offset, setOffset] = useState(0);
  const resource = useResource<{
    items: Annotation[];
    nextOffset: number | null;
  }>(`/me/annotations?bookmarkedOnly=${bookmarkedOnly}&offset=${offset}`);
  return (
    <>
      <span className="eyebrow">KEEP WHAT HELPS YOU GROW</span>
      <h1>Câu đã lưu & ghi chú</h1>
      <p className="muted">
        Không gian riêng để quay lại những câu cần suy nghĩ thêm.
      </p>
      <label className="checkbox">
        <input
          type="checkbox"
          checked={bookmarkedOnly}
          onChange={(e) => {
            setBookmarkedOnly(e.target.checked);
            setOffset(0);
          }}
        />{" "}
        Chỉ câu đã đánh dấu
      </label>
      <ErrorNotice error={resource.error} retry={resource.reload} />
      {resource.loading ? (
        <Loading />
      ) : resource.data?.items.length ? (
        <div className="saved-questions">
          {resource.data.items.map((item) => (
            <article key={item.id}>
              <span className="eyebrow">
                {item.isBookmarked ? "ĐÃ ĐÁNH DẤU" : "GHI CHÚ RIÊNG"}
              </span>
              <h2>{item.prompt}</h2>
              <p className="muted">{item.title}</p>
              {item.noteText ? (
                <p className="saved-note-text">{item.noteText}</p>
              ) : null}
              <Link
                className="back-link"
                to={`/attempts/${item.sourceAttemptId}`}
              >
                Mở lần làm bài gốc →
              </Link>
            </article>
          ))}
        </div>
      ) : (
        <div className="empty">
          Bạn chưa lưu câu nào. Đánh dấu khi làm bài hoặc viết ghi chú ở trang
          chữa bài.
        </div>
      )}
      <div className="note-actions">
        <button
          className="secondary"
          disabled={offset === 0 || resource.loading}
          onClick={() => setOffset(Math.max(0, offset - 30))}
        >
          Trang trước
        </button>
        <button
          className="secondary"
          disabled={resource.loading || resource.data?.nextOffset == null}
          onClick={() => setOffset(resource.data!.nextOffset!)}
        >
          Trang tiếp
        </button>
      </div>
    </>
  );
}

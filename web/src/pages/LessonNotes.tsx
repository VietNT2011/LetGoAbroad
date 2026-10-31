import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Highlighter, Save, RotateCcw } from "lucide-react";
import { api, ApiError } from "../api";
import { useResource, ErrorNotice, Loading } from "../hooks";
import "./lesson-notes.css";

export type LessonNote = {
  id: string;
  contentVersionId: string;
  title: string;
  version: number;
  blockKey: "body";
  anchorStart: number;
  anchorEnd: number;
  quote: string;
  noteText: string;
  isHighlighted: boolean;
  isDeleted: boolean;
  etag: string;
};
type NoteDraft = {
  id: string;
  anchorStart: number;
  anchorEnd: number;
  quote: string;
  noteText: string;
  isHighlighted: boolean;
  expectedEtag: string | null;
};

/** VI: Hợp các vùng chồng nhau để giữ nguyên textContent/offset nguồn. EN: Merge overlapping ranges without changing source text or UTF-16 offsets. */
function highlightedBody(body: string, notes: LessonNote[]): ReactNode {
  const ranges = notes
    .filter((note) => note.isHighlighted && !note.isDeleted)
    .sort((a, b) => a.anchorStart - b.anchorStart);
  const merged: { start: number; end: number }[] = [];
  for (const note of ranges) {
    const previous = merged.at(-1);
    if (previous && note.anchorStart <= previous.end)
      previous.end = Math.max(previous.end, note.anchorEnd);
    else merged.push({ start: note.anchorStart, end: note.anchorEnd });
  }
  const fragments: ReactNode[] = [];
  let cursor = 0;
  for (const range of merged) {
    fragments.push(body.slice(cursor, range.start));
    fragments.push(
      <mark key={range.start}>{body.slice(range.start, range.end)}</mark>,
    );
    cursor = range.end;
  }
  fragments.push(body.slice(cursor));
  return fragments;
}

/** VI: Chỉ selection nằm trọn trong body; DOM Range và API cùng dùng UTF-16. EN: Capture only selections fully inside the body using the API's UTF-16 coordinate system. */
export function LessonNotes({
  versionId,
  body,
}: {
  versionId: string;
  body: string;
}) {
  const resource = useResource<LessonNote[]>(
    `/lessons/${versionId}/annotations/`,
  );
  const bodyRef = useRef<HTMLDivElement>(null);
  const editorRef = useRef<HTMLDivElement>(null);
  const [updates, setUpdates] = useState<Record<string, LessonNote>>({});
  const [draft, setDraft] = useState<NoteDraft>();
  const [selection, setSelection] = useState<{
    start: number;
    end: number;
    quote: string;
  }>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [message, setMessage] = useState("");
  const [server, setServer] = useState<LessonNote>();
  const [removed, setRemoved] = useState<LessonNote>();
  const [quoteSearch, setQuoteSearch] = useState("");
  const [occurrence, setOccurrence] = useState(0);
  const quoteMatches: number[] = [];
  if (quoteSearch.trim()) {
    let from = 0;
    while (quoteMatches.length < 50) {
      const match = body.indexOf(quoteSearch, from);
      if (match < 0) break;
      quoteMatches.push(match);
      from = match + 1;
    }
  }
  useEffect(() => {
    if (draft) editorRef.current?.scrollIntoView({ block: "center" });
  }, [draft?.id]);
  useEffect(() => {
    if (resource.data && window.location.hash.startsWith("#lesson-note-"))
      document
        .getElementById(window.location.hash.slice(1))
        ?.scrollIntoView({ block: "center" });
  }, [resource.data]);
  // VI: Retry cùng payload giữ CommandId; không đánh dấu thành công trước ACK. EN: Identical retries retain command IDs and only acknowledged writes update the view.
  const commands = useRef(new Map<string, string>());
  const notesById = new Map(resource.data?.map((note) => [note.id, note]));
  for (const note of Object.values(updates)) notesById.set(note.id, note);
  const notes = [...notesById.values()]
    .filter((note) => !note.isDeleted)
    .sort((a, b) => a.anchorStart - b.anchorStart || a.id.localeCompare(b.id));
  const command = (payload: string) => {
    if (!commands.current.has(payload))
      commands.current.set(payload, crypto.randomUUID());
    return commands.current.get(payload)!;
  };
  const accept = (note: LessonNote) =>
    setUpdates((old) => ({ ...old, [note.id]: note }));
  const captureSelection = () => {
    const root = bodyRef.current,
      selected = window.getSelection();
    if (
      !root ||
      !selected ||
      selected.rangeCount !== 1 ||
      selected.isCollapsed
    ) {
      setSelection(undefined);
      return;
    }
    const range = selected.getRangeAt(0);
    if (
      !root.contains(range.startContainer) ||
      !root.contains(range.endContainer)
    ) {
      setSelection(undefined);
      return;
    }
    const prefix = document.createRange();
    prefix.selectNodeContents(root);
    prefix.setEnd(range.startContainer, range.startOffset);
    const start = prefix.toString().length,
      end = start + range.toString().length;
    if (
      end - start > 4000 ||
      !body.slice(start, end).trim() ||
      body.slice(start, end) !== range.toString()
    ) {
      setSelection(undefined);
      return;
    }
    setSelection({ start, end, quote: body.slice(start, end) });
  };
  const startDraft = () => {
    if (!selection) return;
    setDraft({
      id: crypto.randomUUID(),
      anchorStart: selection.start,
      anchorEnd: selection.end,
      quote: selection.quote,
      noteText: "",
      isHighlighted: true,
      expectedEtag: null,
    });
    setServer(undefined);
    setError(undefined);
    setMessage("");
  };
  const store = async (input: NoteDraft) => {
    const payload = JSON.stringify(input);
    const note = await api<LessonNote>(
      `/lessons/${versionId}/annotations/${input.id}`,
      {
        method: "PUT",
        body: JSON.stringify({
          ...input,
          blockKey: "body",
          clientRequestId: command(payload),
        }),
      },
    );
    commands.current.delete(payload);
    accept(note);
    return note;
  };
  const save = async () => {
    if (!draft) return;
    setBusy(true);
    setError(undefined);
    setMessage("");
    try {
      await store(draft);
      setDraft(undefined);
      setServer(undefined);
      setMessage("Đã lưu đoạn trích và ghi chú riêng.");
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const remove = async (note: LessonNote) => {
    setBusy(true);
    setError(undefined);
    setMessage("");
    const payload = JSON.stringify({ id: note.id, remove: note.etag });
    try {
      const deleted = await api<LessonNote>(
        `/lessons/${versionId}/annotations/${note.id}/remove`,
        {
          method: "POST",
          body: JSON.stringify({
            clientRequestId: command(payload),
            expectedEtag: note.etag,
          }),
        },
      );
      commands.current.delete(payload);
      accept(deleted);
      setRemoved(deleted);
      setMessage("Đã xóa khỏi danh sách. Bạn có thể hoàn tác.");
      if (draft?.id === note.id) setDraft(undefined);
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const undo = async () => {
    if (!removed) return;
    setBusy(true);
    setError(undefined);
    try {
      await store({
        id: removed.id,
        anchorStart: removed.anchorStart,
        anchorEnd: removed.anchorEnd,
        quote: removed.quote,
        noteText: removed.noteText,
        isHighlighted: removed.isHighlighted,
        expectedEtag: removed.etag,
      });
      setRemoved(undefined);
      setMessage("Đã khôi phục ghi chú.");
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const refresh = () => {
    setUpdates({});
    resource.reload();
  };
  return (
    <section className="lesson-annotation-section">
      <p className="lesson-selection-help" id="lesson-selection-help">
        Chọn đoạn văn bằng chuột, rồi bấm “Ghi chú đoạn đã chọn”. Khi dùng bàn
        phím, mở mục chọn đoạn bằng văn bản bên dưới. Tối đa 4000 ký tự mỗi
        đoạn; chỉ bạn xem được ghi chú.
      </p>
      <div
        className="lesson-body annotated-body"
        ref={bodyRef}
        tabIndex={0}
        aria-label="Nội dung bài học có thể chọn văn bản"
        aria-describedby="lesson-selection-help"
        onMouseUp={captureSelection}
        onKeyUp={captureSelection}
        onTouchEnd={captureSelection}
      >
        {highlightedBody(body, notes)}
      </div>
      <div className="lesson-selection-tools">
        <button
          className="secondary"
          disabled={!selection || busy || resource.loading || !!resource.error}
          onClick={startDraft}
        >
          <Highlighter size={15} /> Ghi chú đoạn đã chọn
        </button>
        {selection ? (
          <small>Đã chọn {selection.end - selection.start} ký tự</small>
        ) : (
          <small>Chưa chọn đoạn văn</small>
        )}
      </div>
      <details className="lesson-keyboard-selector">
        <summary>Chọn đoạn bằng văn bản hoặc bàn phím</summary>
        <label>
          Đoạn trích cần tìm trong bài
          <textarea
            rows={2}
            maxLength={4000}
            value={quoteSearch}
            onChange={(event) => {
              setQuoteSearch(event.target.value);
              setOccurrence(0);
            }}
          />
        </label>
        {quoteSearch ? (
          <p>
            {quoteMatches.length
              ? `${quoteMatches.length === 50 ? "Ít nhất " : ""}${quoteMatches.length} vị trí khớp chính xác.`
              : "Chưa tìm thấy đoạn khớp nguyên văn. Kiểm tra dấu câu và xuống dòng."}
          </p>
        ) : null}
        {quoteMatches.length > 1 ? (
          <label>
            Vị trí đoạn trích
            <select
              value={occurrence}
              onChange={(event) => setOccurrence(Number(event.target.value))}
            >
              {quoteMatches.map((offset, index) => (
                <option key={offset} value={index}>
                  Vị trí {index + 1} · ký tự {offset + 1}
                </option>
              ))}
            </select>
          </label>
        ) : null}
        <button
          className="secondary"
          disabled={
            quoteMatches[occurrence] == null ||
            busy ||
            resource.loading ||
            !!resource.error
          }
          onClick={() => {
            const start = quoteMatches[occurrence];
            setDraft({
              id: crypto.randomUUID(),
              anchorStart: start,
              anchorEnd: start + quoteSearch.length,
              quote: quoteSearch,
              noteText: "",
              isHighlighted: true,
              expectedEtag: null,
            });
            setError(undefined);
            setServer(undefined);
            setMessage("");
          }}
        >
          Ghi chú đoạn trích này
        </button>
      </details>
      <ErrorNotice error={resource.error} retry={refresh} />
      <ErrorNotice error={error} />
      {message ? (
        <p className="notice" role="status">
          {message}
        </p>
      ) : null}
      {removed ? (
        <button className="secondary" disabled={busy} onClick={undo}>
          <RotateCcw size={15} /> Hoàn tác xóa ghi chú
        </button>
      ) : null}
      {draft ? (
        <div className="lesson-note-editor" ref={editorRef}>
          <h2>
            {draft.expectedEtag
              ? "Sửa ghi chú riêng"
              : "Ghi chú cho đoạn đã chọn"}
          </h2>
          <blockquote>{draft.quote}</blockquote>
          <label>
            Nội dung ghi chú
            <textarea
              value={draft.noteText}
              maxLength={4000}
              rows={3}
              disabled={busy}
              onChange={(e) => setDraft({ ...draft, noteText: e.target.value })}
            />
          </label>
          <label className="checkbox">
            <input
              type="checkbox"
              checked={draft.isHighlighted}
              disabled={busy}
              onChange={(e) =>
                setDraft({ ...draft, isHighlighted: e.target.checked })
              }
            />{" "}
            Tô sáng đoạn này trong bài học
          </label>
          <div className="note-actions">
            <button
              className="primary"
              disabled={
                busy || (!draft.isHighlighted && !draft.noteText.trim())
              }
              onClick={save}
            >
              <Save size={15} />
              {busy ? "Đang lưu…" : "Lưu đoạn trích & ghi chú"}
            </button>
            <button
              className="secondary"
              disabled={busy}
              onClick={() => {
                setDraft(undefined);
                setServer(undefined);
                setError(undefined);
              }}
            >
              Đóng trình soạn
            </button>
          </div>
          {error instanceof ApiError && error.status === 412 ? (
            <div className="notice">
              <p>Ghi chú đã đổi ở nơi khác. Bản đang gõ vẫn được giữ.</p>
              <button
                className="link-button"
                disabled={busy}
                onClick={async () => {
                  try {
                    const latest = await api<LessonNote>(
                      `/lessons/${versionId}/annotations/${draft.id}`,
                    );
                    setServer(latest);
                    accept(latest);
                  } catch (e) {
                    setError(e as Error);
                  }
                }}
              >
                Xem bản mới để so sánh
              </button>
            </div>
          ) : null}
          {server ? (
            <div className="lesson-note-conflict">
              <h3>Bản trên server{server.isDeleted ? " · đã xóa" : ""}</h3>
              <p>{server.noteText || "(Không có nội dung ghi chú)"}</p>
              <button
                className="secondary"
                onClick={() => {
                  setDraft({ ...draft, expectedEtag: server.etag });
                  setError(undefined);
                  setServer(undefined);
                }}
              >
                Dùng phiên bản mới để lưu bản đang gõ
                {server.isDeleted ? " và khôi phục" : ""}
              </button>
            </div>
          ) : null}
        </div>
      ) : null}
      <div className="section-heading">
        <h2>Ghi chú bài học · {notes.length}</h2>
        <button className="link-button" disabled={busy} onClick={refresh}>
          Tải ghi chú mới nhất
        </button>
      </div>
      {resource.loading ? (
        <Loading />
      ) : (
        <div className="lesson-note-list">
          {notes.map((note, index) => (
            <article key={note.id} id={`lesson-note-${note.id}`}>
              <small>
                Đoạn {index + 1}
                {note.isHighlighted ? " · Đã tô sáng" : " · Chỉ ghi chú"}
              </small>
              <blockquote>{note.quote}</blockquote>
              {note.noteText ? <p>{note.noteText}</p> : null}
              <div className="note-actions">
                <button
                  className="secondary"
                  disabled={busy}
                  onClick={() => {
                    setDraft({
                      id: note.id,
                      anchorStart: note.anchorStart,
                      anchorEnd: note.anchorEnd,
                      quote: note.quote,
                      noteText: note.noteText,
                      isHighlighted: note.isHighlighted,
                      expectedEtag: note.etag,
                    });
                    setError(undefined);
                    setServer(undefined);
                    setMessage("");
                    editorRef.current?.scrollIntoView({ block: "center" });
                  }}
                >
                  Sửa ghi chú đoạn {index + 1}
                </button>
                <button
                  className="link-button"
                  disabled={busy}
                  onClick={() => remove(note)}
                >
                  Xóa ghi chú đoạn {index + 1}
                </button>
              </div>
            </article>
          ))}
          {!notes.length ? (
            <p className="muted">
              Chưa có ghi chú cho phiên bản này. Chọn một đoạn để bắt đầu.
            </p>
          ) : null}
        </div>
      )}
    </section>
  );
}

export function SavedLessonNotes() {
  const [offset, setOffset] = useState(0);
  const resource = useResource<{
    items: LessonNote[];
    nextOffset: number | null;
  }>(`/me/lesson-annotations?offset=${offset}`);
  return (
    <section className="saved-lesson-notes">
      <h2>Đoạn trích trong bài học</h2>
      <ErrorNotice error={resource.error} retry={resource.reload} />
      {resource.loading ? (
        <Loading />
      ) : resource.data?.items.length ? (
        <div className="saved-questions">
          {resource.data.items.map((note) => (
            <article key={note.id}>
              <span className="eyebrow">
                {note.title} · VERSION {note.version}
              </span>
              <blockquote>{note.quote}</blockquote>
              {note.noteText ? (
                <p className="saved-note-text">{note.noteText}</p>
              ) : null}
              <Link
                className="back-link"
                to={`/learn/${note.contentVersionId}#lesson-note-${note.id}`}
              >
                Mở đúng phiên bản bài học →
              </Link>
            </article>
          ))}
        </div>
      ) : (
        <p className="muted">Chưa có đoạn trích được lưu từ bài học.</p>
      )}
      <div className="note-actions">
        <button
          className="secondary"
          disabled={!offset || resource.loading}
          onClick={() => setOffset(Math.max(0, offset - 30))}
        >
          Trang ghi chú trước
        </button>
        <button
          className="secondary"
          disabled={resource.loading || resource.data?.nextOffset == null}
          onClick={() => setOffset(resource.data!.nextOffset!)}
        >
          Trang ghi chú tiếp
        </button>
      </div>
    </section>
  );
}

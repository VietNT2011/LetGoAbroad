import { useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { ErrorNotice } from "../hooks";

export type EditableCard = {
  id: string;
  front: string;
  back: string;
  etag: string;
  contentVersion: number;
};
type Command = {
  clientRequestId: string;
  expectedEtag: string;
  front: string;
  back: string;
};

/** VI: Giữ draft khi xung đột; retry mạng dùng đúng payload/event cũ. EN: Preserve conflicting drafts and retry unknown outcomes with the identical command. */
export function CardEditor({
  card,
  saved,
  cancel,
}: {
  card: EditableCard;
  saved: () => void;
  cancel: () => void;
}) {
  const [front, setFront] = useState(card.front);
  const [back, setBack] = useState(card.back);
  const [etag, setEtag] = useState(card.etag);
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [server, setServer] = useState<EditableCard>();
  const [error, setError] = useState<Error>();
  const pending = useRef<Command | undefined>(undefined);
  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (busy || conflict) return;
    pending.current ??= {
      clientRequestId: crypto.randomUUID(),
      expectedEtag: etag,
      front,
      back,
    };
    setBusy(true);
    setError(undefined);
    try {
      await api(`/cards/${card.id}`, {
        method: "PUT",
        body: JSON.stringify(pending.current),
      });
      pending.current = undefined;
      setUncertain(false);
      saved();
    } catch (failure) {
      setError(failure as Error);
      if (
        failure instanceof ApiError &&
        failure.status >= 400 &&
        failure.status < 500
      ) {
        pending.current = undefined;
        setUncertain(false);
        setConflict(failure.status === 412);
      } else setUncertain(true);
    } finally {
      setBusy(false);
    }
  };
  const compare = async () => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      setServer(await api<EditableCard>(`/cards/${card.id}`));
    } catch (failure) {
      setError(failure as Error);
    } finally {
      setBusy(false);
    }
  };
  const reconcile = (useServer: boolean) => {
    if (!server) return;
    if (useServer) {
      setFront(server.front);
      setBack(server.back);
    }
    setEtag(server.etag);
    setServer(undefined);
    setConflict(false);
    setError(undefined);
  };
  return (
    <section className="card-create" aria-label="Sửa nội dung thẻ">
      <h3>Sửa hai mặt thẻ</h3>
      <p>
        Đổi nội dung sẽ tạo phiên bản mới và đưa lịch ôn về bước 0, đến hạn
        ngay. Ngữ cảnh và lịch sử ôn cũ được giữ; thẻ đã cất vẫn được cất. Lưu
        nội dung không đổi giữ nguyên lịch.
      </p>
      <form onSubmit={save}>
        <label htmlFor="edit-card-front">
          Mặt trước
          <textarea
            id="edit-card-front"
            required
            maxLength={500}
            value={front}
            disabled={busy || uncertain}
            onChange={(e) => setFront(e.target.value)}
          />
        </label>
        <label htmlFor="edit-card-back">
          Mặt sau
          <textarea
            id="edit-card-back"
            required
            maxLength={2000}
            value={back}
            disabled={busy || uncertain}
            onChange={(e) => setBack(e.target.value)}
          />
        </label>
        <ErrorNotice error={error} />
        {uncertain ? (
          <p role="status">
            Chưa nhận được xác nhận. Gửi lại cùng nội dung để không tạo thêm
            phiên bản.
          </p>
        ) : null}
        {conflict ? (
          <div className="notice">
            <p>
              Thẻ trên máy chủ đã đổi. Bản nháp của bạn vẫn ở trên; tải bản hiện
              tại để so sánh trước khi chọn cách lưu.
            </p>
            <button type="button" disabled={busy} onClick={compare}>
              Tải bản để so sánh
            </button>
            {server ? (
              <div aria-label="Nội dung hiện tại trên máy chủ">
                <h4>Máy chủ · phiên bản {server.contentVersion}</h4>
                <p className="card-version-text">{server.front}</p>
                <p className="card-version-text">{server.back}</p>
                <button type="button" onClick={() => reconcile(true)}>
                  Dùng nội dung máy chủ
                </button>
                <button type="button" onClick={() => reconcile(false)}>
                  Giữ bản nháp để lưu lại
                </button>
              </div>
            ) : null}
          </div>
        ) : null}
        <div className="card-filters">
          <button className="button primary" disabled={busy || conflict}>
            {busy
              ? "Đang lưu…"
              : uncertain
                ? "Gửi lại bản sửa"
                : "Lưu nội dung thẻ"}
          </button>
          <button type="button" disabled={busy || uncertain} onClick={cancel}>
            Hủy sửa
          </button>
        </div>
      </form>
    </section>
  );
}

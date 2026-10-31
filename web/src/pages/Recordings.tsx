import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { ErrorNotice, Loading, useResource } from "../hooks";
import { useAudioRecorder } from "../useAudioRecorder";
import { audioUpload } from "../audioUpload";
import { AudioPlayer } from "./Media";
import {
  RecordingJournal,
  RecordingReflectionEditor,
} from "./RecordingReflection";
import {
  RecordingReference,
  pauseOtherRecordingAudio,
} from "./RecordingReference";
import "./cards.css";
import "./media.css";

type Recording = {
  id: string;
  fileName: string;
  status: string;
  failureCode: string | null;
  durationMs: number | null;
  attribution: string;
  createdAt: string;
};
type RecordingPage = { items: Recording[]; nextOffset: number | null };

/** VI: Bản thu ở máy cho đến khi bấm Lưu; thư viện dùng ACL owner kể cả Admin. EN: Draft capture stays local until explicit Save; library access remains owner-only, including Admin. */
export default function RecordingsPage() {
  const page = useRef<HTMLElement>(null);
  const recorder = useAudioRecorder();
  const [manual, setManual] = useState<File>();
  const selected = manual ?? recorder.file;
  const [preview, setPreview] = useState<string>();
  const [source, setSource] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState("");
  const [offset, setOffset] = useState(0);
  const [deleteId, setDeleteId] = useState<string>();
  const task = useRef<ReturnType<typeof audioUpload> | undefined>(undefined);
  const controller = useRef<AbortController | undefined>(undefined);
  const library = useResource<RecordingPage>(`/me/recordings?offset=${offset}`);
  const [visibleLibrary, setVisibleLibrary] = useState<RecordingPage>();
  // VI: Poll trạng thái xử lý không được tháo editor/nháp hay reset audio của hàng Ready.
  // EN: Processing polls must not unmount ready-row editors/drafts or reset their audio.
  useEffect(() => {
    if (library.data) setVisibleLibrary(library.data);
  }, [library.data]);
  const capturing = ["requesting", "recording", "stopping"].includes(
    recorder.phase,
  );
  useEffect(() => {
    if (!selected) {
      setPreview(undefined);
      return;
    }
    const url = URL.createObjectURL(selected);
    setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [selected]);
  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => {
    if (
      !library.data?.items.some((x) =>
        ["uploaded", "processing", "delete_pending"].includes(x.status),
      )
    )
      return;
    const timer = setTimeout(library.reload, 2500);
    return () => clearTimeout(timer);
  }, [library.data, library.reload]);
  const save = async () => {
    if (!selected || busy || capturing) return;
    setBusy(true);
    setError(undefined);
    try {
      task.current ??= audioUpload(
        selected,
        "recording",
        source,
        (bytes, total) =>
          setNotice(
            `Đã gửi ${Math.floor((bytes * 100) / total)}%. Bản thu vẫn cần được xác minh.`,
          ),
      );
      controller.current = new AbortController();
      const id = await task.current.send(controller.current.signal);
      task.current = undefined;
      setNotice(`Đã nhận bản thu. Kho lưu trữ đang xử lý; mã ${id}.`);
      setManual(undefined);
      recorder.discard();
      library.reload();
    } catch (failure) {
      setError(failure as Error);
    } finally {
      setBusy(false);
    }
  };
  const remove = async (id: string) => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    controller.current = new AbortController();
    try {
      await api<void>(`/media/${id}`, {
        method: "DELETE",
        signal: controller.current.signal,
      });
      setDeleteId(undefined);
      setNotice(
        "Đã nhận yêu cầu xóa. Quyền phát mới bị chặn ngay; kho đang dọn file.",
      );
      library.reload();
    } catch (failure) {
      setError(failure as Error);
    } finally {
      setBusy(false);
    }
  };
  return (
    <section
      className="cards-page"
      ref={page}
      onPlayCapture={(event) => {
        if (page.current) pauseOtherRecordingAudio(page.current, event.target);
      }}
    >
      <header className="cards-heading">
        <span className="eyebrow">HEAR YOUR OWN PROGRESS</span>
        <h1>Ghi âm của tôi</h1>
        <p>
          Đọc một đoạn, nghe lại và tự điều chỉnh. Không chấm phát âm tự động
          hoặc gửi bản thu cho người khác.
        </p>
      </header>
      <RecordingReference />
      <section className="card-create" aria-label="Tạo bản ghi âm">
        <h2>Thử nói thành tiếng</h2>
        <p>
          Ghi trực tiếp tối đa 2 phút 50 giây; chọn file tối đa20 MiB, thời
          lượng tối đa3 phút. Đổi tab sẽ dừng mic. Nghe lại bản nháp rồi bấm Lưu
          riêng để gửi lên kho của bạn.
        </p>
        <div className="card-filters">
          <button
            type="button"
            className="button primary"
            disabled={busy || capturing || !!task.current}
            onClick={() => {
              setManual(undefined);
              void recorder.start();
            }}
          >
            Bắt đầu ghi âm
          </button>
          {recorder.phase === "recording" ? (
            <button type="button" className="secondary" onClick={recorder.stop}>
              Dừng ghi âm
            </button>
          ) : null}
          {recorder.phase === "requesting" ? (
            <button type="button" onClick={recorder.discard}>
              Hủy chờ mic
            </button>
          ) : null}
        </div>
        <p role="status">
          {recorder.phase === "requesting"
            ? "Đang chờ quyền microphone…"
            : recorder.phase === "recording"
              ? "Đang ghi âm · microphone đang bật"
              : recorder.phase === "stopping"
                ? "Đang kết thúc bản thu…"
                : recorder.file
                  ? "Bản nháp đã ở trên thiết bị; chưa gửi lên kho."
                  : "Microphone chưa bật."}
        </p>
        <ErrorNotice error={recorder.error} />
        <label>
          Hoặc chọn file audio
          <input
            type="file"
            accept="audio/mpeg,audio/wav,audio/ogg,audio/mp4,audio/webm"
            disabled={busy || capturing || !!task.current}
            onChange={(e) => {
              recorder.discard();
              setManual(e.target.files?.[0]);
            }}
          />
        </label>
        {preview ? (
          <div className="lesson-audio">
            <p>{selected?.name} · bản nháp chưa lưu</p>
            <audio
              aria-label="Nghe bản nháp"
              controls
              preload="metadata"
              src={preview}
            />
          </div>
        ) : null}
        <label>
          Ghi chú nguồn bản thu
          <input
            maxLength={2000}
            value={source}
            disabled={busy || !!task.current}
            onChange={(e) => setSource(e.target.value)}
            placeholder="Ví dụ: Tôi tự đọc bài luyện hôm nay"
          />
        </label>
        <div className="card-filters">
          <button
            type="button"
            className="button primary"
            disabled={!selected || !source.trim() || busy || capturing}
            onClick={save}
          >
            {busy
              ? "Đang gửi…"
              : task.current
                ? "Gửi lại phần chưa xác nhận"
                : "Lưu riêng bản thu"}
          </button>
          {busy && task.current ? (
            <button type="button" onClick={() => controller.current?.abort()}>
              Tạm dừng gửi
            </button>
          ) : null}
          <button
            type="button"
            disabled={busy || capturing || !!task.current}
            onClick={() => {
              setManual(undefined);
              recorder.discard();
            }}
          >
            Bỏ bản nháp
          </button>
          {task.current && !busy ? (
            <button
              type="button"
              onClick={() => {
                task.current = undefined;
                setError(undefined);
                library.reload();
                setNotice(
                  "Đã bỏ phiên gửi trong tab. Kiểm tra thư viện trước khi gửi lại vì kho có thể đã nhận bản thu.",
                );
              }}
            >
              Bỏ phiên gửi
            </button>
          ) : null}
        </div>
        <ErrorNotice error={error} />
        <p role="status">{notice}</p>
        <p>
          Bản nháp và trạng thái tiếp tục gửi chỉ nằm trong tab này. Tải lại
          hoặc đóng tab sẽ mất bản chưa lưu.
        </p>
      </section>
      <section aria-label="Thư viện ghi âm riêng">
        <h2>Thư viện riêng của bạn</h2>
        <button
          type="button"
          className="link-button"
          disabled={busy}
          onClick={library.reload}
        >
          Cập nhật thư viện
        </button>
        <ErrorNotice error={library.error} retry={library.reload} />
        {library.loading && !visibleLibrary ? (
          <Loading />
        ) : visibleLibrary ? (
          <>
            {visibleLibrary.items.length ? (
              <div className="saved-questions">
                {visibleLibrary.items.map((item) => (
                  <article key={item.id}>
                    <h3>{item.fileName}</h3>
                    <p>{item.attribution}</p>
                    <p>
                      Trạng thái: {item.status}
                      {item.failureCode ? ` · ${item.failureCode}` : ""}
                      {item.durationMs != null
                        ? ` · ${(item.durationMs / 1000).toFixed(1)} giây`
                        : ""}
                    </p>
                    {item.status === "ready" ? (
                      <>
                        <AudioPlayer assetId={item.id} />
                        <RecordingReflectionEditor assetId={item.id} />
                      </>
                    ) : null}
                    {!["delete_pending", "expired"].includes(item.status) ? (
                      deleteId === item.id ? (
                        <div className="card-filters">
                          <p>
                            Xóa file audio khỏi kho riêng? Nhận xét đã lưu vẫn
                            được giữ. Hãy lưu nháp ghi chú trước khi xóa.
                          </p>
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => remove(item.id)}
                          >
                            Xác nhận xóa bản thu
                          </button>
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => setDeleteId(undefined)}
                          >
                            Giữ bản thu
                          </button>
                        </div>
                      ) : (
                        <button
                          type="button"
                          className="link-button"
                          disabled={busy}
                          onClick={() => setDeleteId(item.id)}
                        >
                          Xóa bản thu
                        </button>
                      )
                    ) : null}
                  </article>
                ))}
              </div>
            ) : (
              <p className="card-empty">
                Bạn chưa có bản ghi âm đã lưu. Bản nháp ở trên chỉ xuất hiện
                trong thư viện sau khi gửi.
              </p>
            )}
            <div className="card-pagination">
              <button
                type="button"
                disabled={!offset || busy}
                onClick={() => {
                  setVisibleLibrary(undefined);
                  setOffset(Math.max(0, offset - 30));
                }}
              >
                Trang trước
              </button>
              <button
                type="button"
                disabled={visibleLibrary.nextOffset === null || busy}
                onClick={() => {
                  const next = visibleLibrary.nextOffset!;
                  setVisibleLibrary(undefined);
                  setOffset(next);
                }}
              >
                Trang sau
              </button>
            </div>
          </>
        ) : null}
      </section>
      <RecordingJournal />
    </section>
  );
}

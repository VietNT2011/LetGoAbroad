import { useEffect, useRef, useState } from "react";
import { api, ApiError } from "../api";
import { ErrorNotice, Loading, useResource } from "../hooks";
import { type RecordingSourceSuggestion } from "../recordingSource";
import {
  RecordingHistory,
  RecordingSourceSummary,
  type RecordingSourceView,
} from "./RecordingHistory";
import "./recording-reflection.css";

export type ReflectionView = {
  assetId: string;
  fileName: string;
  status: string;
  etag: string | null;
  notes: string;
  clarity: number | null;
  rhythm: number | null;
  updatedAt: string | null;
  source?: RecordingSourceView | null;
  revision?: number;
};
type Draft = Pick<ReflectionView, "notes" | "clarity" | "rhythm">;
type Command = Draft & { clientRequestId: string; expectedEtag: string | null };
type SourceCommand = {
  clientRequestId: string;
  expectedEtag: string | null;
  sourceContentVersionId: string | null;
  sourceSegmentId: string | null;
};
type Pending =
  | { kind: "notes"; body: Command }
  | { kind: "source"; body: SourceCommand };

/** VI: Mở ghi chú chỉ đọc; mỗi bản thu có nháp riêng và save rõ ràng. EN: Opening a reflection is read-only; each recording has its own draft and explicit save. */
export function RecordingReflectionEditor({
  assetId,
  onSaved,
  suggestedSource,
}: {
  assetId: string;
  onSaved?: (value: ReflectionView) => void;
  suggestedSource?: RecordingSourceSuggestion;
}) {
  const [mounted, setMounted] = useState(false);
  return (
    <details
      className="reflection-editor"
      onToggle={(event) => {
        if (event.currentTarget.open) setMounted(true);
      }}
    >
      <summary>Ghi chú & tự đánh giá</summary>
      {mounted ? (
        <ReflectionForm
          key={assetId}
          assetId={assetId}
          onSaved={onSaved}
          suggestedSource={suggestedSource}
        />
      ) : null}
    </details>
  );
}

export function ReflectionForm({
  assetId,
  onSaved,
  suggestedSource,
}: {
  assetId: string;
  onSaved?: (value: ReflectionView) => void;
  suggestedSource?: RecordingSourceSuggestion;
}) {
  const resource = useResource<ReflectionView>(`/media/${assetId}/reflection`);
  const [baseline, setBaseline] = useState<ReflectionView>();
  const [draft, setDraft] = useState<Draft>({
    notes: "",
    clarity: null,
    rhythm: null,
  });
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const [unknown, setUnknown] = useState(false);
  const [stale, setStale] = useState(false);
  const [comparison, setComparison] = useState<ReflectionView>();
  const [notice, setNotice] = useState("");
  const pending = useRef<Pending | undefined>(undefined);
  const operation = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => operation.current?.abort(), []);
  useEffect(() => {
    if (!baseline && resource.data) {
      setBaseline(resource.data);
      setDraft({
        notes: resource.data.notes,
        clarity: resource.data.clarity,
        rhythm: resource.data.rhythm,
      });
    }
  }, [baseline, resource.data]);
  const save = async (proposal?: Pending) => {
    if (!baseline || busy || stale) return;
    pending.current ??= proposal ?? {
      kind: "notes",
      body: {
        ...draft,
        clientRequestId: crypto.randomUUID(),
        expectedEtag: baseline.etag,
      },
    };
    const command = pending.current;
    setBusy(true);
    setError(undefined);
    setNotice("");
    operation.current = new AbortController();
    try {
      // VI: Khi chưa rõ kết quả, gửi lại đúng event/body; không đổi nháp phía sau lệnh cũ.
      // EN: Unknown outcomes retain the exact event/body, preventing an old command from silently changing meaning.
      const result = await api<ReflectionView>(
        `/media/${assetId}/reflection${command.kind === "source" ? "/source" : ""}`,
        {
          method: "PUT",
          body: JSON.stringify(command.body),
          signal: operation.current.signal,
        },
      );
      pending.current = undefined;
      setUnknown(false);
      setBaseline(result);
      // VI: Đổi bài mẫu không được ghi đè nháp nhận xét chưa lưu. EN: A source acknowledgment must not replace an unsaved note draft.
      if (command.kind === "notes")
        setDraft({
          notes: result.notes,
          clarity: result.clarity,
          rhythm: result.rhythm,
        });
      setNotice(
        command.kind === "source"
          ? "Đã lưu liên kết bài mẫu. Nháp nhận xét của bạn vẫn ở nguyên."
          : "Đã lưu ghi chú riêng. Tự đánh giá không cộng điểm thi hoặc hoàn thành bài học.",
      );
      onSaved?.(result);
    } catch (failure) {
      setError(failure as Error);
      if (failure instanceof ApiError && failure.status < 500) {
        pending.current = undefined;
        setUnknown(false);
        setStale(failure.status === 412);
      } else setUnknown(true);
    } finally {
      setBusy(false);
    }
  };
  const compare = async () => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    operation.current = new AbortController();
    try {
      setComparison(
        await api<ReflectionView>(`/media/${assetId}/reflection`, {
          signal: operation.current.signal,
        }),
      );
    } catch (failure) {
      setError(failure as Error);
    } finally {
      setBusy(false);
    }
  };
  if (!baseline)
    return (
      <>
        <ErrorNotice error={resource.error} retry={resource.reload} />
        {resource.loading || resource.data ? <Loading /> : null}
      </>
    );
  if (!baseline.etag && baseline.status !== "ready")
    return (
      <p>
        {["pending_upload", "uploaded", "processing"].includes(baseline.status)
          ? "Chờ audio Ready rồi nghe lại trước khi tạo tự đánh giá."
          : "Bản thu này không còn khả dụng để tạo tự đánh giá mới."}
      </p>
    );
  return (
    <div className="reflection-form">
      <p>
        Đây là nhận xét của chính bạn, không phải điểm phát âm tự động. Có thể
        bỏ trống các mức tự đánh giá.
      </p>
      {baseline.status === "deleted" ? (
        <p>
          File audio đã xóa. Ghi chú riêng vẫn được giữ và có thể chỉnh sửa.
        </p>
      ) : null}
      <ErrorNotice error={error} />
      <RecordingSourceSummary source={baseline.source} />
      <div className="button-row">
        {suggestedSource ? (
          <button
            type="button"
            disabled={busy || unknown || stale}
            onClick={() =>
              save({
                kind: "source",
                body: {
                  clientRequestId: crypto.randomUUID(),
                  expectedEtag: baseline.etag,
                  sourceContentVersionId: suggestedSource.versionId,
                  sourceSegmentId: suggestedSource.segmentId,
                },
              })
            }
          >
            Liên kết bài mẫu đang mở
          </button>
        ) : null}
        {baseline.source ? (
          <button
            type="button"
            disabled={busy || unknown || stale}
            onClick={() =>
              save({
                kind: "source",
                body: {
                  clientRequestId: crypto.randomUUID(),
                  expectedEtag: baseline.etag,
                  sourceContentVersionId: null,
                  sourceSegmentId: null,
                },
              })
            }
          >
            Bỏ liên kết mẫu hiện tại
          </button>
        ) : null}
      </div>
      {unknown ? (
        <p role="status">
          Chưa xác nhận lệnh lưu. Nháp đang giữ nguyên để gửi lại đúng lệnh.
        </p>
      ) : null}
      <fieldset disabled={busy || unknown}>
        <legend>{baseline.fileName}</legend>
        <label>
          Ghi chú của bạn
          <textarea
            maxLength={2000}
            rows={4}
            value={draft.notes}
            onChange={(event) =>
              setDraft((value) => ({ ...value, notes: event.target.value }))
            }
          />
        </label>
        <div className="reflection-scores">
          {(["clarity", "rhythm"] as const).map((key) => (
            <label key={key}>
              {key === "clarity"
                ? "Độ rõ giọng — tự nhận xét"
                : "Nhịp đọc — tự nhận xét"}
              <select
                value={draft[key] ?? ""}
                onChange={(event) =>
                  setDraft((value) => ({
                    ...value,
                    [key]:
                      event.target.value === ""
                        ? null
                        : Number(event.target.value),
                  }))
                }
              >
                <option value="">Chưa tự đánh giá</option>
                {[1, 2, 3, 4, 5].map((value) => (
                  <option key={value} value={value}>
                    {value} / 5
                  </option>
                ))}
              </select>
            </label>
          ))}
        </div>
      </fieldset>
      {stale ? (
        <div className="reflection-conflict">
          <p>Nháp của bạn vẫn ở trên. Bản lưu đã thay đổi ở nơi khác.</p>
          <button type="button" disabled={busy} onClick={compare}>
            Đọc bản mới để so sánh
          </button>
          {comparison ? (
            <>
              <h4>Bản đang lưu trên máy chủ</h4>
              <p className="reflection-text">
                {comparison.notes || "Chưa có ghi chú."}
              </p>
              <p>
                Rõ giọng: {comparison.clarity ?? "chưa chọn"} · Nhịp đọc:{" "}
                {comparison.rhythm ?? "chưa chọn"}
              </p>
              <RecordingSourceSummary source={comparison.source} />
              <button
                type="button"
                disabled={busy}
                onClick={() => {
                  setBaseline(comparison);
                  setComparison(undefined);
                  setStale(false);
                  setError(undefined);
                }}
              >
                Giữ nháp của tôi và dùng phiên bản mới để lưu
              </button>
            </>
          ) : null}
        </div>
      ) : null}
      <button
        type="button"
        className="secondary"
        disabled={busy || stale}
        onClick={() => save()}
      >
        {busy
          ? "Đang lưu…"
          : unknown
            ? "Gửi lại lệnh chưa xác nhận"
            : "Lưu tự đánh giá riêng"}
      </button>
      {notice ? <p role="status">{notice}</p> : null}
      <RecordingHistory assetId={assetId} revision={baseline.revision ?? 0} />
    </div>
  );
}

export function RecordingJournal({
  suggestedSource,
}: { suggestedSource?: RecordingSourceSuggestion } = {}) {
  const [mounted, setMounted] = useState(false);
  return (
    <details
      className="reflection-editor"
      onToggle={(event) => {
        if (event.currentTarget.open) setMounted(true);
      }}
    >
      <summary>Nhật ký tự đánh giá đã lưu</summary>
      {mounted ? <JournalRows suggestedSource={suggestedSource} /> : null}
    </details>
  );
}

function JournalRows({
  suggestedSource,
}: {
  suggestedSource?: RecordingSourceSuggestion;
}) {
  const [offset, setOffset] = useState(0);
  const [visible, setVisible] = useState<{
    items: ReflectionView[];
    nextOffset: number | null;
  }>();
  const journal = useResource<{
    items: ReflectionView[];
    nextOffset: number | null;
  }>(`/me/recording-journal?offset=${offset}`);
  // VI: Giữ cây editor khi đọc lại danh sách để không mất nháp đang sửa ở một hàng khác.
  // EN: Keep editor trees mounted during refresh, preserving drafts in other rows.
  useEffect(() => {
    if (journal.data) setVisible(journal.data);
  }, [journal.data]);
  return (
    <div className="reflection-journal">
      <p>
        Chỉ ghi chú của bạn. Xóa file audio không làm mất các nhận xét đã lưu.
      </p>
      <button type="button" onClick={journal.reload}>
        Cập nhật nhật ký
      </button>
      <ErrorNotice error={journal.error} retry={journal.reload} />
      {journal.loading && !visible ? (
        <Loading />
      ) : visible ? (
        <>
          {visible.items.length ? (
            visible.items.map((item) => (
              <article key={item.assetId}>
                <h3>{item.fileName}</h3>
                {item.updatedAt ? (
                  <p className="muted">
                    Tự đánh giá gần nhất:{" "}
                    <time dateTime={item.updatedAt}>
                      {new Date(item.updatedAt).toLocaleString("vi-VN")}
                    </time>
                  </p>
                ) : null}
                <p className="reflection-text">
                  {item.notes || "Chưa có ghi chú."}
                </p>
                <p>
                  Rõ giọng: {item.clarity ?? "chưa chọn"} · Nhịp đọc:{" "}
                  {item.rhythm ?? "chưa chọn"}
                  {item.status === "deleted" ? " · Audio đã xóa" : ""}
                </p>
                <RecordingSourceSummary source={item.source} />
                <RecordingReflectionEditor
                  assetId={item.assetId}
                  suggestedSource={suggestedSource}
                  onSaved={(value) =>
                    setVisible((current) =>
                      current
                        ? {
                            ...current,
                            items: current.items.map((row) =>
                              row.assetId === value.assetId
                                ? {
                                    ...value,
                                    status: row.status,
                                    fileName: row.fileName,
                                  }
                                : row,
                            ),
                          }
                        : current,
                    )
                  }
                />
              </article>
            ))
          ) : (
            <p>Bạn chưa lưu tự đánh giá nào.</p>
          )}
          <div className="card-pagination">
            <button
              type="button"
              disabled={!offset}
              onClick={() => {
                setVisible(undefined);
                setOffset(Math.max(0, offset - 30));
              }}
            >
              Trang nhật ký trước
            </button>
            <button
              type="button"
              disabled={
                visible.nextOffset == null || visible.nextOffset > 100000
              }
              onClick={() => {
                const next = visible.nextOffset!;
                setVisible(undefined);
                setOffset(next);
              }}
            >
              Trang nhật ký sau
            </button>
          </div>
        </>
      ) : null}
    </div>
  );
}

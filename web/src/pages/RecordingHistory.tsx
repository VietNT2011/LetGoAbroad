import { useState } from "react";
import { Link } from "react-router-dom";
import { ErrorNotice, Loading, useResource } from "../hooks";

export type RecordingSourceView = {
  versionId: string;
  title: string;
  version: number;
  mediaAssetId: string | null;
  audioAvailable: boolean;
  archived: boolean;
  segmentId: string | null;
  segmentOrder: number | null;
  startMs: number | null;
  endMs: number | null;
};
type Revision = {
  number: number;
  kind: string;
  notes: string;
  clarity: number | null;
  rhythm: number | null;
  updatedAt: string;
  capturedAt: string;
  source: RecordingSourceView | null;
};

export function RecordingSourceSummary({
  source,
}: {
  source?: RecordingSourceView | null;
}) {
  if (!source) return null;
  const href = `/recordings?lesson=${source.versionId}${source.segmentId ? `&segment=${source.segmentId}` : ""}`;
  return (
    <div className="recording-source-summary">
      <p>
        <b>Bài mẫu:</b> {source.title} · v{source.version}
        {source.archived ? " · đã lưu trữ" : ""}
      </p>
      {source.segmentOrder != null ? (
        <p>
          Đoạn {source.segmentOrder + 1} · {(source.startMs ?? 0) / 1000}–
          {(source.endMs ?? 0) / 1000} giây
        </p>
      ) : null}
      <Link to={`/learn/${source.versionId}`}>Xem đúng phiên bản bài mẫu</Link>
      {" · "}
      {source.audioAvailable ? (
        <Link to={href}>Mở mẫu để nghe</Link>
      ) : (
        <span>Audio mẫu hiện không còn khả dụng.</span>
      )}
    </div>
  );
}

/** VI: Lịch sử chỉ đọc và tải khi mở; không dựng các player/đáp án transcript trong danh sách. EN: Load immutable history only on opening, without player fan-out or transcript-answer disclosure. */
export function RecordingHistory({
  assetId,
  revision,
}: {
  assetId: string;
  revision: number;
}) {
  const [mounted, setMounted] = useState(false);
  return (
    <details
      className="reflection-history"
      onToggle={(event) => {
        if (event.currentTarget.open) setMounted(true);
      }}
    >
      <summary>Lịch sử nhận xét & bài mẫu</summary>
      {mounted ? (
        <HistoryRows key={`${assetId}:${revision}`} assetId={assetId} />
      ) : null}
    </details>
  );
}
function HistoryRows({ assetId }: { assetId: string }) {
  const [before, setBefore] = useState<number | null>(null);
  const history = useResource<{ items: Revision[]; nextBefore: number | null }>(
    `/media/${assetId}/reflection/history${before == null ? "" : `?before=${before}`}`,
  );
  return (
    <div>
      <ErrorNotice error={history.error} retry={history.reload} />
      {history.loading ? (
        <Loading />
      ) : history.data ? (
        <>
          {history.data.items.length ? (
            history.data.items.map((item) => (
              <article key={item.number}>
                <h4>
                  {item.kind === "legacy_snapshot"
                    ? "Nhận xét có sẵn trước khi theo dõi lịch sử"
                    : `Lần lưu ${item.number}${item.kind === "source" ? " · đổi bài mẫu" : ""}`}
                </h4>
                <p className="muted">
                  Nhận xét lúc{" "}
                  <time dateTime={item.updatedAt}>
                    {new Date(item.updatedAt).toLocaleString("vi-VN")}
                  </time>
                </p>
                {item.kind === "legacy_snapshot" ? (
                  <p className="muted">
                    Bản chụp được ghi lại lúc{" "}
                    {new Date(item.capturedAt).toLocaleString("vi-VN")}; không
                    phải một lần sửa được ghi nhận trước đó.
                  </p>
                ) : null}
                <p className="reflection-text">
                  {item.notes || "Chưa có ghi chú."}
                </p>
                <p>
                  Rõ giọng: {item.clarity ?? "chưa chọn"} · Nhịp đọc:{" "}
                  {item.rhythm ?? "chưa chọn"}
                </p>
                <RecordingSourceSummary source={item.source} />
              </article>
            ))
          ) : (
            <p>Chưa có lịch sử theo dõi. Nhận xét hiện tại vẫn được giữ.</p>
          )}
          <div className="card-pagination">
            <button
              type="button"
              disabled={before == null}
              onClick={() => setBefore(null)}
            >
              Về lần lưu mới nhất
            </button>
            <button
              type="button"
              disabled={history.data.nextBefore == null}
              onClick={() => setBefore(history.data!.nextBefore)}
            >
              Xem các bản cũ hơn
            </button>
          </div>
        </>
      ) : null}
    </div>
  );
}

import { useEffect, useRef, useState } from "react";
import { Upload, RefreshCw, Music2, CheckCircle2 } from "lucide-react";
import { api, post } from "../api";
import { ErrorNotice, useResource } from "../hooks";
import "./media.css";
import { audioUpload } from "../audioUpload";
type Asset = {
  id: string;
  fileName: string;
  status: string;
  failureCode: string | null;
  durationMs: number | null;
  attribution: string;
};
type Job = {
  id: string;
  resourceId: string;
  kind: string;
  state: string;
  attempts: number;
  lastErrorCode: string | null;
};
/** VI: Upload SAS không được ghi vào localStorage/log. EN: Keep scoped upload URLs in memory and never log or persist access grants. */
export function MediaAdmin({ admin }: { admin: boolean }) {
  const assets = useResource<Asset[]>("/admin/media");
  const jobs = useResource<Job[]>(admin ? "/admin/jobs" : null);
  const [file, setFile] = useState<File>();
  const [attribution, setAttribution] = useState("");
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState("");
  const [error, setError] = useState<Error>();
  const uploadTask = useRef<ReturnType<typeof audioUpload> | undefined>(
    undefined,
  );
  const uploadAbort = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => uploadAbort.current?.abort(), []);
  const [deleteId, setDeleteId] = useState<string>();
  const retries = useRef<Record<string, string>>({});
  const remove = async (id: string) => {
    setBusy(true);
    try {
      await api<void>(`/media/${id}`, { method: "DELETE" });
      setDeleteId(undefined);
      setProgress("Đã yêu cầu xóa. Quyền phát mới đã bị chặn.");
      assets.reload();
      if (admin) jobs.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const retry = async (id: string) => {
    setBusy(true);
    try {
      retries.current[id] ??= crypto.randomUUID();
      await post(`/admin/jobs/${id}/retry`, {
        clientRequestId: retries.current[id],
      });
      setProgress("Đã đưa job mới vào hàng đợi; lịch sử lỗi cũ được giữ lại.");
      assets.reload();
      jobs.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const upload = async () => {
    if (!file || busy) return;
    setBusy(true);
    setError(undefined);
    try {
      setProgress(
        "Đang gửi audio; khi lỗi mạng có thể tiếp tục phần chưa xác nhận.",
      );
      uploadTask.current ??= audioUpload(
        file,
        "lesson_audio",
        attribution,
        (bytes, total) =>
          setProgress(
            `Đã gửi ${Math.floor((bytes * 100) / total)}% · ${bytes}/${total} byte được xác nhận.`,
          ),
      );
      uploadAbort.current = new AbortController();
      const assetId = await uploadTask.current.send(uploadAbort.current.signal);
      uploadTask.current = undefined;
      setProgress(
        `Đã nhận file. Worker đang xác minh và xử lý. Mã media: ${assetId}`,
      );
      assets.reload();
      if (admin) jobs.reload();
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  // VI: REST polling là đường phục hồi, không báo Ready từ tiến độ upload. EN: Poll durable server state; uploaded bytes alone never imply a ready asset.
  useEffect(() => {
    if (
      !assets.data?.some((x) =>
        ["uploaded", "processing", "delete_pending"].includes(x.status),
      )
    )
      return;
    const timer = setTimeout(() => {
      assets.reload();
      if (admin) jobs.reload();
    }, 2500);
    return () => clearTimeout(timer);
  }, [assets.data, assets.reload, admin, jobs.reload]);
  return (
    <section className="cms-import media-admin">
      <div className="section-heading">
        <div>
          <span className="eyebrow">MEDIA, WITH A CLEAR SOURCE</span>
          <h2>Audio & hàng đợi xử lý</h2>
        </div>
        <button
          className="secondary"
          onClick={() => {
            assets.reload();
            if (admin) jobs.reload();
          }}
        >
          <RefreshCw size={13} /> Cập nhật
        </button>
      </div>
      <ErrorNotice error={error ?? assets.error} />
      <div className="cms-new-fields">
        <label>
          File audio · tối đa 100 MiB
          <input
            type="file"
            accept="audio/mpeg,audio/wav,audio/ogg,audio/mp4,audio/webm"
            disabled={busy || !!uploadTask.current}
            onChange={(e) => {
              setFile(e.target.files?.[0]);
              uploadTask.current = undefined;
            }}
          />
        </label>
        <label>
          Nguồn & quyền sử dụng
          <input
            value={attribution}
            disabled={busy || !!uploadTask.current}
            onChange={(e) => setAttribution(e.target.value)}
            placeholder="Tác giả, giấy phép hoặc căn cứ sử dụng"
            required
          />
        </label>
      </div>
      <button
        className="primary"
        disabled={busy || !file || !attribution}
        onClick={upload}
      >
        <Upload size={14} />
        {busy
          ? "Đang upload…"
          : uploadTask.current
            ? "Gửi lại phần chưa xác nhận"
            : "Upload & xử lý"}
      </button>
      {busy && uploadTask.current ? (
        <button
          type="button"
          className="secondary"
          onClick={() => uploadAbort.current?.abort()}
        >
          Tạm dừng gửi
        </button>
      ) : null}
      {uploadTask.current && !busy ? (
        <button
          type="button"
          className="secondary"
          onClick={() => {
            uploadTask.current = undefined;
            setError(undefined);
            setProgress(
              "Đã bỏ phiên trong trình duyệt. Kiểm tra danh sách trước khi gửi bản mới: phiên cũ có thể đã được nhận nếu ACK bị mất.",
            );
          }}
        >
          Bỏ phiên gửi hiện tại
        </button>
      ) : null}
      <p>
        Tiếp tục khi mạng lỗi trong tab này, trước khi phiên gửi hết hạn. Tải
        lại hoặc đóng tab sẽ mất trạng thái tiếp tục; file và URL truy cập không
        được lưu vào bộ nhớ trình duyệt lâu dài.
      </p>
      {progress ? <p className="media-status">{progress}</p> : null}
      <div className="asset-list">
        {assets.data?.map((asset) => (
          <article key={asset.id}>
            <Music2 size={20} />
            <div>
              <b>{asset.fileName}</b>
              <code>{asset.id}</code>
              <span>{asset.attribution}</span>
            </div>
            <span className={`asset-status ${asset.status}`}>
              {asset.status === "ready" ? <CheckCircle2 size={14} /> : null}
              {asset.status}
              {asset.failureCode ? ` · ${asset.failureCode}` : ""}
            </span>
            {asset.status === "ready" ? (
              <audio
                controls
                preload="none"
                src={`/api/v1/media/${asset.id}/content`}
              />
            ) : null}
            {!["deleted", "delete_pending"].includes(asset.status) ? (
              deleteId === asset.id ? (
                <div className="button-row">
                  <button
                    className="secondary"
                    disabled={busy}
                    onClick={() => remove(asset.id)}
                  >
                    Xác nhận xóa
                  </button>
                  <button
                    className="secondary"
                    onClick={() => setDeleteId(undefined)}
                  >
                    Giữ lại
                  </button>
                </div>
              ) : (
                <button
                  className="secondary"
                  disabled={busy}
                  onClick={() => setDeleteId(asset.id)}
                >
                  Xóa audio
                </button>
              )
            ) : null}
          </article>
        ))}
      </div>
      {admin ? (
        <details>
          <summary>Trạng thái job và lịch sử retry</summary>
          <ErrorNotice error={jobs.error} />
          <div className="job-list">
            {jobs.data?.map((job) => (
              <div key={job.id}>
                <code>{job.id}</code>
                <span>
                  {job.kind} · {job.state} · {job.attempts}/4 attempts
                </span>
                {job.lastErrorCode ? <span>{job.lastErrorCode}</span> : null}
                {job.state === "failed" ? (
                  <button
                    className="secondary"
                    disabled={busy}
                    onClick={() => retry(job.id)}
                  >
                    Thử xử lý lại
                  </button>
                ) : null}
              </div>
            ))}
          </div>
        </details>
      ) : null}
    </section>
  );
}
export function AudioPlayer({ assetId }: { assetId: string }) {
  const audio = useRef<HTMLAudioElement>(null);
  const position = useRef(0);
  const [revision, setRevision] = useState(0);
  const [failed, setFailed] = useState(false);
  return (
    <div className="lesson-audio">
      <span className="eyebrow">TAKE A MOMENT TO LISTEN</span>
      <audio
        ref={audio}
        controls
        preload="metadata"
        src={`/api/v1/media/${assetId}/content?r=${revision}`}
        onError={() => setFailed(true)}
        onTimeUpdate={() => {
          position.current = audio.current?.currentTime ?? 0;
        }}
        onLoadedMetadata={() => {
          if (audio.current)
            audio.current.currentTime = Math.min(
              position.current,
              audio.current.duration || position.current,
            );
          setFailed(false);
        }}
      />
      {failed ? (
        <button className="secondary" onClick={() => setRevision((v) => v + 1)}>
          <RefreshCw size={14} /> Thử phát lại
        </button>
      ) : null}
      <label>
        Tốc độ
        <select
          aria-label="Tốc độ phát"
          defaultValue="1"
          onChange={(e) => {
            if (audio.current)
              audio.current.playbackRate = Number(e.target.value);
          }}
        >
          {[0.75, 1, 1.25, 1.5].map((rate) => (
            <option key={rate}>{rate}</option>
          ))}
        </select>
      </label>
    </div>
  );
}

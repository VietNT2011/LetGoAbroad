import { useRef, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { ApiError, post } from "../api";
import { ErrorNotice, Loading, useResource } from "../hooks";
import type { PathTemplateSummary } from "./PathTimeline";
import "./path-choices.css";

type Revision = {
  id: string;
  number: number;
  pathVersionId: string | null;
  title: string;
  version: number | null;
  pathStartedOn: string | null;
  timeZoneId: string;
  studyDays: string;
  recordedAt: string;
  effectiveAt: string;
  schedulingTimeZoneId: string;
  action: string;
  selectionReason: string;
  userReason: string;
  placementAttemptId: string | null;
  status: "active" | "scheduled" | "past" | "superseded";
};
type History = {
  expectedEtag: string | null;
  activeRevisionId: string | null;
  activePathVersionId: string | null;
  revisions: Revision[];
};
type ChoiceCommand = {
  clientRequestId: string;
  pathVersionId: string;
  expectedEtag: string;
  reason: string;
};
const dayNames = ["CN", "T2", "T3", "T4", "T5", "T6", "T7"];
const when = (instant: string, zone: string) =>
  new Intl.DateTimeFormat("vi-VN", {
    dateStyle: "short",
    timeStyle: "short",
    timeZone: zone,
  }).format(new Date(instant));

/** VI: So sánh lịch sử trước khi chấp nhận token mới, giữ bản lựa chọn khi xung đột. EN: Preserve the choice draft and require explicit comparison before accepting a newer token. */
export function PathChoices({
  templates,
  onChanged,
}: {
  templates: PathTemplateSummary[];
  onChanged: () => void;
}) {
  const history = useResource<History>("/learning/path-revisions");
  const [target, setTarget] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [conflict, setConflict] = useState(false);
  const [compared, setCompared] = useState(false);
  const command = useRef<ChoiceCommand | undefined>(undefined);
  const submit = async (event?: FormEvent, cancel = false) => {
    event?.preventDefault();
    if (busy || conflict || !history.data?.expectedEtag) return;
    const id = cancel ? history.data.activePathVersionId : target;
    const explanation = cancel ? "Giữ lộ trình đang học" : reason;
    if (!id) return;
    if (
      command.current?.pathVersionId !== id ||
      command.current.reason !== explanation ||
      command.current.expectedEtag !== history.data.expectedEtag
    )
      command.current = {
        clientRequestId: crypto.randomUUID(),
        pathVersionId: id,
        reason: explanation,
        expectedEtag: history.data.expectedEtag,
      };
    setBusy(true);
    setError(undefined);
    try {
      await post("/learning/enrollment/switch-path", command.current);
      command.current = undefined;
      setTarget("");
      setReason("");
      history.reload();
      onChanged();
    } catch (error) {
      setError(error as Error);
      if (error instanceof ApiError && error.status === 412) {
        setConflict(true);
        setCompared(false);
      }
    } finally {
      setBusy(false);
    }
  };
  if (history.loading) return <Loading />;
  if (history.error)
    return <ErrorNotice error={history.error} retry={history.reload} />;
  if (!history.data?.activePathVersionId) return null;
  const active = history.data.revisions.find(
    (x) => x.id === history.data!.activeRevisionId,
  );
  const pending = history.data.revisions.find((x) => x.status === "scheduled");
  const alternatives = templates.filter(
    (x) => x.id !== history.data!.activePathVersionId,
  );
  return (
    <section className="path-choices lesson">
      <h2>Chuyển lộ trình và lịch sử lựa chọn</h2>
      <p>
        Chuyển mẫu áp dụng từ nửa đêm kế tiếp theo múi giờ ghi trong yêu cầu.
        Nhiệm vụ hôm nay giữ nguyên; bài luyện đang chạy giữ thời hạn cũ. Các
        lần chọn tiếp theo trước mốc đó dùng cùng thời điểm đã hẹn.
      </p>
      <ErrorNotice error={error} />
      {conflict ? (
        <div className="path-choice-conflict" role="status">
          <p>
            Lựa chọn đã thay đổi ở phiên khác. Bản đang chọn và lý do của bạn
            vẫn được giữ.
          </p>
          <button
            className="secondary"
            disabled={busy}
            onClick={() => {
              setCompared(true);
              history.reload();
            }}
          >
            Tải lịch sử để so sánh
          </button>
          {compared ? (
            <button
              className="secondary"
              disabled={busy}
              onClick={() => {
                setConflict(false);
                setError(undefined);
                command.current = undefined;
              }}
            >
              Đã xem lịch sử, giữ lựa chọn này
            </button>
          ) : null}
        </div>
      ) : null}
      {active ? (
        <p className="muted">
          Đang áp dụng: {active.title}
          {active.version ? ` · v${active.version}` : ""}. Tiến độ bài học theo
          đúng phiên bản nội dung được giữ.
        </p>
      ) : null}
      {pending ? (
        <div className="notice success" role="status">
          <p>
            {pending.action === "cancel_pending_switch"
              ? "Đã hẹn giữ mẫu hiện tại"
              : "Đã hẹn chuyển"}
            : {pending.title} ·{" "}
            {when(pending.effectiveAt, pending.schedulingTimeZoneId)} (
            {pending.schedulingTimeZoneId}).
          </p>
          {pending.pathVersionId !== history.data.activePathVersionId ? (
            <button
              className="secondary"
              disabled={busy || conflict}
              onClick={() => submit(undefined, true)}
            >
              Giữ lộ trình đang học
            </button>
          ) : null}
        </div>
      ) : null}
      {alternatives.length ? (
        <form onSubmit={submit}>
          <label>
            Mẫu muốn chuyển
            <select
              value={target}
              required
              disabled={busy}
              onChange={(event) => setTarget(event.target.value)}
            >
              <option value="">Chọn mẫu đang nhận đăng ký</option>
              {alternatives.map((x) => (
                <option key={x.id} value={x.id}>
                  {x.title} · v{x.version} · {x.nodeCount} bước
                </option>
              ))}
            </select>
          </label>
          <label>
            Lý do chuyển mẫu
            <textarea
              value={reason}
              required
              maxLength={500}
              rows={2}
              disabled={busy}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Bạn muốn tập trung vào điều gì?"
            />
          </label>
          <button
            className="primary"
            disabled={busy || conflict || !target || !reason.trim()}
          >
            {busy ? "Đang lưu…" : "Đặt lịch chuyển mẫu"}
          </button>
        </form>
      ) : (
        <p>
          Chưa có mẫu khác đang nhận đăng ký. Bạn vẫn tiếp tục học trên mẫu đã
          ghim.
        </p>
      )}
      <details className="path-choice-history" open={conflict || undefined}>
        <summary>
          Lịch sử chọn lộ trình · {history.data.revisions.length} bản ghi gần
          đây
        </summary>
        <button
          className="link-button"
          disabled={busy}
          onClick={history.reload}
        >
          Cập nhật lịch sử lựa chọn
        </button>
        <ol>
          {history.data.revisions.map((x) => (
            <li key={x.id}>
              <strong>
                #{x.number} · {x.title}
                {x.version ? ` · v${x.version}` : ""}
              </strong>
              <p>
                {
                  {
                    active: "Đang áp dụng",
                    scheduled: "Chờ mốc áp dụng",
                    past: "Đã áp dụng",
                    superseded: "Đã được thay thế",
                  }[x.status]
                }{" "}
                · {when(x.effectiveAt, x.schedulingTimeZoneId)} (
                {x.schedulingTimeZoneId})
              </p>
              {x.placementAttemptId ? (
                <Link to={`/attempts/${x.placementAttemptId}/result`}>
                  Xem đánh giá đầu vào ban đầu
                </Link>
              ) : null}
              <p>
                {x.userReason ||
                  (x.action === "observed_baseline"
                    ? "Cấu hình được ghi nhận khi nâng cấp"
                    : x.action === "enrollment"
                      ? "Lựa chọn khi tạo lộ trình"
                      : "Áp dụng mẫu có phiên bản")}
              </p>
              <small>
                Ngày học:{" "}
                {x.studyDays
                  .split(",")
                  .map((d) => dayNames[Number(d)])
                  .join(", ")}{" "}
                · {x.timeZoneId}
                {x.pathStartedOn ? ` · Bắt đầu ${x.pathStartedOn}` : ""}
              </small>
            </li>
          ))}
        </ol>
      </details>
    </section>
  );
}

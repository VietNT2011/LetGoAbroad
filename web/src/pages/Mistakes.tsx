import { useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError, post } from "../api";
import { ErrorNotice, Loading, useResource } from "../hooks";
import "./cards.css";
import "./annotations.css";

type Mistake = {
  id: string;
  questionId: string;
  prompt: string;
  part: number;
  skill: string;
  sourceAttemptId: string;
  wrongCount: number;
  lastWrongAt: string;
  status: string;
  nextReviewAt: string | null;
  correctReviewDays: number;
  cycleTimeZoneId: string;
  canReview: boolean;
  activeAttemptId: string | null;
  groupQuestionCount: number;
};
type Page = { items: Mistake[]; nextOffset: number | null };

/** VI: Sổ riêng lấy từ bài đã chấm; nhập lịch sử chỉ khi người học bấm, không tự sửa bằng chứng gốc. EN: Historical import is explicit and never rewrites original answers/results. */
export default function MistakesPage() {
  const [status, setStatus] = useState("learning");
  const [part, setPart] = useState("");
  const [skill, setSkill] = useState("");
  const [dueOnly, setDueOnly] = useState(false);
  const [offset, setOffset] = useState(0);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState("");
  const pending = useRef<string | undefined>(undefined);
  const query = new URLSearchParams({
    status,
    dueOnly: String(dueOnly),
    offset: String(offset),
  });
  if (part) query.set("part", part);
  if (skill) query.set("skill", skill);
  const path = `/me/mistakes?${query}`;
  const importHistory = async () => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    pending.current ??= crypto.randomUUID();
    try {
      const result = await post<{
        attemptsProcessed: number;
        hasMore: boolean;
      }>("/me/mistakes/import-history", { clientRequestId: pending.current });
      pending.current = undefined;
      setRevision((v) => v + 1);
      setNotice(
        `Đã đối chiếu ${result.attemptsProcessed} bài đã chấm. ${result.hasMore ? "Còn lịch sử; bấm nạp tiếp để bổ sung." : "Lịch sử hiện có đã được đối chiếu."}`,
      );
    } catch (failure) {
      setError(failure as Error);
      if (failure instanceof ApiError && failure.status < 500)
        pending.current = undefined;
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="cards-page">
      <header className="cards-heading">
        <span className="eyebrow">TURN ERRORS INTO PRACTICE</span>
        <h1>Sổ câu sai</h1>
        <p>
          Quay lại câu chưa đúng, giữ trọn ngữ cảnh và ôn đến khi bạn nắm chắc
          hơn.
        </p>
      </header>
      <aside className="card-policy">
        <h2>Hai ngày ôn đúng, một bước nắm vững</h2>
        <p>
          Câu được đánh dấu đã nắm sau hai lần ôn đúng ở hai ngày khác nhau. Lần
          đầu hẹn sau 24 giờ; ôn sai hẹn lại sau 10 phút và bắt đầu lại chu kỳ.
          Múi giờ của chu kỳ được giữ từ lần sai; đổi hồ sơ không tự cộng thêm
          ngày.
        </p>
        <p>
          Phiên ôn giữ cả nhóm câu để đọc đủ ngữ cảnh; chỉ câu bạn chọn cập nhật
          trạng thái sổ. Điểm ôn không cộng vào điểm mock, thống kê kỹ năng hay
          hoàn thành bài gốc.
        </p>
        <button
          type="button"
          className="button primary"
          disabled={busy}
          onClick={importHistory}
        >
          {busy
            ? "Đang đối chiếu…"
            : pending.current
              ? "Gửi lại yêu cầu nạp"
              : "Nạp câu sai từ lịch sử"}
        </button>
        <p>
          Chỉ nạp các bài đã chấm; mỗi lượt đối chiếu tối đa 50 bài cũ. Các bài
          làm mới được ghi nhận tự động.
        </p>
        <p role="status">{notice}</p>
        <ErrorNotice error={error} />
      </aside>
      <div className="card-filters" aria-label="Lọc sổ câu sai">
        <label>
          Trạng thái{" "}
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setOffset(0);
            }}
          >
            <option value="learning">Đang ôn</option>
            <option value="mastered">Đã nắm</option>
            <option value="all">Tất cả</option>
          </select>
        </label>
        <label>
          Part{" "}
          <select
            value={part}
            onChange={(e) => {
              setPart(e.target.value);
              setOffset(0);
            }}
          >
            <option value="">Mọi Part</option>
            {[1, 2, 3, 4, 5, 6, 7].map((n) => (
              <option key={n} value={n}>
                Part {n}
              </option>
            ))}
          </select>
        </label>
        <label>
          Kỹ năng{" "}
          <select
            value={skill}
            onChange={(e) => {
              setSkill(e.target.value);
              setOffset(0);
            }}
          >
            <option value="">Mọi kỹ năng</option>
            <option value="grammar">Ngữ pháp</option>
            <option value="reading">Đọc hiểu</option>
            <option value="listening">Nghe hiểu</option>
            <option value="vocabulary">Từ vựng</option>
          </select>
        </label>
        <label className="checkbox">
          <input
            type="checkbox"
            checked={dueOnly}
            onChange={(e) => {
              setDueOnly(e.target.checked);
              setOffset(0);
            }}
          />{" "}
          Chỉ đến hạn
        </label>
      </div>
      <MistakeList
        key={`${path}:${revision}`}
        path={path}
        offset={offset}
        page={setOffset}
      />
    </section>
  );
}

export function MistakeList({
  path,
  offset,
  page,
}: {
  path: string;
  offset: number;
  page: (offset: number) => void;
}) {
  const resource = useResource<Page>(path);
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const pending = useRef<Record<string, string>>({});
  const start = async (id: string) => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    pending.current[id] ??= crypto.randomUUID();
    try {
      const attempt = await post<{ id: string }>(`/me/mistakes/${id}/review`, {
        clientRequestId: pending.current[id],
      });
      delete pending.current[id];
      navigate(`/attempts/${attempt.id}`);
    } catch (failure) {
      setError(failure as Error);
      if (failure instanceof ApiError && failure.status < 500) {
        delete pending.current[id];
        resource.reload();
      }
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <ErrorNotice error={resource.error} retry={resource.reload} />
      <ErrorNotice error={error} />
      <button
        type="button"
        className="link-button"
        disabled={busy}
        onClick={resource.reload}
      >
        Cập nhật sổ câu sai
      </button>
      {resource.loading ? (
        <Loading />
      ) : resource.data ? (
        <>
          {resource.data.items.length ? (
            <div className="saved-questions">
              {resource.data.items.map((item) => (
                <article key={item.id}>
                  <span className="eyebrow">
                    Part {item.part} · {item.skill}
                  </span>
                  <h2>{item.prompt}</h2>
                  <p>
                    {item.wrongCount} lần chưa đúng · {item.correctReviewDays}/2
                    ngày ôn đúng ·{" "}
                    {item.status === "mastered" ? "Đã nắm" : "Đang ôn"}
                  </p>
                  {item.nextReviewAt ? (
                    <p>
                      Hẹn ôn:{" "}
                      <time dateTime={item.nextReviewAt}>
                        {new Date(item.nextReviewAt).toLocaleString("vi-VN")}
                      </time>
                    </p>
                  ) : null}
                  <p>Chu kỳ theo múi giờ {item.cycleTimeZoneId}.</p>
                  <div className="card-filters">
                    {item.activeAttemptId ? (
                      <Link
                        className="button primary"
                        to={`/attempts/${item.activeAttemptId}`}
                      >
                        Tiếp tục phiên ôn
                      </Link>
                    ) : (
                      <button
                        type="button"
                        className="button primary"
                        disabled={busy || !item.canReview}
                        onClick={() => start(item.id)}
                      >
                        Ôn trọn nhóm · {item.groupQuestionCount} câu
                      </button>
                    )}
                    <Link
                      className="back-link"
                      to={`/attempts/${item.sourceAttemptId}/result`}
                    >
                      Xem lời giải gốc →
                    </Link>
                  </div>
                </article>
              ))}
            </div>
          ) : (
            <p className="card-empty">
              Chưa có câu phù hợp. Bạn có thể nạp lịch sử hoặc đổi bộ lọc; không
              có dữ liệu mẫu được thêm vào sổ.
            </p>
          )}
          <div className="card-pagination">
            <button
              type="button"
              disabled={busy || !offset}
              onClick={() => page(Math.max(0, offset - 30))}
            >
              Trang trước
            </button>
            <button
              type="button"
              disabled={busy || resource.data.nextOffset === null}
              onClick={() => page(resource.data!.nextOffset!)}
            >
              Trang sau
            </button>
          </div>
        </>
      ) : null}
    </>
  );
}

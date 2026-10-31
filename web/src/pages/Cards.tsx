import { useEffect, useRef, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { ApiError, post } from "../api";
import { ErrorNotice, Loading, useResource } from "../hooks";
import { useAuth } from "../Auth";
import { useStudyActivity } from "../useStudyActivity";
import "./cards.css";
import { CardEditor } from "./CardEditor";

type Card = {
  id: string;
  front: string;
  back: string;
  contexts: string[];
  dueAt: string;
  intervalDays: number;
  successStep: number;
  schedulerVersion: number;
  archivedAt: string | null;
  canReview: boolean;
  etag: string;
  contentVersion: number;
};
type CardPage = { items: Card[]; dueCount: number; nextOffset: number | null };
type Rating = "again" | "hard" | "good" | "easy";
const ratings: { value: Rating; label: string }[] = [
  { value: "again", label: "Quên · Again" },
  { value: "hard", label: "Khó · Hard" },
  { value: "good", label: "Nhớ · Good" },
  { value: "easy", label: "Rất dễ · Easy" },
];
const formatTime = (value: string) => new Date(value).toLocaleString("vi-VN");

/** VI: Thẻ tự viết và tự đánh giá, không thay điểm bài thi hay tự tạo nghĩa từ. EN: Personal recall ratings never modify assessment scores or fabricate dictionary senses. */
export default function CardsPage() {
  const { user } = useAuth();
  const [filter, setFilter] = useState("due");
  const [offset, setOffset] = useState(0);
  const [selected, setSelected] = useState<string>();
  const [params] = useSearchParams();
  const batchId = params.get("batch");
  const batch = useResource<{
    id: string;
    status: string;
    items: {
      cardId: string;
      front: string;
      completed: boolean;
      archived: boolean;
    }[];
  }>(batchId ? `/review-batches/${encodeURIComponent(batchId)}` : null);
  const studyId =
    selected ?? batch.data?.items.find((item) => !item.completed)?.cardId;
  const changed = () => {
    resource.reload();
    batch.reload();
    if (batchId) setSelected(undefined);
  };
  const resource = useResource<CardPage>(
    `/cards/?dueOnly=${filter === "due"}&archived=${filter === "archived"}&offset=${offset}`,
  );
  useStudyActivity(
    resource.data && !resource.error ? "cards" : null,
    batchId ?? "cards",
    user?.id,
  );
  const changeFilter = (value: string) => {
    setFilter(value);
    setOffset(0);
    setSelected(undefined);
  };
  return (
    <section className="cards-page">
      <header className="cards-heading">
        <span className="eyebrow">WORDS YOU WANT TO KEEP</span>
        <h1>Mỗi thẻ, một điều bạn muốn nhớ.</h1>
        <p>
          Tự viết từ hoặc cụm từ, thêm ngữ cảnh và ôn khi đến hạn. Bạn tự chọn
          mức nhớ sau khi mở mặt sau.
        </p>
      </header>
      {batchId ? (
        <section className="card-policy" aria-label="Nhiệm vụ ôn hôm nay">
          <h2>Thẻ trong nhiệm vụ của bạn</h2>
          <ErrorNotice error={batch.error} retry={batch.reload} />
          {batch.loading ? (
            <Loading />
          ) : batch.data ? (
            <>
              <p role="status">
                {batch.data.items.filter((item) => item.completed).length}/
                {batch.data.items.length} thẻ đã ôn ·{" "}
                {batch.data.status === "completed"
                  ? "Đã hoàn thành nhiệm vụ"
                  : batch.data.status === "pending"
                    ? "Đang ôn"
                    : "Nhiệm vụ đã đóng"}
              </p>
              <ul>
                {batch.data.items.map((item) => (
                  <li key={item.cardId}>
                    {item.completed ? "✓ " : ""}
                    <button
                      type="button"
                      className="link-button"
                      onClick={() => setSelected(item.cardId)}
                    >
                      {item.front}
                    </button>
                    {item.archived
                      ? " · Thẻ đã cất: khôi phục để tiếp tục ôn."
                      : ""}
                  </li>
                ))}
              </ul>
              <p>
                Mỗi mức tự đánh giá được chấp nhận tính là một lượt ôn thẻ;
                không phải điểm đúng của bài thi.
              </p>
            </>
          ) : null}
        </section>
      ) : null}
      <CreateCardForm
        saved={(id) => {
          setSelected(id);
          resource.reload();
        }}
      />
      <div className="card-filters" role="group" aria-label="Danh sách thẻ">
        {[
          { value: "due", label: "Đến hạn" },
          { value: "all", label: "Thẻ đang học" },
          { value: "archived", label: "Thẻ đã cất" },
        ].map((item) => (
          <button
            type="button"
            key={item.value}
            aria-pressed={filter === item.value}
            onClick={() => changeFilter(item.value)}
          >
            {item.label}
          </button>
        ))}
        <button type="button" className="link-button" onClick={resource.reload}>
          Tải lại danh sách
        </button>
      </div>
      <ErrorNotice error={resource.error} retry={resource.reload} />
      {resource.loading ? (
        <Loading />
      ) : resource.data ? (
        <>
          <p role="status">
            {resource.data.dueCount} thẻ đang đến hạn. Thời điểm hiển thị theo
            múi giờ thiết bị của bạn.
          </p>
          {!resource.data.items.length ? (
            <p className="card-empty">
              {filter === "due"
                ? "Bạn đã ôn hết thẻ đến hạn. Có thể thêm thẻ của riêng mình ở trên."
                : "Chưa có thẻ trong danh sách này."}
            </p>
          ) : (
            <ul className="personal-cards">
              {resource.data.items.map((card) => (
                <li key={card.id}>
                  <button
                    type="button"
                    onClick={() => setSelected(card.id)}
                    aria-pressed={selected === card.id}
                  >
                    <b>{card.front}</b>
                    <span>
                      {card.archivedAt ? (
                        "Đã cất"
                      ) : (
                        <>
                          Hạn ôn:{" "}
                          <time dateTime={card.dueAt}>
                            {formatTime(card.dueAt)}
                          </time>
                        </>
                      )}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <div className="card-pagination">
            <button
              type="button"
              disabled={offset === 0}
              onClick={() => setOffset(Math.max(0, offset - 30))}
            >
              Trang trước
            </button>
            <span>Trang {offset / 30 + 1}</span>
            <button
              type="button"
              disabled={resource.data.nextOffset === null}
              onClick={() => setOffset(resource.data!.nextOffset!)}
            >
              Trang sau
            </button>
          </div>
        </>
      ) : null}
      {studyId ? (
        <CardStudy key={studyId} id={studyId} changed={changed} />
      ) : null}
      <aside className="card-policy">
        <h2>Lịch ôn v1 hoạt động thế nào?</h2>
        <p>
          Again: 10 phút và về bước0. Hard: ít nhất1 ngày, giữ bước. Good: 1,3,7
          ngày cho ba bước đầu, sau đó gấp đôi. Easy: ít nhất4 ngày, sau đó
          ×2,5. Tối đa180 ngày; các phép nhân làm tròn nửa lên. Đây là quy tắc
          của sản phẩm, chưa phải mô hình trí nhớ đã kiểm định.
        </p>
        <p>
          Thẻ và lịch sử là riêng tư. Cất thẻ tạm dừng ôn; khôi phục giữ nguyên
          lịch sử và hạn ôn. Lịch ngày dành tối đa30% ngân sách cho thẻ đến hạn,
          một phút ước lượng mỗi thẻ. Từ điển và deck dùng chung sẽ được triển
          khai riêng.
        </p>
      </aside>
    </section>
  );
}

function CreateCardForm({ saved }: { saved: (id: string) => void }) {
  const [front, setFront] = useState("");
  const [back, setBack] = useState("");
  const [context, setContext] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState("");
  const command = useRef<{ key: string; id: string } | undefined>(undefined);
  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(undefined);
    const payload = { front, back, sourceContext: context };
    const key = JSON.stringify(payload);
    command.current =
      command.current?.key === key
        ? command.current
        : { key, id: crypto.randomUUID() };
    try {
      const card = await post<Card>("/cards/", {
        ...payload,
        clientRequestId: command.current.id,
      });
      setFront("");
      setBack("");
      setContext("");
      command.current = undefined;
      setNotice(
        "Đã lưu thẻ và ngữ cảnh. Lưu trùng hai mặt sẽ dùng thẻ hiện có.",
      );
      saved(card.id);
    } catch (error) {
      setError(error as Error);
    } finally {
      setBusy(false);
    }
  };
  return (
    <details className="card-create">
      <summary>Tạo thẻ của bạn</summary>
      <form onSubmit={save}>
        <label htmlFor="card-front">
          Mặt trước · từ hoặc cụm từ
          <textarea
            id="card-front"
            required
            maxLength={500}
            value={front}
            onChange={(event) => setFront(event.target.value)}
            disabled={busy}
          />
        </label>
        <label htmlFor="card-back">
          Mặt sau · nghĩa hoặc điều muốn nhớ
          <textarea
            id="card-back"
            required
            maxLength={2000}
            value={back}
            onChange={(event) => setBack(event.target.value)}
            disabled={busy}
          />
        </label>
        <label htmlFor="card-context">
          Ngữ cảnh nguồn (không bắt buộc)
          <textarea
            id="card-context"
            maxLength={2000}
            value={context}
            onChange={(event) => setContext(event.target.value)}
            disabled={busy}
          />
        </label>
        <ErrorNotice error={error} />
        <p role="status">{notice}</p>
        <button className="button primary" disabled={busy}>
          {busy ? "Đang lưu…" : "Lưu thẻ"}
        </button>
      </form>
    </details>
  );
}

function CardStudy({ id, changed }: { id: string; changed: () => void }) {
  const resource = useResource<Card>(`/cards/${id}`);
  const [revealed, setRevealed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState("");
  const [stale, setStale] = useState(false);
  const [editing, setEditing] = useState(false);
  const pending = useRef<
    { reviewEventId: string; expectedEtag: string; rating: Rating } | undefined
  >(undefined);
  const archiveCommand = useRef<
    | { clientRequestId: string; expectedEtag: string; archived: boolean }
    | undefined
  >(undefined);
  const [retryRating, setRetryRating] = useState<Rating>();
  const heading = useRef<HTMLHeadingElement>(null);
  // VI: Chọn thẻ chuyển focus tới phần vừa mở, kể cả khi danh sách dài. EN: Move keyboard focus to the explicitly selected card instead of leaving it above a long list.
  useEffect(() => {
    heading.current?.focus();
  }, [id]);
  const refresh = () => {
    pending.current = undefined;
    archiveCommand.current = undefined;
    setRetryRating(undefined);
    setStale(false);
    setRevealed(false);
    setError(undefined);
    resource.reload();
  };
  const fail = (failure: unknown) => {
    const value = failure as Error;
    setError(value);
    if (
      value instanceof ApiError &&
      [404, 409, 412, 422].includes(value.status)
    ) {
      setStale(true);
      setRetryRating(undefined);
      pending.current = undefined;
    }
  };
  const review = async (rating: Rating) => {
    if (busy || stale || !resource.data) return;
    setBusy(true);
    setError(undefined);
    setNotice("");
    pending.current ??= {
      reviewEventId: crypto.randomUUID(),
      expectedEtag: resource.data.etag,
      rating,
    };
    try {
      const result = await post<{ card: Card }>(
        `/cards/${id}/reviews`,
        pending.current,
      );
      setNotice(`Đã ghi nhận. Hạn ôn tiếp: ${formatTime(result.card.dueAt)}.`);
      pending.current = undefined;
      setRetryRating(undefined);
      setRevealed(false);
      resource.reload();
      changed();
    } catch (failure) {
      setRetryRating(rating);
      fail(failure);
    } finally {
      setBusy(false);
    }
  };
  const archive = async () => {
    if (busy || stale || !resource.data || retryRating) return;
    setBusy(true);
    setError(undefined);
    archiveCommand.current ??= {
      clientRequestId: crypto.randomUUID(),
      expectedEtag: resource.data.etag,
      archived: !resource.data.archivedAt,
    };
    try {
      await post<Card>(`/cards/${id}/archive`, archiveCommand.current);
      setNotice(
        archiveCommand.current.archived
          ? "Đã cất thẻ. Lịch sử vẫn được giữ."
          : "Đã khôi phục thẻ với lịch cũ.",
      );
      archiveCommand.current = undefined;
      setRevealed(false);
      resource.reload();
      changed();
    } catch (failure) {
      fail(failure);
    } finally {
      setBusy(false);
    }
  };
  const card = resource.data;
  return (
    <section className="card-study" aria-label="Thẻ đang xem">
      <h2 ref={heading} tabIndex={-1}>
        Thử nhớ trước khi mở mặt sau
      </h2>
      <p role="status">{notice}</p>
      <ErrorNotice error={resource.error} retry={resource.reload} />
      <ErrorNotice error={error} />
      {stale ? (
        <div className="notice">
          <p>
            Thẻ đã đổi hoặc không còn khả dụng. Tải bản hiện tại rồi tự chọn lại
            mức nhớ; lần đánh giá vừa rồi chưa được áp dụng.
          </p>
          <button type="button" onClick={refresh}>
            Tải trạng thái thẻ hiện tại
          </button>
        </div>
      ) : null}
      {resource.loading ? (
        <Loading />
      ) : card ? (
        <>
          <div className="recall-card">
            <p className="card-face-label">MẶT TRƯỚC</p>
            <p className="card-front">{card.front}</p>
            {revealed ? (
              <div className="card-answer">
                <p className="card-face-label">MẶT SAU</p>
                <p>{card.back}</p>
                {card.contexts.length ? (
                  <>
                    <h3>Ngữ cảnh của bạn</h3>
                    <ul>
                      {card.contexts.map((context, index) => (
                        <li key={index}>{context}</li>
                      ))}
                    </ul>
                  </>
                ) : null}
              </div>
            ) : (
              <button
                type="button"
                className="button primary"
                onClick={() => setRevealed(true)}
              >
                Mở mặt sau
              </button>
            )}
          </div>
          <p>
            {card.archivedAt ? (
              "Thẻ đã cất"
            ) : (
              <>
                Hạn ôn:{" "}
                <time dateTime={card.dueAt}>{formatTime(card.dueAt)}</time>
              </>
            )}{" "}
            · khoảng {card.intervalDays} ngày · bước {card.successStep}
          </p>
          {!card.archivedAt && !card.canReview ? (
            <p>
              Thẻ chưa đến hạn.{" "}
              <button
                className="link-button"
                type="button"
                disabled={busy}
                onClick={resource.reload}
              >
                Kiểm tra lại hạn ôn
              </button>
            </p>
          ) : null}
          {revealed && card.canReview ? (
            <div
              className="card-ratings"
              role="group"
              aria-label="Tự đánh giá mức nhớ"
            >
              {ratings.map((rating) => (
                <button
                  type="button"
                  key={rating.value}
                  disabled={
                    busy ||
                    editing ||
                    stale ||
                    !!retryRating ||
                    !!archiveCommand.current
                  }
                  onClick={() => review(rating.value)}
                >
                  {rating.label}
                </button>
              ))}
            </div>
          ) : null}
          {retryRating && !stale ? (
            <div className="notice">
              <p>
                Chưa nhận được xác nhận. Gửi lại cùng sự kiện để tránh cộng hai
                lần.
              </p>
              <button
                type="button"
                disabled={busy}
                onClick={() => review(retryRating)}
              >
                Gửi lại mức{" "}
                {ratings.find((r) => r.value === retryRating)?.label}
              </button>
            </div>
          ) : null}
          <button
            className="link-button"
            type="button"
            disabled={busy || editing || stale || !!retryRating}
            onClick={archive}
          >
            {card.archivedAt ? "Khôi phục thẻ" : "Cất thẻ"}
          </button>
          <button
            type="button"
            className="link-button"
            disabled={
              busy ||
              editing ||
              stale ||
              !!retryRating ||
              !!archiveCommand.current
            }
            onClick={() => setEditing(true)}
          >
            Sửa nội dung thẻ
          </button>
          {editing ? (
            <CardEditor
              card={card}
              cancel={() => setEditing(false)}
              saved={() => {
                setEditing(false);
                setRevealed(false);
                setNotice(
                  "Đã lưu nội dung thẻ. Lịch ôn thay đổi nếu hai mặt đã đổi.",
                );
                resource.reload();
                changed();
              }}
            />
          ) : null}
          <CardHistory key={`${id}:${card.etag}`} id={id} />
          <CardVersions key={`versions:${id}:${card.etag}`} id={id} />
        </>
      ) : null}
    </section>
  );
}

function CardHistory({ id }: { id: string }) {
  const [open, setOpen] = useState(false);
  const [offset, setOffset] = useState(0);
  const resource = useResource<{
    items: {
      id: string;
      rating: Rating;
      reviewedAt: string;
      beforeIntervalDays: number;
      afterIntervalDays: number;
      beforeSuccessStep: number;
      afterSuccessStep: number;
      schedulerVersion: number;
      contentVersion: number;
      front: string;
      back: string;
    }[];
    nextOffset: number | null;
  }>(open ? `/cards/${id}/reviews?offset=${offset}` : null);
  return (
    <details
      className="card-history"
      onToggle={(event) => setOpen(event.currentTarget.open)}
    >
      <summary>Lịch sử ôn thẻ</summary>
      {open ? (
        <>
          <ErrorNotice error={resource.error} retry={resource.reload} />
          {resource.loading ? (
            <Loading />
          ) : resource.data ? (
            <>
              {!resource.data.items.length ? (
                <p>Chưa có lần ôn nào.</p>
              ) : (
                <ol>
                  {resource.data.items.map((item) => (
                    <li key={item.id}>
                      <time dateTime={item.reviewedAt}>
                        {formatTime(item.reviewedAt)}
                      </time>{" "}
                      · {ratings.find((r) => r.value === item.rating)?.label}
                      <br />
                      Khoảng {item.beforeIntervalDays} →{" "}
                      {item.afterIntervalDays} ngày; bước{" "}
                      {item.beforeSuccessStep} → {item.afterSuccessStep}; lịch v
                      {item.schedulerVersion}.
                      <details>
                        <summary>
                          Nội dung đã ôn · phiên bản {item.contentVersion}
                        </summary>
                        <p className="card-version-text">{item.front}</p>
                        <p className="card-version-text">{item.back}</p>
                      </details>
                    </li>
                  ))}
                </ol>
              )}
              <div className="card-pagination">
                <button
                  type="button"
                  disabled={!offset}
                  onClick={() => setOffset(Math.max(0, offset - 30))}
                >
                  Lịch sử trước
                </button>
                <button
                  type="button"
                  disabled={resource.data.nextOffset === null}
                  onClick={() => setOffset(resource.data!.nextOffset!)}
                >
                  Lịch sử sau
                </button>
              </div>
            </>
          ) : null}
        </>
      ) : null}
    </details>
  );
}

function CardVersions({ id }: { id: string }) {
  const [open, setOpen] = useState(false);
  const [offset, setOffset] = useState(0);
  const resource = useResource<{
    items: { number: number; front: string; back: string; createdAt: string }[];
    nextOffset: number | null;
  }>(open ? `/cards/${id}/versions?offset=${offset}` : null);
  return (
    <details
      className="card-history"
      onToggle={(e) => setOpen(e.currentTarget.open)}
    >
      <summary>Lịch sử nội dung thẻ</summary>
      {open ? (
        <>
          <ErrorNotice error={resource.error} retry={resource.reload} />
          {resource.loading ? (
            <Loading />
          ) : resource.data ? (
            <>
              <ol>
                {resource.data.items.map((version) => (
                  <li key={version.number}>
                    <b>Phiên bản {version.number}</b> ·{" "}
                    <time dateTime={version.createdAt}>
                      {formatTime(version.createdAt)}
                    </time>
                    <p className="card-version-text">{version.front}</p>
                    <p className="card-version-text">{version.back}</p>
                  </li>
                ))}
              </ol>
              <div className="card-pagination">
                <button
                  type="button"
                  disabled={!offset}
                  onClick={() => setOffset(Math.max(0, offset - 30))}
                >
                  Phiên bản mới hơn
                </button>
                <button
                  type="button"
                  disabled={resource.data.nextOffset === null}
                  onClick={() => setOffset(resource.data!.nextOffset!)}
                >
                  Phiên bản cũ hơn
                </button>
              </div>
            </>
          ) : null}
        </>
      ) : null}
    </details>
  );
}

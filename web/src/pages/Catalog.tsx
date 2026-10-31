import { useDeferredValue, useEffect, useRef, useState } from "react";
import {
  Link,
  useLocation,
  useNavigate,
  useNavigationType,
  useSearchParams,
} from "react-router-dom";
import {
  ArrowUpRight,
  ArrowRight,
  Headphones,
  BookOpen,
  Clock3,
  Search,
  Sparkles,
  PenLine,
} from "lucide-react";
import type { Content } from "../api";
import { useResource, ErrorNotice, Loading } from "../hooks";
import { useAuth } from "../Auth";
import { TodayPanel } from "./Learning";
import { LearningArtwork } from "../LearningArtwork";
// VI: Mỗi kỹ năng giữ một màu ở mọi thứ tự/lọc. EN: Skill colors remain stable when cards are reordered or filtered.
const skillArt: Record<string, { tone: string; word: string }> = {
  grammar: { tone: "violet", word: "Aa" },
  listening: { tone: "blue", word: "Hello!" },
  reading: { tone: "teal", word: "Read" },
  vocabulary: { tone: "amber", word: "Words" },
};
const skillNames: Record<string, string> = {
  grammar: "NGỮ PHÁP",
  vocabulary: "TỪ VỰNG",
  listening: "NGHE HIỂU",
  reading: "ĐỌC HIỂU",
};
export function ContentCard({
  item,
  index = 0,
}: {
  item: Content;
  index?: number;
}) {
  const Icon =
    item.skill === "listening"
      ? Headphones
      : item.skill === "grammar"
        ? PenLine
        : BookOpen;
  const art = skillArt[item.skill] ?? { tone: "blue", word: "Learn" };
  return (
    <Link
      className="content-card"
      to={item.kind === "lesson" ? `/learn/${item.id}` : `/tests/${item.id}`}
    >
      <div className={`card-art tone-${art.tone}`} aria-hidden="true">
        <span className="art-number">{String(index + 1).padStart(2, "0")}</span>
        <span className="art-word">{art.word}</span>
        <Icon size={68} strokeWidth={1.8} />
        <span className="art-star">✧</span>
        <span className="art-lines" />
      </div>
      <div className="card-copy">
        <span className="eyebrow">{skillNames[item.skill] ?? item.skill}</span>
        <h3>{item.title}</h3>
        <p>{item.summary}</p>
        <div className="card-meta">
          <span>
            <Clock3 size={15} aria-hidden="true" /> {item.minutes} phút
          </span>
          <span>
            {item.kind === "assessment"
              ? `${item.questionCount} câu hỏi`
              : "Bài học nền tảng"}
          </span>
          <ArrowUpRight size={18} aria-hidden="true" />
        </div>
        {item.isFixture ? (
          <small className="fixture">Dữ liệu demo kỹ thuật</small>
        ) : null}
      </div>
    </Link>
  );
}
export function Catalog({ tests = false }: { tests?: boolean }) {
  const [filters] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const navigationType = useNavigationType();
  const urlSearch = (filters.get("search") ?? "").slice(0, 200);
  const [search, setSearch] = useState(urlSearch);
  const pendingFilters = useRef(filters);
  // VI: Draft cập nhật đồng bộ để router bất đồng bộ không nuốt ký tự; Back/deep link khôi phục từ URL. EN: A synchronous input draft prevents asynchronous navigation from dropping keystrokes; Back/deep links restore URL state.
  useEffect(() => {
    if (navigationType !== "POP" && location.state?.catalogFilters) return;
    pendingFilters.current = filters;
    setSearch(urlSearch);
  }, [filters, location.key, location.state, navigationType, urlSearch]);
  const requestedSkill = filters.get("skill") ?? "";
  const skill = Object.hasOwn(skillArt, requestedSkill) ? requestedSkill : "";
  // VI: URL giữ bộ lọc; gõ thay history hiện tại để không tạo một entry mỗi ký tự. EN: URL filters survive reload/Back; typing replaces history rather than adding an entry per character.
  const updateFilter = (key: "search" | "skill", value: string) => {
    if (value === (key === "search" ? search : skill)) return;
    if (key === "search") setSearch(value);
    // VI: Ghép bản dự kiến mới nhất và giữ anchor để cập nhật router không mất bộ lọc hay focus. EN: Merge the latest intent and retain the anchor so navigation cannot lose a filter or move typing focus.
    const next = new URLSearchParams(pendingFilters.current);
    if (value) next.set(key, value);
    else next.delete(key);
    pendingFilters.current = next;
    navigate(
      {
        pathname: location.pathname,
        search: next.size ? `?${next}` : "",
        hash: location.hash,
      },
      {
        replace: key === "search",
        preventScrollReset: true,
        state: { catalogFilters: true },
      },
    );
  };
  const query = useDeferredValue(search);
  const { data, loading, error, reload } = useResource<Content[]>(
    `/catalog?search=${encodeURIComponent(query)}&skill=${skill}`,
  );
  const items = data?.filter(
    (x) => x.kind === (tests ? "assessment" : "lesson"),
  );
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">
            {tests ? "PRACTICE MAKES PROGRESS" : "EXPLORE & GROW"}
          </span>
          <h1>{tests ? "Thử sức, hiểu mình hơn." : "Mở ra một điều mới."}</h1>
          <p>
            {tests
              ? "Luyện tập có mục tiêu. Hiểu rõ từng câu trả lời."
              : "Từng bài học nhỏ, kết nối thành một hành trình lớn."}
          </p>
        </div>
        <span className="outline-tag">
          <Sparkles size={14} /> Biên soạn cho người tự học
        </span>
      </div>
      <div className="filterbar">
        <div className="tabs">
          {[
            ["", "Tất cả"],
            ["grammar", "Ngữ pháp"],
            ["listening", "Nghe hiểu"],
            ["reading", "Đọc hiểu"],
            ["vocabulary", "Từ vựng"],
          ].map(([value, label]) => (
            <button
              className={skill === value ? "selected" : ""}
              aria-pressed={skill === value}
              key={value}
              onClick={() => updateFilter("skill", value)}
            >
              {label}
            </button>
          ))}
        </div>
        <label className="search">
          <Search size={18} aria-hidden="true" />
          <input
            type="search"
            name="lesson-search"
            maxLength={200}
            autoComplete="off"
            placeholder={
              tests ? "Tìm bài bạn muốn luyện…" : "Tìm điều bạn muốn học…"
            }
            aria-label={tests ? "Tìm bài luyện" : "Tìm bài học"}
            value={search}
            onChange={(e) => updateFilter("search", e.target.value)}
          />
        </label>
      </div>
      <p className="catalog-results" role="status" aria-live="polite">
        {loading
          ? "Đang tìm nội dung…"
          : error
            ? "Chưa tải được danh sách."
            : `${items?.length ?? 0} ${tests ? "bài luyện" : "bài học"}`}
      </p>
      <ErrorNotice error={error} retry={reload} />
      {loading ? (
        <Loading />
      ) : items?.length ? (
        <div className="card-grid">
          {items.map((item, i) => (
            <ContentCard item={item} key={item.id} index={i} />
          ))}
        </div>
      ) : !error ? (
        <div className="empty">
          Chưa có nội dung phù hợp. Hãy thử bộ lọc khác.
        </div>
      ) : null}
    </>
  );
}
export function Dashboard() {
  const { user } = useAuth();
  const { data, loading, error, reload } = useResource<Content[]>("/catalog");
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">A NEW DAY, A LITTLE PROGRESS</span>
          <h1>
            Chào {user?.displayName.split(" ").slice(-1)}, mình học nhé{" "}
            <span className="wave" aria-hidden="true">
              ✦
            </span>
          </h1>
          <p>Mỗi lần bạn xuất hiện là một bước tiến. Hôm nay cũng vậy.</p>
        </div>
        <span className="date-tag">
          {new Intl.DateTimeFormat("vi-VN", {
            timeZone: user?.timeZoneId ?? "Asia/Ho_Chi_Minh",
            weekday: "long",
            day: "numeric",
            month: "long",
          }).format(new Date())}
        </span>
      </div>
      <section className="hero-panel">
        <div>
          <span className="eyebrow">YOUR NEXT CHAPTER</span>
          <h2>
            Một chút hôm nay.
            <br />
            <span>Một thế giới ngày mai.</span>
          </h2>
          <p>
            Học theo nhịp của bạn, với một lộ trình rõ ràng. <br />
            Bắt đầu từ điều nhỏ nhất và cứ thế tiến lên.
          </p>
          <a href="#today-plan" className="primary">
            Học bài hôm nay <ArrowRight size={19} aria-hidden="true" />
          </a>
        </div>
        <div className="hero-graphic" aria-hidden="true">
          <LearningArtwork />
        </div>
      </section>
      <TodayPanel />
      <div className="section-heading">
        <div>
          <span className="eyebrow">FIND YOUR MOMENTUM</span>
          <h2>Một bài học cho hôm nay</h2>
        </div>
        <Link to="/catalog">
          Khám phá tất cả <ArrowUpRight size={16} />
        </Link>
      </div>
      <ErrorNotice error={error} retry={reload} />
      {loading ? (
        <Loading />
      ) : (
        <div className="card-grid">
          {data
            ?.filter((x) => x.kind === "lesson")
            .slice(0, 3)
            .map((item, i) => (
              <ContentCard key={item.id} item={item} index={i} />
            ))}
        </div>
      )}
    </>
  );
}

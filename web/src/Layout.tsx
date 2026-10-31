import {
  NavLink,
  Outlet,
  Link,
  useLocation,
  useNavigationType,
} from "react-router-dom";
import {
  BookOpen,
  LayoutDashboard,
  Route,
  Headphones,
  ClipboardList,
  Layers,
  Settings,
  LogOut,
  ArrowUpRight,
  Shield,
  Users,
  Bookmark,
  Menu,
  X,
  Mic,
} from "lucide-react";
import { useAuth } from "./Auth";
import { useEffect, useRef, useState } from "react";
import { ErrorNotice } from "./hooks";
export function Layout() {
  const { user, logout } = useAuth();
  const [error, setError] = useState<Error>();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuButton = useRef<HTMLButtonElement>(null);
  const main = useRef<HTMLElement>(null);
  const location = useLocation();
  const navigationType = useNavigationType();
  const previousLocation = useRef(location);
  // VI: Trang mới mở từ đầu; deep link đợi nội dung bất đồng bộ, không giữ vị trí cuộn của trang cũ. EN: New routes start at the top; deep links wait for asynchronous content instead of retaining the previous route's scroll offset.
  useEffect(() => {
    // VI: Lọc bằng query không phải trang mới; giữ con trỏ gõ và vị trí cuộn khi PUSH/REPLACE đổi loại. EN: Query-only filtering keeps typing focus and scroll even when navigation switches between PUSH and REPLACE.
    const previous = previousLocation.current;
    previousLocation.current = location;
    if (
      previous.pathname === location.pathname &&
      previous.hash === location.hash &&
      previous.search !== location.search
    )
      return;
    const hash = location.hash.slice(1);
    if (!hash) {
      if (navigationType !== "POP") {
        window.scrollTo({ top: 0, behavior: "instant" });
        main.current?.focus({ preventScroll: true });
      }
      return;
    }
    const reveal = () => {
      const target = document.getElementById(hash);
      if (!target) return false;
      target.scrollIntoView({ block: "start", behavior: "instant" });
      return true;
    };
    if (reveal() || !main.current) return;
    const observer = new MutationObserver(() => {
      if (reveal()) observer.disconnect();
    });
    observer.observe(main.current, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, [location.pathname, location.hash, location.search, navigationType]);
  const closeMenu = () => setMenuOpen(false);
  const links = [
    { to: "/dashboard", title: "Tổng quan", icon: LayoutDashboard },
    { to: "/paths", title: "Lộ trình của tôi", icon: Route },
    { to: "/catalog", title: "Khám phá bài học", icon: Headphones },
    { to: "/tests", title: "Luyện tập & kiểm tra", icon: ClipboardList },
    { to: "/saved", title: "Câu đã lưu & ghi chú", icon: Bookmark },
    { to: "/review/mistakes", title: "Sổ câu sai", icon: ClipboardList },
    { to: "/vocabulary", title: "Từ vựng & ôn tập", icon: Layers },
    { to: "/recordings", title: "Ghi âm của tôi", icon: Mic },
    { to: "/groups", title: "Cộng đồng học tập", icon: Users },
  ];
  return (
    <>
      <a className="skip-link" href="#main-content">
        Đến nội dung chính
      </a>
      <div className="app-shell">
        <aside
          className={`sidebar${menuOpen ? " menu-open" : ""}`}
          onBlur={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget)) closeMenu();
          }}
          onKeyDown={(event) => {
            if (event.key === "Escape" && menuOpen) {
              closeMenu();
              menuButton.current?.focus();
            }
          }}
        >
          <div className="sidebar-head">
            <Link to="/dashboard" className="brand" onClick={closeMenu}>
              <BookOpen size={25} aria-hidden="true" /> englishhub
              <span className="dot">.</span>
            </Link>
            {/* VI: Menu dạng disclosure giữ đủ tên và chức năng trên mobile. EN: A keyboard-accessible disclosure keeps complete navigation and account actions on mobile. */}
            <button
              type="button"
              ref={menuButton}
              className="mobile-menu-button"
              aria-controls="sidebar-menu"
              aria-expanded={menuOpen}
              aria-label={menuOpen ? "Đóng menu" : "Mở menu"}
              onClick={() => setMenuOpen((open) => !open)}
            >
              {menuOpen ? (
                <X size={23} aria-hidden="true" />
              ) : (
                <Menu size={23} aria-hidden="true" />
              )}
            </button>
          </div>
          <div className="sidebar-menu" id="sidebar-menu">
            <div className="workspace-label">KHÔNG GIAN HỌC TẬP</div>
            <nav aria-label="Điều hướng học tập">
              {links.map(({ to, title, icon: Icon }) =>
                to === "/groups" ? (
                  <span className="nav-soon" aria-disabled="true" key={to}>
                    <Icon size={19} aria-hidden="true" />
                    {title}
                    <small>Sắp có</small>
                  </span>
                ) : (
                  <NavLink key={to} to={to} onClick={closeMenu}>
                    <Icon size={19} aria-hidden="true" />
                    {title}
                  </NavLink>
                ),
              )}
            </nav>
            <div className="sidebar-tip">
              <span className="tiny-spark" aria-hidden="true">
                ✦
              </span>
              <h3>
                Small steps.
                <br />
                Big possibilities.
              </h3>
              <p>
                Hôm nay là một ngày tốt
                <br />
                để tiến thêm một bước.
              </p>
              <Link to="/catalog" onClick={closeMenu}>
                Tìm cảm hứng học <ArrowUpRight size={15} aria-hidden="true" />
              </Link>
            </div>
            <div className="sidebar-bottom">
              {user?.roles.some((x) =>
                ["Admin", "ContentEditor"].includes(x),
              ) ? (
                <NavLink to="/admin" onClick={closeMenu}>
                  <Shield size={18} aria-hidden="true" /> Quản trị nội dung
                </NavLink>
              ) : null}
              <NavLink to="/settings" onClick={closeMenu}>
                <Settings size={18} aria-hidden="true" /> Cài đặt tài khoản
              </NavLink>
              <button
                onClick={() => {
                  closeMenu();
                  logout().catch(setError);
                }}
              >
                <LogOut size={18} aria-hidden="true" /> Đăng xuất
              </button>
            </div>
          </div>
        </aside>
        <main
          className="main-content"
          id="main-content"
          ref={main}
          tabIndex={-1}
        >
          <header className="topbar">
            <span>
              Không gian của bạn{" "}
              <span className="breadcrumb">/ Học một chút mỗi ngày</span>
            </span>
            <div className="profile">
              <span className="online-dot" aria-hidden="true" />
              <span>{user?.displayName}</span>
              <span className="avatar" aria-hidden="true">
                {user?.displayName
                  .split(" ")
                  .map((x) => x[0])
                  .slice(-2)
                  .join("")}
              </span>
            </div>
          </header>
          <div className="page-content">
            <ErrorNotice error={error} />
            <Outlet />
          </div>
          <footer>
            englishhub. <span>Made for your next chapter.</span>
            <span>Nội dung demo tự biên soạn · Không phải đề ETS</span>
          </footer>
        </main>
      </div>
    </>
  );
}

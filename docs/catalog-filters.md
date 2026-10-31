# Catalog links / Liên kết thư viện có bộ lọc

VI: Trang bài học và bài luyện lưu lựa chọn trong URL, ví dụ `/catalog?skill=grammar&search=present` hoặc `/tests?skill=reading&search=Mini`. Tải lại, mở liên kết hoặc Back/Forward giữ đúng từ khóa và kỹ năng. Đăng nhập từ liên kết được bảo vệ cũng trở về nguyên đường dẫn, query và anchor.

EN: Typing replaces the current history entry; selecting a different skill adds an entry. Back therefore steps through deliberate skill/page choices rather than every character. Clearing a filter removes its parameter. Unsupported skill values are ignored for selection/API filtering, while unrelated parameters are retained. Search input and restored URL text are bounded to 200 characters. Sharing a link preserves a query, not a frozen result set; publication changes can change its results.

## Input and navigation

The input keeps a synchronous local draft while React Router updates the URL. An immediately updated pending-query reference merges rapid skill/typing intents. Internal filter navigations are marked in history state; POP and external route/deep-link navigation resynchronize from the URL. This prevents lost characters and a fast search from erasing a just-selected skill. Results still use deferred search and abort obsolete requests through the existing resource hook.

Query-only changes retain the current hash, typing focus and scroll position, including when navigation changes between PUSH and REPLACE. Full path/hash navigation retains the layout's route-focus and asynchronous-anchor behavior. Catalog links to content keep normal browser history, so browser Back restores the filtered list. The lesson's explicit “Trở lại bài học” link and the sidebar still open the general library deliberately.

## Observed checks

Browser checks on 17/11/2026 cover native rapid typing, immediate skill-then-typing, lesson open/Back, category Back/Forward, reload, switching between lesson/test routes, clearing filters, an unsupported skill, Unicode/ampersand encoding, and login returning to query plus hash. Native typing initially revealed dropped characters and rapid updates revealed a lost skill; both were corrected before the final build. No assessment was started/submitted and no profile/calendar was saved in this check.

TypeScript/Vite production builds pass. The backend is unchanged from the separately verified reinforcement feature (155 SQL/media and 149 SQLite tests); CI validates each subsequent head. IME composition on physical devices, screen readers and full automated cross-browser history coverage remain unverified.

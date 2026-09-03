# EnglishHub — Đặc tả sản phẩm và thiết kế kỹ thuật

> Tài liệu bàn giao để triển khai một nền tảng tự học tiếng Anh bằng .NET, ưu tiên chiều sâu backend và các tình huống database thực tế.

| Thuộc tính | Giá trị |
|---|---|
| Phiên bản | 1.0 |
| Ngày khảo sát và soạn | 09/11/2026, múi giờ Asia/Ho_Chi_Minh |
| Trạng thái | Đặc tả đề xuất để triển khai; chưa phải ứng dụng đã xây dựng |
| Đối tượng triển khai | Người học .NET và AI coding agent |
| Hướng sản phẩm | Tự học theo lộ trình, luyện kỹ năng, luyện đề, media, cộng đồng học tập |
| Mục tiêu kỹ thuật | .NET backend, SQL, transaction, concurrency, background jobs, lưu trữ media, realtime, tích hợp dịch vụ gọi trực tuyến |
| Quyết định đã chốt | AI là giai đoạn sau; P0–P3 hoạt động độc lập với AI |
| Tên EnglishHub | Tên làm việc của project, có thể đổi khi đặt tên repository |

## Mục lục

1. [Mục tiêu và quyết định phạm vi](#s01)
2. [Khảo sát sản phẩm tại Việt Nam](#s02)
3. [Danh mục tính năng và thứ tự triển khai](#s03)
4. [Người dùng, quyền và màn hình](#s04)
5. [Hành trình sử dụng xuyên suốt](#s05)
6. [Đặc tả lộ trình và lịch học](#s06)
7. [Đặc tả ngân hàng câu hỏi, luyện tập và thi thử](#s07)
8. [Đặc tả nghe, đọc, video, IPA và từ vựng](#s08)
9. [Đặc tả nhóm học, chat và gọi trực tuyến](#s09)
10. [Kiến trúc và stack](#s10)
11. [Mô hình dữ liệu](#s11)
12. [Bất biến dữ liệu và transaction](#s12)
13. [Upload, xử lý và phân phối media](#s13)
14. [HTTP API và hợp đồng dữ liệu](#s14)
15. [Hợp đồng realtime](#s15)
16. [Tích hợp cuộc gọi](#s16)
17. [Background jobs, outbox và phục hồi](#s17)
18. [Bảo mật và quyền truy cập](#s18)
19. [Hiệu năng, quan sát và vận hành](#s19)
20. [Ma trận tình huống kiểm thử](#s20)
21. [Dữ liệu mẫu và kịch bản demo CV](#s21)
22. [Backlog triển khai và điều kiện hoàn thành](#s22)
23. [Hướng dẫn cho AI triển khai](#s23)
24. [Điểm mở rộng AI sau này](#s24)
25. [Nguồn tham khảo](#s25)

<a id="s01"></a>
## 1. Mục tiêu và quyết định phạm vi

### 1.1. Vấn đề sản phẩm giải quyết

Người tự học thường có nhiều tài liệu nhưng thiếu một luồng nối liền: biết trình độ hiện tại, chọn việc nên học hôm nay, luyện một kỹ năng, hiểu lỗi sai, lưu kiến thức cần ôn và nhìn lại tiến bộ. EnglishHub cung cấp luồng đó, sau đó bổ sung không gian học chung để người học luyện nghe và giao tiếp với bạn bè.

Luồng trung tâm:

**Đánh giá đầu vào → chọn mục tiêu → lộ trình → nhiệm vụ hôm nay → học/luyện → chữa bài → ôn lại → đo tiến độ.**

### 1.2. Giả định triển khai được chọn

- Giao diện tiếng Việt; nội dung học tiếng Anh.
- Sản phẩm ban đầu tập trung TOEIC Listening & Reading và kiến thức nền tiếng Anh. Mô hình nội dung cho phép mở rộng loại chương trình học.
- Người dùng chính là sinh viên/người đi làm tự học; một người có thể vừa học một mình vừa tham gia nhóm.
- Một hệ thống dùng chung, chưa triển khai mô hình SaaS nhiều trung tâm/tên miền riêng.
- Nội dung ban đầu do chủ project biên soạn hoặc nhập từ nguồn có quyền sử dụng; hồ sơ nguồn gắn vào nội dung/media.
- Khởi đầu với backend một ứng dụng có các module rõ ràng và một worker dùng chung mã nguồn.
- Một tài khoản Learner và một tài khoản Admin là đủ để tự vận hành bản demo. Nhóm học/call có thể thử bằng các phiên trình duyệt và thiết bị khác nhau.
- P0–P3 dùng quy tắc xác định sẵn, dữ liệu biên soạn và chấm đáp án khách quan. AI tự sinh bài, STT, chấm phát âm và chữa viết thuộc P4.

### 1.3. Thế nào là project đủ tốt để đưa vào CV

1. Chạy được một hành trình học hoàn chỉnh với dữ liệu thật của project.
2. Có integration test chứng minh các bất biến quan trọng khi request chạy đồng thời hoặc được gửi lại.
3. Có quá trình upload và xử lý media thực, có lỗi và phục hồi được.
4. Có chat lưu bền, đồng bộ lại sau mất mạng; có quyền nhóm được kiểm tra tại backend.
5. Khi hoàn thành P3, gọi được giữa hai thiết bị trên hai mạng khác nhau.
6. Có tài liệu setup, API, migration, test, chỉ số đo thực và video demo.

### 1.4. Giới hạn phát hành

| Bản | Phạm vi có thể phát hành độc lập |
|---|---|
| P0 — Learning Core | Tài khoản, nội dung có phiên bản, lộ trình theo quy tắc, luyện Part/mini test/full mock, chấm bài, audio, lịch sử và quản trị nội dung |
| P1 — Media & Review | Dictation, video/phụ đề, ghi âm cá nhân, IPA, flashcards/SRS, sổ câu sai và phân tích kỹ năng |
| P2 — Study Together | Nhóm học, chat realtime lưu bền, đọc chưa đọc, thông báo, hiện diện và đồng bộ bài nghe trong phòng |
| P3 — Calls | Voice/video call 1–1 và nhóm tối đa 6 người qua media server; quản lý quyền, trạng thái, reconnect |
| P4 — AI | Gợi ý/chữa bài, STT, phát âm, cá nhân hóa sâu; đặc tả riêng trước khi bật |

Full scope là P0–P3. Một giai đoạn chỉ được đánh dấu hoàn tất sau khi đáp ứng tiêu chí nghiệm thu của chính giai đoạn đó. P4 không phải điều kiện hoàn thành project hiện tại.

<a id="s02"></a>
## 2. Khảo sát sản phẩm tại Việt Nam

### 2.1. Phương pháp và mức độ xác minh

Khảo sát có chọn lọc các nền tảng người dùng nêu và một số sản phẩm phục vụ người học Việt Nam. Ngày truy cập: 09/11/2026. Đây là đối chiếu tính năng công khai, không phải bảng xếp hạng thị phần hay xác nhận mức tăng trưởng.

- **UI:** đã xem giao diện công khai bằng trình duyệt.
- **Public:** đã đọc trang tính năng/khóa học/hướng dẫn chính thức.
- **Advertised:** nhà cung cấp giới thiệu tính năng; chưa kiểm thử toàn bộ hành vi sau đăng nhập.
- **Proposed:** quyết định thiết kế riêng của EnglishHub.

ByeTOEIC trả 403 với công cụ đọc web, nhưng truy cập được bằng trình duyệt thông thường. Đã xem trang chủ, danh sách module, trang luyện nghe và điểm yêu cầu đăng nhập. Đậu English cũng đã được xem qua giao diện trang chủ và luyện nghe. Khảo sát dừng tại nơi yêu cầu tài khoản; các chức năng có điều kiện đăng nhập được ghi đúng mức độ xác minh.

### 2.2. Đối chiếu các nền tảng

| Nền tảng | Tín hiệu tính năng đã đọc/quan sát | Mức xác minh | Bài học thiết kế cho EnglishHub |
|---|---|---|---|
| ByeTOEIC | Luyện đề LR, luyện từng Part, giải thích và lịch sử; nghe theo loại bài/độ khó; ngữ pháp, từ vựng, đọc; lộ trình nhiều chặng; liên kết đến shadowing, cộng đồng, messages và arena | UI + Advertised, [C01](https://byetoeic.com/), [C02](https://byetoeic.com/listening) | Một ngân hàng nội dung dùng lại cho nhiều cách luyện; nối kết quả bài làm với việc cần ôn |
| Đậu TOEIC / Đậu English | Nghe tách câu, lưu từ/ghi chú; đọc, ngữ pháp và ôn câu sai; thu âm và so bài mẫu; shadowing; video/lộ trình và tiến độ | UI + Public + Advertised, [C03](https://dauenglish.com/), [C04](https://dauenglish.com/listen/nghe-chep) | Nghe–đọc–từ vựng cùng một ngữ cảnh; ghi âm cá nhân là tính năng hữu ích ngay trước AI |
| STUDY4 | Thư viện đề; lộ trình và bài giảng; dictation; học từ vựng bằng flashcards; tạo thẻ từ highlights ở trang kết quả; thống kê theo dạng câu | Public + Advertised, [C05](https://study4.com/tests/), [C06](https://study4.com/lp/complete-toeic-v2/), [C07](https://study4.com/flashcards/) | Trang chữa bài là đầu mối để tra từ, ghi chú, lưu từ và tạo nhiệm vụ ôn |
| PREP | Kiểm tra đầu vào, study plan, bài giảng và Test Practice; kết nối học nền với luyện đề | Public + Advertised, [C08](https://prepedu.com/vi/toeic), [C09](https://prepedu.com/vi/toeic/test-practice) | Theo dõi mục tiêu/chặng học; phân biệt luyện tập và mô phỏng bài thi |
| VOCA | Lộ trình theo kỹ năng; phát âm IPA có lý thuyết, video, thực hành và kiểm tra | Public + Advertised, [C10](https://www.voca.vn/skills), [C11](https://www.voca.vn/guide/huong-dan-su-dung-he-thong-pronunciation-7) | IPA cần nội dung hướng dẫn, ví dụ, audio và bài phân biệt âm; dữ liệu IPA tách khỏi transcript |
| ProTOEIC | Tự học theo lộ trình, luyện các mảng kiến thức, xem lịch sử, lưu câu ôn và tra từ trong bài đọc; giới thiệu hỏi đáp/cộng đồng | Public + Advertised, [C12](https://protoeic.com/) | Kết nối mục tiêu, bài làm, câu sai và ôn tập thành vòng lặp có thể theo dõi |

Tên miền dautoeic.com chuyển hướng tới dauenglish.com tại thời điểm khảo sát. Các địa chỉ dautoeic.vn và dautoeic.com.vn cũng xuất hiện trong kết quả tìm kiếm với tên tương tự. Tài liệu chỉ xác nhận chuyển hướng đã quan sát; không suy luận các tên miền còn lại có cùng chủ sở hữu hay cùng hệ thống.

### 2.3. Ma trận tính năng chọn lọc

| Ý tưởng | Nguồn cảm hứng | EnglishHub quyết định |
|---|---|---|
| Đầu vào → mục tiêu → lộ trình ngày | ByeTOEIC, PREP, STUDY4 | P0; rule engine, có giải thích lý do chọn nhiệm vụ |
| Luyện theo Part/kỹ năng/độ khó | ByeTOEIC, Đậu English | P0; taxonomy và chọn nhóm câu hỏi nguyên vẹn |
| Full mock, mini test, chữa bài | ByeTOEIC, STUDY4, PREP | P0; đồng hồ server, autosave, phiên bản đề cố định |
| Nghe tách câu, dictation, transcript song ngữ | Đậu English, STUDY4 | P1; segment có timestamp và bản dịch biên soạn |
| Highlight → từ vựng → ôn tập | STUDY4, Đậu English | P1; lưu ngữ cảnh, nguồn và lịch ôn riêng mỗi người |
| IPA và video hướng dẫn | VOCA | P1; UK/US được gắn nhãn, nguồn biên soạn rõ ràng |
| Thu âm và so với bài mẫu | Đậu English | P1; nghe lại và tự đánh giá, chưa chấm phát âm |
| Chat nhóm, đồng bộ bài nghe, call | Yêu cầu của người dùng; thiết kế Proposed | P2/P3; không khẳng định các nền tảng khảo sát đều có các chức năng này |
| XP, streak, bảng xếp hạng nhóm | Tín hiệu công khai ở ByeTOEIC/Đậu English | P2 mở rộng; tính từ activity đã xác nhận tại server |
| AI, đa trung tâm, thu phí, affiliate | Một số trang có giới thiệu; ngoài phạm vi đầu | P4 hoặc tài liệu mở rộng; không kéo vào điều kiện P0–P3 |

Thiết kế kỹ thuật trong các phần sau là phương án cho EnglishHub. Không có căn cứ để kết luận các website được khảo sát sử dụng stack hay schema này.

<a id="s03"></a>
## 3. Danh mục tính năng và thứ tự triển khai

| ID | Tính năng | Bản | Mức bắt buộc |
|---|---|---|---|
| AUTH-01 | Đăng ký, đăng nhập, đăng xuất, xác minh email, quên mật khẩu | P0 | Must |
| PROF-01 | Mục tiêu, thời gian học, lịch học, múi giờ, hồ sơ tiến độ | P0 | Must |
| CAT-01 | Danh mục khóa/chặng/bài, tìm và lọc nội dung | P0 | Must |
| CMS-01 | Soạn, preview, publish, archive; nội dung có phiên bản | P0 | Must |
| PATH-01 | Đánh giá đầu vào hoặc bắt đầu từ nền tảng | P0 | Must |
| PATH-02 | Lộ trình và danh sách việc học theo ngày | P0 | Must |
| PRA-01 | Luyện Part, luyện theo kỹ năng/tag và mini test | P0 | Must |
| EXAM-01 | Full mock, autosave, hết giờ, submit và chấm khách quan | P0 | Must |
| REV-01 | Chữa bài, lời giải, bookmark, lịch sử lần làm | P0 | Must |
| MEDIA-01 | Upload audio, xác minh, xử lý nền, phát có quyền | P0 | Must |
| DASH-01 | Tổng quan học tập, thống kê đúng/sai theo kỹ năng | P0 | Must |
| LIST-01 | Dictation, điền từ, nghe hiểu, nghe lại đoạn | P1 | Must |
| VIDEO-01 | Video bài học, phụ đề theo đoạn, resume, tốc độ phát | P1 | Must |
| VOC-01 | Từ điển nội bộ, bộ từ, lưu từ trong ngữ cảnh, flashcards | P1 | Must |
| VOC-02 | Lịch ôn SRS, lịch sử review, giải quyết xung đột nhiều thiết bị | P1 | Must |
| IPA-01 | Bảng âm, khẩu hình/video, ví dụ, minimal pairs | P1 | Must |
| REC-01 | Ghi âm cá nhân, upload, phát lại, so audio mẫu, tự đánh giá | P1 | Must |
| REV-02 | Sổ câu sai và phiên ôn lại không làm sai lệch điểm thi thử | P1 | Must |
| GROUP-01 | Tạo nhóm, lời mời, xin tham gia, quyền host/member | P2 | Must |
| CHAT-01 | Chat nhóm, đính kèm, reply, sửa/xóa, unread, pagination | P2 | Must |
| RT-01 | Online/typing, reconnect, đồng bộ lại dữ liệu bị lỡ | P2 | Must |
| ROOM-01 | Phòng học nghe chung, host điều khiển playback | P2 | Must |
| NOTI-01 | Thông báo trong app, lịch học và sự kiện nhóm | P2 | Must |
| GAME-01 | Streak, XP và bảng xếp hạng nhóm tuần | P2 | Should |
| CALL-01 | Voice/video call, tối đa 6 người, thiết bị, mute, leave, reconnect | P3 | Must |
| CALL-02 | Host mời ra/đóng phòng, đồng bộ trạng thái và webhook idempotent | P3 | Must |
| AI-00 | AI tutor, STT, chấm nói/viết, sinh nội dung | P4 | Deferred |

Must áp dụng trong bản tương ứng, không đồng nghĩa tất cả phải có trước khi phát hành P0. Should không chặn nghiệm thu bản tương ứng nhưng phải được ghi trạng thái thực tế trong README.

<a id="s04"></a>
## 4. Người dùng, quyền và màn hình

### 4.1. Vai trò

| Vai trò | Quyền |
|---|---|
| Guest | Xem landing, danh mục và bài preview; tạo lộ trình nháp cục bộ nếu UI hỗ trợ |
| Learner | Học, làm bài, lưu từ/ghi chú, xem dữ liệu cá nhân; tạo và tham gia nhóm |
| ContentEditor | Soạn nháp, upload media, preview nội dung; không tự cấp quyền quản trị |
| Admin | Quản lý tài khoản/nội dung, publish, archive, xem trạng thái job và xử lý báo cáo |
| GroupOwner / Moderator / Member | Quyền trong từng nhóm; tách khỏi role toàn hệ thống |
| RoomHost / Participant | Quyền trong từng phiên phòng học/call; không suy ra từ role Admin |

Một người có thể có nhiều vai trò. Quyền được kiểm tra từ tài nguyên và membership hiện hành tại backend. Biết ID nhóm, ID bản ghi hay tên SignalR group không tạo quyền truy cập.

### 4.2. Danh sách màn hình

| Route giao diện | Nội dung chính |
|---|---|
| / | Giới thiệu, trải nghiệm preview, đăng nhập |
| /onboarding | Mục tiêu, ngày dự kiến, phút/ngày, múi giờ, đánh giá đầu vào |
| /dashboard | Việc hôm nay, từ đến hạn ôn, tiếp tục bài đang học, tiến độ |
| /paths và /paths/:id | Chặng học, điều kiện mở khóa, lịch ngày/tuần, lý do nhiệm vụ |
| /learn/:lessonId | Bài học, media, transcript, bài tập và ghi chú |
| /practice | Chọn Part, kỹ năng, độ khó, số câu/nhóm câu |
| /tests và /tests/:id | Bộ đề, phiên bản, số câu, thời lượng, chế độ |
| /attempts/:id | Làm bài, trạng thái lưu, đồng hồ server, đánh dấu câu |
| /attempts/:id/result | Điểm raw, đúng/sai, giải thích, thống kê và lưu kiến thức |
| /review/mistakes | Câu cần ôn, lọc tag/Part, bắt đầu phiên ôn |
| /vocabulary và /review/cards | Bộ từ, thẻ cá nhân và phiên ôn |
| /pronunciation | IPA, ví dụ, cặp âm, ghi âm tự nghe |
| /groups và /groups/:id | Thành viên, hoạt động, chat và phòng học |
| /rooms/:id | Bài nghe chung, chat, trạng thái host, nút tham gia call |
| /settings | Hồ sơ, múi giờ, thiết bị, thông báo và dữ liệu cá nhân |
| /admin/* | CMS, import, media/jobs, publish và báo cáo nội dung |

Mỗi màn hình phải có loading, empty, lỗi có thể thử lại, lỗi quyền truy cập và trạng thái mất mạng nếu có thao tác ghi. Nút thuộc giai đoạn chưa triển khai không được trình bày như chức năng đã hoạt động.

<a id="s05"></a>
## 5. Hành trình sử dụng xuyên suốt

### J01 — Người mới có lộ trình và hoàn thành ngày đầu

1. Đăng ký/xác minh hoặc dùng tài khoản demo đã chuẩn bị.
2. Chọn mục tiêu 450/650/800 hoặc mục tiêu nền tảng; nhập 20/30/45 phút mỗi ngày và các ngày có thể học.
3. Làm placement mini test, hoặc chọn bắt đầu từ căn bản.
4. Backend gán template phù hợp bằng rule version hiện hành, tạo enrollment và nhiệm vụ ngày đầu.
5. Dashboard hiển thị tối đa ba nhiệm vụ cốt lõi, tổng thời lượng dự kiến và lý do chọn.
6. Hoàn thành bài và quiz; nhiệm vụ chuyển trạng thái một lần; sự kiện được dùng để cập nhật dashboard.

Nghiệm thu: refresh/đăng nhập lại vẫn có cùng enrollment và các nhiệm vụ đã hoàn thành; gửi lại sự kiện không cộng tiến độ hai lần.

### J02 — Luyện theo Part rồi quay lại lỗi sai

Chọn Part/tag → backend chọn các nhóm câu phù hợp → lưu snapshot phiên luyện → autosave → nộp → chấm theo phiên bản đáp án → xem lời giải → lưu câu cần ôn/từ vựng → tạo phiên ôn riêng.

Nghiệm thu: câu trả lời và lời giải không xuất hiện trong payload đề đang làm; phiên ôn không ghi đè kết quả lần thi ban đầu.

### J03 — Nghe một đoạn và lưu từ theo ngữ cảnh

Mở lesson → phát audio → loop đoạn → điền từ hoặc nghe chép → kiểm tra với transcript biên soạn → chọn từ → xem IPA/nghĩa → lưu thẻ kèm câu nguồn → thẻ xuất hiện trong lịch ôn cá nhân.

Nghiệm thu: lưu một từ từ hai ngữ cảnh không làm mất hai nguồn; refresh vẫn giữ vị trí nghe và các đoạn đã hoàn thành.

### J04 — Thi thử có mất mạng

Bắt đầu full mock → lưu từng thay đổi → mất mạng → UI giữ bản nháp chưa được server xác nhận và hiển thị rõ trạng thái → kết nối lại → tải trạng thái và thời gian còn lại từ server → áp dụng các thay đổi còn hợp lệ → nộp hoặc tự kết thúc khi hết giờ.

Nghiệm thu: đồng hồ vẫn chạy trong thời gian mất mạng; đáp án gửi sau deadline bị từ chối; phần chưa đồng bộ không được hiển thị như đã nộp thành công.

### J05 — Học chung rồi gọi luyện nói

Tạo nhóm → thành viên dùng lời mời → chat → host tạo phòng gắn bài nghe → mọi người theo playback của host → tham gia call → bật/tắt mic/camera → rời phòng → lịch sử chat và phiên học vẫn còn.

Nghiệm thu: thành viên bị loại mất quyền gửi/nhận nội dung mới và quyền xin token call; việc đóng phiên không xóa lịch sử học.

<a id="s06"></a>
## 6. Đặc tả lộ trình và lịch học

### PATH-01 — Đánh giá và chọn template

- Placement bản đầu gồm 20 câu biên soạn, có cả listening và reading, gắn tag kỹ năng; không coi đây là chứng chỉ trình độ hay quy đổi điểm TOEIC chính thức.
- Ngưỡng gợi ý ban đầu: 0–7 câu đúng chọn Foundation; 8–14 chọn Core; 15–20 chọn Exam Practice. Đây là quy tắc demo của sản phẩm, phải lưu cấu hình/version và có thể điều chỉnh.
- Kỹ năng có dưới 5 quan sát được hiển thị là “chưa đủ dữ liệu”, không gán đánh giá mạnh/yếu chắc chắn.
- Người dùng có thể đổi template thủ công. Lịch sử placement và lý do lựa chọn được giữ lại.
- Bỏ placement thì gợi ý Foundation và ghi rõ cơ sở là lựa chọn của người học.

### PATH-02 — Cấu trúc

PathTemplate → PathVersion → PathStage → PathNode. Mỗi node trỏ tới một ContentVersion hoặc một PracticeBlueprint có thời lượng ước tính, kỹ năng và điều kiện hoàn thành.

- Template được publish là immutable. Enrollment ghim vào một TemplateVersion.
- Prerequisite tạo thành đồ thị không chu trình; CMS kiểm tra cycle trước publish.
- Node có thể yêu cầu hoàn thành node trước hoặc đạt ngưỡng đúng ở quiz; điều kiện được cấu hình rõ theo node.
- Một node nghe được tính hoàn thành bằng bài tập/quiz gắn với node hoặc thao tác xác nhận tự học theo cấu hình, không chỉ dựa vào việc tua đến cuối media.

### PATH-03 — Bộ lập lịch theo quy tắc

Đầu vào: enrollment, ngày học theo timezone, ngân sách phút/ngày, node đã hoàn thành, thẻ đến hạn ôn, kỹ năng có dữ liệu và task còn dang dở.

Thứ tự lựa chọn:

1. Task đang làm dở còn phù hợp với enrollment hiện hành.
2. Ôn đến hạn, dành tối đa 30% ngân sách phút; phần dư vẫn có thể học thêm tự chọn.
3. Một node nền tảng đã mở khóa hoặc một bài kỹ năng cần cải thiện.
4. Một practice ngắn nếu còn ngân sách; lựa chọn whole question group.

Mỗi task trả reasonCode và reasonText, ví dụ DUE_REVIEW hoặc WEAK_SKILL. Tối đa ba task cốt lõi/ngày, phần luyện thêm nằm riêng. Nếu không có nội dung thích hợp thì trả trạng thái thiếu nội dung và gợi ý lựa chọn có sẵn; không sinh ID bài giả.

- Sinh task idempotent theo EnrollmentId + LocalDate + SlotKey.
- Regenerate chỉ thay task chưa bắt đầu; giữ task đang làm/đã xong.
- Task bỏ lỡ được đánh dấu missed. Ngày tiếp theo chọn lại nếu còn phù hợp, gắn liên kết nguồn; không nhân bản vô hạn backlog.
- Đổi mục tiêu tạo PlanRevision có hiệu lực cho các ngày tới; dữ liệu đã học vẫn giữ.
- Sự kiện hoàn thành task và thay đổi tiến độ dùng khóa duy nhất theo task và nguồn kết quả.

### PATH-04 — Thống kê

- Hiển thị tỉ lệ đúng theo Part/tag, số câu quan sát, số lần luyện, thời gian hoạt động và xu hướng.
- Tách full mock chưa xem đáp án với practice có gợi ý/ôn lại.
- Độ chính xác kỹ năng lấy theo tối đa 50 câu hợp lệ gần nhất; hiện số mẫu bên cạnh.
- Bài đã lộ đáp án, fixture kiểm thử và lần ôn lặp không tham gia ước lượng thành tích full mock.
- Mục tiêu điểm là ý định của người dùng. Bản đầu hiển thị raw score và mức hoàn thành; chưa tự tạo bảng quy đổi 990.

<a id="s07"></a>
## 7. Đặc tả ngân hàng câu hỏi, luyện tập và thi thử

### 7.1. Taxonomy

ContentProgram gồm GeneralEnglish và ToeicLR. Skill gồm Listening, Reading, Vocabulary, Grammar, Pronunciation. ToeicPart là giá trị 1–7 cho nội dung phù hợp. SkillTag chi tiết, ví dụ main_idea, inference, noun_form, tense, synonyms, numbers.

Mỗi câu thuộc một QuestionGroup. Group có stimulus dùng chung: audio, hình, đoạn văn hoặc nhiều đoạn văn. Câu trong cùng hội thoại/đoạn đọc được chọn và xáo trộn ở cấp group để giữ ngữ cảnh.

QuestionType bản đầu: SingleChoice và FillBlank. SpeakingRecording và FreeWriting là bài luyện tự đánh giá, không chấm khách quan vào raw score LR.

Stimulus dùng ContentBlock có blockKey ổn định và type text/image/table/audio_reference. Một group Part 7 có thể chứa nhiều tài liệu; không ghép mất ranh giới email/thông báo/bảng biểu. Explanation có thể chứa EvidenceAnchor theo blockKey và khoảng ký tự của đúng version để UI tô sáng dẫn chứng. Với blueprint full mock, Part 2 có ba lựa chọn và câu hỏi nằm trong audio; các Part trắc nghiệm khác có bốn lựa chọn theo cấu hình định dạng. Validator xác minh loại media bắt buộc, số option và kích thước group của từng Part trước publish.

### PRA-01 — Tạo phiên luyện

Đầu vào: program, Part/tag, difficulty, questionCount mục tiêu, chế độ có/không tính giờ.

- Chọn ngẫu nhiên có seed; kết quả chọn được lưu ngay khi tạo attempt.
- Giữ nguyên group và thứ tự câu trong group. Số câu thực tế có thể lệch mục tiêu để giữ group; UI báo số thực tế trước bắt đầu.
- Không chọn lại cùng câu trong một attempt. Có tùy chọn ưu tiên câu chưa gặp hoặc cần ôn.
- Không đủ câu thì trả số sẵn có và yêu cầu xác nhận cấu hình mới; không âm thầm lặp câu.
- Attempt ghim QuestionVersion, GroupVersion, đáp án chấm và grading policy phía server.
- Đáp án/lời giải được trả ở API result, theo chính sách disclosure; payload bắt đầu làm bài chỉ chứa nội dung cần làm.

### EXAM-01 — Định dạng bài thi

TOEIC LR dạng đầy đủ tham khảo gồm 100 câu Listening và 100 câu Reading, với khoảng 45 phút Listening và 75 phút Reading. Cấu trúc tổng thể theo [ETS, T01](https://www.ets.org/content/ets-org/ca/en/toeic/test-takers/about/listening-reading.html).

| Part | Dạng bài trong blueprint đầy đủ | Số câu cấu hình |
|---|---|---:|
| 1 | Mô tả hình ảnh | 6 |
| 2 | Hỏi–đáp | 25 |
| 3 | Hội thoại | 39 |
| 4 | Bài nói ngắn | 30 |
| 5 | Câu chưa hoàn chỉnh | 30 |
| 6 | Hoàn thành văn bản | 16 |
| 7 | Đọc hiểu | 54 |

Các con số trên là blueprint được cấu hình cho bản mô phỏng. CMS phải kiểm tra tổng theo section, group và media trước publish. Có thể tạo mini test với định dạng/thời lượng riêng. UI phân biệt rõ mini test, practice và full mock.

### EXAM-02 — Vòng đời attempt

Trạng thái bền vững: Created → InProgress → Graded; InProgress cũng có thể chuyển Abandoned. Finalization và chấm raw score diễn ra trong cùng transaction. Graded lưu FinalizationReason = manual_submit hoặc deadline; với deadline chỉ chấm câu đã được server xác nhận trước hạn. Các tên “submitted/expired” dùng ở event/metric mô tả nguyên nhân, không phải hai trạng thái trung gian có thể bị bỏ dở. Sau finalization, đáp án đóng băng.

- Backend xác định StartedAt, SectionStartedAt và DeadlineAt theo UTC; gửi ServerNow để client vẽ đồng hồ.
- Practice cho phép pause nếu blueprint bật. Full mock không pause; refresh, đổi tab hoặc mất mạng không gia hạn.
- Khi Listening kết thúc, server đóng section rồi mở Reading theo thời điểm cấu hình. Job có thể chạy trễ nhưng deadline logic không thay đổi.
- Chuyển sang Reading sớm chỉ được phép nếu ExamPolicy cho phép; bản đầu full mock không cho chuyển sớm.
- Trong full mock, UI khóa seek/pause phần listening theo chính sách mô phỏng. Đây là hành vi học tập của ứng dụng, không phải cơ chế giám sát thi có chứng nhận.

### EXAM-03 — Autosave và submit

- Mỗi answer có revision/ETag; cập nhật yêu cầu If-Match và ClientRequestId.
- Client autosave sau 500 ms không gõ/chọn thêm; gộp tối đa 20 thay đổi/request, bắt buộc hiển thị trạng thái đang lưu/đã lưu/chưa đồng bộ.
- Server xác minh attempt thuộc người dùng, đang mở, câu thuộc snapshot và section còn hạn.
- Kết quả autosave gồm acknowledged request IDs, version mới và thời gian server.
- Submit chờ các thay đổi đang gửi được xác nhận; cho phép gửi một final answer batch trong cùng transaction với submit, có kiểm tra version.
- AcceptedAt là thời điểm đồng hồ server được đọc sau khi lấy khóa attempt trong transaction; AcceptedAt tại hoặc sau DeadlineAt không được thêm đáp án mới. Thời điểm client gửi hoặc request vừa vào hàng đợi không tự giữ quyền nộp trước hạn. Job hết giờ dùng cùng logic finalization với submit.
- Hai submit đồng thời trả về cùng kết quả logic; cùng Idempotency-Key nhưng payload khác trả 409.

### EXAM-04 — Chấm và chữa bài

- SingleChoice chấm bằng OptionId của QuestionVersion, không tin chữ A/B/C/D client gửi.
- FillBlank dùng AcceptedAnswerVariants biên soạn và NormalizationPolicyVersion.
- Quy tắc mặc định: Unicode NFC, trim, chuẩn hóa apostrophe cong, gộp whitespace, không phân biệt hoa thường; dấu câu cuối câu có thể bỏ theo cấu hình. Chưa dùng fuzzy matching để quyết định đúng/sai.
- Câu chưa trả lời là sai khi tính raw score. Lưu CorrectCount, TotalCount, BlankCount, ListeningRaw, ReadingRaw.
- ScoreCalculationVersion và ContentVersion được lưu với kết quả.
- Nếu muốn thêm điểm 990 sau này, dùng một ScoringTableVersion có nguồn, được Admin publish và nhãn “ước lượng”; không nội suy tùy ý thành điểm ETS chính thức.
- Trang chữa bài có: lựa chọn của người dùng, đáp án, lời giải tiếng Việt, stimulus/transcript đã được phép mở, tag và nút lưu từ/câu sai.

### REV-01 — Câu sai và ghi chú

- MistakeItem gắn UserId + QuestionVersionId; lưu lần sai gần nhất, số lần sai và lịch ôn. Mỗi lần trả lời vẫn nằm trong AttemptAnswer gốc.
- Một lần ôn đúng không xóa lịch sử sai. Trạng thái “đã nắm” theo quy tắc công bố, mặc định hai lần ôn đúng ở hai ngày học khác nhau.
- Highlight/Note lưu Anchor theo ContentVersion, block/segment và khoảng ký tự; nội dung nguồn thay đổi tạo version mới.
- Xóa ghi chú riêng không làm thay đổi attempt hay nội dung dùng chung.

<a id="s08"></a>
## 8. Đặc tả nghe, đọc, video, IPA và từ vựng

### LIST-01 — Các chế độ nghe

| Chế độ | Hành vi | Cách lưu |
|---|---|---|
| Listen & Understand | Nghe đoạn, trả lời câu hỏi hiểu nội dung | Attempt + Answers |
| Fill the Blanks | Điền các token được biên soạn trong transcript | ListeningSession + SegmentAttempt |
| Dictation | Gõ cả câu; sau kiểm tra hiển thị token thiếu/thừa/khác | SegmentAttempt + normalization version |
| Shadowing | Phát mẫu → ghi âm → nghe lại; có transcript/IPA khi được cấp | Recording + SelfAssessment |

- TranscriptSegment có StartMs, EndMs, TextEn, TextVi, thứ tự và source version.
- Phải thỏa 0 ≤ StartMs < EndMs ≤ DurationMs. Một track subtitle có thể cho overlap có chủ đích; dictation segment mặc định không overlap.
- Loop một câu, chuyển câu, chỉnh tốc độ 0.75/1/1.25/1.5 và ẩn/hiện transcript ở chế độ học.
- Reveal transcript/gợi ý được ghi vào session; tỉ lệ đúng có trợ giúp hiển thị riêng.
- Vị trí phát là trạng thái tiếp tục học, không tự tạo bằng chứng hoàn thành bài.
- So sánh token dictation dùng thuật toán edit distance để hiển thị lỗi; việc chấm vẫn theo accepted variants và normalization policy.

### VIDEO-01 — Video và phụ đề

- Lesson hỗ trợ video tự lưu có quyền sử dụng và ExternalEmbed theo provider allowlist.
- P0 audio/MP4 phát qua HTTP có hỗ trợ range; P1 bổ sung rendition HLS cho video dài.
- Phụ đề nhập SRT/VTT được kiểm tra, chuẩn hóa thành segment theo millisecond.
- Nhấp một câu phụ đề để seek, bật loop câu, hiển thị bản dịch và lưu từ trong ngữ cảnh.
- Resume lưu theo PlaybackSession; một phiên cũ không được ghi đè vị trí của phiên mới đã được người dùng chọn làm phiên hiện hành.
- Completion gồm điều kiện bài học cấu hình, ví dụ xem ít nhất 80% các khoảng thời gian khác nhau và hoàn thành quiz. Nhịp play do client báo chỉ là tín hiệu sản phẩm, không phải chứng cứ giám sát.
- Video ngoài dùng embed chính thức; lỗi hoặc bị gỡ hiển thị trạng thái nội dung không khả dụng, có quy trình thay nguồn/version.

### IPA-01 — Phiên âm và phát âm

- IPA là chuỗi Unicode biên soạn; transcript là nội dung lời nói. Hai dữ liệu được lưu riêng.
- PronunciationVariant gồm Accent = en-GB/en-US, IpaText, AudioAssetId, SourceCitation và trạng thái duyệt.
- PhonemeLesson có ký hiệu, mô tả khẩu hình tiếng Việt, hình/video, từ ví dụ và bài minimal pairs.
- Không áp đặt một danh sách “44 âm” cho mọi accent như một chân lý của schema; danh sách âm thuộc một CurriculumVersion có accent và nguồn.
- Audio nghe mẫu, IPA và từ ví dụ phải cùng accent hoặc được gắn nhãn khác biệt rõ ràng.
- Ghi âm tự luyện không nhận điểm phát âm máy ở P0–P3. Người dùng có thể tự đánh dấu khó/dễ và ghi chú.

### VOC-01 — Từ vựng theo ngữ cảnh

- DictionaryEntry biểu diễn headword; WordSense tách loại từ/nghĩa/ví dụ; PronunciationVariant gắn entry và accent.
- UserCard lưu mặt trước/mặt sau do người dùng chọn, WordSense nếu có, ngữ cảnh nguồn và owner.
- Lưu cùng sense nhiều lần trả card hiện có và bổ sung CardSourceContext, tránh mất ngữ cảnh mới.
- Cho phép tạo thẻ cụm từ/tự viết khi chưa có dictionary entry; không giả một nghĩa được suy luận tự động.
- Deck dùng chung có DeckVersion; bản học cá nhân có trạng thái riêng, không dùng trạng thái SRS chung giữa nhiều người.

### VOC-02 — Ôn tập xác định sẵn

SRS v1 là lịch ôn đơn giản của sản phẩm, không quảng bá như mô hình trí nhớ đã được kiểm định.

| Rating | Quy tắc v1 |
|---|---|
| Again | DueAt = now + 10 phút; đặt lại bước học về 0 |
| Hard | Nếu mới: +1 ngày; nếu đã có interval: max(1, round(interval × 1.2)) ngày |
| Good | Ba lần thành công đầu: 1, 3, 7 ngày; sau đó round(interval × 2) ngày |
| Easy | Nếu mới: +4 ngày; nếu đã có interval: max(4, round(interval × 2.5)) ngày |

- Interval tối đa 180 ngày; làm tròn theo quy tắc AwayFromZero cho giá trị dương.
- Again đặt SuccessStep về 0; Hard giữ nguyên SuccessStep; Good tăng SuccessStep một bước; Easy tăng một bước và dùng interval tương ứng. IntervalDays và SuccessStep luôn lưu trước/sau trong CardReview.
- DueAt lưu UTC. Phiên ôn hôm nay xét các thẻ DueAt ≤ now; không đợi đổi ngày địa phương.
- Review yêu cầu ReviewEventId và ExpectedRowVersion. Event + thay đổi lịch + outbox cùng transaction.
- Hai thiết bị review cùng version: chỉ một review được chấp nhận; thiết bị còn lại nhận 412 và tải trạng thái mới. Không tự động nhận cả hai như hai lần nhớ liên tiếp.
- Lưu SchedulerVersion với review để kiểm tra và thay thuật toán sau này.

### REC-01 — Ghi âm cá nhân

- Ghi âm bằng trình duyệt, hiển thị thời lượng và trạng thái mic, nghe thử trước khi lưu.
- Mỗi bản ghi tối đa 3 phút và 20 MiB trong cấu hình demo; backend kiểm tra thực tế sau upload.
- Mặc định private; chỉ chia sẻ vào nhóm bằng thao tác rõ ràng và kiểm tra quyền nhóm.
- Cho phép xóa bản ghi cá nhân. Metadata tự đánh giá có thể giữ nhưng không còn URL phát sau khi xóa.
- Media format phụ thuộc trình duyệt; worker chuẩn hóa rendition nghe lại, lưu format gốc phục vụ debug có giới hạn thời gian.

<a id="s09"></a>
## 9. Đặc tả nhóm học, chat và gọi trực tuyến

### GROUP-01 — Nhóm học

- Nhóm có owner, tên, mô tả, visibility = private/public và giới hạn mặc định 50 thành viên.
- Public cho phép xem mô tả; nội dung chat/media chỉ dành cho thành viên được chấp thuận.
- Private tham gia qua invite token hoặc request được duyệt. Invite token lưu hash, expiry, giới hạn lượt dùng và trạng thái thu hồi.
- Mọi lượt redeem invite đều kiểm tra điều kiện và tăng số lượt trong cùng transaction với membership.
- Một người có một Membership record duy nhất mỗi nhóm; rời rồi tham gia lại thay lifecycle/version của record đó.
- Owner muốn rời phải chuyển quyền trước. Transfer ownership dùng optimistic concurrency và ràng buộc bảo đảm có đúng một owner của nhóm đang hoạt động.
- Owner/moderator có thể loại thành viên; thao tác cập nhật quyền là nguồn sự thật cho chat, attachment, playback và call.

### CHAT-01 — Tin nhắn bền vững

- Tin nhắn text tối đa 4.000 ký tự; attachment tối đa ba asset đã Ready, tổng 20 MiB theo cấu hình.
- Hỗ trợ text, ảnh, audio ghi âm và liên kết đến bài học. Video cá nhân/đính kèm tùy ý là mở rộng sau.
- Có reply một cấp tới MessageId trong cùng nhóm; payload trả preview đã kiểm tra quyền.
- Client tự tạo ClientMessageId cho mỗi tin. Gửi lại cùng ID và cùng payload trả cùng MessageId/sequence.
- Lưu DB trước khi broadcast. ACK gửi cho client sau commit, gồm MessageId và GroupSequence.
- Sau mất mạng, client hỏi lịch sử sau lastReceivedSequence; SignalR chỉ là kênh cập nhật nhanh.
- GroupSequence là số tăng trong phạm vi nhóm; cấp trong cùng transaction với insert, có unique index GroupId + Sequence. Có thể có khoảng trống sau sửa đổi vận hành; client không giả định luôn liền tuyệt đối.
- Lấy lịch sử bằng cursor theo sequence, tối đa 50 tin/lần. Không dùng offset lớn cho lịch sử dài.
- Người gửi sửa trong 15 phút; phiên bản và EditedAt được lưu. Admin/moderator có thể ẩn tin vi phạm với audit.
- Xóa tin tạo tombstone để giữ chuỗi hội thoại, reply và vị trí đọc. Attachment chỉ được dọn khi hết tham chiếu còn hiệu lực.
- Read cursor lưu LastReadSequence, cập nhật bằng max để tránh thiết bị cũ làm tăng unread trở lại.
- Unread tính theo tin hiển thị sau cursor, loại tin tự gửi/tombstone theo policy v1; không đơn thuần lấy LastSequence trừ LastReadSequence.

### RT-01 — Hiện diện

- User có thể có nhiều connection/thiết bị. Online khi còn ít nhất một lease hợp lệ.
- Heartbeat presence mỗi 20 giây, TTL 60 giây; typing TTL 5 giây, không ghi SQL từng lần gõ.
- Dùng Redis ở P2 để quản lý presence và SignalR backplane khi chạy nhiều instance; SQL giữ membership/chat/history.
- Reconnect phải xác thực lại quyền và tham gia lại các nhóm được phép. SignalR group không thay thế authorization và membership bền vững; xem [T06](https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-10.0).

### ROOM-01 — Phòng nghe chung

- Một nhóm có nhiều lịch sử phiên nhưng chỉ một StudyRoom active trong cấu hình ban đầu.
- Host chọn một LessonVersion/media đã được nhóm truy cập. Thành viên thường theo playback; có thể tắt follow để tự xem và bật lại.
- RoomPlaybackState lưu media/version, paused, positionMs, playbackRate, updatedAtServer, revision và hostUserId.
- Lệnh play/pause/seek/change lesson phải đến từ host, có CommandId và ExpectedRevision.
- Server tính vị trí hiện hành từ trạng thái và thời gian; client mới vào nhận snapshot rồi bù độ trễ.
- Chỉ ghi SQL khi có lệnh trạng thái, không ghi từng frame. Redis/presence giữ tín hiệu ngắn hạn.
- Host mất kết nối: chờ grace period 60 giây; sau đó moderator/owner có thể claim host qua compare-and-swap. Không để mọi client tự nhận quyền host.
- Đóng room kết thúc đồng bộ playback/call đang gắn với room, giữ lịch sử chat và metadata phiên.

### CALL-01 — Phạm vi voice/video

- Hỗ trợ call 1–1 và nhóm từ 2 đến tối đa 6 người, bắt đầu bằng audio; camera là tùy chọn.
- Dùng LiveKit SFU cho media và signaling của cuộc gọi; ASP.NET Core quản lý room, membership, token, giới hạn người và lịch sử phiên.
- Chat/notification/playback của sản phẩm tiếp tục qua SignalR. Không truyền frame audio/video liên tục qua hub ứng dụng.
- Màn hình trước khi vào: chọn mic/camera, nghe/nhìn thử tại thiết bị; trạng thái từ chối quyền rõ ràng.
- Trong call: mute/unmute bản thân, bật/tắt camera, chọn thiết bị, danh sách người tham gia, trạng thái reconnect, rời phòng.
- Host có thể mời người ra/đóng call; mute từ xa nếu có chỉ hạ quyền hoặc tắt track, không tự bật mic của người khác.
- Bản P3 không ghi hình/ghi âm cuộc gọi. Ghi âm cá nhân REC-01 là luồng tách biệt.
- Chi tiết token, giữ chỗ và xử lý webhook ở mục 16.

<a id="s10"></a>
## 10. Kiến trúc và stack

### 10.1. Quyết định kiến trúc

| Hạng mục | Quyết định |
|---|---|
| Backend | ASP.NET Core 10 Web API, C#, EF Core 10; chọn bản patch ổn định khi khởi tạo và khóa version |
| Database | SQL Server 2022 Developer cho local/test; schema tương thích bản SQL Server được dùng khi deploy |
| Cấu trúc | Modular monolith; API và Worker là hai process từ cùng solution |
| Frontend | React + TypeScript + Vite, chọn phiên bản ổn định khi khởi tạo và commit lockfile |
| Auth | ASP.NET Core Identity; cookie HttpOnly, same-origin, antiforgery cho thao tác ghi |
| Object storage | Azure Blob Storage; local/test dùng Azurite |
| Jobs/outbox | SQL-backed durable queue và worker có lease, retry, idempotency |
| Media processor | FFmpeg/ffprobe chạy trong worker container giới hạn tài nguyên |
| Realtime | ASP.NET Core SignalR; Redis/backplane từ P2 |
| Calls | LiveKit self-host local; cấu hình deployment riêng cho môi trường ngoài internet |
| Tests | xUnit, integration test với SQL Server thực/container và Azurite; test frontend và trình duyệt theo các luồng cốt lõi |
| Quan sát | Structured logging, OpenTelemetry, metrics; health endpoints |
| Runtime local | Docker Compose theo profile core/social/calls |

.NET 10 là lựa chọn LTS tại thời điểm soạn; tham khảo [T02](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Azurite hỗ trợ phát triển Azure Storage cục bộ, xem [T03](https://learn.microsoft.com/en-us/azure/storage/common/storage-install-azurite). Patch và image digest được chốt khi triển khai thay vì ghi một số version dễ lỗi thời vào đặc tả.

### 10.2. Sơ đồ

~~~mermaid
flowchart LR
    B[Browser / React] -->|HTTPS REST| A[ASP.NET Core API]
    B <-->|SignalR: chat / notification / playback| A
    A --> D[(SQL Server)]
    A --> O[(Private Blob Storage)]
    B -->|Scoped upload grant: staging| O
    W[Worker: jobs / outbox / media] --> D
    W --> O
    W --> F[FFmpeg process]
    W -->|Committed realtime events| A
    A --> R[(Redis: P2)]
    A -->|Room policy and access token| L[LiveKit SFU: P3]
    B <-->|WebRTC audio / video| L
~~~

Trong P0/P1, một API instance là cấu hình chuẩn. Outbox phát realtime được bật từ P2: worker dùng notifier có service authentication để yêu cầu API phát event; khi có nhiều API instance thì dùng Redis backplane. Database và outbox vẫn là nơi khôi phục thông tin sau sự cố.

### 10.3. Module và boundary

| Module | Sở hữu dữ liệu / hành vi |
|---|---|
| Identity | Users, profile, sessions, roles, nhóm quyền hệ thống |
| Content | Chương trình, taxonomy, lessons, question/group versions, exams, transcript |
| Learning | Enrollment, kế hoạch ngày, lesson progress, skill evidence |
| Assessment | Attempts, answers, finalization, grading, result |
| Review | Mistakes, dictionary, cards, SRS và notes |
| Media | UploadSession, asset/rendition, recording và storage lifecycle |
| Community | Groups, memberships, messages, read cursors, study rooms |
| Calls | CallSession, participant admission, token policy và provider events |
| Notifications | Notification records, preferences, delivery |
| Operations | Outbox, inbox/dedup, jobs, audit và import |

- Mỗi module có application service và query rõ ràng; chia cùng một database ở giai đoạn hiện tại.
- Một use case được phép dùng transaction chung nếu cập nhật các aggregate liên quan trong database này; ghi rõ trong test và application service.
- Controller/hub nhận DTO, gọi application service; không nhân đôi nghiệp vụ vào hub.
- Projection/query dùng AsNoTracking và chọn trường cần thiết. EF Core là data access chính; raw SQL dành cho atomic update/claim khi có lý do rõ ràng.
- Interface phục vụ boundary thật: IClock, IObjectStorage, IMediaProcessor, IRealtimeNotifier, ICallProvider. Tránh tạo abstraction cho mọi lớp chỉ để đủ tầng.

### 10.4. Cấu trúc repository đề xuất

~~~text
EnglishHub.sln
src/
  EnglishHub.Api/
  EnglishHub.Application/
    Identity/ Content/ Learning/ Assessment/ Review/
    Media/ Community/ Calls/ Notifications/ Operations/
  EnglishHub.Domain/
  EnglishHub.Infrastructure/
  EnglishHub.Worker/
web/
tests/
  EnglishHub.UnitTests/
  EnglishHub.IntegrationTests/
  EnglishHub.ContractTests/
  e2e/
deploy/
  compose.yaml
  ffmpeg/
  livekit/
docs/
  spec.md
  adr/
  api/
  runbooks/
seed/
  manifests/
  fixtures/
~~~

Migrations nằm trong Infrastructure. Worker chỉ chạy loại job được bật bằng feature flag và chỉ khởi động khi dependency tương ứng sẵn sàng.

<a id="s11"></a>
## 11. Mô hình dữ liệu

### 11.1. Quy ước

- PK dùng uniqueidentifier, sinh server; không dùng MAX(Id) + 1. Sequence chat là bigint riêng theo nhóm.
- Mọi thời điểm tuyệt đối dùng datetimeoffset(3), lưu offset +00:00. Ngày học dùng date + TimeZoneId snapshot.
- Dữ liệu Unicode dùng nvarchar. IPA, tiếng Việt và emoji được kiểm thử end-to-end.
- Bản ghi cập nhật có RowVersion kiểu rowversion; API biểu diễn ETag dạng chuỗi Base64 có dấu nháy theo HTTP.
- Bản ghi lịch sử/phiên bản đã publish ưu tiên immutable. Dữ liệu truy vấn quan trọng nằm trong cột quan hệ; JSON dành cho cấu hình có SchemaVersion hoặc snapshot phụ.
- Khóa ngoại mặc định NO ACTION với dữ liệu học/thi/lịch sử; cascade chỉ áp dụng child nháp khi đã thiết kế và kiểm thử.
- Trạng thái dùng enum có mapping số ổn định trong DB và tên snake_case ở JSON; không thay nghĩa số enum sau migration.
- Nội dung soft-delete có DeletedAt; published version được archive. Nội dung bị thu hồi quyền sử dụng được xử lý bởi quy trình takedown, có thể dừng phát media nhưng giữ metadata kết quả.
- Metadata blob lưu StorageKey và ETag/hash; không lưu SAS URL vào DB.

ContentRoots là registry gốc với Id, Kind và ProgramId nullable. ContentVersions là registry phiên bản: Id, RootId, Kind, Version, PublicationStatus, PublishedAt, SchemaVersion, PayloadHash và RowVersion. RootId + Kind có composite FK tới ContentRoots. Các bảng gốc Lessons/Questions/QuestionGroups/ExamSets/PathTemplates/Decks dùng PK đồng thời là FK tới ContentRoots; các bảng LessonVersions/QuestionGroupVersions/QuestionVersions/ExamVersions/PathVersions/DeckVersions dùng PK đồng thời là FK tới ContentVersions. Vì vậy ContentVersionId trong note/task/source là FK thực, không là chuỗi polymorphic không kiểm chứng. Các trường version/publication chung được lưu một nơi trong registry; bảng dưới mô tả trường logic, không yêu cầu nhân đôi cột chung ở bảng con. Publish validator kiểm tra Kind phù hợp với bảng con và liên kết. Payload của version đã publish bất biến; archive/takedown thay trạng thái truy cập có audit. Con trỏ CurrentPublishedVersionId phải tham chiếu version của đúng RootId bằng composite FK/alternate key, không trỏ nhầm một nội dung khác.

### 11.2. Sơ đồ quan hệ rút gọn

~~~mermaid
erDiagram
    User ||--o{ Enrollment : follows
    PathVersion ||--o{ Enrollment : pins
    PathVersion ||--o{ PathNode : contains
    Enrollment ||--o{ DailyTask : plans
    Lesson ||--o{ LessonVersion : versions
    LessonVersion ||--o{ TranscriptSegment : includes
    MediaAsset ||--o{ MediaRendition : generates
    ExamVersion ||--o{ ExamItem : contains
    QuestionGroupVersion ||--o{ QuestionVersion : groups
    User ||--o{ Attempt : starts
    Attempt ||--o{ AttemptItem : snapshots
    AttemptItem ||--|| AttemptAnswer : receives
    User ||--o{ UserCard : owns
    UserCard ||--o{ CardReview : reviews
    StudyGroup ||--o{ GroupMembership : authorizes
    StudyGroup ||--o{ ChatMessage : stores
    StudyGroup ||--o{ StudyRoom : hosts
    StudyRoom ||--o{ CallSession : runs
    CallSession ||--o{ CallParticipant : admits
~~~

### 11.3. Identity, content và assessment

| Bảng | Trường chính ngoài PK/audit | Ràng buộc quan trọng |
|---|---|---|
| Users / Identity tables | Theo ASP.NET Core Identity, status, SecurityStamp | Unique normalized email theo cấu hình; không lưu password tự viết |
| UserProfiles | UserId, DisplayName, TimeZoneId, DailyMinutes, StudyDaysMask, TargetScore | Unique UserId; DailyMinutes trong 10–180 |
| Programs | Code, Name, Status | Unique Code |
| ContentRoots | Kind, ProgramId nullable, CreatedAt | Registry cho các bảng gốc; alternate key Id + Kind |
| ContentVersions | RootId, Kind, Version, PublicationStatus, PublishedAt, SchemaVersion, PayloadHash, RowVersion | Unique Kind + RootId + Version; registry cho các bảng version |
| SkillTags | ProgramId, Code, ParentId, Name, Skill | Unique ProgramId + Code; ParentId không tạo cycle |
| Lessons | ProgramId, Slug, CurrentPublishedVersionId, Status | Unique ProgramId + Slug |
| LessonVersions | LessonId, Version, Title, Type, Body, EstimatedMinutes, Difficulty, PublicationStatus, SchemaVersion | Unique LessonId + Version; Published immutable |
| ContentSourceRecords | OriginType, SourceUrl, Attribution, UsageBasis, VerifiedBy | Hồ sơ nguồn có ID ổn định, được version nội dung/media tham chiếu |
| ContentVersionSources | ContentVersionId, SourceRecordId, Role | Composite PK; hai FK thực |
| LessonAssets | LessonVersionId, MediaAssetId, Role, SortOrder | Unique version + role + order; FK asset Ready khi publish |
| TranscriptTracks | LessonVersionId, MediaAssetId, Language, Version, Kind | Track ghim asset/version |
| TranscriptSegments | TrackId, SortOrder, StartMs, EndMs, TextEn, TextVi, AcceptedVariantsJson | Unique TrackId + SortOrder; StartMs < EndMs |
| QuestionGroups / QuestionGroupVersions | ProgramId, Version, Part, Stimulus, AudioAssetId, Difficulty | Published group immutable; mọi câu trong đề ghim version |
| Questions / QuestionVersions | GroupVersionId, Order, Type, Prompt, Explanation, NormalizationPolicyVersion | Unique GroupVersionId + Order |
| QuestionOptions | QuestionVersionId, OptionKey, Text, IsCorrect | Unique question + key; publish validator kiểm tra số đáp án đúng |
| QuestionSkillTags | QuestionVersionId, SkillTagId, Weight | Composite PK; Weight dương, chính sách trọng số có version |
| ExamSets / ExamVersions | ProgramId, Slug, Version, BlueprintJson, PolicyVersion, PublicationStatus | Unique set + version |
| ExamItems | ExamVersionId, GroupVersionId, QuestionVersionId, Section, Order | Unique exam + order và exam + question; bảo toàn group |
| Attempts | UserId, Mode, ExamVersionId nullable, Status, Section, StartedAt, DeadlineAt, FinalizedAt, FinalizationReason, PolicyVersion, RowVersion | Owner bắt buộc; FK exam nếu full mock |
| AttemptItems | AttemptId, QuestionVersionId, GroupVersionId, Section, Order, GradingSnapshotId | Unique attempt + order; unique attempt + question; alternate key Id + QuestionVersionId |
| GradingSnapshots | AttemptId, PayloadJson, SchemaVersion, Hash | Private server-only; ghim đáp án/lời giải/policy |
| AttemptAnswers | AttemptItemId, QuestionVersionId, SelectedOptionId nullable, AnswerText nullable, UpdatedAt, RowVersion | Unique AttemptItemId; tạo row rỗng khi start để luôn có ETag; composite FK bảo vệ quan hệ |
| AttemptHelpEvents | AttemptId, AttemptItemId nullable, Type, OccurredAt, ClientRequestId | Ghi nhận reveal/hint; unique attempt + request |
| AttemptResults | AttemptId, CorrectCount, TotalCount, BlankCount, ListeningRaw, ReadingRaw, CalculationVersion, BreakdownJson | Unique AttemptId; CorrectCount + sai + trống = TotalCount |
| AnswerRequestReceipts | UserId, AttemptId, ClientRequestId, RequestHash, ResponseJson | Unique user + attempt + request; dùng replay |

QuestionOptions có alternate key (QuestionVersionId, Id). AttemptAnswers có FK (AttemptItemId, QuestionVersionId) tới alternate key của AttemptItems và FK (QuestionVersionId, SelectedOptionId) tới QuestionOptions; SelectedOptionId nullable cho câu trống/fill blank. Application service còn kiểm tra kiểu đáp án và policy. Tạo row AttemptAnswer rỗng cho mỗi item trong transaction start; row đó cung cấp ETag đầu tiên.

### 11.4. Learning, review và phát âm

| Bảng | Trường chính | Ràng buộc |
|---|---|---|
| PathTemplates / PathVersions | ProgramId, Version, RuleVersion, PublicationStatus | Published immutable |
| PathStages | PathVersionId, StageKey, Title, Order | Unique path version + stage key/order |
| PracticeBlueprints | ProgramId, Part, TagsJson, GroupSelectionRuleJson, TimingPolicyJson, Version | Cấu hình xác định sẵn; version đã dùng không sửa trực tiếp |
| PathNodes | PathVersionId, StageId, NodeKey, ContentVersionId nullable, BlueprintId nullable, Order, CompletionRuleJson, EstimatedMinutes | Unique path version + node key; CHECK đúng một trong ContentVersionId/BlueprintId có giá trị |
| PathPrerequisites | NodeId, RequiresNodeId | Composite PK; publish validator phát hiện cycle |
| Enrollments | UserId, PathVersionId, ActiveKey, Status, PlanRevision, StartedAt, RowVersion | Unique filtered UserId + ProgramId khi ActiveKey bật; ProgramId được lưu để enforce |
| DailyTasks | EnrollmentId, LocalDate, TimeZoneId, SlotKey, SourceNodeId, ReasonCode, Status, EstimatedMinutes, ReplacesTaskId | Unique enrollment + date + slot |
| TaskCompletions | DailyTaskId, SourceType, SourceId, CompletedAt | Unique DailyTaskId; source bắt buộc thuộc user |
| LessonProgress | UserId, LessonVersionId, Status, CurrentPlaybackSessionId, CompletedAt, RowVersion | Unique user + lesson version |
| PlaybackSessions | UserId, LessonVersionId, ClientSessionId, LastSequence, ResumeMs, WatchedIntervalsJson, LastSeenAt | Unique user + client session; sequence đơn điệu |
| ListeningSessions | UserId, LessonVersionId, TrackId, Mode, PolicyVersion, Status, StartedAt, CompletedAt, RowVersion | Track/media version cố định trong session |
| SegmentAttempts | ListeningSessionId, SegmentId, ClientRequestId, AnswerText, IsCorrect, Assisted, DifferenceJson, CheckedAt, NormalizationPolicyVersion | Unique session + request; giữ các lần thử riêng |
| SkillEvidence | UserId, AttemptAnswerId, TagId, IsCorrect, Assisted, Mode, ObservedAt | Unique answer + tag; bất biến sau ghi nhận |
| MistakeItems | UserId, QuestionVersionId, LastWrongAt, WrongCount, ReviewStatus, NextReviewAt, RowVersion | Unique user + question version |
| DictionaryEntries | Headword, NormalizedHeadword, Language | Index normalized headword; homograph tách entry/sense khi cần |
| WordSenses | EntryId, PartOfSpeech, MeaningVi, DefinitionEn, ExampleEn, ExampleVi | Sense ID ổn định |
| PronunciationVariants | EntryId, Accent, IpaText, AudioAssetId, SourceRecordId | Index EntryId + Accent; cho phép nhiều variant có nhãn |
| PhonemeLessons | CurriculumVersionId, Symbol, Accent, LessonVersionId, SortOrder | Unique curriculum + symbol + accent |
| UserCards | UserId, WordSenseId nullable, CustomKey nullable, Front, Back, DueAt, IntervalDays, SuccessStep, SchedulerVersion, RowVersion, ArchivedAt | Unique filtered user + sense khi sense khác null và card active; custom key có unique filtered riêng |
| CardSourceContexts | CardId, ContentVersionId, SegmentId nullable, SelectedText, SentenceText | Unique context fingerprint trong card |
| CardReviews | ReviewEventId, UserId, CardId, Rating, PreviousStateJson, NewStateJson, ReviewedAt, SchedulerVersion | Unique ReviewEventId |
| Decks / DeckVersions / DeckItems | OwnerId nullable, Visibility, Version, WordSenseId | Shared deck publish version; progress riêng theo UserCard |
| Notes / Highlights | UserId, ContentVersionId, BlockKey, StartOffset, EndOffset, Text, Body, RowVersion | Chỉ owner đọc/sửa; validate anchor |

Độ dài offset của note/highlight thống nhất là UTF-16 code units để tương thích chuỗi JavaScript; server C# áp dụng cùng quy ước và kiểm tra biên ký tự, không chấp nhận khoảng cắt giữa surrogate pair. Card có WordSenseId thì CustomKey phải null; custom card bắt buộc CustomKey và WordSenseId null. Migrations đặt CHECK cho hai kiểu card, không để các hàng không có danh tính nội dung.

### 11.5. Media, community và operations

| Bảng | Trường chính | Ràng buộc |
|---|---|---|
| MediaAssets | OwnerId, Purpose, Visibility, Status, StagingKey, CanonicalKey, SourceETag, Sha256, ByteSize, DurationMs, Mime, SourceRecordId, Generation, RowVersion | StorageKey server sinh; asset Ready mới được gắn vào nội dung publish |
| UploadSessions | AssetId, UserId, ExpectedSize, DeclaredType, ExpiresAt, State, CompletedAt, RowVersion | Unique AssetId; session chỉ cho đúng owner/purpose |
| UserStorageQuotas / UploadQuotaReservations | UserId, LimitBytes, UsedBytes, ReservedBytes, RowVersion; UploadSessionId, UserId, Bytes, State | Giữ quota bằng atomic update; release/finalize reservation một lần |
| MediaRenditions | AssetId, Generation, Kind, StorageKey, Mime, ByteSize, Status | Unique asset + generation + kind |
| Recordings | UserId, LessonVersionId, SegmentId nullable, AssetId, SelfRating, SharedGroupId nullable, DeletedAt | Unique AssetId; private mặc định |
| StudyGroups | OwnerId, Name, Visibility, Status, NextMessageSequence, RowVersion | Ownership transfer khóa group row |
| GroupMemberships | GroupId, UserId, Role, Status, MembershipVersion, JoinedAt, LeftAt, RowVersion | Unique GroupId + UserId |
| GroupInvites | GroupId, TokenHash, ExpiresAt, MaxUses, UsedCount, RevokedAt, RowVersion | Unique token hash; 0 ≤ UsedCount ≤ MaxUses |
| ChatMessages | GroupId, SenderId, ClientMessageId, Sequence, Type, Body, ReplyToId, EditedAt, DeletedAt, RowVersion | Unique GroupId + Sequence; unique sender + group + ClientMessageId |
| MessageVersions | MessageId, Version, Body, ChangedAt, ChangedBy | Unique message + version; đọc lịch sử theo quyền moderation |
| MessageAttachments | MessageId, AssetId | Composite PK; asset có quyền nhóm và Ready |
| GroupReadCursors | GroupId, UserId, LastReadSequence, UpdatedAt | Unique group + user; cursor chỉ tăng |
| StudyRooms | GroupId, HostUserId, Status, ActiveKey nullable, CreatedAt, EndedAt, RowVersion | Filtered unique GroupId + ActiveKey khi active |
| RoomPlaybackStates | RoomId, MediaAssetId, LessonVersionId, PositionMs, Rate, Paused, UpdatedAt, Revision | Unique RoomId |
| CallSessions | RoomId, ProviderRoomName, Status, MaxParticipants, AdmissionEpoch, StartedAt, EndedAt, RowVersion | Unique ProviderRoomName; một active call/StudyRoom |
| CallParticipants | CallSessionId, UserId, ProviderIdentity, AdmissionStatus, LeaseUntil, MembershipVersion, Epoch, JoinedAt, LeftAt | Unique call + user; slot đang giữ được tính vào capacity |
| ProviderEventInbox | Provider, EventId, PayloadHash, ReceivedAt, ProcessedAt, Status | Unique Provider + EventId |
| Notifications | UserId, EventId, Type, PayloadJson, CreatedAt, ReadAt | Unique user + event + type |
| ActivityEvents | EventId, UserId, SourceType, SourceId, ActivityType, OccurredAt, LocalDate, TimeZoneId, DurationSeconds | Unique EventId và source + activity theo policy |
| DailyActivitySummaries | UserId, LocalDate, TimeZoneId, QualifiedActivities, ActiveSeconds, XP | Unique user + date + timezone snapshot |
| OutboxEvents | EventId, AggregateType, AggregateId, AggregateVersion, Type, PayloadJson, OccurredAt, State, NextAttemptAt, LeaseUntil, LeaseToken, Attempts | Unique EventId; index state + next attempt |
| ConsumerInbox | ConsumerName, EventId, HandledAt | Composite PK |
| BackgroundJobs | Type, DedupKey, PayloadJson, State, DueAt, LeaseUntil, LeaseToken, Attempts, LastErrorCode | Unique DedupKey; index State + DueAt |
| IdempotencyRecords | UserId, Operation, Key, RequestHash, ResourceId, ResponseCode, ExpiresAt | Unique user + operation + key |
| AuditLogs | ActorId, Action, TargetType, TargetId, CorrelationId, OccurredAt, ChangesJson | Append-only; không ghi cookie/token/blob SAS |
| ImportBatches / ImportRows | UserId, ImportKey, SchemaVersion, State, ErrorSummary; BatchId, RowNo, SourceKey, State | Unique owner + ImportKey; unique batch + row/source |

Presence và typing không nằm trong bảng lịch sử SQL. Redis lưu user→connections/room presence với TTL; SQL vẫn là nguồn quyền truy cập.

### 11.6. Index tối thiểu theo truy vấn

| Truy vấn | Index dự kiến | Cách kiểm tra |
|---|---|---|
| Lịch sử bài làm cá nhân | Attempts(UserId, StartedAt DESC, Id), include status/mode | Keyset query và execution plan |
| Dashboard việc hôm nay | DailyTasks(EnrollmentId, LocalDate, Status) | Không scan tất cả task của hệ thống |
| Thẻ đến hạn | UserCards(UserId, DueAt, Id), filtered ArchivedAt IS NULL | Lấy theo cursor; thử nhiều user |
| Lịch sử chat | ChatMessages(GroupId, Sequence DESC), include sender/type/deleted | Page 50 tin, không scan toàn bảng |
| Câu cần ôn | MistakeItems(UserId, ReviewStatus, NextReviewAt, Id) | Tránh join làm nhân bản câu |
| Worker nhận việc | BackgroundJobs(State, DueAt, Id), include lease | Test hai worker nhận việc đồng thời |
| Outbox chưa phát | OutboxEvents(State, NextAttemptAt, Id) | Không scan event đã processed |
| Nội dung công khai | Lessons(ProgramId, Status, Slug), SkillTag bridge index | Projection, không Include graph vô hạn |
| Membership authorization | GroupMemberships(GroupId, UserId), include status/role/version | Có đúng unique index |

Index được xác nhận bằng execution plan với dữ liệu seed hiệu năng. Không tạo index trên mọi cột hoặc INCLUDE toàn bộ nội dung text.

<a id="s12"></a>
## 12. Bất biến dữ liệu và transaction

### 12.1. Những điều luôn phải đúng

| ID | Bất biến |
|---|---|
| INV-01 | Một attempt chỉ được finalization và sinh kết quả logic một lần |
| INV-02 | Đáp án chỉ tham chiếu câu/option trong snapshot của attempt |
| INV-03 | Đáp án sau deadline không được nhận, kể cả worker hết giờ chưa chạy |
| INV-04 | Published content/version không bị sửa làm thay đổi bài đã làm |
| INV-05 | Duplicate request cùng khóa/cùng payload không tạo hiệu ứng nghiệp vụ mới |
| INV-06 | Cùng khóa nhưng payload khác là conflict, không replay mù |
| INV-07 | Một review event chỉ thay lịch card một lần; hai review cùng version không đều thành công |
| INV-08 | Một daily task hoàn thành chỉ đóng góp một completion event |
| INV-09 | Một client message có một message bền vững trong cùng group/sender |
| INV-10 | Read cursor, accepted event version và sequence không lùi do client cũ |
| INV-11 | Mọi API/hub command mới kiểm tra quyền hiện hành; tài nguyên private chỉ trả theo ACL. Media URL/call token đã cấp có cửa sổ thu hồi được quy định tại mục 13/16 |
| INV-12 | Asset chưa Ready không được phát/công bố làm media của bài đã publish |
| INV-13 | Canonical blob đã được xác minh không bị ghi lại bằng upload grant cũ |
| INV-14 | Worker mất lease không được commit trạng thái/output như worker hiện hành |
| INV-15 | Capacity call tính cả slot Pending còn hạn, không vượt MaxParticipants |
| INV-16 | Đóng room/call ngăn admission mới và tạo hành động loại participant hiện tại |
| INV-17 | Một sự kiện provider/outbox được xử lý lặp không nhân bản dữ liệu dẫn xuất |
| INV-18 | Thống kê và leaderboard có thể rebuild từ kết quả/activity nguồn |
| INV-19 | Answer keys, token và private media URL không nằm trong payload công khai/log |
| INV-20 | Mọi dữ liệu mẫu/fixture được đánh nhãn và không giả làm nội dung/điểm được kiểm định |

### 12.2. Ranh giới transaction

| Use case | Cùng một transaction SQL | Bên ngoài transaction |
|---|---|---|
| Start attempt | Attempt + item snapshot + answer rows rỗng + private grading snapshot + idempotency receipt | Fetch blob/hiển thị player |
| Save answer | Khóa/kiểm tra attempt và deadline + answer versions + receipt | Broadcast trạng thái nếu cần |
| Submit / expire | Finalization + raw result + outbox + receipt | Cập nhật dashboard dẫn xuất từ outbox |
| Review card | Dedup event + kiểm tra RowVersion + cập nhật card + review log + outbox | Thông báo reminder |
| Complete task | Completion + task status + outbox | Recompute dashboard |
| Send message | Kiểm tra membership + cấp group sequence + insert + attachment refs + outbox + receipt | SignalR fanout |
| Redeem invite | Kiểm tra/cập nhật lượt dùng + membership + audit/outbox | Notify group |
| Kick member | Membership version/status + pending admissions invalidation + audit/outbox | Remove SignalR connections và provider participant |
| Complete upload | Chuyển trạng thái có điều kiện + lưu SourceETag + enqueue durable job | Blob HEAD/copy/FFmpeg theo saga |
| Admit call participant | Khóa CallSession + kiểm tra membership hiện tại + pending slot/epoch | Tạo token và gọi provider |

Blob, Redis, SignalR và LiveKit không nằm trong transaction SQL. Mọi tác động sau commit phải retry được và có bước reconciliation. EF Core hỗ trợ transaction cho nhiều thao tác và concurrency token để phát hiện ghi đè; xem [T04](https://learn.microsoft.com/en-us/ef/core/saving/transactions), [T05](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

### 12.3. Mẫu xử lý finalization

~~~text
BEGIN transaction
  lock attempt row for update
  verify owner and request idempotency
  if already finalized: return persisted result
  read authoritative server time after locking attempt
  if final batch exists: validate versions and deadline, apply accepted answers
  determine reason = manual_submit or deadline
  freeze answer set
  calculate raw score using pinned grading snapshot
  insert AttemptResult (unique AttemptId)
  update attempt status = Graded, reason and FinalizedAt
  insert OutboxEvent (unique EventId)
  save idempotency response
COMMIT
return persisted result
~~~

Khóa parent attempt cũng được lấy trong use case autosave để serialize với submit/expiry. Giữ transaction ngắn; không gọi external API, FFmpeg hoặc chờ người dùng trong transaction. Retry deadlock ở mức toàn use case idempotent với số lần giới hạn, không retry lỗi nghiệp vụ như hết giờ.

### 12.4. Khi nào dùng công cụ nào

- Optimistic concurrency/ETag: soạn bài nháp, sửa note, review card, đổi playback và cấu hình nhóm.
- Atomic conditional update hoặc khóa row transaction: nhận job, cấp sequence nhóm, redeem invite, finalization và giữ slot call.
- Unique constraint: khóa idempotency, membership, result, dedup consumer và request receipt.
- Không coi “SELECT thấy chưa tồn tại rồi INSERT” là cách đủ để chống race. Phải có constraint và cách trả kết quả khi xung đột.
- Không coi mặc định có transaction trong SaveChanges là đã giải quyết mọi race giữa hai request. Điều kiện đọc/ghi và isolation của cả use case phải được kiểm chứng.
- Thứ tự lấy khóa thống nhất: Group → Membership → Room → Call → Participant khi có nhiều tài nguyên liên quan. Các use case độc lập không lấy thêm khóa không cần thiết.

<a id="s13"></a>
## 13. Upload, xử lý và phân phối media

### 13.1. Quyết định lưu trữ

Database lưu metadata, trạng thái, quyền, nguồn và quan hệ. Audio, video, ảnh, phụ đề xuất bản và recording nằm trong private blob storage. Đường dẫn blob do server sinh bằng asset ID/generation; tên file người dùng chỉ là nhãn hiển thị đã sanitize.

| Purpose | Allowlist đầu vào đề xuất | Giới hạn demo |
|---|---|---|
| Lesson audio | MP3, M4A, WAV, OGG | 100 MiB, 60 phút |
| Lesson video | MP4, WebM | 500 MiB, 60 phút |
| User recording | WebM/Opus, OGG, M4A theo hỗ trợ trình duyệt | 20 MiB, 3 phút |
| Lesson/group image | JPEG, PNG, WebP | 5 MiB |
| Subtitle | SRT, VTT UTF-8 | 2 MiB |

Giới hạn là cấu hình sản phẩm. Kiểm tra MIME khai báo, chữ ký file, metadata decode thực và quota; chỉ extension là chưa đủ. Nếu phải nhận file qua API, dùng streaming cho file lớn; hướng dẫn ASP.NET Core phân biệt buffering và streaming tại [T07](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0).

### 13.2. Vòng đời asset

~~~mermaid
stateDiagram-v2
    [*] --> PendingUpload
    PendingUpload --> Uploaded: Complete + xác minh staging ETag
    PendingUpload --> Expired: hết hạn
    Uploaded --> Processing: worker nhận lease
    Processing --> Ready: canonical + renditions verified
    Processing --> Failed: vượt retry / file không hợp lệ
    Ready --> DeletePending: yêu cầu xóa hợp lệ
    Failed --> DeletePending: cleanup
    Expired --> DeletePending: cleanup
    DeletePending --> Deleted: blob cleanup xác nhận
~~~

### 13.3. Luồng upload chuẩn

1. Client gọi tạo UploadSession, gửi purpose, expected size và MIME; server giữ quota dựa trên expected size bằng atomic reservation trước khi cấp grant.
2. Server kiểm tra role/quota và cấp quyền upload chỉ tới một staging blob, TTL mặc định 15 phút, không có quyền đọc/list container.
3. Client upload bằng block upload; giữ uploadSessionId/block IDs để tiếp tục sau mất mạng. Progress lấy từ số byte đã truyền.
4. Client gọi complete. Server HEAD staging để lấy size/ETag; xác minh session, owner và giới hạn; CAS trạng thái và lưu job trong DB.
5. Worker copy/read staging có If-Match theo ETag đã ghi, tạo canonical source với key mới chỉ worker được ghi. Nếu staging bị sửa sau complete thì fail/revalidate; không xử lý một nội dung khác dưới metadata cũ.
6. Worker probe/decode canonical source, tính hash và duration thực, tạo rendition ở generation đang xử lý.
7. Khi đủ output hợp lệ, commit Ready + rendition refs + quota accounting thực tế + outbox bằng lease token hiện hành. Quota của source/renditions được tính theo policy thống nhất; nếu vượt quota thì fail có lý do trước publish.
8. Dọn staging bằng job có thể retry. Client nhận AssetReady/AssetFailed; polling status là đường dự phòng.

Upload SAS không tự enforce được mọi giới hạn size/duration. Canonical copy được cách ly khỏi quyền upload cũ; chỉ asset đã qua kiểm tra mới xuất bản. Không trả upload grant của canonical path cho browser.

### 13.4. Xử lý media

- P0 chuẩn hóa audio phát lại và metadata; P1 thêm thumbnail, waveform tùy chọn và HLS.
- Worker dùng process arguments riêng, không ghép input người dùng thành shell command.
- FFmpeg có timeout, giới hạn CPU/memory, thư mục làm việc riêng và profile codec cho phép; worker không chạy với đặc quyền hệ thống.
- Rendition key có assetId/generation/kind. Output của job cũ không ghi đè generation đang dùng.
- HLS có master playlist + variants + segments cùng generation; đánh Ready chỉ khi manifest và toàn bộ output tham chiếu được xác minh.
- Subtitle parsing tách job hoặc bước có kiểm soát; không dùng tên subtitle để truy cập path bên ngoài vùng làm việc.
- Không tự tạo transcript bằng STT trong P0–P3. Editor nhập transcript/subtitle hoặc dùng dữ liệu mẫu đã biên soạn.

### 13.5. Phát và seek

- Endpoint playback kiểm tra quyền hiện tại rồi cấp URL đọc ngắn hạn cho audio/MP4 hoặc playback ticket cho HLS.
- MP3/MP4 phải hỗ trợ Range, trả 206/Content-Range đúng, 416 với range không hợp lệ và ETag/conditional requests phù hợp.
- Với HLS, API/gateway phục vụ playlist/segment bằng một playback ticket scope asset + generation + viewer, TTL ngắn; không để playlist private tham chiếu các segment public.
- Ticket/URL mặc định 2 phút; player xin gia hạn trong phiên học còn quyền. Không ghi URL có credential vào analytics/log.
- Quyền bị thu hồi chặn cấp mới ngay. URL đọc đã cấp có thể còn dùng đến TTL; đó là giới hạn được công bố của cơ chế URL ký. Luồng cần thu hồi tức thời dùng proxy kiểm tra quyền từng request.
- Signed URL hết hạn trả lỗi quyền thích hợp; player tự xin URL mới một lần rồi giữ vị trí hiện tại, tránh vòng retry vô hạn.

### 13.6. Xóa và reconciliation

- Bản ghi private recording do owner xóa chuyển DeletePending ngay, API ngừng cấp playback mới.
- Published lesson/media đang được phiên bản nội dung giữ tham chiếu không hard-delete qua thao tác sửa bài thông thường.
- Job xóa blob idempotent: blob đã không còn vẫn thành công; DB chuyển Deleted sau xác nhận.
- Cleanup orphan staging sau 24 giờ, job abandoned sau lease timeout; giới hạn tuổi và batch để tránh xóa nhầm blob đang upload. Reservation quota của session expired/failed được release idempotent.
- Reconciler so sánh metadata với object storage định kỳ, báo missing/corrupt assets; sửa trạng thái có audit.
- Dữ liệu ghi âm giữ theo lựa chọn retention của sản phẩm, mặc định 30 ngày với bản nháp chưa gắn bài; bản đã lưu do người dùng quản lý.

<a id="s14"></a>
## 14. HTTP API và hợp đồng dữ liệu

### 14.1. Quy ước chung

- Prefix /api/v1; JSON camelCase, enum snake_case; ID là UUID dạng chuỗi.
- Thời gian ISO 8601 UTC với hậu tố Z; duration/position dùng millisecond dạng số nguyên.
- Cookie xác thực HttpOnly/Secure ở môi trường HTTPS, same-origin. Mọi POST/PUT/PATCH/DELETE từ browser gửi antiforgery token qua X-CSRF-Token; chỉ webhook đã có chữ ký và endpoint service-auth được miễn cơ chế browser này.
- POST tạo hiệu ứng có thể retry yêu cầu Idempotency-Key tối đa 64 ký tự. Scope = user + operation + key; giữ receipt tối thiểu 24 giờ, bất biến domain tiếp tục chống trùng sau khi receipt hết hạn.
- Cùng key/payload replay kết quả. Cùng key/payload khác trả 409 idempotency_conflict. Thao tác cùng key đang chạy trả 409 operation_in_progress với Retry-After.
- Update tài nguyên mutable yêu cầu If-Match/ETag; thiếu precondition trả 428, version cũ trả 412 và phiên bản hiện tại được phép xem.
- Cursor là opaque token, pageSize 1–50, mặc định 20. Sort luôn có tie-breaker ID hoặc sequence.
- Error trả application/problem+json, có code, status, title, traceId và errors theo field nếu có; không lộ stacktrace/nội dung chấm riêng.
- 401 chưa đăng nhập; 403 không có quyền; 404 dùng cho tài nguyên private mà người gọi không được biết; 409 xung đột trạng thái; 412 stale version; 413 quá lớn; 422 nội dung nghiệp vụ không hợp lệ; 429 vượt giới hạn.
- POST creation trả 201 + Location, async command trả 202 + status URL. DELETE idempotent trả 204 khi đã xóa theo cùng quyền.

### 14.2. Danh mục endpoint

Trong cột Key: I = Idempotency-Key, E = If-Match. Quyền “owner” nghĩa là owner của tài nguyên được xử lý, không chỉ đã đăng nhập.

| Method | Path sau /api/v1 | Quyền / Key | Mục đích |
|---|---|---|---|
| GET | /auth/csrf | Public | Cấp antiforgery token |
| POST | /auth/register | Public, rate limit | Tạo account và email verification |
| POST | /auth/login | Public, rate limit | Đặt auth cookie |
| POST | /auth/logout | User | Thu hồi session hiện hành |
| POST | /auth/verify-email | Public, one-time token | Xác minh email |
| POST | /auth/forgot-password | Public, rate limit | Trả phản hồi không phân biệt email tồn tại |
| POST | /auth/reset-password | Public, one-time token | Đổi mật khẩu qua token |
| GET / PATCH | /me | User; PATCH E | Hồ sơ và tùy chọn |
| GET | /programs, /skills, /lessons | Public/User theo visibility | Danh mục/lọc |
| GET | /lessons/{id} | Theo visibility/enrollment | Bản published, không trả grader fields |
| GET | /lessons/{id}/versions/{versionId}/study | Learner | Dữ liệu học được phép reveal |
| POST | /placement-attempts | Learner, I | Tạo placement |
| POST | /enrollments | Learner, I | Ghim path version |
| GET | /me/enrollments, /me/daily-tasks | Learner | Lộ trình và việc hôm nay |
| POST | /enrollments/{id}/replan | Owner, I, E | Tạo PlanRevision cho task chưa bắt đầu |
| POST | /daily-tasks/{id}/complete | Owner, I | Hoàn thành theo bằng chứng hợp lệ |
| POST | /practice-attempts | Learner, I | Chọn group/snapshot luyện tập |
| GET | /exam-sets, /exam-sets/{id} | Learner | Danh mục và policy đề |
| POST | /exam-sets/{id}/attempts | Learner, I | Bắt đầu full/mini mock |
| GET | /attempts/{id} | Owner | Snapshot đề, trạng thái, server clock |
| PUT | /attempts/{id}/answers | Owner, I; từng item có E | Atomic answer batch |
| POST | /attempts/{id}/submit | Owner, I | Final batch + finalization |
| POST | /attempts/{id}/abandon | Owner, I | Kết thúc bỏ dở theo policy |
| GET | /attempts/{id}/result | Owner | Kết quả và explanation được disclose |
| GET | /me/attempts, /me/skill-summary | Owner | Lịch sử và analytics |
| POST | /playback-sessions | Learner, I | Mở phiên tiếp tục bài học |
| PUT | /playback-sessions/{id}/progress | Owner, I | Client sequence + khoảng xem |
| POST | /listening-sessions | Learner, I | Mở luyện nghe theo mode |
| POST | /listening-sessions/{id}/segments/{segmentId}/check | Owner, I | Lưu câu trả lời/nghe chép và kết quả |
| POST | /listening-sessions/{id}/segments/{segmentId}/reveal | Owner, I | Ghi nhận dùng gợi ý/transcript |
| GET | /dictionary | Learner | Tra từ nội bộ theo query/accent |
| GET | /phoneme-curricula, /phoneme-lessons/{id} | Learner | IPA theo curriculum |
| GET / POST | /me/cards | Owner; POST I | Bộ thẻ cá nhân |
| POST | /me/cards/{id}/reviews | Owner, I, E | Review event + đổi lịch |
| GET | /me/cards/due | Owner | Hàng đợi đến hạn |
| GET / POST / DELETE | /me/notes và /me/notes/{id} | Owner; ghi I/E tùy thao tác | Notes/highlights |
| GET | /me/mistakes | Owner | Sổ câu sai |
| POST | /me/mistake-review-attempts | Owner, I | Ôn lại thành phiên riêng |
| POST | /uploads | User/Editor theo purpose, I | UploadSession + staging grant |
| POST | /uploads/{id}/complete | Owner, I, E | Kiểm tra upload, tạo processing job |
| GET | /media/{id} | Theo owner/content/group | Metadata và trạng thái |
| POST | /media/{id}/playback-access | Theo tài nguyên cha | URL/ticket ngắn hạn, chống cache |
| GET | /media/{id}/stream | Ticket hợp lệ | Range audio/MP4 qua proxy nếu dùng |
| GET | /media/{id}/hls/{generation}/{path} | Ticket hợp lệ | Manifest/segment cùng generation |
| DELETE | /media/{id} | Owner/Admin, I, E | DeletePending theo reference policy |
| POST / GET | /recordings, /recordings/{id} | Owner; POST I | Gắn asset vào bản ghi luyện nói |
| POST | /recordings/{id}/share | Owner + group member, I, E | Chia sẻ rõ ràng vào một nhóm |
| GET / POST | /groups | User; POST I | Danh mục được phép xem/tạo nhóm |
| GET / PATCH | /groups/{id} | Member/Owner; PATCH E | Chi tiết/cấu hình nhóm |
| POST | /groups/{id}/invites | Owner/Moderator, I | Tạo lời mời |
| POST | /group-invites/redeem | User, I | Redeem token, giới hạn số dùng |
| POST | /groups/{id}/members/{userId}/remove | Owner/Moderator, I, E | Thu hồi membership |
| POST | /groups/{id}/transfer-owner | Owner, I, E | Chuyển quyền và audit |
| GET | /groups/{id}/messages | Member | Cursor history/catch-up |
| POST | /groups/{id}/messages | Member, I | Gửi tin qua cùng service với SignalR |
| PATCH / DELETE | /groups/{id}/messages/{messageId} | Sender/Moderator, I, E | Sửa/xóa có version/tombstone |
| PUT | /groups/{id}/read-cursor | Member, I | Cập nhật đơn điệu |
| POST | /groups/{id}/rooms | Member theo policy, I | Mở StudyRoom |
| GET / POST | /rooms/{id}, /rooms/{id}/commands | Member; lệnh host I/E | Snapshot và playback command |
| POST | /rooms/{id}/close | Host/Owner, I, E | Đóng room |
| POST | /rooms/{id}/calls | Member theo policy, I | Tạo call provider room |
| POST | /calls/{id}/join | Member, I | Admission + token cá nhân |
| POST | /calls/{id}/leave | Participant, I | Giải phóng slot idempotent |
| POST | /calls/{id}/participants/{userId}/remove | Host/Owner, I | Thu hồi và remove tại provider |
| POST | /calls/{id}/close | Host/Owner, I, E | Đóng admission và provider room |
| POST | /integrations/livekit/webhook | Provider signature | Inbox và xử lý event |
| GET / PUT | /me/notifications, /me/notification-preferences | Owner; ghi E | Thông báo và tùy chọn |
| POST | /me/notifications/{id}/read | Owner, I | Đánh dấu đã đọc |
| POST / PATCH | /admin/content/* | Editor/Admin, I/E | Soạn version, import/preview |
| POST | /admin/content/{kind}/{id}/publish | Admin, I, E | Validate và publish snapshot |
| GET / POST | /admin/imports, /admin/imports/{id}/retry | Editor/Admin, I | Theo dõi import từng dòng |
| GET / POST | /admin/jobs, /admin/jobs/{id}/retry | Admin, I | Vận hành job có audit |
| GET | /health/live, /health/ready | Public tối giản / internal chi tiết | Health và dependencies |

Route có nhiều method trong một dòng phải được tách thành operation riêng trong OpenAPI. Các route wildcard admin ở bảng là namespace thiết kế; khi triển khai phải khai báo typed endpoint theo lesson/question/exam/path, không nhận tùy ý tên bảng từ client.

### 14.3. Ví dụ tạo attempt

~~~http
POST /api/v1/practice-attempts
Idempotency-Key: 37d06d58-a327-47bc-8da9-b83f0cfd45b2
X-CSRF-Token: <browser-antiforgery-token>
Content-Type: application/json

{
  "programCode": "toeic_lr",
  "part": 3,
  "skillTagIds": ["<uuid>"],
  "targetQuestionCount": 10,
  "difficulty": "core",
  "timed": false
}
~~~

~~~json
{
  "attemptId": "<uuid>",
  "mode": "practice",
  "status": "in_progress",
  "actualQuestionCount": 12,
  "selectionReason": "Giữ trọn 4 nhóm hội thoại, mỗi nhóm 3 câu.",
  "serverNow": "2026-11-09T03:00:00.000Z",
  "deadlineAt": null,
  "policyVersion": 1,
  "items": [
    {
      "attemptItemId": "<uuid>",
      "questionVersionId": "<uuid>",
      "groupId": "<uuid>",
      "order": 1,
      "prompt": "<authored question>",
      "options": [{"id": "<uuid>", "label": "A", "text": "<option>"}],
      "answer": {"selectedOptionId": null, "etag": "\"<base64>\""}
    }
  ]
}
~~~

Response mẫu chỉ minh họa một item/option; API thật trả đủ số được chọn. Không có isCorrect, answer key hoặc explanation riêng trong DTO này.

### 14.4. Ví dụ autosave

~~~json
{
  "clientRequestId": "<uuid>",
  "answers": [
    {
      "attemptItemId": "<uuid>",
      "selectedOptionId": "<uuid>",
      "expectedEtag": "\"<base64>\""
    }
  ]
}
~~~

Batch là atomic: một item stale/hết hạn làm toàn batch thất bại, trả code và current versions được phép xem. Client reconcile rồi gửi lại với một ClientRequestId mới khi người dùng đã chấp nhận trạng thái mới. Retry cùng request ban đầu dùng đúng ID cũ.

### 14.5. Ví dụ lỗi có thể xử lý

~~~json
{
  "type": "urn:englishhub:problem:attempt-expired",
  "title": "Phiên làm bài đã hết thời gian",
  "status": 409,
  "code": "attempt_expired",
  "traceId": "<trace-id>",
  "attemptId": "<uuid>",
  "serverNow": "2026-11-09T05:00:00.000Z",
  "resultUrl": "/api/v1/attempts/<uuid>/result"
}
~~~

### 14.6. Version API và import

- API v1 bổ sung field theo hướng tương thích; thay ý nghĩa/trạng thái phải có migration/contract version.
- JSON import có schemaVersion, externalSourceKey, source metadata, asset references và các row keys ổn định.
- Import chạy dry-run để báo lỗi trước; commit theo aggregate/version hợp lệ, lưu trạng thái từng row.
- Retry import chỉ nhận lại row chưa commit; unique source keys chống tạo lại lesson/group/question.
- Import không tự publish. Admin xem validation report rồi publish version bằng command riêng.

<a id="s15"></a>
## 15. Hợp đồng realtime

### 15.1. Hub và envelope

Một endpoint /hubs/study, xác thực bằng cùng Identity cookie. Client không tự chọn userId làm danh tính kết nối. Tên SignalR group do server tạo từ ID tài nguyên sau authorization.

~~~json
{
  "eventId": "<uuid>",
  "type": "chat.message_created",
  "schemaVersion": 1,
  "aggregateId": "<group-uuid>",
  "aggregateVersion": 45,
  "occurredAt": "2026-11-09T03:00:00.000Z",
  "payload": {"messageId": "<uuid>", "sequence": 45}
}
~~~

Client dedup bằng eventId và merge message bằng messageId/sequence. AggregateVersion luôn có scope cụ thể; version nhóm chat không dùng thay playback revision hoặc membership version.

### 15.2. Client → server

| Method | Input cốt lõi | Authorization / lưu trữ |
|---|---|---|
| JoinGroup | groupId, lastReceivedSequence | Membership đang active; đăng ký connection và trả snapshot/catch-up cursor |
| LeaveGroup | groupId | Chỉ rời kênh realtime, không tự rời membership |
| SendMessage | groupId, clientMessageId, text, replyToId, attachmentIds | Application service dùng cùng transaction như REST |
| MarkRead | groupId, sequence, commandId | Cursor đơn điệu, kiểm tra sequence thuộc nhóm |
| SetTyping | groupId, isTyping | Membership; ephemeral, rate-limited |
| Heartbeat | clientSessionId, activeRoomIds | Gia hạn presence lease cho các room có quyền |
| JoinStudyRoom | roomId, lastPlaybackRevision | Membership + room active; trả snapshot |
| ControlPlayback | roomId, commandId, expectedRevision, action, positionMs | Host hiện hành; CAS + outbox |

### 15.3. Server → client

| Event | Ý nghĩa / xử lý |
|---|---|
| chat.message_created | Thêm message đã commit; có đủ DTO hiển thị được phép xem |
| chat.message_updated | Áp dụng message version mới hơn |
| chat.message_deleted | Thay bằng tombstone, dọn preview theo quyền |
| chat.read_cursor_updated | Đồng bộ unread giữa nhiều thiết bị của cùng user |
| group.membership_changed | Tải lại quyền; bị remove thì rời room/kênh |
| room.playback_changed | Áp dụng revision mới, hiệu chỉnh theo server clock |
| room.host_changed / room.closed | Thay quyền UI hoặc kết thúc phiên |
| presence.changed / typing.changed | Hiển thị trạng thái có TTL |
| media.asset_ready / media.asset_failed | Owner/editor biết job xong; luôn có REST fallback |
| notification.created | Upsert theo notificationId, không cộng unread hai lần |
| call.state_changed | Trạng thái business của call; không vận chuyển media |

### 15.4. Reconnect, thứ tự và mất event

1. Kết nối lại với auth hiện hành; không giả SignalR group cũ còn hiệu lực.
2. Join lại những group/room backend còn cho phép.
3. Tải message sau lastReceivedSequence và snapshot playback/call hiện tại.
4. Merge theo ID/version; nếu event đến trước REST response, vẫn chỉ có một bản ghi trên UI.
5. Message gửi trong lúc offline nằm local pending với ClientMessageId ổn định; khi kết nối lại được kiểm tra quyền mới rồi gửi.
6. Nếu membership bị thu hồi trong lúc offline, pending message báo rejected; không tự chuyển sang nhóm khác.

Outbox phát theo cơ chế at-least-once, có thể lặp hoặc đến lệch thứ tự giữa aggregate. Client và consumer phải chịu được điều đó. Những sự kiện nhạy cảm về quyền không broadcast chỉ vì connection còn nằm trong group cũ: notifier lọc recipient theo membership hiện hành; membership revoke còn tạo command loại connection. Tin đã được gửi trước revocation có thể đến client muộn do mạng; yêu cầu không nhận thao tác trái quyền áp dụng theo thời điểm server commit và phiên bản membership. Cơ chế phát phải có test race send/kick.

<a id="s16"></a>
## 16. Tích hợp cuộc gọi

### 16.1. Trách nhiệm từng thành phần

- .NET: chính sách ai được vào, giới hạn người, phòng thuộc nhóm nào, đóng/mời ra, lịch sử và mapping danh tính.
- LiveKit: vận chuyển audio/video, signaling cuộc gọi, track/subscription và reconnect media.
- Browser: quyền mic/camera, lựa chọn thiết bị, render local/remote tracks và nút điều khiển.
- Database: CallSession và admission bền vững; Redis không phải nguồn cấp quyền.

Token LiveKit chứa identity, room và grants được ký ở server. Hết hạn token chủ yếu điều khiển admission, không tự đảm bảo ngắt một kết nối đang hoạt động; xem [T08](https://docs.livekit.io/frontends/reference/tokens-grants/). Deploy thực cần cấu hình TLS, địa chỉ có thể truy cập và mạng/TURN phù hợp; xem [T09](https://docs.livekit.io/transport/self-hosting/deployment/) và [T10](https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API/Signaling_and_video_calling).

### 16.2. Tạo call

1. Host gọi create call, kiểm tra StudyRoom active và membership/role.
2. Transaction tạo CallSession trạng thái provisioning, providerRoomName ngẫu nhiên gắn CallSessionId + epoch; enqueue CreateProviderRoom.
3. Worker tạo provider room với giới hạn 6 participant, ghi nhận thành công rồi chuyển open.
4. Retry cùng command không tạo hai room; providerRoomName là khóa ổn định.
5. Nếu provisioning fail, call chuyển failed với lỗi có thể thử lại; không hiển thị như đang có cuộc gọi hoạt động.

### 16.3. Admission và token

1. Join yêu cầu user là member active, call open và chưa bị block trong call hiện hành.
2. Transaction khóa CallSession, kiểm tra lại membership/epoch, tính pending còn hạn + active participants.
3. Nếu đủ chỗ, trả 409 call_full. Nếu chưa đủ, upsert admission pending với lease 30 giây.
4. Identity provider ổn định theo call + user; reconnect của cùng user tái sử dụng admission, không chiếm slot thứ hai.
5. Cấp JWT scope đúng provider room, quyền join/publish audio/video/subscribe phù hợp, TTL 60 giây; không cấp quyền quản trị provider hoặc room tùy ý.
6. Webhook participant_joined đã xác minh chuyển admission active; nếu không có admission hợp lệ/current membership thì remove participant và ghi audit.
7. Một pending slot hết lease chỉ được giải phóng sau kiểm tra/reconcile; webhook cũ không hồi sinh admission đã revoked.

Frontend chỉ nhận server URL, token ngắn hạn và call state cần thiết. API secret của provider không xuất hiện trong bundle hay log.

### 16.4. Rời, thu hồi và đóng

- Leave idempotent; provider webhook/heartbeat reconciliation xác nhận participant đã rời. Rời group khác với tắt một tab call.
- Kick/revoke: commit admission revoked + tăng membership/admission version trước; tạo outbox remove tại provider và loại connection realtime.
- Close: call chuyển closing, chặn token mới ngay tại .NET, đóng room provider, sau xác nhận chuyển closed.
- Webhook đến trễ không chuyển closed/revoked trở lại open/active.
- Khi provider lỗi, UI thể hiện đang đồng bộ trạng thái; worker tiếp tục remove/close với backoff.
- Với token đã cấp, cơ chế self-host có thể còn cửa sổ admission trong TTL. Webhook/reconciler remove người không còn quyền; không hứa thu hồi media tức thời chỉ bằng expiry JWT. Mục tiêu vận hành là ngắt sau khi provider nhận remove, và phát hiện lệch trạng thái trong 5 giây ở môi trường demo.
- Kịch bản private group nhạy cảm muốn loại hoàn toàn cửa sổ token cũ cần một ADR bổ sung về admission gateway/provider revocation, không được tự tuyên bố đã giải quyết bằng cookie hay SignalR.

### 16.5. Webhook và reconciliation

- Xác minh chữ ký theo body gốc và secret đúng provider; fail trước khi ghi hiệu ứng nghiệp vụ.
- Dedup Provider + EventId. Callback hợp lệ trả ACK nhanh sau khi lưu inbox; xử lý idempotent ở worker.
- Chỉ nhận mapping providerRoomName/identity đã được hệ thống phát hành.
- Mỗi transition kiểm tra call status, epoch, membership/admission version; không phụ thuộc thứ tự đến của webhook.
- Poll/reconcile call đang mở theo nhịp cấu hình 5 giây cho demo, giảm/tối ưu khi đo tải; dùng provider API với timeout và rate budget.
- Nếu vượt 6 do sai lệch ngoại lệ, giữ admission hợp lệ theo thời điểm đã commit và remove phần không hợp lệ; lỗi phải có alert/test.

### 16.6. Adapter .NET

ICallProvider bao gồm CreateRoom, GetRoomParticipants, RemoveParticipant, DeleteRoom và ValidateWebhook. Dùng typed HttpClient gọi server API chính thức của LiveKit, timeout/cancellation rõ ràng và contract test theo phiên bản API đã pin; JWT được ký bằng thư viện JWT .NET được duy trì, grants theo tài liệu provider. Có thể dùng SDK .NET nếu kiểm chứng được hỗ trợ/phù hợp, nhưng lựa chọn đó phải ghi trong ADR-007; không giả định một SDK tồn tại hoặc có API giống Node/Go.

<a id="s17"></a>
## 17. Background jobs, outbox và phục hồi

### 17.1. Job catalog

| Job | DedupKey đề xuất | Retry / điều kiện |
|---|---|---|
| ProcessMedia | media:{assetId}:{generation} | Retry lỗi tạm; file sai format là permanent failure |
| DeleteMedia | media-delete:{assetId}:{generation} | Blob không tồn tại coi là thành công |
| FinalizeExpiredAttempt | attempt-finalize:{attemptId} | Cùng service với submit, unique result |
| BuildDailyPlan | daily-plan:{enrollmentId}:{revision}:{localDate} | Chỉ thay task chưa bắt đầu |
| UpdateLearningProjection | projection:{consumer}:{eventId} | ConsumerInbox + cập nhật projection cùng transaction |
| PublishRealtimeEvent | realtime:{eventId} | At-least-once, client dedup |
| DeliverNotification | notify:{userId}:{type}:{eventId} | Unique notification; theo preferences |
| ProcessImport | import:{batchId}:{schemaVersion} | Checkpoint từng aggregate/row |
| CreateProviderRoom | call-create:{callId}:{epoch} | Provider room key ổn định |
| RemoveCallParticipant | call-remove:{callId}:{userId}:{admissionVersion} | Bản đã revoked không được tái cấp |
| CloseProviderRoom | call-close:{callId}:{epoch} | Idempotent |
| ReconcileCalls | reconcile-call:{callId}:{timeBucket} | Chỉ một bucket active; không gây backlog vô hạn |
| CleanupStaging | cleanup:{utcDate}:{shard} | Kiểm tra age/reference/session trước xóa |
| RebuildAnalytics | rebuild:{projection}:{version}:{range} | Admin command có audit, có checkpoint |

### 17.1a. Activity, streak và XP

- Qualified activity v1 gồm: hoàn thành một bài có điều kiện hợp lệ; finalization một practice/mock; hoặc hoàn thành một lượt review đến hạn. Chỉ mở tab, gửi chat hoặc vào call không tự cộng thành tích học.
- XP đề xuất: lesson 10, assessment 10, due-card review 1; cap 50 XP/ngày. Mỗi nguồn chỉ được thưởng một lần theo user + localDate + activityType + source root. Giữ khóa duy nhất cho AwardEvent thay vì chỉ kiểm tra trước bằng C#.
- ActivityEvents là nguồn của streak/XP. Nếu triển khai GAME-01, bổ sung XpAwards với unique key trên bộ trường nêu trên và DailyActivitySummary để đọc nhanh.
- Streak là số ngày học địa phương liên tiếp có ít nhất một qualified activity. Event lưu TimeZoneId và LocalDate tại thời điểm phát sinh; đổi timezone chỉ áp dụng cho event tương lai.
- Thời gian học được ghi nhãn ước tính: heartbeat có sequence, khoảng đóng góp tối đa 30 giây mỗi lần; hợp nhất khoảng active trùng giữa nhiều tab trước khi tổng hợp. Nếu cần chỉ số này, bổ sung ActivitySessions/ActivityIntervals thay vì cộng dồn mọi heartbeat vào một counter.
- Bảng xếp hạng nhóm chỉ xét người còn là member và XP trong cửa sổ tuần theo timezone nhóm được cấu hình; UI ghi rõ cách tính. GAME-01 là Should, không chặn P2 nếu chưa làm phần award/leaderboard.

### 17.2. Claim và lease

- BackgroundJobs có State, DueAt, LeaseUntil, LeaseToken, Attempts, LastErrorCode và LastErrorAt.
- Claim dùng một atomic SQL statement/transaction với update lock và skip rows đang được worker khác giữ, ví dụ cơ chế UPDLOCK/READPAST phù hợp isolation đã cấu hình; có integration test trên SQL Server.
- LeaseToken là giá trị mới cho mỗi claim. Heartbeat gia hạn; commit trạng thái yêu cầu token trùng và lease hợp lệ.
- Tối đa 4 attempts tổng cộng; retry tạm sau 15, 60, 300 giây cộng jitter nhỏ. Permanent error đi failed ngay.
- Worker crash được nhận lại sau lease expiry; job chưa hoàn thành không bị mất chỉ vì process chết.
- Retry thủ công tạo generation/attempt mới phù hợp nhưng giữ audit lỗi cũ; không xóa lịch sử để che trạng thái.

### 17.3. Outbox

Transaction ghi thay đổi nghiệp vụ và OutboxEvent cùng nhau. Worker đọc event đã commit, phát cho consumer, đánh processed sau ACK. Nếu crash giữa phát và đánh dấu, event có thể phát lại.

Consumer cập nhật dữ liệu dẫn xuất phải ghi ConsumerInbox và cập nhật projection trong cùng transaction. Broadcast realtime cũng có eventId để client dedup. Nền tảng tham khảo pattern tại [T11](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos); project này tự thiết kế bảng outbox trên SQL Server.

- Payload chứa reference/version và dữ liệu tối thiểu cần dùng; không nhét binary/token.
- AggregateVersion giúp bỏ event cũ khi áp dụng projection trạng thái, nhưng không dùng một “last global version” để bỏ mất các sự kiện độc lập.
- Consumer thống kê cộng dồn dùng inbox từng EventId; projection snapshot dùng CAS theo aggregate version.
- Job/outbox admin view có hàng đợi, lỗi, số retry, tuổi event và action retry theo quyền.

### 17.4. Các trường hợp khôi phục bắt buộc

| Lỗi | Kết quả mong muốn |
|---|---|
| API commit attempt nhưng client chưa nhận response | Retry trả attempt đã tạo |
| Submit commit rồi worker analytics chết | Result xem được; projection cập nhật khi worker chạy lại |
| Upload xong nhưng complete chưa gọi | Session hiện uploaded/staging thực; client gọi complete lại hoặc cleanup sau expiry |
| Worker tạo rendition rồi chết trước commit | Lần claim mới xác minh/reuse output hợp lệ; không publish file dở |
| Xóa blob xong nhưng DB chưa đổi Deleted | Retry thấy blob vắng rồi hoàn tất DB |
| Tin nhắn commit nhưng SignalR fanout lỗi | REST history/catch-up thấy tin; outbox thử phát lại |
| Call participant đã vào nhưng webhook lỡ | Reconciler đồng bộ theo provider và admission hiện hành |
| Kick đã commit nhưng provider tạm lỗi | Chặn thao tác/token mới; retry remove và theo dõi độ trễ thu hồi |

<a id="s18"></a>
## 18. Bảo mật và quyền truy cập

Đây là các hành vi cần triển khai vì sản phẩm có tài khoản, dữ liệu riêng, file upload và phòng gọi; không phải checklist yêu cầu người dùng xác nhận trước khi làm project.

| Khu vực | Yêu cầu thực thi |
|---|---|
| Identity | Dùng password hashing/session management của Identity; email/reset token một lần, có expiry; rate-limit login/reset |
| Cookie/CSRF | Cookie HttpOnly/Secure; antiforgery trên unsafe browser methods; không lưu access secret trong localStorage |
| Authorization | Kiểm tra owner/enrollment/group membership trên từng use case, cả REST và hub |
| Revocation | MembershipVersion + active check; hủy realtime subscription và call admission; xử lý giới hạn URL/token đã cấp như mục 13/16 |
| Exam | DTO riêng cho bài đang làm, grader và result; không serialize entity trực tiếp |
| Text | Render text đã encode; rich text theo allowlist sanitizer; link external có kiểm soát |
| Upload | Purpose/size/quota, signature/probe, staging private, canonical immutable và path server sinh |
| Media access | Asset ACL theo tài nguyên cha; ticket scope và TTL; kiểm tra attachment sau khi đổi group |
| External URL | Chỉ nhận provider embed allowlist; không tải URL tùy ý từ client vào mạng nội bộ |
| Secrets | User-secrets/env/secret store; không commit SMTP, blob key hoặc LiveKit secret |
| Webhooks | Kiểm tra chữ ký body gốc, dedup event và mapping tài nguyên |
| Internal notifier | Service credential riêng và network policy; không cho browser gọi endpoint fanout tùy ý |
| Logs | Không log mật khẩu, cookie, token, SAS, raw audio; hạn chế email/nội dung chat ở log mặc định |
| Recording/calls | Ghi âm cá nhân private mặc định; chia sẻ rõ ràng; P3 không có recording cuộc gọi |
| Admin | Audit publish, role/membership change, retry job và nội dung bị gỡ |
| Rate/quota | Hạn mức theo user/operation; quota upload kiểm tra nguyên tử, không chỉ đếm trước rồi ghi |

Rate limiter trong memory là theo từng API instance. Khi chạy nhiều instance và cần một quota tổng, dùng shared store hoặc gateway, không giả định middleware local chia sẻ counter; xem [T12](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).

### 18.1. Retention và dữ liệu nguồn

- Gắn OriginType, SourceUrl, Attribution và UsageBasis cho tài liệu/media khi publish.
- Nội dung của website khảo sát chỉ dùng để tham khảo chức năng; không lấy toàn bộ đề, audio, transcript, giao diện hoặc branding làm seed của project.
- Audio/video demo dùng bản tự thu hoặc nguồn được phép sử dụng; transcript/IPA có người biên soạn/nguồn xác định.
- Người dùng có thể xóa note/card/recording cá nhân; việc xóa account xử lý theo workflow có audit và trạng thái pending, không cascade mù qua chat/lịch sử chung.
- Takedown media chặn phát trước, sau đó xử lý object cleanup; kết quả bài làm giữ raw score và metadata tối thiểu, không tiếp tục phân phối nội dung đã bị gỡ.

<a id="s19"></a>
## 19. Hiệu năng, quan sát và vận hành

### 19.1. Mục tiêu đo, chưa phải kết quả đạt được

Mốc benchmark tham chiếu: một máy phát triển 8 logical cores, RAM 16 GB; ghi rõ OS, container limits, dataset, tool và số instance trong báo cáo. Nếu phần cứng khác thì đo lại và công bố cấu hình thật.

| Chỉ số | Mục tiêu ban đầu / phạm vi |
|---|---|
| API query thông thường | p95 < 500 ms với 50 virtual users; không tính binary media/first cold start |
| Save answer | p95 < 500 ms, không nhận đáp án trùng/sau deadline |
| Full mock grading 200 câu | p95 < 2 giây trong dataset demo; đo riêng lúc 20 submit đồng thời |
| Chat | 100 connection, 10 message/giây tổng; độ trễ commit→hiển thị p95 < 1 giây trong môi trường test |
| Catch-up | Lấy 50 tin mới từ DB bằng cursor, không bỏ sót tin đã commit |
| Upload | File lớn không được buffer toàn bộ vào RAM API; kiểm tra process memory khi hai upload cùng lúc |
| Media jobs | Báo hàng đợi và thời gian xử lý theo duration/codec; chưa đặt SLA transcode cố định |
| Calls | 2 thiết bị/2 mạng hoạt động; demo nhóm tối đa 6; lần thứ 7 bị từ chối admission |
| Reliability | Các bất biến INV phải giữ khi retry, crash, reconnect và chạy hai worker |

Các ngưỡng cần hiệu chỉnh sau lần benchmark đầu. Đúng dữ liệu và hành vi phục hồi là gate bắt buộc; thành tích throughput chỉ được ghi vào CV sau khi đo.

### 19.2. Metrics/log/trace

- HTTP latency/error theo route, DB duration/deadlock/concurrency conflict.
- Attempt started/submitted/expired, autosave conflict và request sau deadline.
- Job queue depth/oldest age/retry/permanent failure; outbox lag/duplicate.
- Upload byte count, processing duration, missing blob và orphan cleanup count.
- Active connections, reconnect, chat ACK/fanout latency và catch-up count.
- Call provisioning/join/leave/rejected/full, provider error, revocation lag và reconciliation drift.
- CorrelationId xuyên request → DB event → job → notifier; log ID nghiệp vụ vừa đủ, không dump toàn payload riêng tư.

### 19.3. Chạy local và deploy

| Profile | Services |
|---|---|
| core | web, API, Worker, SQL Server, Azurite; mail sink/dev sender tùy môi trường |
| social | core + Redis và notifier |
| calls | social + LiveKit và cấu hình mạng/TURN cho môi trường tương ứng |

- Một file cấu hình mẫu mô tả đầy đủ biến môi trường và feature flags, không chứa production secret.
- API và frontend cùng origin qua reverse proxy; HTTPS ở môi trường công khai. Localhost có thể dùng dev certificate được người triển khai cấu hình.
- Migration là bước riêng, có kiểm tra và backup khi cần; không để nhiều API instance tranh nhau auto-migrate lúc startup.
- Lưu Data Protection keys bền vững và chia sẻ đúng giữa API instances để cookie không vô hiệu sau restart/scale.
- Health live kiểm tra process; ready kiểm tra SQL/storage cần thiết. Calls provider hỏng chỉ làm module Calls degraded, không làm mất khả năng đọc bài đã có nếu core còn hoạt động.
- Backup/restore gồm SQL và media manifests/object storage; diễn tập restore dữ liệu mẫu và kiểm tra ref không gãy.
- Không hứa triển khai call ngoài internet chỉ bằng compose local. Runbook phải ghi DNS/TLS, public ports, UDP/TURN và kiểm tra hai mạng theo hướng dẫn provider.

<a id="s20"></a>
## 20. Ma trận tình huống kiểm thử

### 20.1. Integration và hành vi dữ liệu

Các test transaction/concurrency dùng SQL Server thực hoặc container. EF InMemory không là bằng chứng cho khóa, unique index, rollback hay isolation của SQL Server.

| ID | Setup / kích hoạt | Kết quả phải kiểm tra | Gate |
|---|---|---|---|
| DB-01 | 20 request tạo attempt cùng idempotency key | Một attempt và một item snapshot; cùng resource ID | P0 |
| DB-02 | Cùng key nhưng hai payload khác nhau | Một thành công, request khác 409; không dữ liệu trộn | P0 |
| DB-03 | Hai answer update cùng ExpectedETag | Một update thành công, một 412; không lost update | P0 |
| DB-04 | Autosave đua với submit | Các thao tác serialize; answer set cuối nhất quán với ACK | P0 |
| DB-05 | Submit thủ công đua expiry job | Một result, một finalization, một bộ event nguồn | P0 |
| DB-06 | Gửi đáp án khi deadline đã qua nhưng job chưa chạy | Server từ chối đáp án mới | P0 |
| DB-07 | Gây lỗi sau ghi status nhưng trước insert result | Rollback hoàn toàn; retry finalization thành công | P0 |
| DB-08 | Editor publish v2 khi user đang làm v1 | User chấm theo v1; payload result chỉ ra version | P0 |
| DB-09 | Đưa OptionId thuộc câu khác vào answer | Từ chối; DB không có answer sai quan hệ | P0 |
| DB-10 | Hai plan generation chạy đồng thời | Không trùng task/slot; task đã xong giữ nguyên | P0 |
| DB-11 | Phát completion event 10 lần | Tiến độ/XP chỉ tăng theo một nguồn hợp lệ | P0/P2 |
| DB-12 | Hai thiết bị review một card cùng version | Một CardReview được áp dụng; thiết bị cũ 412 | P1 |
| DB-13 | Retry ReviewEventId | Một lịch review; response replay hoặc trạng thái tương đương | P1 |
| DB-14 | Phiên playback cũ gửi progress sau phiên mới | Không ghi đè resume của phiên hiện hành | P1 |
| DB-15 | Sửa transcript của lesson đang có notes | Version cũ/anchors cũ còn giải nghĩa được | P1 |
| DB-16 | Retry import sau một số row đã commit | Không sinh lại row đã có; lỗi được chỉ đúng dòng | P0 |
| DB-17 | Hai worker cùng claim một job | Chỉ một lease hợp lệ được commit output | P0 |
| DB-18 | Worker chết/mất lease khi xử lý | Worker mới nhận lại; worker cũ không ghi Ready | P0 |
| DB-19 | Hai request gửi chat cùng ClientMessageId | Một message/sequence, không cộng unread hai lần | P2 |
| DB-20 | Message commit rồi notifier bị ngắt | Catch-up lấy được tin, outbox phát lại không trùng UI | P2 |
| DB-21 | Hai read cursor đến ngược thứ tự | LastReadSequence không lùi | P2 |
| DB-22 | Invite còn một lượt, hai người redeem | Chỉ một lượt được dùng; UsedCount không vượt MaxUses | P2 |
| DB-23 | Host mất mạng, hai moderator claim | Một host mới; command host cũ bị từ chối | P2 |
| DB-24 | Kick đồng thời với gửi tin | Lệnh được linearize theo membership transaction; sau revoke không có tin mới được nhận trái quyền | P2 |
| DB-25 | Room còn một slot call, 10 join đồng thời | Không quá capacity gồm pending; từ chối phần còn lại | P3 |
| DB-26 | Provider webhook lặp/đến sai thứ tự | Không tăng participant count sai hoặc hồi sinh closed call | P3 |
| DB-27 | Join token được yêu cầu ngay sau membership revoke | Backend từ chối, provider remove được retry | P3 |
| DB-28 | Đổi timezone gần nửa đêm | Event cũ giữ local date snapshot, event mới dùng timezone mới | P2 |
| DB-29 | Hai lần cleanup chạy cùng asset | Xóa idempotent, không tác động asset đang được tham chiếu | P1 |
| DB-30 | Rebuild analytics từ dữ liệu nguồn | Kết quả bằng projection hợp lệ, không nhân đôi source | P1/P2 |

### 20.2. Media, quyền và trình duyệt

| ID | Kịch bản | Kết quả |
|---|---|---|
| M-01 | Upload file vượt giới hạn hoặc giả extension | Từ chối/Failed có lý do; không Ready |
| M-02 | Upload bị ngắt rồi tiếp tục | Reuse UploadSession/block IDs, không tạo asset ngoài ý muốn |
| M-03 | Ghi lại staging bằng upload grant cũ sau complete | Canonical không đổi; ETag mismatch được phát hiện nếu chưa copy |
| M-04 | FFmpeg timeout/file lỗi | Giải phóng tài nguyên; Failed có code; retry theo policy |
| M-05 | Seek vào giữa MP4/audio | 206 và byte range đúng; 416 với range vượt |
| M-06 | URL playback hết hạn giữa bài | Xin quyền mới có kiểm tra và giữ vị trí; không retry vô hạn |
| M-07 | Asset HLS thiếu segment | Chưa Ready hoặc báo corrupt, không publish rendition hỏng |
| M-08 | Gọi trực tiếp URL asset private bằng user khác | Từ chối; không dựa vào việc giấu nút |
| M-09 | Unicode IPA, tiếng Việt và emoji | Lưu/đọc/tra cứu đúng; UI có font fallback phù hợp |
| M-10 | Subtitle timestamp âm/vượt duration | Import báo lỗi cụ thể theo segment |
| M-11 | Trình duyệt từ chối mic/camera | Có trạng thái và hướng dẫn thử lại; học nghe vẫn hoạt động |
| M-12 | Hai thiết bị dùng mạng khác nhau | Call kết nối và phục hồi theo cấu hình; kiểm tra trường hợp cần TURN |
| M-13 | Member bị remove khi giữ tab chat/call | Dừng thao tác mới, loại subscription/provider; đo độ trễ và giới hạn token tồn tại |
| M-14 | Xóa recording đã chia sẻ | Playback access mới bị chặn và attachment báo đã gỡ |

### 20.3. Unit, contract và end-to-end

- Unit: planner rules, prerequisite cycle detection, normalization, scoring, scheduler, state transition và authorization policy thuần.
- Contract: OpenAPI, error codes, DTO không chứa đáp án khi chưa disclose, event schema và webhook signature.
- End-to-end: J01–J05, có refresh/offline/reconnect và hai browser contexts để thử quyền.
- Tải: benchmark trước/sau index với execution plan, dataset và script được commit; không chỉ đo một request trên DB trống.
- Fault injection chỉ bật ở môi trường test/demo được cấu hình: kill worker, tạm ngắt notifier/provider, gây lỗi giữa transaction.
- Test có FakeClock cho deadline/SRS/timezone để chạy nhanh và ổn định; vẫn có integration test clock/lease trên SQL.

<a id="s21"></a>
## 21. Dữ liệu mẫu và kịch bản demo CV

### 21.1. Seed nghiệp vụ

| Hạng mục | Dữ liệu tối thiểu để nghiệm thu |
|---|---|
| Tài khoản | Admin, ContentEditor và ba Learner; tài khoản demo chỉ tạo trong môi trường demo/test |
| Chương trình | ToeicLR + kiến thức nền; các SkillTag cần cho 7 Part |
| Path | Foundation/Core/Exam Practice; ít nhất một path có 14 ngày, mỗi ngày 2–3 nhiệm vụ hợp lý |
| Lesson | 6 bài nền tảng có nội dung/lời giải, đủ dữ liệu để hoàn thành tuần đầu |
| Assessment | Một placement 20 câu, hai mini test 20 câu; dữ liệu cho một full mock 200 câu được biên soạn riêng hoặc đánh nhãn fixture |
| Nghe | Hai audio có quyền sử dụng, mỗi audio tối thiểu 10 segment có timestamp/transcript/bản dịch |
| Video | Hai video ngắn có quyền sử dụng, VTT và thumbnail; một asset cố ý lỗi để demo recovery |
| Từ vựng | 50 sense có nghĩa/ví dụ, thẻ cá nhân và nhiều ngữ cảnh; IPA/accent có nguồn |
| Phát âm | Một curriculum demo với ít nhất 8 phoneme lessons và minimal pairs |
| Nhóm | Hai nhóm, membership khác nhau; một invitation có một lượt dùng để thử race |
| Chat | Lịch sử đủ dài để phân trang/catch-up, tin đã sửa/xóa, một recording chia sẻ |
| Calls | Một room hợp lệ và một room người dùng hiện tại không có quyền |

Một full mock fixture chỉ dùng kiểm thử format/timer/grade phải mang IsFixture = true và nhãn “dữ liệu demo kỹ thuật”. Để quảng bá chất lượng ôn thi thực, cần một bộ nội dung được biên soạn/kiểm tra tương ứng; không tự biến fixture thành đề thi chính thức.

### 21.2. Seed hiệu năng tách biệt

- Tạo bằng script có random seed và manifest: 10.000 user giả, 100.000 attempt, 1.000.000 answer và 200.000 message là profile mở rộng.
- Có profile nhỏ hơn để chạy trên máy ít RAM. Không yêu cầu mọi lần test tạo bộ lớn nhất.
- Bản ghi hiệu năng gắn IsSynthetic/source riêng; không trộn vào demo thành tích người dùng.
- So sánh truy vấn trước/sau index với cùng dữ liệu và query parameters; lưu execution plan và báo cáo.

### 21.3. Demo 5–7 phút

1. Đăng nhập Learner, xem lộ trình và hoàn thành một nhiệm vụ nghe.
2. Loop câu, xem transcript, lưu một từ có IPA vào thẻ cá nhân và ôn thẻ.
3. Làm mini test; mở hai tab để chứng minh stale update được xử lý; submit lặp trả cùng kết quả.
4. Admin upload media, xem Processing → Ready; cho một job lỗi rồi retry.
5. Hai Learner chat, ngắt một kết nối rồi reconnect và catch-up; thử gửi duplicate ClientMessageId.
6. Mở room học chung/call, dùng hai thiết bị, đóng room hoặc thu hồi membership và quan sát xử lý.

Các bước 5–6 chỉ xuất hiện khi P2/P3 đã nghiệm thu. Video demo nói rõ dữ liệu giả lập và phần nào đang hoạt động thật.

### 21.4. Mô tả CV sau khi hoàn thành

Ví dụ cách mô tả phạm vi, chỉ dùng những dòng tương ứng phần đã triển khai:

- Xây dựng nền tảng học tiếng Anh bằng ASP.NET Core và SQL Server, hỗ trợ lộ trình, nội dung có phiên bản, timed assessments và ôn tập cá nhân.
- Thiết kế autosave/finalization idempotent, xử lý concurrency và background jobs có lease; kiểm chứng bằng integration tests với SQL Server.
- Tích hợp object storage và pipeline xử lý audio/video, cùng chat lưu bền qua SignalR và đồng bộ sau reconnect.
- Tích hợp voice/video rooms qua WebRTC provider, kiểm tra quyền nhóm, giới hạn participant và webhook deduplication.

Chỉ thêm p95, tải đồng thời hoặc tỉ lệ cải thiện sau khi có script đo và báo cáo tương ứng. Liên kết repo, demo, OpenAPI và tài liệu quyết định kiến trúc.

<a id="s22"></a>
## 22. Backlog triển khai và điều kiện hoàn thành

### 22.1. Các mốc công việc

| Milestone | Công việc cụ thể | Phụ thuộc | Đầu ra bắt buộc |
|---|---|---|---|
| M00 — Bootstrap | Solution, web shell, compose core, configuration, health, migrations, CI cơ bản | Không | Một lệnh dựng local, API ready, web gọi được API |
| M01 — Identity & Content | Identity, profile, program/tags, version registry, CMS draft/validate/publish, import dry-run | M00 | Login, role enforcement, published version immutable |
| M02 — Assessment | Blueprint, attempt snapshot, answer version, autosave, finalization, raw grade, result | M01 | J02/J04; DB-01…09 và không lộ answer key |
| M03 — Learning Path | Placement, templates/prerequisites, enrollment, daily tasks, completion và dashboard | M02 | J01; DB-10/11; rules và reasonCodes |
| M04 — Media Core | Blob/Azurite, upload lifecycle, durable job lease, audio playback/range và cleanup | M01 | Upload thật, xử lý thật, test retry/crash |
| P0 Gate | Kết nối M01–M04 thành hành trình; seed, tài liệu setup và demo | M01–M04 | Học và thi thử được end-to-end |
| M05 — Rich Learning | Segments, dictation, video/VTT, resume, IPA và recording private | P0 | J03; M-01…11 liên quan |
| M06 — Review Engine | Dictionary, cards/context, SRS, mistakes, skill analytics | M05 | DB-12…15/30; không trộn điểm luyện lại vào mock |
| P1 Gate | Hoàn thiện quản trị media/nội dung và vòng ôn | M05/M06 | Bản học cá nhân hoàn chỉnh |
| M07 — Community | Groups, invites, quyền, chat REST/cursors, attachments, read state | P1 | DB-19/21/22/24; private ACL |
| M08 — Realtime | SignalR, notifier/outbox, Redis, presence, reconnect, room playback | M07 | DB-20/23; J05 phần chat/học chung |
| P2 Gate | Test hai user, mất mạng, nhiều tab và revoke | M07/M08 | Chat lưu bền, catch-up và room state nhất quán |
| M09 — Calls | LiveKit adapter, provisioning, admissions, tokens, webhook, reconcile, thiết bị | P2 | DB-25…27, M-12/13, call giữa hai mạng |
| P3 Gate | Runbook public deployment, lỗi provider và chặn admission trái quyền | M09 | Voice/video call hoạt động đúng phạm vi |
| M10 — Portfolio | Benchmark, execution plans, CI hoàn chỉnh, video demo, ADR/runbooks | Theo bản phát hành | Chỉ số đo thật và tài liệu reviewer có thể chạy lại |

M02 và M04 có thể phát triển độc lập sau M01, nhưng tích hợp tuần tự và kiểm tra ràng buộc content/media trước P0 Gate. Mỗi milestone là một change có thể review; không tạo sẵn toàn bộ cấu trúc rồi để các module chứa placeholder.

### 22.2. Definition of Done chung

- Có acceptance criteria tương ứng và test cốt lõi đã chạy.
- Migration tạo được DB mới và nâng cấp từ milestone trước; index/constraint có tên rõ.
- API có OpenAPI, DTO/error codes thực tế và ví dụ gọi.
- Không dùng list/in-memory làm nơi lưu chính cho dữ liệu bắt buộc bền vững.
- Không nuốt exception rồi trả thành công; không fake response thay integration đang lỗi.
- UI có loading/empty/error/retry và không báo lưu thành công khi backend chưa ACK.
- Source/attribution có trong seed, asset và nội dung được publish.
- README ghi rõ feature flags, cách chạy, giới hạn, dependency và các phần chưa hoàn thành.
- Có log/metric đủ để tìm lỗi use case vừa thêm.
- Khi scope thay đổi, cập nhật đặc tả/ADR và test liên quan trong cùng change.

### 22.3. ADR cần tạo khi triển khai

1. ADR-001: modular monolith và transaction boundary.
2. ADR-002: cookie same-origin/CSRF và authorization trong SignalR.
3. ADR-003: published content version và private grading snapshot.
4. ADR-004: SQL job queue/outbox, claim lease và retry strategy.
5. ADR-005: Azure Blob/Azurite, staging→canonical và media access.
6. ADR-006: chat persistence, ordering, dedup và read cursors.
7. ADR-007: LiveKit admissions, giới hạn thu hồi token và reconciliation.
8. ADR-008: planner/SRS v1, cách tính thống kê và giới hạn điểm ước lượng.

### 22.4. Các giá trị cấu hình cần chốt ở repository

| Nhóm | Cấu hình / mặc định từ đặc tả |
|---|---|
| FeatureFlags | EnableCommunity, EnableCalls, EnableAi = false; chỉ bật giai đoạn đã triển khai |
| Assessments | BatchSize 20, autosave debounce 500 ms, blueprint/policy version |
| Uploads | Size/duration theo purpose, staging grant 15 phút, orphan age 24 giờ |
| Playback | Access TTL 2 phút; HLS generation; allowed embed hosts |
| Jobs | Lease phù hợp loại job, heartbeat ngắn hơn lease, tổng attempts 4, backoff 15/60/300 giây |
| Realtime | Presence heartbeat 20 giây, TTL 60 giây, typing TTL 5 giây |
| Community | Group max 50; message 4.000 ký tự; page max 50 |
| Calls | Max participants 6; pending admission 30 giây; token TTL 60 giây; reconciliation demo 5 giây |
| Planner | Tối đa 3 core tasks/ngày; review budget 30%; rule/template versions |
| Review | Scheduler v1, interval cap 180 ngày; normalization policy version |
| Idempotency | Receipt retention ≥24 giờ; domain unique keys không hết hiệu lực theo receipt |
| Storage/Provider | Endpoint/credential từ secret configuration; không ghi giá trị thật vào spec/repo |

<a id="s23"></a>
## 23. Hướng dẫn cho AI triển khai

### 23.1. Nhiệm vụ triển khai

Triển khai EnglishHub theo đặc tả này, bắt đầu M00, hoàn thành từng milestone rồi mới đi tiếp. Dùng quyết định trong tài liệu làm mặc định; ghi giả định cụ thể vào ADR khi một chi tiết cần bổ sung. Giữ mục tiêu backend và độ đúng dữ liệu làm trọng tâm.

### 23.2. Trình tự làm việc cho mỗi milestone

1. Đọc mục liên quan, milestone, INV và test case IDs.
2. Liệt kê use cases, DTO, dữ liệu, constraints và transaction trước khi tạo endpoint.
3. Tạo migration và test ràng buộc/đồng thời cần thiết.
4. Viết application service, storage/provider adapter và API/hub gọi cùng logic.
5. Kết nối frontend vừa đủ để demo use case; xử lý cả lỗi và trạng thái chờ.
6. Chạy unit/integration/contract/end-to-end phù hợp với thay đổi.
7. Cập nhật OpenAPI, README, source seed và ADR.
8. Báo chính xác phần hoàn thành, kiểm thử đã chạy và phần còn thiếu; không đánh dấu milestone xong chỉ vì code biên dịch.

### 23.3. Ràng buộc triển khai

- P0–P3 không gọi LLM, không cần AI API key, không cần STT hay dịch tự động để hành trình chính hoạt động.
- Full mock/assessment phải chấm bằng đáp án/version xác định sẵn.
- Frontend không được truy cập trực tiếp DB hoặc có key toàn quyền của blob/LiveKit.
- Không gộp binary media vào JSON/base64 lưu SQL cho tiện.
- Không chạy công việc transcode dài trong request API.
- Không coi SignalR events là lịch sử chat duy nhất hoặc xác nhận commit.
- Không tự thay LiveKit bằng truyền video base64 qua WebSocket.
- Không thêm microservice, event broker, Kubernetes hoặc vector DB nếu chưa có yêu cầu và ADR giải thích lợi ích cho phạm vi hiện tại.
- Có thể dùng test double cho provider trong unit/CI, nhưng bản demo của module phải có integration thật và ghi rõ môi trường.
- Dữ liệu demo phải có nhãn; không đưa số liệu giả hoặc tính năng stub vào mô tả thành tích.
- Chủ động hoàn thành thao tác phát triển cục bộ trong phạm vi đã giao. Quyết định phát sinh chi phí dịch vụ hoặc xuất bản ra ngoài được chuẩn bị thành cấu hình/kết quả cụ thể để người dùng xem xét.

### 23.4. Checklist trước khi yêu cầu review

- [ ] Feature IDs, INV IDs và test IDs liên quan được liệt kê trong mô tả change.
- [ ] Constraint/unique index tồn tại trong migration, không chỉ trong validation C#.
- [ ] Có test đồng thời/retry khi use case có nguy cơ tương ứng.
- [ ] API trả đúng 401/403/404/409/412/422; không trả HTTP 200 cho lỗi nghiệp vụ.
- [ ] Không lộ đáp án, token, private URL hoặc dữ liệu user khác.
- [ ] Background job tồn tại qua restart và có failure state.
- [ ] UI không tạo thông báo thành công giả khi chưa được server xác nhận.
- [ ] OpenAPI và setup chạy lại được từ repository mới clone.
- [ ] Các phụ thuộc ngoài được cấu hình/ghi rõ; module disabled không làm core chết.

### 23.5. Hình thức bàn giao code

Repository phải có README, compose profiles, sample env không chứa secret, migrations, seed manifests, test commands, OpenAPI, runbooks và ít nhất các ADR tương ứng phần đã xây. Mỗi milestone nên có một demo script hoặc hướng dẫn thao tác ngắn để người khác kiểm tra được kết quả.

<a id="s24"></a>
## 24. Điểm mở rộng AI sau này

Phần này định hướng boundary, không phải yêu cầu triển khai ở P0–P3. Không tạo các endpoint trả kết quả AI giả để lấp giao diện.

| Ý tưởng P4 | Dữ liệu nền đã chuẩn bị | Điều kiện trước khi bật |
|---|---|---|
| Giải thích câu sai theo ngữ cảnh | QuestionVersion, explanation, attempt result | Bộ câu kiểm thử, nguồn chuẩn, quota và version prompt |
| Gợi ý lộ trình sâu hơn | SkillEvidence, task outcomes | So sánh với rule baseline, cho người dùng biết lý do |
| STT cho recording | Private media + consent + source | Quyền xử lý, chi phí, accuracy dataset, retention |
| Chấm phát âm | Accent/IPA, audio mẫu, recording | Speech-specific evaluator; không lấy transcript đúng làm bằng chứng phát âm đúng |
| Chữa bài viết | Bài viết, rubric version, feedback history | Tiêu chí, bộ đánh giá và nhãn điểm luyện tập |
| Gợi ý bài tương tự | Taxonomy và content version | Kiểm tra quyền nội dung, đánh giá retrieval |

Khi làm P4, tạo tài liệu bổ sung về nhà cung cấp, ngân sách, bảo vệ dữ liệu, evaluation và failure modes. Giữ rule-based planner, chấm khách quan và nội dung học cốt lõi là các luồng hoạt động được khi AI không khả dụng.

<a id="s25"></a>
## 25. Nguồn tham khảo

### 25.1. Khảo sát sản phẩm

Tất cả truy cập ngày 09/11/2026. “Advertised” mô tả nội dung nhà cung cấp công bố, không phải xác nhận độc lập hiệu quả học tập, số người dùng hay chất lượng điểm dự đoán.

| ID | Nguồn | Phạm vi sử dụng trong tài liệu |
|---|---|---|
| C01 | [ByeTOEIC — trang chủ](https://byetoeic.com/) | Đã đọc UI; danh mục module, lộ trình và giới thiệu tính năng |
| C02 | [ByeTOEIC — luyện nghe](https://byetoeic.com/listening) | Đã đọc UI danh sách/lọc; vào bài cụ thể dẫn tới đăng nhập |
| C03 | [Đậu English — trang chủ](https://dauenglish.com/) | Đã đọc UI; dautoeic.com chuyển hướng đến đây |
| C04 | [Đậu English — luyện nghe chép](https://dauenglish.com/listen/nghe-chep) | Đã đọc UI danh mục và thao tác yêu cầu đăng nhập |
| C05 | [STUDY4 — thư viện đề](https://study4.com/tests/) | Danh mục đề công khai |
| C06 | [STUDY4 — Complete TOEIC](https://study4.com/lp/complete-toeic-v2/) | Giới thiệu lộ trình, bài giảng, dictation và các mảng học |
| C07 | [STUDY4 — flashcards](https://study4.com/flashcards/) | Bộ từ và hướng dẫn tạo thẻ từ highlights |
| C08 | [PREP — TOEIC](https://prepedu.com/vi/toeic) | Đầu vào, lộ trình và cấu trúc chương trình công khai |
| C09 | [PREP — Test Practice](https://prepedu.com/vi/toeic/test-practice) | Giới thiệu phòng luyện đề và các kỹ năng |
| C10 | [VOCA — Study Path for Skills](https://www.voca.vn/skills) | Phân chia kiến thức nền và kỹ năng |
| C11 | [VOCA — hướng dẫn Pronunciation](https://www.voca.vn/guide/huong-dan-su-dung-he-thong-pronunciation-7) | Cấu trúc học phát âm có lý thuyết, video, thực hành, kiểm tra |
| C12 | [ProTOEIC — trang chủ](https://protoeic.com/) | Giới thiệu luồng tự học, lưu câu ôn và hỏi đáp |

### 25.2. Tài liệu kỹ thuật và định dạng

| ID | Nguồn chính thức | Phạm vi |
|---|---|---|
| T01 | [ETS — TOEIC Listening and Reading](https://www.ets.org/content/ets-org/ca/en/toeic/test-takers/about/listening-reading.html) | Định dạng tổng thể LR và thời lượng section |
| T02 | [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) | Lựa chọn .NET LTS |
| T03 | [Microsoft — Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-install-azurite) | Object storage emulator local |
| T04 | [EF Core — Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) | Ranh giới transaction |
| T05 | [EF Core — Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) | Optimistic concurrency/rowversion |
| T06 | [SignalR — Users and groups](https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-10.0) | Connection/group/reconnect và quyền |
| T07 | [ASP.NET Core — File uploads](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0) | Buffering/streaming và xử lý upload |
| T08 | [LiveKit — Access tokens and grants](https://docs.livekit.io/frontends/reference/tokens-grants/) | Identity, room grants, TTL và lifecycle token |
| T09 | [LiveKit — Deployment](https://docs.livekit.io/transport/self-hosting/deployment/) | Public deployment và điều kiện mạng |
| T10 | [MDN — Signaling and video calling](https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API/Signaling_and_video_calling) | Phân biệt signaling và luồng media WebRTC |
| T11 | [Microsoft — Transactional Outbox](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos) | Tham khảo pattern; cách áp dụng SQL Server là thiết kế riêng của project |
| T12 | [ASP.NET Core — Rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0) | Giới hạn theo instance và kiểm tra tải |

Các tham số nghiệp vụ, scheduler, cấu trúc DB, API và tiêu chí tải trong tài liệu là quyết định đề xuất của EnglishHub. Người triển khai cần kiểm chứng bằng test và benchmark trên môi trường thực tế.

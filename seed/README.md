# Original demonstration content / Nội dung demo nguyên gốc

`foundation-lessons.json` chứa 8 bài có nội dung Anh–Việt, ví dụ và quiz. Kết hợp 6 bài đầu trong DemoSeed/DemoLearningSeed, thư viện có 14 bài nền tảng. Tác giả project cung cấp các đoạn này theo CC0; chúng không sao chép từ nền tảng khảo sát hoặc ETS.

DemoAssessmentSeed bổ sung 20 câu grammar khác nhau; mini reading có 5 email/ngữ cảnh × 4 câu. Placement dùng pool câu hỏi demo và quy tắc phân tầng v1, chưa là bài đánh giá trình độ đã được kiểm định.

DemoFillBlankSeed bổ sung 5 câu gõ đáp án công sở theo CC0, có biến thể được biên soạn và policy v1. Đây là bài luyện riêng, không phải một Part thuộc blueprint TOEIC LR.

DemoPathSeed ghim 14 bài hiện có và 4 mốc practice thành 3 stage của lịch Foundation 14 ngày học gợi ý. Node có prerequisite và điều kiện quiz/raw-score rõ ràng. Seed chỉ tạo lần đầu; thay đổi version nội dung/danh mục sau đó không viết lại lộ trình hoặc enrollment đã ghim.

`core-curriculum.json` có 8 bài, 16 câu quiz và 12 câu luyện mới. `exam-curriculum.json` có 6 bài, 12 câu quiz và 13 câu luyện mới (hai nhóm hoàn thành đoạn văn × 4 câu và một nhóm ba tài liệu × 5 câu). Tất cả được biên soạn nguyên gốc theo CC0, không sao chép câu hỏi ETS. DemoCoreSeed/DemoExamSeed dùng publisher atomic, chỉ thêm khi chưa có template; checkpoint mới yêu cầu đủ câu trong một lượt và đạt 60%. Xem [Core](../docs/core-curriculum.md) và [Exam Practice](../docs/exam-curriculum.md) về lịch, nguồn tham khảo cấu trúc và giới hạn nội dung. Không có audio, quy đổi 990 hay kiểm định độ khó cho các starter này.

Full mock 200 câu là **fixture kỹ thuật** có counts 6/25/39/30/30/16/54 và nhóm câu nguyên vẹn. Nó kiểm thử blueprint, timer section, autosave và grading; các câu grammar được dùng lại để kiểm tra kỹ thuật, không phải bản mô phỏng nội dung TOEIC LR. `IsFixture=true` loại kết quả khỏi thống kê thành tích.

`listening-scripts.json` chứa3 bài nghe nguyên gốc (9 câu song ngữ,6 câu hỏi). `listening-curriculum.json` ghim timing/hash/bytes; `audio/` chứa MP3 giọng tổng hợp Flite slt cùng thông tin nguồn và notices. Demo Docker opt-in upload thật trước khi publish; không đổi các lộ trình đã ghim. Xem [bộ nghe khởi đầu](../docs/listening-curriculum.md) và [nguồn audio](audio/NOTICE.md). Bản nghe tự nhiên đa giọng và rà soát chuyên môn vẫn còn cần bổ sung.

Synthetic WAV tones in integration tests are technical fixtures only. No tone is shipped as learning speech. Video and a broader reviewed listening corpus remain future work.

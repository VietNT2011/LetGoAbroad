namespace EnglishHub.Domain;

/// <summary>VI: Ghi chú riêng neo vào body bất biến của đúng version bài học. EN: A private note anchors to the immutable body of one lesson version.</summary>
public sealed class LessonAnnotation : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid ContentVersionId { get; set; }
    public ContentVersion ContentVersion { get; set; } = null!;
    public string BlockKey { get; set; } = "body";
    public int AnchorStart { get; set; }
    public int AnchorEnd { get; set; }
    public string Quote { get; set; } = "";
    public string NoteText { get; set; } = "";
    public bool IsHighlighted { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>VI: Offset UTF-16 giống DOM Range; không cắt surrogate pair hoặc chấp nhận quote khác nguồn. EN: UTF-16 offsets match DOM ranges without splitting surrogate pairs or trusting mismatched quotes.</summary>
public static class TextAnchor
{
    public const int MaxQuoteLength = 4000;
    public static bool Boundary(string text, int offset) => offset >= 0 && offset <= text.Length && (offset == 0 || offset == text.Length || !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]));
    public static void Validate(string body, string blockKey, int start, int end, string quote)
    {
        DomainError.Require(blockKey == "body" && start >= 0 && end > start && end <= body.Length && end - start <= MaxQuoteLength && Boundary(body, start) && Boundary(body, end), 422, "invalid_lesson_anchor", "Chọn 1–4000 ký tự trong đúng body bài học; không cắt đôi ký tự Unicode.");
        DomainError.Require(!string.IsNullOrWhiteSpace(quote) && quote == body[start..end], 422, "quote_mismatch", "Đoạn trích không khớp đúng phiên bản bài học.");
    }
}

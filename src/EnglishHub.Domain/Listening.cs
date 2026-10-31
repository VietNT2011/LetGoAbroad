namespace EnglishHub.Domain;

/// <summary>VI: Đoạn nghe thuộc version bài học bất biến, hai bản ngôn ngữ được biên soạn riêng. EN: Authored bilingual segments belong to an immutable lesson version.</summary>
public sealed class TranscriptSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentVersionId { get; set; }
    public int Order { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string TextEn { get; set; } = "";
    public string TextVi { get; set; } = "";
    public string AcceptedAnswersJson { get; set; } = "[]";
    public int NormalizationPolicyVersion { get; set; } = AnswerNormalization.CurrentVersion;
    public bool IgnoreTerminalPunctuation { get; set; } = true;
}

public sealed class ListeningSession : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid ContentVersionId { get; set; }
    public ContentVersion ContentVersion { get; set; } = null!;
    public Guid MediaAssetId { get; set; }
    public MediaAsset Media { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>VI: Chỉ session hiện hành được cập nhật vị trí tiếp tục; ETag chống request cũ ghi đè. EN: Only the explicitly current session can update playback resume, guarded by an ETag.</summary>
public sealed class ListeningResume : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid ContentVersionId { get; set; }
    public Guid SessionId { get; set; }
    public long PositionMs { get; set; }
}

public sealed class ListeningDisclosure
{
    public Guid UserId { get; set; }
    public Guid SegmentId { get; set; }
    public DateTimeOffset FirstDisclosedAt { get; set; }
}

/// <summary>VI: Lần trả lời chỉ thêm, không biến lượt sửa thành điểm thi mới. EN: Append-only segment responses remain separate from assessment and lesson-completion scores.</summary>
public sealed class SegmentAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid ContentVersionId { get; set; }
    public Guid SegmentId { get; set; }
    public int Number { get; set; }
    public string AnswerText { get; set; } = "";
    public bool IsCorrect { get; set; }
    public bool IsAssisted { get; set; }
    public DateTimeOffset AnsweredAt { get; set; }
}

public sealed record DictationToken(string Kind, string? Expected, string? Actual);

/// <summary>VI: Edit distance chỉ giải thích token; quyết định đúng/sai vẫn là exact-match theo policy. EN: Token edit distance explains differences without changing exact normalized grading.</summary>
public static class DictationDiff
{
    public static DictationToken[] Compare(string expected, string actual, int policy, bool ignoreTerminalPunctuation)
    {
        var left = AnswerNormalization.Normalize(expected, policy, ignoreTerminalPunctuation).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var right = AnswerNormalization.Normalize(actual, policy, ignoreTerminalPunctuation).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        DomainError.Require(left.Length <= 200 && right.Length <= 200, 422, "dictation_token_limit", "Mỗi câu tối đa200 token để so sánh.");
        var costs = new int[left.Length + 1, right.Length + 1];
        for(var i = 0; i <= left.Length; i++) costs[i, 0] = i;
        for(var j = 0; j <= right.Length; j++) costs[0, j] = j;
        for(var i = 1; i <= left.Length; i++) for(var j = 1; j <= right.Length; j++)
            costs[i,j] = Math.Min(costs[i-1,j-1] + (left[i-1] == right[j-1] ? 0 : 1), Math.Min(costs[i-1,j] + 1, costs[i,j-1] + 1));
        var result = new List<DictationToken>(); var x = left.Length; var y = right.Length;
        // VI: Khi hòa chọn thay thế, rồi thiếu, rồi thừa để phản hồi xác định. EN: Prefer substitution, then deletion, then insertion for deterministic ties.
        while(x > 0 || y > 0)
        {
            if(x > 0 && y > 0 && left[x-1] == right[y-1]) { result.Add(new("equal", left[--x], right[--y])); }
            else if(x > 0 && y > 0 && costs[x,y] == costs[x-1,y-1] + 1) { result.Add(new("replace", left[--x], right[--y])); }
            else if(x > 0 && costs[x,y] == costs[x-1,y] + 1) { result.Add(new("delete", left[--x], null)); }
            else { result.Add(new("insert", null, right[--y])); }
        }
        result.Reverse(); return result.ToArray();
    }
}

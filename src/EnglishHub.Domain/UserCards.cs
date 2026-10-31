namespace EnglishHub.Domain;

/// <summary>VI: Thẻ tự viết thuộc riêng người học; không suy diễn nghĩa từ từ điển chưa có. EN: Personal authored cards do not imply a dictionary sense or shared scheduling state.</summary>
public sealed class UserCard : VersionedEntity
{
    public Guid UserId { get; set; }
    public string CustomKey { get; set; } = "";
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public int ContentVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public int IntervalDays { get; set; }
    public int SuccessStep { get; set; }
    public int SchedulerVersion { get; set; } = 1;
    public DateTimeOffset? ArchivedAt { get; set; }
    public List<CardSourceContext> Contexts { get; set; } = [];
}

/// <summary>VI: Nội dung chỉ thêm phiên bản; lượt ôn luôn ghim đúng hai mặt đã học. EN: Append-only text versions preserve what each historical review actually presented.</summary>
public sealed class UserCardVersion
{
    public Guid UserCardId { get; set; }
    public int Number { get; set; }
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CardSourceContext
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserCardId { get; set; }
    public string ContextKey { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>VI: Lịch sử chỉ thêm, giữ trạng thái trước/sau cùng phiên bản thuật toán. EN: Append-only review evidence records both scheduler states and the algorithm version.</summary>
public sealed class CardReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserCardId { get; set; }
    public UserCard Card { get; set; } = null!;
    public int ContentVersion { get; set; } = 1;
    public UserCardVersion Content { get; set; } = null!;
    public Guid ReviewEventId { get; set; }
    public CardRating Rating { get; set; }
    public int SchedulerVersion { get; set; }
    public DateTimeOffset ReviewedAt { get; set; }
    public DateTimeOffset BeforeDueAt { get; set; }
    public DateTimeOffset AfterDueAt { get; set; }
    public int BeforeIntervalDays { get; set; }
    public int AfterIntervalDays { get; set; }
    public int BeforeSuccessStep { get; set; }
    public int AfterSuccessStep { get; set; }
}

public enum CardRating { Again, Hard, Good, Easy }
public sealed record CardSchedule(int IntervalDays, int SuccessStep, DateTimeOffset DueAt);

/// <summary>VI: SRS v1 là quy tắc sản phẩm xác định sẵn, không phải mô hình trí nhớ đã kiểm định. EN: This deterministic product schedule makes no validated memory-model claim.</summary>
public static class CardScheduler
{
    public const int Version = 1;
    public static CardSchedule Next(int intervalDays, int successStep, CardRating rating, DateTimeOffset now)
    {
        DomainError.Require(intervalDays is >= 0 and <= 180 && successStep is >= 0 and < int.MaxValue && Enum.IsDefined(rating), 422, "invalid_card_schedule", "Trạng thái hoặc mức ôn không hợp lệ.");
        now = now.ToUniversalTime();
        // VI: Again trở về bước mới và interval0; ngày ôn tính từ UTC hiện tại, không chờ nửa đêm. EN: Again resets both learning state and interval; due instants are elapsed UTC time, not local midnights.
        if (rating == CardRating.Again) return new(0, 0, now.AddMinutes(10));
        var interval = rating switch
        {
            CardRating.Hard => Math.Max(1, (int)Math.Round(intervalDays * 1.2, MidpointRounding.AwayFromZero)),
            CardRating.Easy => Math.Max(4, (int)Math.Round(intervalDays * 2.5, MidpointRounding.AwayFromZero)),
            _ => successStep switch { 0 => 1, 1 => 3, 2 => 7, _ => Math.Max(1, intervalDays * 2) }
        };
        interval = Math.Min(180, interval);
        return new(interval, rating == CardRating.Hard ? successStep : successStep + 1, now.AddDays(interval));
    }
}

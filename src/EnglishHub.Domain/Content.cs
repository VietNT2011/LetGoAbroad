using System.Security.Cryptography;

namespace EnglishHub.Domain;

/// <summary>VI: ETag phản ánh phiên bản ghi. EN: A database concurrency token backs each mutable resource.</summary>
public abstract class VersionedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public byte[] RowVersion { get; set; } = RandomNumberGenerator.GetBytes(8);
}
public enum ContentKind { Lesson = 0, Assessment = 1 }
public enum PublicationStatus { Draft = 0, Published = 1, Archived = 2 }
public enum QuestionType { SingleChoice = 0, FillBlank = 1 }

/// <summary>VI: Gốc nội dung ổn định xuyên các bản phát hành. EN: A stable identity survives publication of new versions.</summary>
public sealed class ContentRoot : VersionedEntity
{
    public string Slug { get; set; } = "";
    public ContentKind Kind { get; set; }
    public Guid? CurrentPublishedVersionId { get; set; }
    public ContentVersion? CurrentPublishedVersion { get; set; }
    public List<ContentVersion> Versions { get; set; } = [];
}

/// <summary>VI: Bản publish bất biến; attempt luôn ghim Id này. EN: Published payloads are immutable and attempts pin this version.</summary>
public sealed class ContentVersion : VersionedEntity
{
    public Guid RootId { get; set; }
    public ContentRoot Root { get; set; } = null!;
    public int Version { get; set; } = 1;
    public PublicationStatus Status { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    public string Skill { get; set; } = "grammar";
    public int Minutes { get; set; } = 10;
    public int CurriculumOrder { get; set; }
    public Guid? MediaAssetId { get; set; }
    public int DurationSeconds { get; set; } = 1200;
    public bool IsFixture { get; set; }
    public string Attribution { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
    public List<QuestionGroup> Groups { get; set; } = [];
}

/// <summary>VI: Giữ hội thoại/đoạn văn nguyên vẹn khi chọn đề. EN: Select entire groups to preserve shared stimuli.</summary>
public sealed class QuestionGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentVersionId { get; set; }
    public ContentVersion ContentVersion { get; set; } = null!;
    public int Order { get; set; }
    public int Part { get; set; } = 5;
    public string Stimulus { get; set; } = "";
    public List<Question> Questions { get; set; } = [];
}

public sealed class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public QuestionGroup Group { get; set; } = null!;
    public int Order { get; set; }
    public string Prompt { get; set; } = "";
    public string Skill { get; set; } = "grammar";
    public string Explanation { get; set; } = "";
    public QuestionType Type { get; set; }
    // VI: Chỉ CMS/result thấy các biến thể; snapshot giữ nguyên policy. EN: Variants are private until review; attempts pin the authored policy.
    public string AcceptedAnswerVariantsJson { get; set; } = "[]";
    public int NormalizationPolicyVersion { get; set; } = AnswerNormalization.CurrentVersion;
    public bool IgnoreTerminalPunctuation { get; set; }
    public List<QuestionOption> Options { get; set; } = [];
}

public sealed class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

/// <summary>VI: Audit không chứa mật khẩu hay token. EN: Audits record actions without credentials or access tokens.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public Guid ResourceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

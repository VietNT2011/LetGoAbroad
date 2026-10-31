namespace EnglishHub.Domain;

/// <summary>VI: Template ổn định, con trỏ chỉ đổi khi publish version mới. EN: A stable template points to its latest published version without rewriting existing enrollments.</summary>
public sealed class PathTemplate : VersionedEntity
{
    public string Slug { get; set; } = "";
    public string Level { get; set; } = "foundation";
    public Guid? CurrentPublishedVersionId { get; set; }
    public PathVersion? CurrentPublishedVersion { get; set; }
    public List<PathVersion> Versions { get; set; } = [];
}

public sealed class PathVersion : VersionedEntity
{
    public Guid TemplateId { get; set; }
    public PathTemplate Template { get; set; } = null!;
    public int Version { get; set; }
    public PublicationStatus Status { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Attribution { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
    public List<PathStage> Stages { get; set; } = [];
}

public sealed class PathStage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PathVersionId { get; set; }
    public PathVersion PathVersion { get; set; } = null!;
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public int Order { get; set; }
    public List<PathNode> Nodes { get; set; } = [];
}

/// <summary>VI: Mỗi node ghim đúng ContentVersion và điều kiện chấm. EN: Nodes pin content and an explicit evidence-based completion rule.</summary>
public sealed class PathNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PathVersionId { get; set; }
    public Guid StageId { get; set; }
    public PathStage Stage { get; set; } = null!;
    public string Key { get; set; } = "";
    public int Order { get; set; }
    public int StudyDay { get; set; }
    public Guid ContentVersionId { get; set; }
    public ContentVersion ContentVersion { get; set; } = null!;
    public string CompletionRule { get; set; } = "lesson_quiz";
    public int MinimumCorrectPercent { get; set; } = 100;
    public List<PathPrerequisite> Prerequisites { get; set; } = [];
}

public sealed class PathPrerequisite
{
    public Guid PathVersionId { get; set; }
    public Guid NodeId { get; set; }
    public PathNode Node { get; set; } = null!;
    public Guid PrerequisiteNodeId { get; set; }
    public PathNode PrerequisiteNode { get; set; } = null!;
}

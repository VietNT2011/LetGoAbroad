using EnglishHub.Domain;

namespace EnglishHub.Application.Learning;

/// <summary>VI: Gợi ý demo chỉ dựa trên đủ 20 câu của đúng version, không suy trình độ từ một phần đề. EN: The demo recommendation requires a complete twenty-question pinned snapshot, not a shortened practice.</summary>
public static class PlacementPolicy
{
    public const string ContentSlug = "placement-demo";
    public const int QuestionCount = 20;

    // VI: Không yêu cầu current pointer/status ở đây: kết quả cũ vẫn ghim version gốc sau lần publish khác. EN: Do not require a current pointer/status here; historical evidence keeps its original version after republishing.
    public static bool HasBlueprint(ContentVersion version) => version.Root.Kind == ContentKind.Assessment
        && version.Root.Slug == ContentSlug && !version.IsFixture
        && version.Groups.Sum(x => x.Questions.Count) == QuestionCount;

    public static bool Qualifies(Attempt? attempt)
    {
        if (attempt is null || attempt.Mode != "placement" || attempt.Status != AttemptStatus.Graded
            || attempt.Result is null || attempt.Result.TotalCount != QuestionCount
            || attempt.Result.CorrectCount is < 0 or > QuestionCount || !HasBlueprint(attempt.ContentVersion)) return false;
        var expected = attempt.ContentVersion.Groups.SelectMany(x => x.Questions).Select(x => x.Id).ToHashSet();
        var observed = attempt.Items.Select(x => x.QuestionId).ToArray();
        // VI: Đủ count chưa đủ; không nhận câu lặp hoặc câu thuộc version khác. EN: Counts alone cannot establish coverage; reject duplicates and questions from a different version.
        return expected.Count == QuestionCount && observed.Length == QuestionCount
            && observed.Distinct().Count() == QuestionCount && expected.SetEquals(observed);
    }
}

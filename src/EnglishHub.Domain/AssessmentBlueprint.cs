namespace EnglishHub.Domain;
/// <summary>VI: Blueprint định dạng, không phải xác nhận chất lượng đề ETS. EN: Validate format counts without claiming ETS content equivalence.</summary>
public static class AssessmentBlueprint
{
    public static bool IsFull(ContentVersion version)
    {
        var expected = new[] { 6, 25, 39, 30, 30, 16, 54 };
        return version.Groups.All(x => x.Part is >= 1 and <= 7) && version.Groups.Sum(x => x.Questions.Count) == 200 && Enumerable.Range(1, 7).All(part => version.Groups.Where(x => x.Part == part).Sum(x => x.Questions.Count) == expected[part - 1]);
    }
}

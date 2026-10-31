namespace EnglishHub.Application.Learning;

public sealed record SkillObservation(string Skill, int ObservedCount, int CorrectCount);

/// <summary>VI: Quy tắc ưu tiên củng cố, không chẩn đoán năng lực. EN: An explicit reinforcement heuristic, not an ability diagnosis.</summary>
public static class WeakSkillPolicy
{
    public const int Version = 1;
    public const int MinimumObservations = 5;
    public const int ThresholdPercent = 60;

    // VI: So sánh phân số gốc, không dùng phần trăm đã làm tròn trên UI. EN: Compare raw fractions rather than the display's rounded percentages.
    public static SkillObservation[] Rank(IEnumerable<SkillObservation> practice) => practice
        .Where(x => x.ObservedCount >= MinimumObservations && x.CorrectCount * 100m < x.ObservedCount * ThresholdPercent)
        .OrderBy(x => x.CorrectCount / (decimal)x.ObservedCount).ThenBy(x => x.Skill, StringComparer.Ordinal).ToArray();
}

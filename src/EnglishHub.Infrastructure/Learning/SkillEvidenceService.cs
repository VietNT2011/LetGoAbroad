using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record SkillEvidencePart(int Part, int ObservedCount, int CorrectCount);
public sealed record SkillEvidenceGroup(string Track, string Skill, int ObservedCount, int CorrectCount, int BlankCount, int? AccuracyPercent, string Status, SkillEvidencePart[] Parts);
public sealed record SkillEvidenceSnapshot(int PolicyVersion, int WindowSize, int MinimumObservations, SkillEvidenceGroup[] Groups);
public sealed record SkillComparisonGroup(string Track, string Skill, SkillEvidenceGroup Recent, SkillEvidenceGroup Previous, int? DifferencePercentagePoints);
public sealed record SkillComparisonSnapshot(int PolicyVersion, int WindowSize, int MinimumObservations, SkillComparisonGroup[] Groups);

/// <summary>VI: Chỉ dùng nội bộ sau window SQL; không serialize answer key ra API. EN: Internal window-query rows never become a public answer-key response.</summary>
public sealed class SkillEvidenceRow
{
    public string Track { get; set; } = "";
    public string Skill { get; set; } = "";
    public int Part { get; set; }
    public long SampleRank { get; set; }
    public int Type { get; set; }
    public Guid? SelectedOptionId { get; set; }
    public Guid? CorrectOptionId { get; set; }
    public string? AnswerText { get; set; }
    public string AcceptedAnswerVariantsJson { get; set; } = "[]";
    public int NormalizationPolicyVersion { get; set; }
    public bool IgnoreTerminalPunctuation { get; set; }
}

/// <summary>VI: 50 câu khác nhau gần nhất mỗi kỹ năng, tách practice và full mock chưa lộ đáp án. EN: Bound each track/skill to its latest fifty distinct question versions.</summary>
public sealed class SkillEvidenceService(HubDbContext db)
{
    private const int WindowSize = 50;
    private const int MinimumObservations = 5;

    public async Task<SkillEvidenceSnapshot> Read(Guid user)
    {
        var rows = await ReadRows(user, WindowSize);
        return new(1, WindowSize, MinimumObservations, rows.GroupBy(x => new { x.Track, x.Skill }).OrderBy(x => x.Key.Track).ThenBy(x => x.Key.Skill)
            .Select(group => Aggregate(group.Key.Track, group.Key.Skill, group)).ToArray());
    }

    public async Task<SkillComparisonSnapshot> ReadComparison(Guid user)
    {
        // VI: Một query chọn tối đa 100 câu khác nhau; hai mẫu không chồng câu và dùng chung eligibility với thống kê/planner. EN: One shared query selects at most 100 distinct versions; disjoint windows retain the evidence/planner eligibility policy.
        var rows = await ReadRows(user, WindowSize * 2);
        var groups = rows.GroupBy(x => new { x.Track, x.Skill }).OrderBy(x => x.Key.Track).ThenBy(x => x.Key.Skill).Select(group =>
        {
            var recent = Aggregate(group.Key.Track, group.Key.Skill, group.Where(x => x.SampleRank <= WindowSize));
            var previous = Aggregate(group.Key.Track, group.Key.Skill, group.Where(x => x.SampleRank > WindowSize));
            // VI: Trừ đúng hai phần trăm đang hiển thị; thiếu mẫu ở một phía thì không suy chênh lệch. EN: Subtract the displayed rounded percentages only when both samples meet the minimum.
            int? difference = recent.AccuracyPercent is int now && previous.AccuracyPercent is int before ? now - before : null;
            return new SkillComparisonGroup(group.Key.Track, group.Key.Skill, recent, previous, difference);
        }).ToArray();
        return new(1, WindowSize, MinimumObservations, groups);
    }

    private async Task<List<SkillEvidenceRow>> ReadRows(Guid user, int limit)
    {
        // VI: Chỉ sort ID/skill; join text đáp án sau window để tránh memory grant lớn cho LOB. EN: Rank narrow IDs/skills before joining answer text to avoid wide LOB sort grants on both providers.
        return await db.Database.SqlQuery<SkillEvidenceRow>($"""
            WITH eligible AS (
                SELECT i.[Id] AS [AttemptItemId], CASE WHEN a.[Mode] = 'full_mock' THEN 'full_mock_unseen' ELSE 'practice' END AS [Track],
                    k.[Skill],
                    i.[QuestionId], i.[Order] AS [ItemOrder], a.[Id] AS [AttemptId], r.[FinalizedAt],
                    ROW_NUMBER() OVER (PARTITION BY CASE WHEN a.[Mode] = 'full_mock' THEN 'full_mock_unseen' ELSE 'practice' END, k.[Skill], i.[QuestionId] ORDER BY r.[FinalizedAt] DESC, a.[Id] DESC) AS [QuestionRank]
                FROM [AttemptItems] i
                JOIN [Attempts] a ON a.[Id] = i.[AttemptId]
                JOIN [AttemptResults] r ON r.[AttemptId] = a.[Id]
                JOIN [GradingKey] k ON k.[AttemptItemId] = i.[Id]
                JOIN [ContentVersions] c ON c.[Id] = a.[ContentVersionId]
                WHERE a.[UserId] = {user} AND a.[Status] = 1 AND c.[IsFixture] = 0
                    AND a.[Mode] IN ('mini','practice','placement','full_mock')
                    AND (a.[Mode] <> 'full_mock' OR (a.[EvidencePolicyVersion] = 1 AND NOT EXISTS (
                        SELECT 1 FROM [AttemptItems] previousItem JOIN [Attempts] previousAttempt ON previousAttempt.[Id] = previousItem.[AttemptId]
                        WHERE previousAttempt.[UserId] = a.[UserId] AND previousItem.[QuestionId] = i.[QuestionId]
                            AND previousAttempt.[Id] <> a.[Id]
                            AND (previousAttempt.[FirstDisclosedAt] <= a.[StartedAt]
                                OR (previousAttempt.[EvidencePolicyVersion] = 0 AND previousAttempt.[Status] = 1 AND previousAttempt.[StartedAt] <= a.[StartedAt]))
                    )))
            ), recent AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY [Track],[Skill] ORDER BY [FinalizedAt] DESC,[AttemptId] DESC,[ItemOrder] DESC) AS [SkillRank]
                FROM eligible WHERE [QuestionRank] = 1
            )
            SELECT recent.[Track],recent.[Skill],recent.[SkillRank] AS [SampleRank],k.[Part],i.[Type],i.[SelectedOptionId],k.[CorrectOptionId],i.[AnswerText],
                k.[AcceptedAnswerVariantsJson],k.[NormalizationPolicyVersion],k.[IgnoreTerminalPunctuation]
            FROM recent
            JOIN [AttemptItems] i ON i.[Id] = recent.[AttemptItemId]
            JOIN [GradingKey] k ON k.[AttemptItemId] = recent.[AttemptItemId]
            WHERE recent.[SkillRank] <= {limit}
            """).ToListAsync();
    }

    private static SkillEvidenceGroup Aggregate(string track, string skill, IEnumerable<SkillEvidenceRow> rows)
    {
        // VI: Chuẩn hóa mỗi câu typed một lần rồi dùng lại cho tổng và Part. EN: Normalize each typed answer once and reuse its outcome for totals and Parts.
        var outcomes = rows.Select(row => new { row.Part, IsCorrect = Correct(row), IsBlank = Blank(row) }).ToArray();
        var correct = outcomes.Count(x => x.IsCorrect);
        return new(track, skill, outcomes.Length, correct, outcomes.Count(x => x.IsBlank),
            outcomes.Length < MinimumObservations ? null : (int)Math.Round(correct * 100m / outcomes.Length, MidpointRounding.AwayFromZero),
            outcomes.Length < MinimumObservations ? "insufficient_data" : "observed",
            outcomes.GroupBy(x => x.Part).OrderBy(x => x.Key).Select(part => new SkillEvidencePart(part.Key, part.Count(), part.Count(x => x.IsCorrect))).ToArray());
    }

    private static bool Blank(SkillEvidenceRow row) => row.Type == (int)QuestionType.FillBlank ? string.IsNullOrWhiteSpace(row.AnswerText) : row.SelectedOptionId is null;
    private static bool Correct(SkillEvidenceRow row) => row.Type == (int)QuestionType.FillBlank
        ? AnswerNormalization.Matches(row.AnswerText, JsonSerializer.Deserialize<string[]>(row.AcceptedAnswerVariantsJson)!, row.NormalizationPolicyVersion, row.IgnoreTerminalPunctuation)
        : row.SelectedOptionId is not null && row.SelectedOptionId == row.CorrectOptionId;
}

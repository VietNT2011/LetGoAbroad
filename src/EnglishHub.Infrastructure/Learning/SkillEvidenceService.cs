using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

/// <summary>VI: Chỉ dùng nội bộ sau window SQL; không serialize answer key ra API. EN: Internal window-query rows never become a public answer-key response.</summary>
public sealed class SkillEvidenceRow
{
    public string Track { get; set; } = "";
    public string Skill { get; set; } = "";
    public int Part { get; set; }
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
    public async Task<object> Read(Guid user)
    {
        // VI: ROW_NUMBER giới hạn trên DB trước khi đọc grading snapshot; cùng SQL chạy trên SQL Server và SQLite. EN: Window functions bound materialized grading data on both relational providers.
        var rows = await db.Database.SqlQuery<SkillEvidenceRow>($"""
            WITH eligible AS (
                SELECT CASE WHEN a.[Mode] = 'full_mock' THEN 'full_mock_unseen' ELSE 'practice' END AS [Track],
                    k.[Skill], k.[Part], i.[Type], i.[SelectedOptionId], k.[CorrectOptionId], i.[AnswerText],
                    k.[AcceptedAnswerVariantsJson], k.[NormalizationPolicyVersion], k.[IgnoreTerminalPunctuation],
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
            SELECT [Track],[Skill],[Part],[Type],[SelectedOptionId],[CorrectOptionId],[AnswerText],
                [AcceptedAnswerVariantsJson],[NormalizationPolicyVersion],[IgnoreTerminalPunctuation]
            FROM recent WHERE [SkillRank] <= 50
            """).ToListAsync();
        var groups = rows.GroupBy(x => new { x.Track, x.Skill }).OrderBy(x => x.Key.Track).ThenBy(x => x.Key.Skill).Select(group => new
        {
            group.Key.Track,
            group.Key.Skill,
            observedCount = group.Count(),
            correctCount = group.Count(Correct),
            blankCount = group.Count(Blank),
            accuracyPercent = group.Count() < 5 ? (int?)null : (int)Math.Round(group.Count(Correct) * 100m / group.Count(), MidpointRounding.AwayFromZero),
            status = group.Count() < 5 ? "insufficient_data" : "observed",
            parts = group.GroupBy(x => x.Part).OrderBy(x => x.Key).Select(part => new { part = part.Key, observedCount = part.Count(), correctCount = part.Count(Correct) }).ToArray()
        }).ToArray();
        return new { policyVersion = 1, windowSize = 50, minimumObservations = 5, groups };
    }

    private static bool Blank(SkillEvidenceRow row) => row.Type == (int)QuestionType.FillBlank ? string.IsNullOrWhiteSpace(row.AnswerText) : row.SelectedOptionId is null;
    private static bool Correct(SkillEvidenceRow row) => row.Type == (int)QuestionType.FillBlank
        ? AnswerNormalization.Matches(row.AnswerText, JsonSerializer.Deserialize<string[]>(row.AcceptedAnswerVariantsJson)!, row.NormalizationPolicyVersion, row.IgnoreTerminalPunctuation)
        : row.SelectedOptionId is not null && row.SelectedOptionId == row.CorrectOptionId;
}

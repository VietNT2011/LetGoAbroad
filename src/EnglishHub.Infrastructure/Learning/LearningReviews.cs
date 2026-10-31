using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed partial class LearningService
{
    private sealed record DailyProposal(DailyTask[] Tasks, DailyReviewBatch? Review);
    private Task<DailyReviewBatch[]> CurrentReviews(Enrollment enrollment, DateOnly today) => db.DailyReviewBatches.Include(x => x.Items)
        .Where(x => x.EnrollmentId == enrollment.Id && x.Status != "superseded" && (x.LocalDate == today || x.Status == "pending")).ToArrayAsync();

    private async Task MarkReviewsMissed(Guid user)
    {
        var pending = await db.DailyReviewBatches.Where(x => x.UserId == user && x.Status == "pending" && x.StartedAt == null).ToArrayAsync();
        foreach (var batch in pending) if (batch.LocalDate < LocalDate(clock.UtcNow, batch.TimeZoneId)) batch.Status = "missed";
        await db.SaveChangesAsync();
    }

    /// <summary>VI: Một batch tính là một task lõi; ước lượng1 phút/thẻ, tối đa30% ngân sách gốc và phần còn lại sau việc được giữ. EN: One batch occupies one core slot, reserving one estimated minute per card within 30% of the original daily budget and remaining protected-work capacity.</summary>
    private async Task<DailyProposal> BuildDailyProposal(Enrollment enrollment, AppUser user, DateOnly today, Guid goal, int budget, int limit, int slotOffset, Guid[] reservedContent, Guid[] ignoredTasks, bool allowReview)
    {
        DailyReviewBatch? review = null;
        var reviewBudget = Math.Min(budget, enrollment.MinutesPerDay * 30 / 100);
        if (allowReview && reviewBudget > 0 && limit > 0)
        {
            var due = await db.UserCards.AsNoTracking().Where(x => x.UserId == user.Id && x.ArchivedAt == null && x.DueAt <= clock.UtcNow)
                .OrderBy(x => x.DueAt).ThenBy(x => x.Id).Take(reviewBudget).Select(x => new { x.Id, x.DueAt }).ToArrayAsync();
            if (due.Length > 0)
            {
                var number = (await db.DailyReviewBatches.Where(x => x.EnrollmentId == enrollment.Id && x.LocalDate == today).Select(x => (int?)x.Number).MaxAsync() ?? 0) + 1;
                review = new DailyReviewBatch { UserId = user.Id, EnrollmentId = enrollment.Id, GoalRevisionId = goal, LocalDate = today, TimeZoneId = user.TimeZoneId, Number = number, EstimatedMinutes = due.Length, CreatedAt = clock.UtcNow,
                    ReasonText = $"Ôn {due.Length} thẻ đã đến hạn; dành {due.Length}/{enrollment.MinutesPerDay} phút, tối đa 30% ngân sách khi lập lịch. Ước lượng 1 phút/thẻ; quy tắc v1.",
                    Items = due.Select((card, order) => new DailyReviewItem { UserId = user.Id, UserCardId = card.Id, Order = order, DueAtWhenAssigned = card.DueAt }).ToList() };
                budget -= review.EstimatedMinutes; limit--;
            }
        }
        return new(await BuildDailyTasks(enrollment, user, today, goal, budget, limit, slotOffset, reservedContent, ignoredTasks), review);
    }

    private static string ReviewAssignment(DailyReviewBatch batch) => Receipts.Hash(new { batch.GoalRevisionId, batch.TimeZoneId, batch.EstimatedMinutes, batch.PlanningPolicyVersion, batch.ReasonCode, batch.ReasonText, cards = batch.Items.OrderBy(x => x.Order).Select(x => new { x.UserCardId, x.DueAtWhenAssigned }).ToArray() });
}

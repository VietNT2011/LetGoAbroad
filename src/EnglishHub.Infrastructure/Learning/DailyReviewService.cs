using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed class DailyReviewService(HubDbContext db, Transactions tx, IClock clock)
{
    public async Task<object> Get(Guid user, Guid id)
    {
        var batch = await db.DailyReviewBatches.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Card).SingleOrDefaultAsync(x => x.Id == id && x.UserId == user)
            ?? throw new DomainError(404, "review_batch_not_found", "Không tìm thấy nhiệm vụ ôn của bạn.");
        return new { batch.Id, batch.Status, batch.LocalDate, batch.TimeZoneId, batch.EstimatedMinutes, batch.StartedAt, batch.CompletedAt,
            items = batch.Items.OrderBy(x => x.Order).Select(x => new { cardId = x.UserCardId, x.Card.Front, completed = x.CardReviewId != null, archived = x.Card.ArchivedAt != null }).ToArray() };
    }

    public Task<TaskStartAck> Start(Guid user, Guid id) => tx.Run($"learning:{user}", async () =>
    {
        var batch = await db.DailyReviewBatches.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user)
            ?? throw new DomainError(404, "review_batch_not_found", "Không tìm thấy nhiệm vụ ôn của bạn.");
        DomainError.Require(batch.Status is "pending" or "completed", 409, "review_batch_closed", "Nhiệm vụ ôn đã được thay hoặc quá ngày.");
        if (batch.StartedAt is null && batch.Status == "pending")
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(batch.TimeZoneId)).DateTime);
            DomainError.Require(today == batch.LocalDate, 409, "plan_day_changed", "Ngày học đã đổi. Hãy tải lịch mới.");
            batch.StartedAt = clock.UtcNow;
        }
        return new TaskStartAck(batch.Id, batch.StartedAt, batch.Status);
    });

    /// <summary>VI: Chỉ nguồn CardReview vừa được chấp nhận mới hoàn thành item; mọi rating đều là một lượt ôn, không phải chứng minh nhớ đúng. EN: Link actual accepted review evidence; a completed recall activity does not assert correct memory.</summary>
    public async Task Record(Guid user, CardReview review)
    {
        var batches = await db.DailyReviewBatches.Include(x => x.Items).Where(x => x.UserId == user && x.Status == "pending" && x.CreatedAt <= review.ReviewedAt && x.Items.Any(i => i.UserCardId == review.UserCardId && i.CardReviewId == null)).ToArrayAsync();
        foreach (var batch in batches)
        {
            batch.Items.Single(x => x.UserCardId == review.UserCardId).CardReviewId = review.Id;
            batch.StartedAt ??= review.ReviewedAt;
            if (batch.Items.All(x => x.CardReviewId != null))
            {
                batch.Status = "completed"; batch.CompletedAt = review.ReviewedAt;
                db.OutboxEvents.Add(new OutboxEvent { Type = "learning.review_batch_completed", AggregateId = batch.Id, OccurredAt = review.ReviewedAt, PayloadJson = JsonSerializer.Serialize(new { userId = user, batchId = batch.Id }) });
            }
        }
    }
}

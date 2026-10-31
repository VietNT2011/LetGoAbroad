using System.Text;
using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record CreateCard(Guid ClientRequestId, string Front, string Back, string? SourceContext = null);
public sealed record ReviewCard(Guid ReviewEventId, string ExpectedEtag, CardRating Rating);
public sealed record ArchiveCard(Guid ClientRequestId, string ExpectedEtag, bool Archived);
public sealed record CardView(Guid Id, string Front, string Back, string[] Contexts, DateTimeOffset DueAt, int IntervalDays, int SuccessStep, int SchedulerVersion, DateTimeOffset? ArchivedAt, string Etag, bool CanReview);
public sealed record CardPage(CardView[] Items, int DueCount, int? NextOffset);
public sealed record CardReviewAck(Guid ReviewId, Guid ReviewEventId, CardView Card);

/// <summary>VI: Mọi truy vấn và lệnh đều theo owner, cả với Admin; dùng cùng khóa learning để nối planner an toàn. EN: Owner-scoped cards share the learning lock so future plan allocation cannot race a review.</summary>
public sealed class CardService(HubDbContext db, Transactions tx, Receipts receipts, IClock clock)
{
    private IQueryable<UserCard> Full => db.UserCards.Include(x => x.Contexts);
    public async Task<CardPage> List(Guid user, bool dueOnly = false, bool archived = false, int offset = 0)
    {
        ValidateOffset(offset);
        var now = clock.UtcNow;
        var query = Full.AsNoTracking().Where(x => x.UserId == user && (x.ArchivedAt != null) == archived);
        if (dueOnly) query = query.Where(x => x.DueAt <= now);
        var page = await query.OrderBy(x => x.DueAt).ThenBy(x => x.Id).Skip(offset).Take(31).ToArrayAsync();
        return new(page.Take(30).Select(View).ToArray(), await db.UserCards.CountAsync(x => x.UserId == user && x.ArchivedAt == null && x.DueAt <= now), page.Length > 30 ? offset + 30 : null);
    }

    public async Task<CardView> Get(Guid user, Guid id) => View(await Owned(user, id, false));

    public async Task<object> History(Guid user, Guid id, int offset = 0)
    {
        ValidateOffset(offset); await Owned(user, id, false);
        var page = await db.CardReviews.AsNoTracking().Where(x => x.UserCardId == id && x.Card.UserId == user)
            .OrderByDescending(x => x.ReviewedAt).ThenBy(x => x.Id).Skip(offset).Take(31)
            .Select(x => new { x.Id, x.ReviewEventId, x.Rating, x.SchedulerVersion, x.ReviewedAt, x.BeforeDueAt, x.AfterDueAt, x.BeforeIntervalDays, x.AfterIntervalDays, x.BeforeSuccessStep, x.AfterSuccessStep }).ToArrayAsync();
        return new { items = page.Take(30).ToArray(), nextOffset = page.Length > 30 ? offset + 30 : (int?)null };
    }

    public Task<CardView> Create(Guid user, CreateCard input) => tx.Run($"learning:{user}", async () =>
    {
        const string scope = "card-create"; var hash = Receipts.Hash(input);
        var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<CardView>(duplicate.ResponseJson)!;
        DomainError.Require(!string.IsNullOrWhiteSpace(input.Front) && input.Front.Length <= 500 && !string.IsNullOrWhiteSpace(input.Back) && input.Back.Length <= 2000 && (input.SourceContext?.Length ?? 0) <= 2000, 422, "invalid_card_text", "Mặt trước tối đa500, mặt sau/ngữ cảnh tối đa2000 ký tự và hai mặt không được trống.");
        var front = input.Front.Trim().Normalize(NormalizationForm.FormC); var back = input.Back.Trim().Normalize(NormalizationForm.FormC);
        var key = Receipts.Hash(new { front, back });
        var card = await Full.SingleOrDefaultAsync(x => x.UserId == user && x.CustomKey == key);
        // VI: Lưu lại thẻ đã cất cần thao tác khôi phục rõ ràng; không reset lịch hay tạo bản sao để né lịch sử. EN: Re-adding an archived card requires explicit restore rather than resetting its schedule or duplicating history.
        DomainError.Require(card?.ArchivedAt is null, 409, "card_archived", "Thẻ này đã được cất. Hãy mở Thẻ đã cất để khôi phục.");
        if (card is null)
        {
            card = new UserCard { UserId = user, CustomKey = key, Front = front, Back = back, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow, DueAt = clock.UtcNow };
            db.UserCards.Add(card);
        }
        var context = input.SourceContext?.Trim().Normalize(NormalizationForm.FormC);
        if (!string.IsNullOrEmpty(context) && !card.Contexts.Any(x => x.Text == context))
        {
            DomainError.Require(card.Contexts.Count < 20, 422, "card_context_limit", "Mỗi thẻ lưu tối đa20 ngữ cảnh.");
            var source = new CardSourceContext { UserCardId = card.Id, ContextKey = Receipts.Hash(context), Text = context, CreatedAt = clock.UtcNow };
            card.Contexts.Add(source); db.CardSourceContexts.Add(source); card.UpdatedAt = clock.UtcNow;
            if (db.Entry(card).State != EntityState.Added) db.Entry(card).Property(x => x.UpdatedAt).IsModified = true;
        }
        await db.SaveChangesAsync();
        var view = View(card); receipts.Save(user, scope, input.ClientRequestId, hash, card.Id, view); return view;
    });

    public Task<CardReviewAck> Review(Guid user, Guid id, ReviewCard input) => tx.Run($"learning:{user}", async () =>
    {
        // VI: Event ID duy nhất theo owner, kể cả khi đổi card; retry lấy ACK gốc trước khi kiểm tra lịch mới. EN: Bind each owner's event to its card/payload and replay before checking the subsequently changed schedule.
        const string scope = "card-review"; var hash = Receipts.Hash(new { id, input });
        var duplicate = await receipts.Find(user, scope, input.ReviewEventId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<CardReviewAck>(duplicate.ResponseJson)!;
        var card = await Owned(user, id); Etags.Check(card, input.ExpectedEtag);
        DomainError.Require(card.ArchivedAt is null, 409, "card_archived", "Khôi phục thẻ trước khi ôn.");
        var now = clock.UtcNow;
        DomainError.Require(card.DueAt <= now, 409, "card_not_due", "Thẻ chưa đến hạn ôn. Hãy tải danh sách mới.");
        var next = CardScheduler.Next(card.IntervalDays, card.SuccessStep, input.Rating, now);
        var review = new CardReview { UserCardId = id, ReviewEventId = input.ReviewEventId, Rating = input.Rating, SchedulerVersion = CardScheduler.Version, ReviewedAt = now,
            BeforeDueAt = card.DueAt, AfterDueAt = next.DueAt, BeforeIntervalDays = card.IntervalDays, AfterIntervalDays = next.IntervalDays, BeforeSuccessStep = card.SuccessStep, AfterSuccessStep = next.SuccessStep };
        card.DueAt = next.DueAt; card.IntervalDays = next.IntervalDays; card.SuccessStep = next.SuccessStep; card.SchedulerVersion = CardScheduler.Version; card.UpdatedAt = now;
        db.CardReviews.Add(review);
        db.OutboxEvents.Add(new OutboxEvent { Type = "review.card_reviewed", AggregateId = review.Id, OccurredAt = now, PayloadJson = JsonSerializer.Serialize(new { userId = user, cardId = id, reviewId = review.Id, schedulerVersion = CardScheduler.Version }) });
        await db.SaveChangesAsync();
        var ack = new CardReviewAck(review.Id, input.ReviewEventId, View(card)); receipts.Save(user, scope, input.ReviewEventId, hash, id, ack); return ack;
    });

    public Task<CardView> Archive(Guid user, Guid id, ArchiveCard input) => tx.Run($"learning:{user}", async () =>
    {
        const string scope = "card-archive"; var hash = Receipts.Hash(new { id, input });
        var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<CardView>(duplicate.ResponseJson)!;
        var card = await Owned(user, id); Etags.Check(card, input.ExpectedEtag);
        if ((card.ArchivedAt != null) != input.Archived) { card.ArchivedAt = input.Archived ? clock.UtcNow : null; card.UpdatedAt = clock.UtcNow; await db.SaveChangesAsync(); }
        var view = View(card); receipts.Save(user, scope, input.ClientRequestId, hash, id, view); return view;
    });

    private async Task<UserCard> Owned(Guid user, Guid id, bool tracking = true) => await (tracking ? Full : Full.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id && x.UserId == user)
        ?? throw new DomainError(404, "card_not_found", "Không tìm thấy thẻ của bạn.");
    private CardView View(UserCard card) => new(card.Id, card.Front, card.Back, card.Contexts.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.Text).ToArray(), card.DueAt, card.IntervalDays, card.SuccessStep, card.SchedulerVersion, card.ArchivedAt, Etags.Of(card), card.ArchivedAt is null && card.DueAt <= clock.UtcNow);
    private static void ValidateOffset(int offset) => DomainError.Require(offset is >= 0 and <= 100000, 422, "invalid_offset", "Vị trí trang không hợp lệ.");
}

using Microsoft.EntityFrameworkCore;
using EnglishHub.Domain;

namespace EnglishHub.Infrastructure.Learning;

public sealed partial class LearningService
{
    public Task<object> PathChoiceHistory(Guid userId) => tx.Run($"learning:{userId}", async () =>
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId);
        return enrollment is null ? (object)new { expectedEtag = (string?)null, activeRevisionId = (Guid?)null, activePathVersionId = (Guid?)null, revisions = Array.Empty<object>() } : await pathChoices.History(enrollment);
    });

    /// <summary>VI: Chốt quyền đăng ký trước retirement; lựa chọn đã lưu giữ chỗ dù mẫu ngừng đăng ký sau đó. EN: Reserve admission under the catalog lock so later retirement preserves the existing reservation.</summary>
    public Task<PathChoiceAck> SwitchPath(Guid userId, SwitchPathInput input) => tx.Run($"learning:{userId}", async () =>
    {
        await tx.Lock("path-catalog");
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId) ?? throw new DomainError(409, "enrollment_required", "Hãy tạo lộ trình trước.");
        return await pathChoices.Schedule(enrollment, input);
    });
}

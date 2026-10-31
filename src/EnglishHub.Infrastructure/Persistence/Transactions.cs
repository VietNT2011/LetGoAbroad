using System.Data;
using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Khóa nghiệp vụ thuộc transaction SQL, không phải khóa in-memory. EN: SQL application locks serialize resource commands across processes.</summary>
public sealed class Transactions(HubDbContext db)
{
    public async Task<T> Run<T>(string resource, Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(db.Database.IsSqlServer() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, ct);
        await Lock(resource, ct);
        var result = await action();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>VI: Enrollment lấy learning trước catalog; CMS chỉ lấy catalog, không đảo thứ tự. EN: Admissions take learning then catalog; CMS takes catalog only to avoid lock-order cycles.</summary>
    public async Task Lock(string resource, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("A resource lock requires an active transaction.");
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'resource_busy', 1;", ct);
    }
}

public static class Etags
{
    public static string Of(VersionedEntity entity) => $"\"{Convert.ToBase64String(entity.RowVersion)}\"";
    public static void Check(VersionedEntity entity, string? expected) =>
        DomainError.Require(!string.IsNullOrWhiteSpace(expected) && expected == Of(entity), 412, "stale_version", "Dữ liệu đã thay đổi. Hãy tải lại / Reload the current version.");
}

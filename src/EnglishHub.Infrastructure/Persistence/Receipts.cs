using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Dùng trong transaction đã khóa đúng resource. EN: Call only inside the same resource-locked business transaction.</summary>
public sealed class Receipts(HubDbContext db, IClock clock)
{
    public static string Hash<T>(T payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    public async Task<CommandReceipt?> Find(Guid user, string scope, Guid command, string hash)
    {
        DomainError.Require(command != Guid.Empty, 422, "invalid_command_id", "ClientRequestId không được rỗng.");
        var receipt = await db.CommandReceipts.SingleOrDefaultAsync(x => x.UserId == user && x.Scope == scope && x.CommandId == command);
        if (receipt is not null) DomainError.Require(receipt.PayloadHash == hash, 409, "idempotency_conflict", "Cùng khóa nhưng payload khác / Same command ID, different payload.");
        return receipt;
    }
    public void Save<T>(Guid user, string scope, Guid command, string hash, Guid resource, T response) => db.CommandReceipts.Add(new CommandReceipt { UserId = user, Scope = scope, CommandId = command, PayloadHash = hash, ResourceId = resource, ResponseJson = JsonSerializer.Serialize(response), CreatedAt = clock.UtcNow });
}

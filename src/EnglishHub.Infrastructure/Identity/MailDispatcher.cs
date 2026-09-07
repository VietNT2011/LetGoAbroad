using System.Net;
using System.Net.Mail;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EnglishHub.Infrastructure.Identity;

public sealed class DevMail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>VI: SMTP thật trong production; hộp thư DB chỉ dành cho local. EN: Production uses SMTP; local development has an explicit database mailbox.</summary>
public sealed class MailDispatcher(HubDbContext db, IConfiguration config, IHostEnvironment env)
{
    public async Task Send(string email, string purpose, Guid userId, string token)
    {
        var origin = config["PublicOrigin"] ?? "http://localhost:5173";
        var link = $"{origin}/{purpose}?userId={userId}&token={Uri.EscapeDataString(token)}";
        if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            db.Set<DevMail>().Add(new DevMail { Recipient = email, Subject = purpose, Body = JsonSerializer.Serialize(new { userId, token, link }) });
            await db.SaveChangesAsync();
            return;
        }
        DomainError.Require(!string.IsNullOrWhiteSpace(config["Smtp:Host"]), 503, "email_unavailable", "Email delivery has not been configured.");
        using var client = new SmtpClient(config["Smtp:Host"], config.GetValue("Smtp:Port", 587)) { EnableSsl = true, Credentials = new NetworkCredential(config["Smtp:User"], config["Smtp:Password"]) };
        using var message = new MailMessage(config["Smtp:From"] ?? throw new InvalidOperationException("Set Smtp:From"), email, $"EnglishHub · {purpose}", link);
        await client.SendMailAsync(message);
    }
}

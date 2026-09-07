using Microsoft.AspNetCore.Identity;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Identity quản lý hash và phiên đăng nhập. EN: Identity owns password hashes, lockout and security stamps.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int TargetScore { get; set; } = 650;
    public int MinutesPerDay { get; set; } = 30;
    public string StudyDays { get; set; } = "1,2,3,4,5";
}

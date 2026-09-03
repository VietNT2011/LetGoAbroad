namespace EnglishHub.Application;

/// <summary>VI: Chỉ server quyết định deadline. EN: Server time is authoritative and replaceable in tests.</summary>
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

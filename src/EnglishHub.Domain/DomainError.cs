namespace EnglishHub.Domain;

/// <summary>VI: Lỗi nghiệp vụ có mã ổn định. EN: A business failure with a stable machine code.</summary>
public sealed class DomainError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public static void Require(bool condition, int status, string code, string message)
    {
        if (!condition) throw new DomainError(status, code, message);
    }
}

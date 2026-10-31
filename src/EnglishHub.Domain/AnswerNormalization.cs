using System.Text;

namespace EnglishHub.Domain;

/// <summary>VI: Policy v1 là so khớp chính xác sau chuẩn hóa, không đoán lỗi gõ. EN: Version one uses exact normalized matching without fuzzy spelling guesses.</summary>
public static class AnswerNormalization
{
    public const int CurrentVersion = 1;
    public const int MaxAnswerLength = 2000;

    public static string Normalize(string? answer, int version, bool ignoreTerminalPunctuation)
    {
        DomainError.Require(version == CurrentVersion, 422, "unsupported_normalization_policy", "Phiên bản chuẩn hóa chưa được hỗ trợ.");
        var text = (answer ?? "").Normalize(NormalizationForm.FormC)
            .Replace('\u2018', '\'').Replace('\u2019', '\'').Replace('\u02bc', '\'').Replace('\uff07', '\'');
        // VI: IsWhiteSpace bao gồm tab, xuống dòng và NBSP. EN: Collapse Unicode whitespace, including tabs, newlines and non-breaking spaces.
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        return ignoreTerminalPunctuation ? text.TrimEnd('.', '!', '?', '\u3002', '\uff01', '\uff1f').TrimEnd() : text;
    }

    public static bool Matches(string? answer, IEnumerable<string> variants, int version, bool ignoreTerminalPunctuation)
    {
        var normalized = Normalize(answer, version, ignoreTerminalPunctuation);
        return normalized.Length > 0 && variants.Any(v => normalized == Normalize(v, version, ignoreTerminalPunctuation));
    }
}

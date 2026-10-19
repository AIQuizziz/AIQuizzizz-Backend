using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Questions.Utils;

/// <summary>
/// Chuyển đổi giữa chuỗi TextFormat do client gửi ("PlainText" | "Html" | "Markdown") và enum <see cref="TextFormat"/>.
/// </summary>
public static class TextFormatHelper
{
    public static readonly string[] AllowedValues =
    {
        nameof(TextFormat.PlainText), nameof(TextFormat.Html), nameof(TextFormat.Markdown)
    };

    /// <summary>Chuỗi rỗng/null được coi là hợp lệ (client cũ không gửi textFormat).</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return AllowedValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Chuẩn hoá chữ hoa/thường ("markdown" -> "Markdown"). Rỗng -> null; giá trị lạ giữ nguyên (đã trim) để validator báo lỗi.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return AllowedValues.FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    /// <summary>
    /// Trả về format client yêu cầu; nếu không gửi (hoặc không hợp lệ) thì dùng <paramref name="fallback"/> (hành vi cũ).
    /// </summary>
    public static TextFormat Resolve(string? value, TextFormat fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var match = AllowedValues.FirstOrDefault(x => string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match != null ? Enum.Parse<TextFormat>(match) : fallback;
    }
}

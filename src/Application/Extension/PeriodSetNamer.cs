using System.Globalization;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

/// <summary>Đặt tên bộ tự tạo theo kỳ (ngày/tuần/tháng) tính theo múi giờ người dùng, để cùng một kỳ luôn rơi vào cùng một bộ.</summary>
public static class PeriodSetNamer
{
    public const string Prefix = "Flashcards";

    public static string NameFor(ExtensionTargetMode mode, DateTimeOffset nowUtc, string? timeZoneId)
    {
        var local = ToLocal(nowUtc, timeZoneId);
        return mode switch
        {
            ExtensionTargetMode.Weekly => $"{Prefix} {ISOWeek.GetYear(local):0000}-W{ISOWeek.GetWeekOfYear(local):00}",
            ExtensionTargetMode.Monthly => $"{Prefix} {local:yyyy-MM}",
            ExtensionTargetMode.Daily => $"{Prefix} {local:yyyy-MM-dd}",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Chỉ chế độ theo kỳ mới có tên tự động")
        };
    }

    /// <summary>Múi giờ không tìm thấy (vd. image thiếu tzdata) thì dùng UTC thay vì lỗi.</summary>
    public static DateTime ToLocal(DateTimeOffset nowUtc, string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var tz))
            return TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime;
        return nowUtc.UtcDateTime;
    }
}

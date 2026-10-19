using System.Text.RegularExpressions;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

public class ExtensionSettingsDto
{
    /// <summary>Fixed | Daily | Weekly | Monthly</summary>
    public string TargetMode { get; set; } = nameof(ExtensionTargetMode.Daily);
    public Guid? TargetQuestionSetId { get; set; }
    public string? TargetQuestionSetName { get; set; }
    public bool TranslateEnabled { get; set; } = true;
    public string SourceLang { get; set; } = "en";
    public string TargetLang { get; set; } = "vi";
    public bool StoreContext { get; set; } = true;
    public string TimeZone { get; set; } = "UTC";

    public static ExtensionSettingsDto From(ExtensionSetting s, string? targetSetName) => new()
    {
        TargetMode = s.TargetMode.ToString(),
        TargetQuestionSetId = s.TargetQuestionSetId,
        TargetQuestionSetName = targetSetName,
        TranslateEnabled = s.TranslateEnabled,
        SourceLang = s.SourceLang,
        TargetLang = s.TargetLang,
        StoreContext = s.StoreContext,
        TimeZone = s.TimeZone
    };

    public static ExtensionSetting Defaults(Guid userId) => new() { UserId = userId };
}

public static partial class ExtensionValidation
{
    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z]{2,4})?$")]
    private static partial Regex LangRegex();

    [GeneratedRegex("^[A-Za-z0-9_+\\-/]{1,64}$")]
    private static partial Regex TimeZoneRegex();

    public static bool IsLang(string? lang) => lang != null && LangRegex().IsMatch(lang);
    public static bool IsTimeZoneId(string? tz) => tz != null && TimeZoneRegex().IsMatch(tz);
}

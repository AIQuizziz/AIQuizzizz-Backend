using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Infrastructure.Flashcards;

public class WiktapiSettings
{
    /// <summary>Địa chỉ WiktAPI tự host (vd http://aiquizz-wiktapi:3000). Trống = dùng Free Dictionary API + MyMemory như trước.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Các bản Wiktionary đã import (mã ngôn ngữ, cách nhau bởi dấu phẩy). Ngôn ngữ không có bản riêng tra trong bản "en".</summary>
    public string Editions { get; set; } = "en,vi";
}

/// <summary>
/// Tra từ + dịch từ bằng WiktAPI tự host (dữ liệu Wiktionary qua kaikki.org), thay cho Free Dictionary API (tra từ) và MyMemory (dịch từ).
/// Câu dài vẫn dịch bằng LibreTranslate. Từ khoá luôn được mã hoá vào đường dẫn, host lấy từ cấu hình (không có SSRF).
///  - Tra từ: GET /v1/{edition}/word/{word}/definitions + /pronunciations (?lang=)
///  - Dịch: từ tiếng Anh -> bảng translations của Wiktionary tiếng Anh (lọc theo mã ngôn ngữ đích);
///          từ ngôn ngữ khác -> tiếng Anh: chính gloss tiếng Anh của mục từ đó trong Wiktionary tiếng Anh.
/// </summary>
public partial class WiktapiService : IDictionaryLookupService, ITranslationService
{
    public const string ProviderName = "wiktionary";
    private static readonly TimeSpan FoundTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromMinutes(10);
    /// <summary>Tối đa số bản dịch lấy cho một từ (cùng một nghĩa)</summary>
    private const int MaxTranslations = 3;

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WiktapiService> _logger;
    private readonly HashSet<string> _editions;

    public WiktapiService(HttpClient http, IMemoryCache cache, ILogger<WiktapiService> logger, IOptions<WiktapiSettings> settings)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _editions = settings.Value.Editions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToHashSet();
    }

    public string Provider => ProviderName;

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LangPattern();

    private static string? NormalizeLang(string? lang)
    {
        var code = (lang ?? string.Empty).Trim().ToLowerInvariant();
        // "en-US" -> "en"
        var dash = code.IndexOf('-');
        if (dash > 0) code = code[..dash];
        return LangPattern().IsMatch(code) ? code : null;
    }

    private string EditionFor(string lang) => _editions.Contains(lang) ? lang : "en";

    private static string WordPath(string edition, string word, string part, string lang) =>
        $"v1/{edition}/word/{Uri.EscapeDataString(word)}/{part}?lang={lang}";

    //#region tra từ
    public async Task<DictionaryLookupResult> LookupAsync(string word, string lang, CancellationToken cancellationToken = default)
    {
        word = (word ?? string.Empty).Trim();
        var code = NormalizeLang(lang);
        if (word.Length == 0 || word.Length > 100 || code == null) return DictionaryLookupResult.NotFound;

        var key = $"wikt:lookup:{code}:{word.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out DictionaryLookupResult? cached) && cached != null) return cached;

        var edition = EditionFor(code);
        var (definitions, defStatus) = await GetAsync(WordPath(edition, word, "definitions", code), cancellationToken);
        if (defStatus == LookupStatus.Error) return DictionaryLookupResult.Error;
        if (defStatus == LookupStatus.NotFound || definitions == null)
        {
            _cache.Set(key, DictionaryLookupResult.NotFound, NotFoundTtl);
            return DictionaryLookupResult.NotFound;
        }

        // nghĩa đầu tiên là nơi gắn bản dịch: xếp theo cùng thứ tự ưu tiên từ loại với bản dịch
        var entry = new DictionaryEntry { Meanings = ByPosPriority(ParseMeanings(definitions)).ToList() };
        definitions.Dispose();

        // Phát âm là phần phụ: lỗi thì vẫn trả nghĩa
        var (pronunciations, _) = await GetAsync(WordPath(edition, word, "pronunciations", code), cancellationToken);
        if (pronunciations != null)
        {
            (entry.Phonetic, entry.AudioUrl) = ParseSounds(pronunciations);
            pronunciations.Dispose();
        }

        var result = entry.Meanings.Count == 0 ? DictionaryLookupResult.NotFound : DictionaryLookupResult.Found(entry);
        _cache.Set(key, result, entry.Meanings.Count == 0 ? NotFoundTtl : FoundTtl);
        return result;
    }

    /// <summary>Mỗi từ loại lấy nghĩa đầu tiên có gloss (gloss cuối là nghĩa cụ thể nhất), ví dụ dạng "example" (bỏ trích dẫn dài), tối đa MaxMeanings.</summary>
    public static List<QFlashcardMeaning> ParseMeanings(JsonDocument doc)
    {
        var meanings = new List<QFlashcardMeaning>();
        if (!doc.RootElement.TryGetProperty("definitions", out var groups) || groups.ValueKind != JsonValueKind.Array) return meanings;

        var seen = new HashSet<string>();
        foreach (var group in groups.EnumerateArray())
        {
            if (meanings.Count >= FlashcardLimits.MaxMeanings) break;
            if (!group.TryGetProperty("senses", out var senses) || senses.ValueKind != JsonValueKind.Array) continue;

            foreach (var sense in senses.EnumerateArray())
            {
                var gloss = LastString(sense, "glosses");
                if (string.IsNullOrWhiteSpace(gloss)) continue;

                var pos = Str(group, "pos")?.ToLowerInvariant();
                if (!seen.Add($"{pos}|{gloss}")) break;

                var meaning = new QFlashcardMeaning { Pos = pos, Definition = Clip(gloss) };
                if (sense.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array)
                {
                    var example = examples.EnumerateArray()
                        .Where(x => Str(x, "type") is null or "example")
                        .Select(x => Str(x, "text"))
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                    if (example != null) meaning.Examples.Add(Clip(example));
                }
                meanings.Add(meaning);
                break; // một nghĩa cho mỗi nhóm từ loại
            }
        }
        return meanings;
    }

    /// <summary>IPA đầu tiên + file âm thanh đầu tiên (Wikimedia Commons, https)</summary>
    public static (string? Phonetic, string? AudioUrl) ParseSounds(JsonDocument doc)
    {
        string? ipa = null, audio = null;
        if (!doc.RootElement.TryGetProperty("pronunciations", out var groups) || groups.ValueKind != JsonValueKind.Array) return (null, null);

        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("sounds", out var sounds) || sounds.ValueKind != JsonValueKind.Array) continue;
            foreach (var sound in sounds.EnumerateArray())
            {
                ipa ??= Str(sound, "ipa");
                var file = Str(sound, "audio");
                if (audio == null && !string.IsNullOrWhiteSpace(file))
                    audio = "https://commons.wikimedia.org/wiki/Special:FilePath/" + Uri.EscapeDataString(file);
            }
            if (ipa != null && audio != null) break;
        }
        return (ipa == null ? null : Clip(ipa), FlashcardUrl.IsHttps(audio) ? audio : null);
    }
    //#endregion

    //#region dịch từ
    public async Task<string?> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
    {
        text = (text ?? string.Empty).Trim();
        var from = NormalizeLang(fromLang);
        var to = NormalizeLang(toLang);
        if (text.Length == 0 || text.Length > 100 || from == null || to == null || from == to) return null;

        var key = $"wikt:tr:{from}:{to}:{text.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out string? cached)) return cached;

        string? translated;
        bool ok;
        if (from == "en")
        {
            // Wiktionary tiếng Anh: bảng dịch của từ tiếng Anh sang mọi ngôn ngữ
            var (doc, status) = await GetAsync(WordPath("en", text, "translations", "en"), cancellationToken);
            ok = status != LookupStatus.Error;
            translated = doc == null ? null : PickTranslations(doc, to);
            doc?.Dispose();
        }
        else if (to == "en")
        {
            // Từ ngôn ngữ khác trong Wiktionary tiếng Anh: gloss chính là nghĩa tiếng Anh
            var (doc, status) = await GetAsync(WordPath("en", text, "definitions", from), cancellationToken);
            ok = status != LookupStatus.Error;
            translated = doc == null ? null : PreferredMeaning(ParseMeanings(doc))?.Definition;
            doc?.Dispose();
        }
        else
        {
            return null; // cặp ngôn ngữ không có dữ liệu
        }

        if (!ok) return null; // lỗi tạm thời: không cache
        _cache.Set(key, translated, translated == null ? NotFoundTtl : FoundTtl);
        return translated;
    }

    private static readonly string[] PosPriority = { "noun", "verb", "adj", "adv" };

    /// <summary>Nghĩa dùng làm bản dịch: ưu tiên danh từ, động từ, tính từ, trạng từ (Wiktionary có thể xếp tính từ trước, vd "nhà" -> "domestic")</summary>
    public static QFlashcardMeaning? PreferredMeaning(List<QFlashcardMeaning> meanings) => ByPosPriority(meanings).FirstOrDefault();

    // OrderBy ổn định: cùng mức ưu tiên giữ thứ tự của Wiktionary
    private static IEnumerable<QFlashcardMeaning> ByPosPriority(IEnumerable<QFlashcardMeaning> meanings) =>
        meanings.OrderBy(m => Array.IndexOf(PosPriority, m.Pos) is var i && i < 0 ? PosPriority.Length : i);

    /// <summary>Bản dịch sang <paramref name="to"/> của nghĩa đầu tiên có bản dịch (tối đa MaxTranslations, không trùng)</summary>
    public static string? PickTranslations(JsonDocument doc, string to)
    {
        if (!doc.RootElement.TryGetProperty("translations", out var groups) || groups.ValueKind != JsonValueKind.Array) return null;

        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("translations", out var items) || items.ValueKind != JsonValueKind.Array) continue;

            var matches = items.EnumerateArray()
                .Where(t => Str(t, "code") == to || Str(t, "lang_code") == to)
                .Select(t => (Sense: Str(t, "sense") ?? string.Empty, Word: Str(t, "word")))
                .Where(t => !string.IsNullOrWhiteSpace(t.Word))
                .ToList();
            if (matches.Count == 0) continue;

            var firstSense = matches[0].Sense;
            var words = matches.Where(t => t.Sense == firstSense).Select(t => t.Word!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTranslations).ToList();
            return Clip(string.Join(", ", words));
        }
        return null;
    }
    //#endregion

    //#region HTTP
    private enum LookupStatus { Found, NotFound, Error }

    private async Task<(JsonDocument? Doc, LookupStatus Status)> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(path, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, LookupStatus.NotFound);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WiktAPI trả {Status} cho {Path}", (int)response.StatusCode, path);
                return (null, LookupStatus.Error);
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return (await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken), LookupStatus.Found);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "Gọi WiktAPI thất bại: {Path}", path);
            return (null, LookupStatus.Error);
        }
    }
    //#endregion

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? LastString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).LastOrDefault()
            : null;

    private static string Clip(string s)
    {
        var t = s.Trim();
        return t.Length > FlashcardLimits.MaxText ? t[..FlashcardLimits.MaxText] : t;
    }
}

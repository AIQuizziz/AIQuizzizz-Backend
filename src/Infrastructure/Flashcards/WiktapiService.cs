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
///  - Wiktionary phân biệt hoa/thường (xem ResolveAsync): chữ người dùng bôi đen có thể viết hoa ở đầu câu / tiêu đề.
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

    /// <summary>Kết quả tra trong cache, kèm mục từ thật đã khớp (vd gõ "Junior" -> mục "junior") để phần dịch dùng lại</summary>
    private sealed record CachedLookup(DictionaryLookupResult Result, string? Headword);

    //#region tra từ
    public async Task<DictionaryLookupResult> LookupAsync(string word, string lang, CancellationToken cancellationToken = default)
    {
        word = (word ?? string.Empty).Trim();
        var code = NormalizeLang(lang);
        if (word.Length == 0 || word.Length > 100 || code == null) return DictionaryLookupResult.NotFound;
        return (await LookupCachedAsync(word, code, cancellationToken))?.Result ?? DictionaryLookupResult.Error;
    }

    /// <summary>null = lỗi tạm thời (không cache)</summary>
    private async Task<CachedLookup?> LookupCachedAsync(string word, string code, CancellationToken cancellationToken)
    {
        // khoá theo đúng chữ đã gõ: "House" và "house" có thể là hai mục từ khác nhau
        var key = $"wikt:lookup:{code}:{word}";
        if (_cache.TryGetValue(key, out CachedLookup? cached) && cached != null) return cached;

        var edition = EditionFor(code);
        var (headword, definitions, status) = await ResolveAsync(edition, word, code, cancellationToken);
        if (status == LookupStatus.Error) return null;
        if (headword == null || definitions == null)
        {
            var notFound = new CachedLookup(DictionaryLookupResult.NotFound, null);
            _cache.Set(key, notFound, NotFoundTtl);
            return notFound;
        }

        var entry = new DictionaryEntry { Meanings = ParseMeanings(definitions) };
        definitions.Dispose();

        // Phát âm là phần phụ: lỗi thì vẫn trả nghĩa
        var (pronunciations, _) = await GetAsync(WordPath(edition, headword, "pronunciations", code), cancellationToken);
        if (pronunciations != null)
        {
            (entry.Phonetic, entry.AudioUrl) = ParseSounds(pronunciations);
            pronunciations.Dispose();
        }

        var result = entry.Meanings.Count == 0
            ? new CachedLookup(DictionaryLookupResult.NotFound, null)
            : new CachedLookup(DictionaryLookupResult.Found(entry), headword);
        _cache.Set(key, result, entry.Meanings.Count == 0 ? NotFoundTtl : FoundTtl);
        return result;
    }

    /// <summary>
    /// Chọn mục từ theo hoa/thường (WiktAPI phân biệt hoa/thường):
    ///  - chữ thường hoặc chỉ hoa chữ đầu: tra cả hai dạng, lấy dạng nhiều nghĩa hơn, bằng nhau thì giữ đúng chữ đã gõ.
    ///    "Created" -> "created" (không có "Created"); "Junior"/"Middle" -> "junior"/"middle" (dạng hoa chỉ là tên thị trấn / vùng đất);
    ///    "House" -> "house"; "English" giữ nguyên ("english" chỉ là thuật ngữ bi-a); "london" -> "London".
    ///  - dạng khác (FOMO, iPhone, Give Up): đúng chữ gốc trước, rồi chữ thường, rồi hoa chữ đầu.
    /// </summary>
    private async Task<(string? Word, JsonDocument? Definitions, LookupStatus Status)> ResolveAsync(
        string edition, string word, string lang, CancellationToken cancellationToken)
    {
        var lower = word.ToLowerInvariant();
        var capitalized = Capitalize(lower);

        if (word != lower && word != capitalized)
        {
            foreach (var candidate in new[] { word, lower, capitalized }.Distinct())
            {
                var (doc, status) = await GetAsync(WordPath(edition, candidate, "definitions", lang), cancellationToken);
                if (status == LookupStatus.Error) return (null, null, LookupStatus.Error);
                if (doc != null) return (candidate, doc, LookupStatus.Found);
            }
            return (null, null, LookupStatus.NotFound);
        }

        var candidates = new[] { word, word == lower ? capitalized : lower }.Distinct().ToArray();
        var results = await Task.WhenAll(candidates.Select(c => GetAsync(WordPath(edition, c, "definitions", lang), cancellationToken)));
        if (results.Any(r => r.Status == LookupStatus.Error))
        {
            foreach (var r in results) r.Doc?.Dispose();
            return (null, null, LookupStatus.Error);
        }

        // OrderBy ổn định: bằng số nghĩa thì giữ chữ đã gõ (đứng đầu mảng)
        var best = candidates.Zip(results)
            .Where(x => x.Second.Doc != null)
            .OrderByDescending(x => SenseCount(x.Second.Doc!))
            .Select(x => (Word: x.First, Doc: x.Second.Doc!))
            .FirstOrDefault();
        foreach (var r in results)
            if (!ReferenceEquals(r.Doc, best.Doc)) r.Doc?.Dispose();
        return best.Doc == null ? (null, null, LookupStatus.NotFound) : (best.Word, best.Doc, LookupStatus.Found);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static int SenseCount(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("definitions", out var groups) && groups.ValueKind == JsonValueKind.Array
            ? groups.EnumerateArray()
                .Where(g => g.TryGetProperty("senses", out var s) && s.ValueKind == JsonValueKind.Array)
                .Sum(g => g.GetProperty("senses").EnumerateArray().Count(s => !string.IsNullOrWhiteSpace(LastString(s, "glosses"))))
            : 0;

    private static readonly string[] PosPriority = { "noun", "verb", "adj", "adv" };

    /// <summary>Nghĩa hiếm / cổ / chỉ là cách viết khác: xếp sau (vd "English" động từ "archaic, rare")</summary>
    private static readonly HashSet<string> DemotedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "rare", "obsolete", "archaic", "dated", "dialectal", "nonstandard", "alt-of", "misspelling"
    };

    /// <summary>
    /// Mỗi từ loại lấy nghĩa đầu tiên có gloss (gloss cuối là nghĩa cụ thể nhất) và một câu ví dụ ngắn (bỏ trích dẫn sách), rồi xếp:
    ///  1. nghĩa hiếm / cổ / cách viết khác xuống cuối;
    ///  2. dạng biến đổi ("simple past of run", "plural of child") lên đầu: "ran" không mở đầu bằng danh từ hiếm "Yarns coiled on a spun-yarn winch";
    ///  3. danh từ > động từ > tính từ > trạng từ > còn lại (Wiktionary có thể để tính từ trước, vd "nhà" -> "domestic").
    /// Tối đa MaxMeanings nghĩa.
    /// </summary>
    public static List<QFlashcardMeaning> ParseMeanings(JsonDocument doc)
    {
        var picked = new List<(QFlashcardMeaning Meaning, bool Demoted, bool FormOf, int Pos)>();
        if (!doc.RootElement.TryGetProperty("definitions", out var groups) || groups.ValueKind != JsonValueKind.Array) return new();

        var seen = new HashSet<string>();
        foreach (var group in groups.EnumerateArray())
        {
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
                        .Where(IsShortExample)
                        .Select(x => Str(x, "text"))
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                    if (example != null) meaning.Examples.Add(Clip(example));
                }

                var tags = Strings(sense, "tags");
                var posIndex = Array.IndexOf(PosPriority, pos);
                picked.Add((meaning, tags.Any(DemotedTags.Contains), tags.Contains("form-of"), posIndex < 0 ? PosPriority.Length : posIndex));
                break; // một nghĩa cho mỗi nhóm từ loại
            }
        }

        // OrderBy ổn định: cùng mức ưu tiên giữ thứ tự của Wiktionary
        return picked
            .OrderBy(x => x.Demoted)
            .ThenBy(x => !x.FormOf)
            .ThenBy(x => x.Pos)
            .Select(x => x.Meaning)
            .Take(FlashcardLimits.MaxMeanings)
            .ToList();
    }

    /// <summary>Câu không ghi type dài hơn mức này coi như trích dẫn</summary>
    private const int MaxUntypedExampleLength = 200;

    /// <summary>
    /// Câu ví dụ ngắn: type "example"; hoặc không ghi type, không có nguồn trích (ref = trích dẫn sách / báo, thường rất dài),
    /// không mở đầu bằng năm và không quá dài (có mục dồn cả nguồn trích vào chữ: "1821-1822, Vicesimus Knox, Remarks on…").
    /// </summary>
    private static bool IsShortExample(JsonElement example) =>
        Str(example, "type") switch
        {
            "example" => true,
            null => !example.TryGetProperty("ref", out _)
                    && Str(example, "text") is { } text
                    && text.Length <= MaxUntypedExampleLength
                    && !CitationStart().IsMatch(text),
            _ => false
        };

    [GeneratedRegex(@"^\s*\d{3,4}\b")]
    private static partial Regex CitationStart();

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

        var key = $"wikt:tr:{from}:{to}:{text}";
        if (_cache.TryGetValue(key, out string? cached)) return cached;

        string? translated;
        if (from == "en")
        {
            // Mục từ thật đã chọn khi tra (vd "Junior" -> "junior"), không có trong từ điển thì cũng không có bảng dịch
            var lookup = await LookupCachedAsync(text, "en", cancellationToken);
            if (lookup == null) return null; // lỗi tạm thời: không cache
            translated = null;
            if (lookup.Headword != null)
            {
                // Wiktionary tiếng Anh: bảng dịch của từ tiếng Anh sang mọi ngôn ngữ
                var (doc, status) = await GetAsync(WordPath("en", lookup.Headword, "translations", "en"), cancellationToken);
                if (status == LookupStatus.Error) return null;
                translated = doc == null ? null : PickTranslations(doc, to);
                doc?.Dispose();
            }
        }
        else if (to == "en")
        {
            // Từ ngôn ngữ khác trong Wiktionary tiếng Anh: gloss chính là nghĩa tiếng Anh
            var (_, doc, status) = await ResolveAsync("en", text, from, cancellationToken);
            if (status == LookupStatus.Error) return null;
            translated = doc == null ? null : PreferredMeaning(ParseMeanings(doc))?.Definition;
            doc?.Dispose();
        }
        else
        {
            return null; // cặp ngôn ngữ không có dữ liệu
        }

        _cache.Set(key, translated, translated == null ? NotFoundTtl : FoundTtl);
        return translated;
    }

    /// <summary>Nghĩa dùng làm bản dịch: nghĩa đứng đầu theo thứ tự của ParseMeanings</summary>
    public static QFlashcardMeaning? PreferredMeaning(List<QFlashcardMeaning> meanings) => meanings.FirstOrDefault();

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
        Strings(e, name).LastOrDefault();

    private static List<string> Strings(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : new List<string>();

    private static string Clip(string s)
    {
        var t = s.Trim();
        return t.Length > FlashcardLimits.MaxText ? t[..FlashcardLimits.MaxText] : t;
    }
}

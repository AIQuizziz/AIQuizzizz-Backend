using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CleanArchitectureBase.Infrastructure.Flashcards;

/// <summary>Tra từ qua https://dictionaryapi.dev (Free Dictionary API). Host cố định, từ khoá luôn được mã hoá vào đường dẫn (không có SSRF).</summary>
public partial class FreeDictionaryService : IDictionaryLookupService
{
    private const string BaseUrl = "https://api.dictionaryapi.dev/api/v2/entries/";
    private static readonly TimeSpan FoundTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FreeDictionaryService> _logger;

    public FreeDictionaryService(HttpClient http, IMemoryCache cache, ILogger<FreeDictionaryService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z]{2,4})?$")]
    private static partial Regex LangPattern();

    public async Task<DictionaryLookupResult> LookupAsync(string word, string lang, CancellationToken cancellationToken = default)
    {
        word = (word ?? string.Empty).Trim();
        lang = (lang ?? "en").Trim();
        if (word.Length == 0 || word.Length > 100 || !LangPattern().IsMatch(lang)) return DictionaryLookupResult.NotFound;

        var key = $"dict:{lang}:{word.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out DictionaryLookupResult? cached) && cached != null) return cached;

        try
        {
            using var response = await GetWithRetryAsync($"{BaseUrl}{lang}/{Uri.EscapeDataString(word.ToLowerInvariant())}", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _cache.Set(key, DictionaryLookupResult.NotFound, NotFoundTtl);
                return DictionaryLookupResult.NotFound;
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Free Dictionary API trả {Status} cho {Lang}/{Word}", (int)response.StatusCode, lang, word);
                return DictionaryLookupResult.Error; // lỗi tạm thời: không cache
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var entry = Parse(doc.RootElement);
            var result = entry == null ? DictionaryLookupResult.NotFound : DictionaryLookupResult.Found(entry);
            _cache.Set(key, result, entry == null ? NotFoundTtl : FoundTtl);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "Tra từ điển thất bại cho {Lang}/{Word}", lang, word);
            return DictionaryLookupResult.Error;
        }
    }

    /// <summary>Số lần gọi tối đa khi dịch vụ trả lỗi máy chủ tạm thời</summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// Free Dictionary API hay trả 5xx thoáng qua (đã gặp HTTP 530 "error code: 1033" của Cloudflare với cả từ có thật, lần sau lại 200).
    /// Gặp 5xx thì thử lại ngay vài lần ngắn; 404 (không có từ), 2xx và timeout không thử lại (timeout đã tốn cả thời gian chờ).
    /// </summary>
    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await _http.GetAsync(url, cancellationToken);
            if ((int)response.StatusCode < 500 || attempt >= MaxAttempts) return response;

            _logger.LogInformation("Free Dictionary API trả {Status}, thử lại lần {Attempt}", (int)response.StatusCode, attempt + 1);
            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }
    }

    /// <summary>Phân tích phản hồi (mảng entry). Mỗi từ loại lấy định nghĩa đầu tiên, tối đa 5 nghĩa.</summary>
    public static DictionaryEntry? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;

        var result = new DictionaryEntry();

        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            result.Phonetic ??= Str(entry, "phonetic");

            if (entry.TryGetProperty("phonetics", out var phonetics) && phonetics.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in phonetics.EnumerateArray())
                {
                    result.Phonetic ??= Str(p, "text");
                    var audio = Str(p, "audio");
                    if (result.AudioUrl == null && FlashcardUrl.IsHttps(audio)) result.AudioUrl = audio;
                }
            }

            if (!entry.TryGetProperty("meanings", out var meanings) || meanings.ValueKind != JsonValueKind.Array) continue;

            foreach (var meaning in meanings.EnumerateArray())
            {
                if (result.Meanings.Count >= FlashcardLimits.MaxMeanings) break;
                if (!meaning.TryGetProperty("definitions", out var defs) || defs.ValueKind != JsonValueKind.Array) continue;

                var first = defs.EnumerateArray().FirstOrDefault(d => !string.IsNullOrWhiteSpace(Str(d, "definition")));
                if (first.ValueKind != JsonValueKind.Object) continue;

                var item = new QFlashcardMeaning
                {
                    Pos = Str(meaning, "partOfSpeech")?.ToLowerInvariant(),
                    Definition = Clip(Str(first, "definition")!)
                };
                var example = Str(first, "example");
                if (!string.IsNullOrWhiteSpace(example)) item.Examples.Add(Clip(example));

                foreach (var source in new[] { first, meaning })
                {
                    if (!source.TryGetProperty("synonyms", out var syn) || syn.ValueKind != JsonValueKind.Array) continue;
                    foreach (var s in syn.EnumerateArray())
                    {
                        var text = s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(text) && item.Synonyms.Count < 5 && !item.Synonyms.Contains(text))
                            item.Synonyms.Add(Clip(text));
                    }
                }

                result.Meanings.Add(item);
            }
        }

        return result.Meanings.Count == 0 ? null : result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string Clip(string s)
    {
        var t = s.Trim();
        return t.Length > FlashcardLimits.MaxText ? t[..FlashcardLimits.MaxText] : t;
    }
}

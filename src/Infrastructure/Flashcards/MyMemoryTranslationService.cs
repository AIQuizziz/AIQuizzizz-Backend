using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArchitectureBase.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CleanArchitectureBase.Infrastructure.Flashcards;

/// <summary>
/// Dịch ngắn qua MyMemory (https://api.mymemory.translated.net, không cần khoá). Host cố định.
/// Chất lượng và hạn mức miễn phí của dịch vụ này chưa được kiểm chứng; khi lỗi trả null để thẻ vẫn được lưu (chỉ không có bản dịch).
/// </summary>
public partial class MyMemoryTranslationService : ITranslationService
{
    private const string BaseUrl = "https://api.mymemory.translated.net/get";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MyMemoryTranslationService> _logger;

    public MyMemoryTranslationService(HttpClient http, IMemoryCache cache, ILogger<MyMemoryTranslationService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z]{2,4})?$")]
    private static partial Regex LangPattern();

    public async Task<string?> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > 500) return null;
        if (!LangPattern().IsMatch(fromLang ?? "") || !LangPattern().IsMatch(toLang ?? "")) return null;
        if (string.Equals(fromLang, toLang, StringComparison.OrdinalIgnoreCase)) return null;

        var key = $"tr:{fromLang}:{toLang}:{text.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out string? cached)) return cached;

        try
        {
            var url = $"{BaseUrl}?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString($"{fromLang}|{toLang}")}";
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MyMemory trả {Status}", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("responseData", out var data)
                || !data.TryGetProperty("translatedText", out var t)
                || t.ValueKind != JsonValueKind.String)
                return null;

            var translated = t.GetString()?.Trim();
            // MyMemory trả thông báo lỗi/hạn mức bằng chính trường văn bản (vd. "MYMEMORY WARNING: ...")
            if (string.IsNullOrWhiteSpace(translated) || translated.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                return null;

            if (translated.Length > FlashcardLimits.MaxText) translated = translated[..FlashcardLimits.MaxText];
            _cache.Set(key, translated, TimeSpan.FromHours(24));
            return translated;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "Dịch thất bại {From}->{To}", fromLang, toLang);
            return null;
        }
    }
}

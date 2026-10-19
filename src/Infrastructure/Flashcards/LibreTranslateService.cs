using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchitectureBase.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CleanArchitectureBase.Infrastructure.Flashcards;

/// <summary>
/// Dịch câu bằng LibreTranslate tự host. Địa chỉ lấy từ cấu hình LibreTranslate:BaseUrl (do quản trị đặt, không phải dữ liệu người dùng),
/// khoá tuỳ chọn LibreTranslate:ApiKey. Chưa cấu hình hoặc dịch vụ lỗi thì trả "unavailable", không ném lỗi.
/// </summary>
public class LibreTranslateService : ISentenceTranslationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LibreTranslateService> _logger;
    private readonly Uri? _baseUri;
    private readonly string? _apiKey;

    public LibreTranslateService(HttpClient http, IMemoryCache cache, IConfiguration configuration, ILogger<LibreTranslateService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        var baseUrl = configuration["LibreTranslate:BaseUrl"];
        if (Uri.TryCreate(baseUrl?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            _baseUri = uri;
        _apiKey = configuration["LibreTranslate:ApiKey"];
    }

    /// <summary>LibreTranslate dùng mã ngôn ngữ ngắn ("pt", "zh"), không có vùng ("pt-BR").</summary>
    public static string ToLibreCode(string lang) => lang.Split('-')[0].ToLowerInvariant();

    public async Task<SentenceTranslationResult> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
    {
        if (_baseUri == null || string.IsNullOrWhiteSpace(text)) return SentenceTranslationResult.Unavailable;

        var source = ToLibreCode(fromLang);
        var target = ToLibreCode(toLang);
        if (source == target) return SentenceTranslationResult.Unavailable;

        var key = $"lt:{source}:{target}:{text}";
        if (_cache.TryGetValue(key, out string? cached) && cached != null) return SentenceTranslationResult.Ok(cached);

        try
        {
            var body = new Dictionary<string, string> { ["q"] = text, ["source"] = source, ["target"] = target, ["format"] = "text" };
            if (!string.IsNullOrEmpty(_apiKey)) body["api_key"] = _apiKey;

            using var response = await _http.PostAsJsonAsync(new Uri(_baseUri, "translate"), body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 400 thường là cặp ngôn ngữ chưa nạp mô hình (LT_LOAD_ONLY) hoặc vượt giới hạn ký tự
                _logger.LogWarning("LibreTranslate trả {Status} cho {From}->{To}", (int)response.StatusCode, source, target);
                return SentenceTranslationResult.Unavailable;
            }

            var result = await response.Content.ReadFromJsonAsync<LibreResponse>(cancellationToken: cancellationToken);
            var translated = result?.TranslatedText?.Trim();
            if (string.IsNullOrEmpty(translated)) return SentenceTranslationResult.Unavailable;

            _cache.Set(key, translated, CacheTtl);
            return SentenceTranslationResult.Ok(translated);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "Dịch câu qua LibreTranslate thất bại {From}->{To}", source, target);
            return SentenceTranslationResult.Unavailable;
        }
    }

    private sealed class LibreResponse
    {
        [JsonPropertyName("translatedText")] public string? TranslatedText { get; set; }
    }
}

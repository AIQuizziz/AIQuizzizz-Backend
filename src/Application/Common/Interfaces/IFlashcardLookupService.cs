using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Common.Interfaces;

public class DictionaryEntry
{
    public string? Phonetic { get; set; }
    public string? AudioUrl { get; set; }
    public List<QFlashcardMeaning> Meanings { get; set; } = new();
}

/// <summary>Kết quả tra từ: found (Entry có dữ liệu) | not_found (từ không có trong từ điển) | error (dịch vụ ngoài lỗi/timeout - tạm thời).</summary>
public class DictionaryLookupResult
{
    public string Status { get; init; } = "error";
    public DictionaryEntry? Entry { get; init; }

    public static DictionaryLookupResult Found(DictionaryEntry entry) => new() { Status = "found", Entry = entry };
    public static readonly DictionaryLookupResult NotFound = new() { Status = "not_found" };
    public static readonly DictionaryLookupResult Error = new() { Status = "error" };
}

/// <summary>Tra từ điển (hiện dùng Free Dictionary API).</summary>
public interface IDictionaryLookupService
{
    Task<DictionaryLookupResult> LookupAsync(string word, string lang, CancellationToken cancellationToken = default);
}

/// <summary>Dịch văn bản ngắn giữa hai ngôn ngữ. Trả null khi thất bại.</summary>
public interface ITranslationService
{
    Task<string?> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default);
}

public class SentenceTranslationResult
{
    /// <summary>ok | unavailable (chưa cấu hình / dịch vụ lỗi / không hỗ trợ cặp ngôn ngữ)</summary>
    public string Status { get; init; } = "unavailable";
    public string? Text { get; init; }

    public static SentenceTranslationResult Ok(string text) => new() { Status = "ok", Text = text };
    public static readonly SentenceTranslationResult Unavailable = new();
}

/// <summary>Dịch câu / đoạn ngắn bằng LibreTranslate tự host (cấu hình LibreTranslate:BaseUrl).</summary>
public interface ISentenceTranslationService
{
    Task<SentenceTranslationResult> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default);
}

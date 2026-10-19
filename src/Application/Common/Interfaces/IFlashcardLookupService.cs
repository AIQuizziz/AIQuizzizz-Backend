using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Common.Interfaces;

public class DictionaryEntry
{
    public string? Phonetic { get; set; }
    public string? AudioUrl { get; set; }
    public List<QFlashcardMeaning> Meanings { get; set; } = new();
}

/// <summary>Tra từ điển (hiện dùng Free Dictionary API). Trả null khi không tìm thấy hoặc dịch vụ ngoài lỗi.</summary>
public interface IDictionaryLookupService
{
    Task<DictionaryEntry?> LookupAsync(string word, string lang, CancellationToken cancellationToken = default);
}

/// <summary>Dịch văn bản ngắn giữa hai ngôn ngữ. Trả null khi thất bại.</summary>
public interface ITranslationService
{
    Task<string?> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default);
}

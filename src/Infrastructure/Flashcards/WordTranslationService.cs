using CleanArchitectureBase.Application.Common.Interfaces;

namespace CleanArchitectureBase.Infrastructure.Flashcards;

/// <summary>
/// Dịch một từ, chỉ dùng dịch vụ tự host: bảng dịch Wiktionary (WiktAPI) trước, không có thì LibreTranslate.
/// Wiktionary chính xác theo nghĩa nhưng không phủ hết từ (vd "serendipity" không có bản dịch tiếng Việt).
/// </summary>
public class WordTranslationService : ITranslationService
{
    private readonly WiktapiService _wiktionary;
    private readonly ISentenceTranslationService _machine;

    public WordTranslationService(WiktapiService wiktionary, ISentenceTranslationService machine)
    {
        _wiktionary = wiktionary;
        _machine = machine;
    }

    public async Task<string?> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
    {
        var fromDictionary = await _wiktionary.TranslateAsync(text, fromLang, toLang, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fromDictionary)) return fromDictionary;

        // Máy dịch luôn "dịch" ra gì đó, kể cả chuỗi vô nghĩa ("zzqqxxw" -> "zzqxxw"): chỉ dùng khi từ có trong từ điển (tra từ đã cache)
        var lookup = await _wiktionary.LookupAsync(text, fromLang, cancellationToken);
        if (lookup.Status == "not_found") return null;

        var machine = await _machine.TranslateAsync(text, fromLang, toLang, cancellationToken);
        if (machine.Status != "ok" || string.IsNullOrWhiteSpace(machine.Text)) return null;
        // Máy dịch trả lại đúng từ gốc = không dịch được (vd LibreTranslate: "ephemeral" -> "ephemeral").
        // Chỉ xét ở đây: bản dịch giống từ gốc lấy từ bảng dịch Wiktionary là có thật (vd "pizza" -> "pizza").
        var translated = machine.Text.Trim();
        return string.Equals(translated, text.Trim(), StringComparison.OrdinalIgnoreCase) ? null : translated;
    }
}

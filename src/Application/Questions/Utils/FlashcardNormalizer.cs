using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Questions.Utils;

/// <summary>Giới hạn và làm sạch DataJson của thẻ ghi nhớ trước khi lưu (dữ liệu đến từ extension/trình duyệt nên không tin tưởng).</summary>
public static class FlashcardNormalizer
{
    public const int MaxMeanings = 5;
    public const int MaxExamples = 3;
    public const int MaxSynonyms = 10;
    public const int MaxTextLength = 1000;
    public const int MaxUrlLength = 2000;

    public static bool IsHttpsUrl(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (url.Length <= MaxUrlLength
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Danh sách lỗi (rỗng nếu hợp lệ). Thẻ không có nghĩa nào vẫn hợp lệ (thẻ nháp, chưa tra được).</summary>
    public static List<string> Validate(QTypeLanguageFlashcard? card)
    {
        var errors = new List<string>();
        if (card == null) { errors.Add("Thiếu dữ liệu thẻ ghi nhớ"); return errors; }

        if (card.Back.Meanings.Count > MaxMeanings) errors.Add($"Tối đa {MaxMeanings} nghĩa cho mỗi thẻ");
        foreach (var m in card.Back.Meanings)
        {
            if (string.IsNullOrWhiteSpace(m.Definition) && string.IsNullOrWhiteSpace(m.Translation))
                errors.Add("Mỗi nghĩa phải có định nghĩa hoặc bản dịch");
            if ((m.Definition?.Length ?? 0) > MaxTextLength || (m.Translation?.Length ?? 0) > MaxTextLength)
                errors.Add($"Định nghĩa/bản dịch không được vượt quá {MaxTextLength} ký tự");
            if (m.Examples.Count > MaxExamples) errors.Add($"Tối đa {MaxExamples} ví dụ cho mỗi nghĩa");
            if (m.Examples.Any(e => e.Length > MaxTextLength) || m.Synonyms.Any(s => s.Length > MaxTextLength))
                errors.Add($"Ví dụ/từ đồng nghĩa không được vượt quá {MaxTextLength} ký tự");
        }

        if (!IsHttpsUrl(card.Back.AudioUrl)) errors.Add("audioUrl phải là địa chỉ https hợp lệ");
        if (!IsHttpsUrl(card.Source?.Url)) errors.Add("source.url phải là địa chỉ https hợp lệ");
        if ((card.Source?.Context?.Length ?? 0) > MaxTextLength) errors.Add($"Ngữ cảnh không được vượt quá {MaxTextLength} ký tự");
        if ((card.Source?.Title?.Length ?? 0) > 500) errors.Add("Tiêu đề trang không được vượt quá 500 ký tự");
        return errors;
    }

    /// <summary>Bản sao đã trim, bỏ mục rỗng, cắt theo giới hạn, ép V = 1. Gọi sau khi Validate.</summary>
    public static QTypeLanguageFlashcard Normalize(QTypeLanguageFlashcard card)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static List<string> L(IEnumerable<string>? xs, int max) =>
            (xs ?? Enumerable.Empty<string>()).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Take(max).ToList();

        return new QTypeLanguageFlashcard
        {
            V = 1,
            Back = new QFlashcardBack
            {
                Phonetic = T(card.Back.Phonetic),
                AudioUrl = T(card.Back.AudioUrl),
                Meanings = card.Back.Meanings.Take(MaxMeanings).Select(m => new QFlashcardMeaning
                {
                    Pos = T(m.Pos)?.ToLowerInvariant(),
                    Definition = (m.Definition ?? string.Empty).Trim(),
                    Translation = T(m.Translation),
                    Examples = L(m.Examples, MaxExamples),
                    Synonyms = L(m.Synonyms, MaxSynonyms)
                }).ToList()
            },
            Lang = card.Lang == null ? null : new QFlashcardLang { From = T(card.Lang.From), To = T(card.Lang.To) },
            Source = card.Source == null ? null : new QFlashcardSource
            {
                Url = T(card.Source.Url), Title = T(card.Source.Title), Context = T(card.Source.Context), CapturedAt = card.Source.CapturedAt
            },
            Lookup = card.Lookup == null ? null : new QFlashcardLookup { Provider = T(card.Lookup.Provider), Status = T(card.Lookup.Status) }
        };
    }
}

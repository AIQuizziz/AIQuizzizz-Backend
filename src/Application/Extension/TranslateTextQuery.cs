using System.Text.RegularExpressions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

public class TranslateResultDto
{
    /// <summary>word | sentence</summary>
    public string Kind { get; set; } = "word";
    /// <summary>Văn bản đã chuẩn hoá (với từ: bỏ dấu câu ở hai đầu)</summary>
    public string Text { get; set; } = string.Empty;
    public string SourceLang { get; set; } = "en";
    public string TargetLang { get; set; } = "vi";
    public string? Translation { get; set; }
    /// <summary>ok | unavailable | skipped (cùng ngôn ngữ nguồn và đích)</summary>
    public string TranslationStatus { get; set; } = "skipped";
    /// <summary>found | not_found | error | skipped (câu không tra từ điển)</summary>
    public string LookupStatus { get; set; } = "skipped";
    /// <summary>Mặt sau thẻ dựng sẵn từ kết quả này, gửi nguyên lại khi bấm "Thêm vào flashcard" (khỏi tra lần hai)</summary>
    public QTypeLanguageFlashcard Back { get; set; } = new();
    /// <summary>Có thể thêm làm thẻ không (mặt trước tối đa <see cref="CaptureFlashcardCommand.MaxFrontLength"/> ký tự)</summary>
    public bool CanInsert { get; set; }
}

/// <summary>
/// Dịch văn bản người dùng vừa bôi đen cho tab Dịch của extension. Một từ: tra Free Dictionary + dịch từ như khi thêm thẻ.
/// Câu / cụm nhiều từ: dịch bằng LibreTranslate tự host.
/// </summary>
[Authorize]
public partial class TranslateTextQuery : IRequest<TranslateResultDto>
{
    public const int MaxLength = 1000;
    /// <summary>Từ dài hơn ngưỡng này (không có khoảng trắng) coi là câu, vd. văn bản tiếng Nhật/Trung viết liền</summary>
    public const int MaxWordLength = 40;

    public string? Text { get; set; }
    /// <summary>Không gửi thì lấy từ cài đặt của người dùng</summary>
    public string? SourceLang { get; set; }
    public string? TargetLang { get; set; }

    [GeneratedRegex("\\s+")]
    internal static partial Regex Whitespace();

    /// <summary>Bỏ dấu câu bao quanh một từ: “documentation,” → documentation</summary>
    public static string TrimPunctuation(string word) => word.Trim(' ', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '“', '”', '‘', '’', '«', '»', '…');

    public static string Normalize(string text) => Whitespace().Replace(text.Trim(), " ");

    public static bool IsWord(string normalized) => !normalized.Contains(' ') && normalized.Length <= MaxWordLength;
}

public class TranslateTextQueryValidator : AbstractValidator<TranslateTextQuery>
{
    public TranslateTextQueryValidator()
    {
        RuleFor(x => x.Text)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Chưa có văn bản để dịch")
            .Must(t => t == null || t.Trim().Length <= TranslateTextQuery.MaxLength)
            .WithMessage($"Chỉ dịch tối đa {TranslateTextQuery.MaxLength} ký tự");
        RuleFor(x => x.SourceLang).Must(l => l == null || ExtensionValidation.IsLang(l)).WithMessage("Mã ngôn ngữ nguồn không hợp lệ");
        RuleFor(x => x.TargetLang).Must(l => l == null || ExtensionValidation.IsLang(l)).WithMessage("Mã ngôn ngữ đích không hợp lệ");
    }
}

public class TranslateTextQueryHandler : IRequestHandler<TranslateTextQuery, TranslateResultDto>
{
    private const string DictionaryProvider = "dictionaryapi.dev";
    private const string SentenceProvider = "libretranslate";

    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IDictionaryLookupService _dictionary;
    private readonly ITranslationService _wordTranslator;
    private readonly ISentenceTranslationService _sentenceTranslator;

    public TranslateTextQueryHandler(
        IApplicationDbContext context,
        IUser user,
        IDictionaryLookupService dictionary,
        ITranslationService wordTranslator,
        ISentenceTranslationService sentenceTranslator)
    {
        _context = context;
        _user = user;
        _dictionary = dictionary;
        _wordTranslator = wordTranslator;
        _sentenceTranslator = sentenceTranslator;
    }

    public async Task<TranslateResultDto> Handle(TranslateTextQuery request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var settings = await _context.ExtensionSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
                       ?? ExtensionSettingsDto.Defaults(userId);

        var sourceLang = request.SourceLang ?? settings.SourceLang;
        var targetLang = request.TargetLang ?? settings.TargetLang;
        var text = TranslateTextQuery.Normalize(request.Text!);

        var isWord = TranslateTextQuery.IsWord(text);
        if (isWord)
        {
            var trimmed = TranslateTextQuery.TrimPunctuation(text);
            if (trimmed.Length > 0) text = trimmed;
        }

        var result = new TranslateResultDto
        {
            Kind = isWord ? "word" : "sentence",
            Text = text,
            SourceLang = sourceLang,
            TargetLang = targetLang,
            CanInsert = text.Length <= CaptureFlashcardCommand.MaxFrontLength,
            Back = new QTypeLanguageFlashcard { Lang = new QFlashcardLang { From = sourceLang, To = targetLang } }
        };

        if (isWord) await FillWord(result, cancellationToken);
        else await FillSentence(result, cancellationToken);
        return result;
    }

    private async Task FillWord(TranslateResultDto result, CancellationToken ct)
    {
        var lookup = await _dictionary.LookupAsync(result.Text, result.SourceLang, ct);
        result.LookupStatus = lookup.Status;
        result.Back.Lookup = new QFlashcardLookup { Provider = DictionaryProvider, Status = lookup.Status };
        if (lookup.Entry != null)
        {
            result.Back.Back.Phonetic = lookup.Entry.Phonetic;
            result.Back.Back.AudioUrl = lookup.Entry.AudioUrl;
            result.Back.Back.Meanings = lookup.Entry.Meanings;
        }

        if (string.Equals(result.SourceLang, result.TargetLang, StringComparison.OrdinalIgnoreCase)) return;

        var translated = await _wordTranslator.TranslateAsync(result.Text, result.SourceLang, result.TargetLang, ct);
        // Dịch vụ trả lại đúng từ gốc khi không dịch được; từ cũng không có trong từ điển thì coi như không dịch được
        var echoed = string.Equals(translated?.Trim(), result.Text, StringComparison.OrdinalIgnoreCase) && lookup.Entry == null;
        if (string.IsNullOrWhiteSpace(translated) || echoed)
        {
            result.TranslationStatus = "unavailable";
            return;
        }

        result.Translation = translated;
        result.TranslationStatus = "ok";
        if (result.Back.Back.Meanings.Count == 0) result.Back.Back.Meanings.Add(new QFlashcardMeaning());
        result.Back.Back.Meanings[0].Translation = translated;
    }

    private async Task FillSentence(TranslateResultDto result, CancellationToken ct)
    {
        result.LookupStatus = "skipped";
        result.Back.Lookup = new QFlashcardLookup { Provider = SentenceProvider, Status = "skipped" };

        if (string.Equals(result.SourceLang, result.TargetLang, StringComparison.OrdinalIgnoreCase)) return;

        var translated = await _sentenceTranslator.TranslateAsync(result.Text, result.SourceLang, result.TargetLang, ct);
        if (translated.Status != "ok" || string.IsNullOrWhiteSpace(translated.Text))
        {
            result.TranslationStatus = "unavailable";
            return;
        }

        result.Translation = translated.Text;
        result.TranslationStatus = "ok";
        result.Back.Back.Meanings.Add(new QFlashcardMeaning { Translation = translated.Text });
    }
}

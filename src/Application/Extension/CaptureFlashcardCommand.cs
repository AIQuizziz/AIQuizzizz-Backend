using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Extension;

public class CaptureFlashcardResult
{
    public Guid QuestionId { get; set; }
    public Guid QuestionSetId { get; set; }
    public string QuestionSetName { get; set; } = string.Empty;
    public string Front { get; set; } = string.Empty;
    /// <summary>Nghĩa ngắn để hiện trong thông báo (bản dịch nếu có, không thì định nghĩa); null nếu chưa tra được</summary>
    public string? Preview { get; set; }
    /// <summary>true: thẻ cùng từ đã có trong bộ đích, không tạo thêm</summary>
    public bool Duplicate { get; set; }
    /// <summary>found | not_found | skipped</summary>
    public string LookupStatus { get; set; } = "skipped";
    public bool Translated { get; set; }
}

/// <summary>Thêm một thẻ ghi nhớ từ extension (người dùng bôi đen từ rồi bấm menu/phím tắt).</summary>
[Authorize]
public partial class CaptureFlashcardCommand : IRequest<CaptureFlashcardResult>
{
    public const int MaxFrontLength = 200;

    /// <summary>Từ/cụm từ được bôi đen (mặt trước)</summary>
    public string? Front { get; set; }
    /// <summary>Không gửi: server tự tra từ điển (+dịch nếu bật). Có gửi: dùng đúng nội dung này (đã sửa tay).</summary>
    public QTypeLanguageFlashcard? Back { get; set; }
    /// <summary>Ghi đè cài đặt cho riêng lần này (không bắt buộc)</summary>
    public string? SourceLang { get; set; }
    public string? TargetLang { get; set; }
    public bool? Translate { get; set; }
    /// <summary>Câu chứa từ; chỉ lưu khi cài đặt StoreContext = true</summary>
    public string? Context { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceTitle { get; set; }
    /// <summary>Ghi đè bộ đích cho riêng lần này (phải là bộ người dùng được sửa)</summary>
    public Guid? TargetQuestionSetId { get; set; }

    [GeneratedRegex("\\s+")]
    internal static partial Regex Whitespace();
}

public class CaptureFlashcardCommandValidator : AbstractValidator<CaptureFlashcardCommand>
{
    public CaptureFlashcardCommandValidator()
    {
        RuleFor(x => x.Front)
            .Must(f => !string.IsNullOrWhiteSpace(f)).WithMessage("Chưa có từ nào được chọn")
            .Must(f => f == null || f.Trim().Length <= CaptureFlashcardCommand.MaxFrontLength)
            .WithMessage($"Chỉ nhận từ/cụm từ tối đa {CaptureFlashcardCommand.MaxFrontLength} ký tự");
        RuleFor(x => x.SourceLang).Must(l => l == null || ExtensionValidation.IsLang(l)).WithMessage("Mã ngôn ngữ nguồn không hợp lệ");
        RuleFor(x => x.TargetLang).Must(l => l == null || ExtensionValidation.IsLang(l)).WithMessage("Mã ngôn ngữ đích không hợp lệ");
        RuleFor(x => x.Context).MaximumLength(2000);
        RuleFor(x => x.SourceTitle).MaximumLength(1000);
        RuleFor(x => x.SourceUrl).MaximumLength(4000);
    }
}

public class CaptureFlashcardCommandHandler : IRequestHandler<CaptureFlashcardCommand, CaptureFlashcardResult>
{
    private const int MaxQuestionsPerSet = 500;
    private const string Provider = "dictionaryapi.dev";

    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    private readonly IQuestionSetService _questionSetService;
    private readonly IDictionaryLookupService _dictionary;
    private readonly ITranslationService _translator;
    private readonly TimeProvider _time;

    public CaptureFlashcardCommandHandler(
        IApplicationDbContext context,
        IUser user,
        IQuestionSetService questionSetService,
        IDictionaryLookupService dictionary,
        ITranslationService translator,
        TimeProvider time)
    {
        _context = context;
        _user = user;
        _questionSetService = questionSetService;
        _dictionary = dictionary;
        _translator = translator;
        _time = time;
    }

    public async Task<CaptureFlashcardResult> Handle(CaptureFlashcardCommand request, CancellationToken cancellationToken)
    {
        var userId = _user.UserId!.Value;
        var front = CaptureFlashcardCommand.Whitespace().Replace(request.Front!.Trim(), " ");

        var settings = await _context.ExtensionSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
                       ?? ExtensionSettingsDto.Defaults(userId);

        var now = _time.GetUtcNow();
        var questionSet = await ResolveTargetSet(userId, settings, request.TargetQuestionSetId, now, cancellationToken);

        // Trùng từ trong bộ đích: không tạo thêm (cũng làm cho việc gửi lại từ hàng đợi offline an toàn)
        var lowered = front.ToLower();
        var duplicate = await _context.Questions
            .Where(q => q.QuestionSetId == questionSet.Id && !q.IsDeleted && q.Type == QuestionType.LanguageFlashcard
                        && q.QuestionText != null && q.QuestionText.ToLower() == lowered)
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate != null)
        {
            var existing = FlashcardQuizBuilder.Parse(duplicate.DataJson);
            return new CaptureFlashcardResult
            {
                QuestionId = duplicate.Id,
                QuestionSetId = questionSet.Id,
                QuestionSetName = questionSet.Name,
                Front = duplicate.QuestionText ?? front,
                Preview = existing == null ? null : FlashcardQuizBuilder.AnswerText(existing),
                Duplicate = true,
                LookupStatus = existing?.Lookup?.Status ?? "skipped"
            };
        }

        if (questionSet.QuestionCount >= MaxQuestionsPerSet)
            throw new ErrorCodeException(ErrorCodes.NUMBER_OF_QUESTION_EXCEED_LIMIT, $"Bộ \"{questionSet.Name}\" đã đủ {MaxQuestionsPerSet} câu");

        var sourceLang = request.SourceLang ?? settings.SourceLang;
        var targetLang = request.TargetLang ?? settings.TargetLang;
        var translate = request.Translate ?? settings.TranslateEnabled;

        var (card, translated) = request.Back != null
            ? (request.Back, request.Back.Back.Meanings.Any(m => !string.IsNullOrWhiteSpace(m.Translation)))
            : await BuildFromLookup(front, sourceLang, targetLang, translate, cancellationToken);

        card.Lang ??= new QFlashcardLang { From = sourceLang, To = translate ? targetLang : null };
        card.Source = BuildSource(request, settings.StoreContext, now);

        // Dữ liệu từ client được kiểm tra như khi tạo câu hỏi thủ công
        var errors = FlashcardNormalizer.Validate(card);
        if (errors.Count > 0) throw new ErrorCodeException(errors.ToArray());
        card = FlashcardNormalizer.Normalize(card);

        var maxOrder = await _context.Questions
            .Where(q => q.QuestionSetId == questionSet.Id && !q.IsDeleted)
            .Select(q => (int?)q.Order)
            .MaxAsync(cancellationToken);

        var question = new Question
        {
            Id = Guid.NewGuid(),
            QuestionSetId = questionSet.Id,
            Type = QuestionType.LanguageFlashcard,
            QuestionText = front,
            TextFormat = TextFormat.PlainText,
            Score = 1,
            Order = (maxOrder ?? -1) + 1,
            DataJson = JsonSerializer.Serialize(card)
        };
        _context.Questions.Add(question);
        questionSet.QuestionCount += 1;

        await _context.SaveChangesAsync(cancellationToken);

        return new CaptureFlashcardResult
        {
            QuestionId = question.Id,
            QuestionSetId = questionSet.Id,
            QuestionSetName = questionSet.Name,
            Front = front,
            Preview = FlashcardQuizBuilder.AnswerText(card),
            LookupStatus = card.Lookup?.Status ?? "skipped",
            Translated = translated
        };
    }

    private async Task<(QTypeLanguageFlashcard Card, bool Translated)> BuildFromLookup(
        string front, string sourceLang, string targetLang, bool translate, CancellationToken ct)
    {
        var card = new QTypeLanguageFlashcard();

        // Tắt tự động dịch/tra: để trống phần nghĩa (người dùng tự điền sau), không gọi dịch vụ ngoài
        if (!translate)
        {
            card.Lookup = new QFlashcardLookup { Provider = Provider, Status = "skipped" };
            return (card, false);
        }

        var lookup = await _dictionary.LookupAsync(front, sourceLang, ct);
        var entry = lookup.Entry;
        if (entry != null)
        {
            card.Back.Phonetic = entry.Phonetic;
            card.Back.AudioUrl = entry.AudioUrl;
            card.Back.Meanings = entry.Meanings;
        }
        card.Lookup = new QFlashcardLookup { Provider = Provider, Status = lookup.Status };

        var translated = false;
        // Cùng ngôn ngữ nguồn và đích: chỉ cần định nghĩa từ điển, không dịch
        if (!string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
        {
            // Bản dịch của chính từ, đặt vào nghĩa đầu tiên (đáp án trắc nghiệm ưu tiên bản dịch). Từ điển không có thì chỉ có bản dịch.
            var text = await _translator.TranslateAsync(front, sourceLang, targetLang, ct);
            // MyMemory trả lại đúng chuỗi gốc khi không dịch được; từ cũng không có trong từ điển thì coi như không dịch được
            var unchanged = string.Equals(text?.Trim(), front, StringComparison.OrdinalIgnoreCase) && card.Back.Meanings.Count == 0;
            if (!string.IsNullOrWhiteSpace(text) && !unchanged)
            {
                if (card.Back.Meanings.Count == 0) card.Back.Meanings.Add(new QFlashcardMeaning());
                card.Back.Meanings[0].Translation = text;
                translated = true;
            }
        }
        return (card, translated);
    }

    private static QFlashcardSource? BuildSource(CaptureFlashcardCommand request, bool storeContext, DateTimeOffset now)
    {
        // Trang http:// (hoặc URL lạ) không được lưu: dữ liệu lưu chỉ nhận https
        var url = FlashcardNormalizer.IsHttpsUrl(request.SourceUrl) ? request.SourceUrl : null;
        var context = storeContext ? request.Context : null;
        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(request.SourceTitle) && string.IsNullOrWhiteSpace(context))
            return null;

        return new QFlashcardSource
        {
            Url = url,
            Title = Truncate(request.SourceTitle, 500),
            Context = Truncate(context, FlashcardNormalizer.MaxTextLength),
            CapturedAt = now.UtcDateTime
        };
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    private async Task<QuestionSet> ResolveTargetSet(
        Guid userId, ExtensionSetting settings, Guid? overrideSetId, DateTimeOffset now, CancellationToken ct)
    {
        var fixedId = overrideSetId ?? (settings.TargetMode == ExtensionTargetMode.Fixed ? settings.TargetQuestionSetId : null);
        if (fixedId.HasValue)
        {
            var set = await _context.QuestionSets.FirstOrDefaultAsync(q => q.Id == fixedId.Value && !q.IsDeleted, ct)
                      ?? throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND,
                          "Bộ câu hỏi đích trong cài đặt extension đã bị xoá. Hãy chọn bộ khác trong tab Cài đặt.");
            if (!await _questionSetService.CanUserEditQuestionSet(userId, set.Id))
                throw new ErrorCodeException(ErrorCodes.COMMON_FORBIDDEN,
                    "Bạn không còn quyền sửa bộ câu hỏi đích trong cài đặt extension. Hãy chọn bộ khác trong tab Cài đặt.");
            return set;
        }

        if (settings.TargetMode == ExtensionTargetMode.Fixed)
            throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND, "Chưa chọn bộ câu hỏi đích trong cài đặt extension");

        var name = PeriodSetNamer.NameFor(settings.TargetMode, now, settings.TimeZone);
        var existing = await _context.QuestionSetUsers
            .Where(x => x.UserId == userId && x.ShareMode == QuestionSetUserShareMode.Owner
                        && !x.QuestionSet!.IsDeleted && x.QuestionSet.Name == name)
            .Select(x => x.QuestionSet!)
            .FirstOrDefaultAsync(ct);
        if (existing != null) return existing;

        var created = new QuestionSet
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Tự tạo bởi extension trình duyệt",
            VisibilityMode = QuestionSetVisibilityMode.Private,
            QuestionCount = 0
        };
        _context.QuestionSets.Add(created);
        _context.QuestionSetUsers.Add(new QuestionSetUser
        {
            UserId = userId,
            QuestionSetId = created.Id,
            ShareMode = QuestionSetUserShareMode.Owner
        });
        return created;
    }
}

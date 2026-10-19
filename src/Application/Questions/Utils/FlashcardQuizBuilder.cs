using System.Text.Json;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Questions.Utils;

/// <summary>
/// Dựng câu trắc nghiệm từ thẻ LanguageFlashcard: đáp án đúng là nghĩa của thẻ, đáp án nhiễu lấy từ nghĩa của các thẻ khác trong bộ.
/// Không lưu đáp án nhiễu: id của lựa chọn = id của câu hỏi (thẻ) sinh ra nó, và việc chọn nhiễu là tất định theo (thẻ, bộ thẻ)
/// nên cùng một bộ thẻ luôn cho cùng tập đáp án.
/// </summary>
public static class FlashcardQuizBuilder
{
    public const int MaxDistractors = 3;

    /// <summary>Hai nghĩa giống nhau từ ngưỡng này (Jaccard theo từ) coi là gần trùng, không dùng làm đáp án nhiễu.</summary>
    private const double NearDuplicateThreshold = 0.8;

    public static QTypeLanguageFlashcard? Parse(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return null;
        try
        {
            // DataJson không có "v" (dữ liệu cũ) được hiểu là version 0: V mặc định 1 nên chỉ ảnh hưởng khi có migration về sau.
            return JsonSerializer.Deserialize<QTypeLanguageFlashcard>(dataJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Nghĩa dùng làm đáp án: bản dịch nếu có, không thì định nghĩa; lấy nghĩa đầu tiên có nội dung.</summary>
    public static string? AnswerText(QTypeLanguageFlashcard card)
    {
        foreach (var meaning in card.Back.Meanings)
        {
            var text = !string.IsNullOrWhiteSpace(meaning.Translation) ? meaning.Translation : meaning.Definition;
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return null;
    }

    /// <summary>Câu hỏi có thể đưa vào học/luyện tập? Thẻ nháp chưa có nghĩa thì không (không dựng được đáp án).</summary>
    public static bool IsPlayable(Question question)
    {
        if (question.Type != QuestionType.LanguageFlashcard) return true;
        var card = Parse(question.DataJson);
        return card != null && AnswerText(card) != null;
    }

    private static string? PrimaryPos(QTypeLanguageFlashcard card) =>
        card.Back.Meanings.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Pos))?.Pos?.Trim().ToLowerInvariant();

    public static List<MultipleChoiceItemDto> BuildOptions(
        Question question,
        QTypeLanguageFlashcard card,
        IEnumerable<Question>? pool,
        bool shuffle,
        bool isShowAnswer)
    {
        var correctText = AnswerText(card);
        if (correctText == null) return new List<MultipleChoiceItemDto>();

        var options = new List<MultipleChoiceItemDto>
        {
            new() { Id = question.Id, Text = correctText, IsAnswer = isShowAnswer ? true : null }
        };

        foreach (var (id, text) in PickDistractors(question, card, correctText, pool))
        {
            options.Add(new MultipleChoiceItemDto { Id = id, Text = text, IsAnswer = isShowAnswer ? false : null });
        }

        // shuffle=false: thứ tự ổn định (theo hash) để review/hiển thị luôn giống nhau; shuffle=true: ngẫu nhiên mỗi lần học
        return shuffle
            ? options.OrderBy(_ => Random.Shared.Next()).ToList()
            : options.OrderBy(o => StableHash(question.Id, o.Id)).ToList();
    }

    private static List<(Guid Id, string Text)> PickDistractors(
        Question question, QTypeLanguageFlashcard card, string correctText, IEnumerable<Question>? pool)
    {
        if (pool == null) return new();

        var pos = PrimaryPos(card);
        var seenTexts = new List<string> { correctText };
        var candidates = new List<(Guid Id, string Text, bool SamePos)>();

        foreach (var other in pool)
        {
            if (other.Id == question.Id || other.IsDeleted || other.Type != QuestionType.LanguageFlashcard) continue;
            var otherCard = Parse(other.DataJson);
            if (otherCard == null) continue;
            var text = AnswerText(otherCard);
            if (text == null) continue;
            candidates.Add((other.Id, text, pos != null && PrimaryPos(otherCard) == pos));
        }

        var picked = new List<(Guid Id, string Text)>();
        // Cùng từ loại trước (để đáp án nhiễu hợp lý), rồi tới thứ tự tất định theo hash để không phụ thuộc thứ tự truy vấn
        foreach (var c in candidates.OrderByDescending(c => c.SamePos).ThenBy(c => StableHash(question.Id, c.Id)))
        {
            if (picked.Count >= MaxDistractors) break;
            if (seenTexts.Any(t => IsNearDuplicate(t, c.Text))) continue;
            seenTexts.Add(c.Text);
            picked.Add((c.Id, c.Text));
        }
        return picked;
    }

    public static bool IsNearDuplicate(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        var intersection = ta.Intersect(tb).Count();
        var union = ta.Union(tb).Count();
        return (double)intersection / union >= NearDuplicateThreshold;
    }

    private static HashSet<string> Tokens(string s) =>
        s.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '.', ':', '(', ')', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

    /// <summary>Hash ổn định giữa các lần chạy (string.GetHashCode/Guid.GetHashCode của .NET không đảm bảo điều này cho string).</summary>
    private static int StableHash(Guid a, Guid b)
    {
        var x = a.ToByteArray();
        var y = b.ToByteArray();
        unchecked
        {
            var h = 17;
            for (var i = 0; i < 16; i++) h = h * 31 + (x[i] ^ y[(i + 5) % 16]);
            return h;
        }
    }
}

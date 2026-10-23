using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.QuestionSets.QuestionJson;

/// <summary>
/// Định dạng JSON công khai để xuất / nhập câu hỏi. Tách khỏi DataJson nội bộ (không có id, vị trí xáo trộn...)
/// để người dùng tự viết hoặc nhờ AI tạo. Ví dụ:
/// <code>
/// { "format": "aiquizzizz/questions", "version": 1,
///   "questionSet": { "name": "...", "description": "...", "tags": ["..."] },
///   "questions": [
///     { "type": "MultipleChoice", "content": "...", "points": 10, "options": [ { "text": "...", "correct": true } ] },
///     { "type": "Matching", "content": "...", "pairs": [ { "left": "...", "right": "..." } ] },
///     { "type": "Ordering", "content": "...", "items": [ "bước 1", "bước 2" ] },
///     { "type": "ShortText", "content": "...", "answer": "..." } ] }
/// </code>
/// </summary>
public static class QuestionJsonFormat
{
    public const string FormatName = "aiquizzizz/questions";
    public const int Version = 1;
    /// <summary>Số câu tối đa trong một lần nhập (bằng giới hạn câu của một bộ)</summary>
    public const int MaxQuestions = 500;
    /// <summary>Số ký tự tối đa của nội dung JSON gửi lên (~2 MB)</summary>
    public const int MaxJsonLength = 2_000_000;

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // giữ nguyên tiếng Việt / ký tự đặc biệt trong file xuất (không \uXXXX) để dễ đọc và sửa tay
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Câu hỏi (dạng dùng khi lưu) -> mục JSON công khai</summary>
    public static QuestionJsonItem FromDto(CreateUpdateQuestionDto dto)
    {
        var item = new QuestionJsonItem
        {
            Type = dto.Type ?? string.Empty,
            Content = dto.QuestionText ?? string.Empty,
            TextFormat = dto.TextFormat,
            Points = dto.Score,
            Explanation = string.IsNullOrWhiteSpace(dto.ExplainText) ? null : dto.ExplainText
        };
        switch (dto.Type)
        {
            case nameof(QuestionType.MultipleChoice):
                item.Options = dto.MultipleChoices?.Select(o => new QuestionJsonOption { Text = o.Text ?? string.Empty, Correct = o.IsAnswer }).ToList();
                break;
            case nameof(QuestionType.Matching):
                item.Pairs = dto.MatchingPairs?.Select(p => new QuestionJsonPair { Left = p.LeftItem ?? string.Empty, Right = p.RightItem ?? string.Empty }).ToList();
                break;
            case nameof(QuestionType.Ordering):
                // mảng items luôn theo THỨ TỰ ĐÚNG
                item.Items = dto.OrderingItems?.OrderBy(o => o.CorrectOrder).Select(o => o.Text ?? string.Empty).ToList();
                break;
            case nameof(QuestionType.ShortText):
                item.Answer = dto.ShortAnswer;
                break;
            case nameof(QuestionType.LanguageFlashcard):
                item.Flashcard = dto.Flashcard;
                break;
        }
        return item;
    }
}

public class QuestionJsonFile
{
    public string Format { get; set; } = QuestionJsonFormat.FormatName;
    public int Version { get; set; } = QuestionJsonFormat.Version;
    public DateTimeOffset? ExportedAt { get; set; }
    public QuestionSetJsonInfo? QuestionSet { get; set; }
    public List<QuestionJsonItem> Questions { get; set; } = new();
}

public class QuestionSetJsonInfo
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
}

public class QuestionJsonItem
{
    /// <summary>MultipleChoice | Matching | Ordering | ShortText | LanguageFlashcard</summary>
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    /// <summary>PlainText | Html | Markdown</summary>
    public string? TextFormat { get; set; }
    public float? Points { get; set; }
    public string? Explanation { get; set; }
    public List<QuestionJsonOption>? Options { get; set; }
    public List<QuestionJsonPair>? Pairs { get; set; }
    /// <summary>Ordering: các mục theo thứ tự đúng</summary>
    public List<string>? Items { get; set; }
    /// <summary>ShortText: đáp án</summary>
    public string? Answer { get; set; }
    public QTypeLanguageFlashcard? Flashcard { get; set; }
}

public class QuestionJsonOption
{
    public string Text { get; set; } = string.Empty;
    public bool Correct { get; set; }
}

public class QuestionJsonPair
{
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;
}

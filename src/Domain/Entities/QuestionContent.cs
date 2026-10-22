using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CleanArchitectureBase.Domain.Entities;

/// <summary>
/// Dấu vân tay nội dung câu hỏi, dùng để biết câu hỏi đã đổi nội dung chưa (tăng Version) và
/// bản sao trong bài test có còn giống câu gốc không. Không gồm Score (điểm trong bài test nằm ở TestQuestion).
/// DataJson được chuẩn hoá trước khi băm: mỗi lần lưu, id đáp án và ShuffleOrder được sinh lại nên không đưa vào hash;
/// chỉ giữ phần có nghĩa (chữ, đáp án đúng, cặp ghép, thứ tự đúng, vị trí tác giả sắp xếp).
/// </summary>
public static class QuestionContent
{
    private const char Separator = '\u001f';

    public static string Hash(Question question) =>
        Hash(question.Type, question.QuestionText, question.TextFormat, question.ExplainText, question.DataJson, question.MediaId);

    public static string Hash(QuestionType type, string? questionText, TextFormat textFormat, string? explainText, string? dataJson, Guid? mediaId)
    {
        var canonical = string.Join(Separator,
            type.ToString(),
            questionText ?? string.Empty,
            textFormat.ToString(),
            explainText ?? string.Empty,
            CanonicalData(type, dataJson),
            mediaId?.ToString() ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>Phần có nghĩa của DataJson theo loại câu hỏi; không đọc được thì dùng nguyên chuỗi</summary>
    public static string CanonicalData(QuestionType type, string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return string.Empty;
        try
        {
            return type switch
            {
                QuestionType.MultipleChoice => JsonSerializer.Serialize(
                    ByPosition(JsonSerializer.Deserialize<List<QTypeMultipleChoice>>(dataJson)!, x => x.Position)
                        .Select(x => new object[] { x.Text, x.IsAnswer })),

                QuestionType.Matching => CanonicalMatching(JsonSerializer.Deserialize<List<QTypeMatching>>(dataJson)!),

                QuestionType.Ordering => JsonSerializer.Serialize(
                    ByPosition(JsonSerializer.Deserialize<List<QTypeOrderingItem>>(dataJson)!, x => x.Position)
                        .Select(x => new object[] { x.Text, x.CorrectOrder })),

                QuestionType.ShortText => JsonSerializer.Deserialize<QTypeShortAnswer>(dataJson)!.Answer ?? string.Empty,

                // Thẻ ghi nhớ không có id sinh ngẫu nhiên: giữ nguyên
                _ => dataJson
            };
        }
        catch (JsonException)
        {
            return dataJson;
        }
    }

    // Dữ liệu cũ có Position = 0 hết: sắp xếp ổn định nên giữ nguyên thứ tự mảng
    private static IEnumerable<T> ByPosition<T>(IEnumerable<T> items, Func<T, int> position) =>
        items.Select((item, index) => (item, index)).OrderBy(x => position(x.item)).ThenBy(x => x.index).Select(x => x.item);

    private static string CanonicalMatching(List<QTypeMatching> items)
    {
        var textById = items.GroupBy(x => x.Id.ToString()).ToDictionary(g => g.Key, g => g.First().Text);
        var pairs = ByPosition(items.Where(x => !string.IsNullOrEmpty(x.AnswerId)), x => x.Position)
            .Select(left => new[] { left.Text, textById.GetValueOrDefault(left.AnswerId!) ?? string.Empty });
        return JsonSerializer.Serialize(pairs);
    }
}

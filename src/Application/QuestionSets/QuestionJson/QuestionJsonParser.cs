using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.QuestionSets.QuestionJson;

/// <summary>Một câu đọc được từ JSON: dữ liệu để lưu + lỗi (chặn) + cảnh báo (không chặn)</summary>
public class ImportedJsonQuestionDto : CreateUpdateQuestionDto
{
    /// <summary>Vị trí câu trong mảng questions (bắt đầu từ 1)</summary>
    public int Index { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ParsedQuestionJson
{
    public QuestionSetJsonInfo? QuestionSet { get; set; }
    public List<ImportedJsonQuestionDto> Questions { get; set; } = new();
}

/// <summary>
/// Đọc JSON câu hỏi theo hướng dễ dãi vì JSON thường do AI (ChatGPT, Gemini...) sinh ra:
/// bỏ khung ```json, chữ dẫn trước / sau; nhận mảng câu hỏi trần hoặc object có "questions";
/// nhận tên trường đồng nghĩa (question / content, score / points...), tên loại viết kiểu khác (mcq, short_answer...),
/// đáp án đúng ghi bằng dấu *, chữ cái (B), số thứ tự hoặc nội dung; cặp ghép dạng "trái = phải".
/// Không đoán được thì để validator báo lỗi theo từng câu.
/// </summary>
public static partial class QuestionJsonParser
{
    private static readonly JsonDocumentOptions DocOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static ParsedQuestionJson Parse(string? raw)
    {
        var text = ExtractJson(raw);
        if (text.Length == 0)
            throw new ErrorCodeException(ErrorCodes.QUESTION_JSON_EMPTY, "Nội dung JSON trống");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: DocOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber.HasValue ? $" (dòng {ex.LineNumber + 1}, cột {ex.BytePositionInLine + 1})" : string.Empty;
            throw new ErrorCodeException(ErrorCodes.QUESTION_JSON_INVALID, $"JSON không hợp lệ{where}");
        }

        JsonArray? questions = null;
        QuestionSetJsonInfo? info = null;
        if (root is JsonArray arr) questions = arr;
        else if (root is JsonObject obj)
        {
            questions = Get(obj, "questions", "items", "quiz", "data") as JsonArray;
            var setNode = Get(obj, "questionSet", "quizSet", "set", "quiz") as JsonObject ?? obj;
            info = ReadSetInfo(setNode);
        }

        if (questions == null || questions.Count == 0)
            throw new ErrorCodeException(ErrorCodes.QUESTION_JSON_EMPTY, "Không tìm thấy mảng \"questions\" hoặc mảng rỗng");
        if (questions.Count > QuestionJsonFormat.MaxQuestions)
            throw new ErrorCodeException(ErrorCodes.QUESTION_JSON_TOO_LARGE, $"Tối đa {QuestionJsonFormat.MaxQuestions} câu mỗi lần nhập (file có {questions.Count} câu)");

        var validator = new CreateUpdateQuestionDto.QuestionCreateDtoValidator();
        var result = new ParsedQuestionJson { QuestionSet = info };
        for (var i = 0; i < questions.Count; i++)
        {
            var q = ReadQuestion(questions[i], i + 1);
            if (q.Errors.Count == 0)
            {
                var validation = validator.Validate(q);
                q.Errors.AddRange(validation.Errors.Select(e => e.ErrorMessage.Trim()).Distinct());
            }
            result.Questions.Add(q);
        }
        return result;
    }

    /// <summary>Bỏ BOM, khung ```json ... ```, chữ dẫn trước / sau khối JSON</summary>
    public static string ExtractJson(string? raw)
    {
        var s = (raw ?? string.Empty).Trim().TrimStart('﻿');
        var fence = FencePattern().Match(s);
        if (fence.Success) s = fence.Groups[1].Value.Trim();
        if (s.Length > 0 && s[0] != '{' && s[0] != '[')
        {
            var start = s.IndexOfAny(['{', '[']);
            var end = Math.Max(s.LastIndexOf('}'), s.LastIndexOf(']'));
            if (start >= 0 && end > start) s = s[start..(end + 1)];
        }
        return s;
    }

    [GeneratedRegex(@"```(?:json|JSON)?\s*\n?([\s\S]*?)```")]
    private static partial Regex FencePattern();

    private static QuestionSetJsonInfo? ReadSetInfo(JsonObject o)
    {
        var name = Str(Get(o, "name", "title"));
        var description = Str(Get(o, "description", "desc"));
        var tags = Get(o, "tags") is JsonArray t
            ? t.Select(x => Str(x)).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct().ToList()
            : null;
        return name == null && description == null && (tags == null || tags.Count == 0)
            ? null
            : new QuestionSetJsonInfo { Name = name?.Trim(), Description = description?.Trim(), Tags = tags };
    }

    private static ImportedJsonQuestionDto ReadQuestion(JsonNode? node, int index)
    {
        var q = new ImportedJsonQuestionDto
        {
            Index = index,
            MultipleChoices = new(), MatchingPairs = new(), OrderingItems = new()
        };
        if (node is not JsonObject o)
        {
            q.Errors.Add("Mỗi câu hỏi phải là một object { ... }");
            return q;
        }

        q.QuestionText = Str(Get(o, "content", "question", "questionText", "text", "prompt", "title"))?.Trim();
        q.ExplainText = Str(Get(o, "explanation", "explain", "explainText", "rationale", "reason"))?.Trim();
        q.TextFormat = TextFormatHelper.Normalize(Str(Get(o, "textFormat", "format")));
        q.Score = ReadPoints(Get(o, "points", "score", "point"), q);

        var options = Get(o, "options", "choices", "answers") as JsonArray;
        var pairs = Get(o, "pairs", "matches", "matching") as JsonArray;
        var items = Get(o, "items", "order", "steps", "sequence", "ordering") as JsonArray;
        var answerNode = Get(o, "answer", "correctAnswer", "correct", "shortAnswer", "correctAnswers");
        var flashcard = Get(o, "flashcard");

        q.Type = NormalizeType(Str(Get(o, "type", "questionType", "kind")))
                 ?? (options != null ? nameof(QuestionType.MultipleChoice)
                     : pairs != null ? nameof(QuestionType.Matching)
                     : items != null ? nameof(QuestionType.Ordering)
                     : flashcard != null ? nameof(QuestionType.LanguageFlashcard)
                     : answerNode != null ? nameof(QuestionType.ShortText) : null);

        switch (q.Type)
        {
            case nameof(QuestionType.MultipleChoice): ReadOptions(options, answerNode, q); break;
            case nameof(QuestionType.Matching): ReadPairs(pairs, q); break;
            case nameof(QuestionType.Ordering): ReadItems(items, q); break;
            case nameof(QuestionType.ShortText): ReadShortAnswer(answerNode, q); break;
            case nameof(QuestionType.LanguageFlashcard):
                try { q.Flashcard = flashcard?.Deserialize<QTypeLanguageFlashcard>(); }
                catch (JsonException) { q.Errors.Add("\"flashcard\" không đúng cấu trúc thẻ ghi nhớ"); }
                break;
        }
        return q;
    }

    private static float ReadPoints(JsonNode? node, ImportedJsonQuestionDto q)
    {
        if (node == null) return 10;
        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return (float)d;
            if (v.TryGetValue<string>(out var s) && float.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
        }
        q.Errors.Add("\"points\" phải là một số");
        return 10;
    }

    /// <summary>Đưa tên loại câu về dạng chuẩn; null nếu không có, giữ nguyên nếu không nhận ra (để validator báo lỗi)</summary>
    public static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        var key = new string(type.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return key switch
        {
            "multiplechoice" or "mcq" or "choice" or "singlechoice" or "multiplechoices" or "multipleanswer" or "tracnghiem" => nameof(QuestionType.MultipleChoice),
            "matching" or "match" or "pairs" or "pair" or "noi" => nameof(QuestionType.Matching),
            "ordering" or "order" or "sequence" or "sort" or "sorting" or "sapxep" => nameof(QuestionType.Ordering),
            "shorttext" or "shortanswer" or "short" or "fillintheblank" or "fillblank" or "text" or "traloingan" => nameof(QuestionType.ShortText),
            "languageflashcard" or "flashcard" => nameof(QuestionType.LanguageFlashcard),
            _ => type.Trim()
        };
    }

    private static void ReadOptions(JsonArray? options, JsonNode? answerNode, ImportedJsonQuestionDto q)
    {
        if (options == null) { q.Errors.Add("Câu trắc nghiệm cần mảng \"options\""); return; }
        foreach (var opt in options)
        {
            if (opt is JsonObject oo)
            {
                q.MultipleChoices!.Add(new CreateMultipleChoiceDto
                {
                    Text = Str(Get(oo, "text", "content", "label", "value", "option"))?.Trim(),
                    IsAnswer = Bool(Get(oo, "correct", "isCorrect", "isAnswer", "answer"))
                });
            }
            else
            {
                var s = Str(opt)?.Trim() ?? string.Empty;
                var star = s.StartsWith('*');
                q.MultipleChoices!.Add(new CreateMultipleChoiceDto { Text = star ? s[1..].Trim() : s, IsAnswer = star });
            }
        }
        // đáp án đúng ghi riêng: "B", 2, "nội dung", ["A","C"]
        if (!q.MultipleChoices!.Any(o => o.IsAnswer) && answerNode != null)
        {
            var keys = answerNode is JsonArray a ? a.ToList() : new List<JsonNode?> { answerNode };
            foreach (var k in keys) MarkCorrect(k, q.MultipleChoices!);
        }
    }

    private static void MarkCorrect(JsonNode? key, List<CreateMultipleChoiceDto> opts)
    {
        if (key is JsonValue v && v.TryGetValue<int>(out var n))
        {
            // chỉ số: chấp nhận cả đếm từ 0 lẫn từ 1 (AI hay dùng 1)
            var idx = n >= 1 && n <= opts.Count ? n - 1 : n;
            if (idx >= 0 && idx < opts.Count) opts[idx].IsAnswer = true;
            return;
        }
        var s = Str(key)?.Trim();
        if (string.IsNullOrEmpty(s)) return;
        var byText = opts.FirstOrDefault(o => string.Equals(o.Text, s, StringComparison.OrdinalIgnoreCase));
        if (byText != null) { byText.IsAnswer = true; return; }
        if (s.Length == 1 && char.IsLetter(s[0]))
        {
            var idx = char.ToUpperInvariant(s[0]) - 'A';
            if (idx >= 0 && idx < opts.Count) opts[idx].IsAnswer = true;
        }
    }

    private static void ReadPairs(JsonArray? pairs, ImportedJsonQuestionDto q)
    {
        if (pairs == null) { q.Errors.Add("Câu nối cần mảng \"pairs\""); return; }
        foreach (var p in pairs)
        {
            string? left = null, right = null;
            if (p is JsonObject po)
            {
                left = Str(Get(po, "left", "term", "key", "prompt", "question", "a"));
                right = Str(Get(po, "right", "definition", "value", "match", "answer", "b"));
            }
            else if (p is JsonArray pa && pa.Count >= 2) { left = Str(pa[0]); right = Str(pa[1]); }
            else if (Str(p) is { } s)
            {
                var m = PairPattern().Match(s);
                if (m.Success) { left = m.Groups[1].Value; right = m.Groups[2].Value; }
                else left = s;
            }
            q.MatchingPairs!.Add(new CreateMatchingPairDto { LeftItem = left?.Trim(), RightItem = right?.Trim() });
        }
    }

    [GeneratedRegex(@"^(.*?)\s*(?:=>|->|=|⟷|↔)\s*(.*)$")]
    private static partial Regex PairPattern();

    private static void ReadItems(JsonArray? items, ImportedJsonQuestionDto q)
    {
        if (items == null) { q.Errors.Add("Câu sắp xếp cần mảng \"items\" theo đúng thứ tự"); return; }
        // phần tử dạng { text, order } thì sắp theo order; dạng chuỗi thì giữ thứ tự mảng
        var parsed = items.Select((it, i) =>
        {
            if (it is JsonObject io)
            {
                var orderNode = Get(io, "order", "position", "correctOrder", "index");
                var order = orderNode is JsonValue ov && ov.TryGetValue<double>(out var d) ? d : i;
                return (Text: Str(Get(io, "text", "content", "value", "label")), Order: order, Pos: i);
            }
            return (Text: Str(it), Order: (double)i, Pos: i);
        }).OrderBy(x => x.Order).ThenBy(x => x.Pos).ToList();
        for (var i = 0; i < parsed.Count; i++)
            q.OrderingItems!.Add(new CreateOrderingItemDto { Text = parsed[i].Text?.Trim(), CorrectOrder = i });
    }

    private static void ReadShortAnswer(JsonNode? answer, ImportedJsonQuestionDto q)
    {
        if (answer is JsonArray a)
        {
            var values = a.Select(x => Str(x)?.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            q.ShortAnswer = values.FirstOrDefault();
            if (values.Count > 1)
                q.Warnings.Add($"Câu trả lời ngắn chỉ lưu một đáp án: dùng \"{values[0]}\", bỏ {values.Count - 1} đáp án còn lại");
            return;
        }
        q.ShortAnswer = Str(answer)?.Trim();
    }

    // ---- tiện ích đọc JsonNode ----

    /// <summary>Lấy giá trị theo tên trường (không phân biệt hoa / thường, bỏ qua _ và -), thử lần lượt các tên đồng nghĩa</summary>
    private static JsonNode? Get(JsonObject o, params string[] names)
    {
        foreach (var name in names)
        {
            var want = Simplify(name);
            foreach (var kv in o)
                if (Simplify(kv.Key) == want) return kv.Value;
        }
        return null;
    }

    private static string Simplify(string key) => new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string? Str(JsonNode? n) => n switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToJsonString(),
        _ => null
    };

    private static bool Bool(JsonNode? n) => n switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Trim().ToLowerInvariant() is "true" or "yes" or "1" or "đúng" or "x",
        JsonValue v when v.TryGetValue<int>(out var i) => i != 0,
        _ => false
    };
}

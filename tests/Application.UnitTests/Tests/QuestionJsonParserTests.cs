using System.Text.Json;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.QuestionSets.QuestionJson;
using CleanArchitectureBase.Domain.Constants;

namespace CleanArchitectureBase.Application.UnitTests.Tests;

public class QuestionJsonParserTests
{
    private const string Standard = """
        {
          "format": "aiquizzizz/questions", "version": 1,
          "questionSet": { "name": "Ôn tập MongoDB", "description": "Cơ bản", "tags": ["mongodb", "database", "mongodb"] },
          "questions": [
            { "type": "MultipleChoice", "content": "MongoDB thuộc nhóm nào?", "points": 10, "explanation": "Lưu document BSON",
              "options": [ { "text": "Key-value", "correct": false }, { "text": "Document NoSQL", "correct": true } ] },
            { "type": "Matching", "content": "Nối toán tử", "pairs": [ { "left": "$gt", "right": "Lớn hơn" }, { "left": "$in", "right": "Trong danh sách" } ] },
            { "type": "Ordering", "content": "Thứ tự pipeline", "items": ["$match", "$group", "$sort"] },
            { "type": "ShortText", "content": "Lệnh tạo index?", "answer": "createIndex", "textFormat": "markdown" }
          ]
        }
        """;

    [Test]
    public void ReadsStandardFormat_AllTypesValid()
    {
        var r = QuestionJsonParser.Parse(Standard);

        r.QuestionSet!.Name.Should().Be("Ôn tập MongoDB");
        r.QuestionSet.Tags.Should().Equal("mongodb", "database");
        r.Questions.Should().HaveCount(4);
        r.Questions.Should().OnlyContain(q => q.Errors.Count == 0);

        var mc = r.Questions[0];
        mc.Type.Should().Be("MultipleChoice"); mc.Score.Should().Be(10); mc.ExplainText.Should().Be("Lưu document BSON");
        mc.MultipleChoices!.Select(o => (o.Text, o.IsAnswer)).Should().Equal(("Key-value", false), ("Document NoSQL", true));
        r.Questions[1].MatchingPairs!.Select(p => (p.LeftItem, p.RightItem)).Should().Equal(("$gt", "Lớn hơn"), ("$in", "Trong danh sách"));
        r.Questions[2].OrderingItems!.Select(o => (o.Text, o.CorrectOrder)).Should().Equal(("$match", 0), ("$group", 1), ("$sort", 2));
        r.Questions[3].ShortAnswer.Should().Be("createIndex");
        r.Questions[3].TextFormat.Should().Be("Markdown");
        r.Questions.Select(q => q.Index).Should().Equal(1, 2, 3, 4);
    }

    [Test]
    public void StripsCodeFenceAndChatter_AcceptsBareArray()
    {
        var raw = "Đây là bộ câu hỏi của bạn:\n```json\n[ { \"type\": \"ShortText\", \"question\": \"2 + 2 = ?\", \"answer\": \"4\" } ]\n```\nChúc bạn học tốt!";
        var r = QuestionJsonParser.Parse(raw);
        r.Questions.Should().ContainSingle();
        r.Questions[0].QuestionText.Should().Be("2 + 2 = ?");
        r.Questions[0].Errors.Should().BeEmpty();
    }

    [Test]
    public void AcceptsAliases_TypeNames_StarMarker_StringScore()
    {
        var r = QuestionJsonParser.Parse("""
            { "questions": [ { "question_type": "mcq", "question": "Màu của trời?", "score": "5", "choices": ["Đỏ", "*Xanh", "Vàng"] } ] }
            """);
        var q = r.Questions.Single();
        q.Type.Should().Be("MultipleChoice"); q.Score.Should().Be(5);
        q.MultipleChoices!.Single(o => o.IsAnswer).Text.Should().Be("Xanh");
        q.Errors.Should().BeEmpty();
    }

    [TestCase("\"C\"", "Ba")]
    [TestCase("2", "Hai")]
    [TestCase("\"Bốn\"", "Bốn")]
    [TestCase("[\"A\", \"D\"]", "Một,Bốn")]
    public void MarksCorrectOptionFromSeparateAnswerField(string answer, string expected)
    {
        var r = QuestionJsonParser.Parse($$"""[ { "type": "MultipleChoice", "content": "Chọn", "options": ["Một", "Hai", "Ba", "Bốn"], "answer": {{answer}} } ]""");
        string.Join(",", r.Questions[0].MultipleChoices!.Where(o => o.IsAnswer).Select(o => o.Text)).Should().Be(expected);
    }

    [Test]
    public void InfersTypeWhenMissing_AndReadsPairStringsAndOrderedObjects()
    {
        var r = QuestionJsonParser.Parse("""
            [ { "content": "Nối", "pairs": ["Việt Nam = Hà Nội", ["Pháp", "Paris"], "Nhật -> Tokyo"] },
              { "content": "Sắp xếp", "items": [ { "text": "Ba", "order": 3 }, { "text": "Một", "order": 1 }, { "text": "Hai", "order": 2 } ] } ]
            """);
        r.Questions[0].Type.Should().Be("Matching");
        r.Questions[0].MatchingPairs!.Select(p => p.RightItem).Should().Equal("Hà Nội", "Paris", "Tokyo");
        r.Questions[1].Type.Should().Be("Ordering");
        r.Questions[1].OrderingItems!.OrderBy(o => o.CorrectOrder).Select(o => o.Text).Should().Equal("Một", "Hai", "Ba");
        r.Questions.Should().OnlyContain(q => q.Errors.Count == 0);
    }

    [Test]
    public void ShortTextWithSeveralAnswers_KeepsFirstWithWarning()
    {
        var q = QuestionJsonParser.Parse("""[ { "type": "short_answer", "content": "Thủ đô?", "answer": ["Hà Nội", "Ha Noi"] } ]""").Questions[0];
        q.ShortAnswer.Should().Be("Hà Nội");
        q.Warnings.Should().ContainSingle();
        q.Errors.Should().BeEmpty();
    }

    [Test]
    public void ReportsPerQuestionErrors_WithoutFailingWholeFile()
    {
        var r = QuestionJsonParser.Parse("""
            [ { "type": "MultipleChoice", "content": "Không có đáp án đúng", "options": ["A", "B"] },
              { "type": "Essay", "content": "Loại lạ" },
              { "type": "Ordering", "content": "Một mục", "items": ["Chỉ một"] },
              "không phải object",
              { "type": "ShortText", "content": "OK", "answer": "đúng" } ]
            """);
        r.Questions[0].Errors.Should().Contain(e => e.Contains("ít nhất một lựa chọn đúng"));
        r.Questions[1].Errors.Should().NotBeEmpty();
        r.Questions[2].Errors.Should().Contain(e => e.Contains("ít nhất 2 mục"));
        r.Questions[3].Errors.Should().ContainSingle();
        r.Questions[4].Errors.Should().BeEmpty();
    }

    [Test]
    public void InvalidJson_ThrowsWithPosition()
    {
        var ex = FluentActions.Invoking(() => QuestionJsonParser.Parse("{ \"questions\": [ { \"content\": \"a\" ")).Should().Throw<ErrorCodeException>().Which;
        ex.Errors.Should().ContainKey(ErrorCodes.QUESTION_JSON_INVALID);
        ex.Errors[ErrorCodes.QUESTION_JSON_INVALID][0].Should().Contain("dòng");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("{ \"questions\": [] }")]
    [TestCase("{ \"name\": \"không có câu hỏi\" }")]
    public void EmptyInput_Throws(string raw)
    {
        FluentActions.Invoking(() => QuestionJsonParser.Parse(raw)).Should().Throw<ErrorCodeException>()
            .Which.Errors.Should().ContainKey(ErrorCodes.QUESTION_JSON_EMPTY);
    }

    [Test]
    public void TooManyQuestions_Throws()
    {
        var items = string.Join(",", Enumerable.Range(0, QuestionJsonFormat.MaxQuestions + 1).Select(i => $"{{\"content\":\"c{i}\",\"answer\":\"a\"}}"));
        FluentActions.Invoking(() => QuestionJsonParser.Parse($"[{items}]")).Should().Throw<ErrorCodeException>()
            .Which.Errors.Should().ContainKey(ErrorCodes.QUESTION_JSON_TOO_LARGE);
    }

    [Test]
    public void ExportThenImport_RoundTripsAllTypes()
    {
        var dtos = new List<CreateUpdateQuestionDto>
        {
            new() { Type = "MultipleChoice", QuestionText = "Q1", Score = 2, TextFormat = "PlainText", ExplainText = "vì",
                MultipleChoices = [new() { Text = "A", IsAnswer = false }, new() { Text = "B", IsAnswer = true }] },
            new() { Type = "Matching", QuestionText = "Q2", Score = 3, MatchingPairs = [new() { LeftItem = "x", RightItem = "1" }, new() { LeftItem = "y", RightItem = "2" }] },
            new() { Type = "Ordering", QuestionText = "Q3", Score = 4, OrderingItems = [new() { Text = "hai", CorrectOrder = 1 }, new() { Text = "một", CorrectOrder = 0 }] },
            new() { Type = "ShortText", QuestionText = "Q4", Score = 5, ShortAnswer = "đáp án", TextFormat = "Markdown" }
        };
        var file = new QuestionJsonFile { QuestionSet = new() { Name = "Bộ" }, Questions = dtos.Select(QuestionJsonFormat.FromDto).ToList() };
        var json = JsonSerializer.Serialize(file, QuestionJsonFormat.SerializerOptions);

        json.Should().Contain("\"format\": \"aiquizzizz/questions\"").And.Contain("đáp án"); // không bị \uXXXX
        var r = QuestionJsonParser.Parse(json);

        r.Questions.Should().OnlyContain(q => q.Errors.Count == 0);
        r.Questions[0].MultipleChoices!.Select(o => (o.Text, o.IsAnswer)).Should().Equal(("A", false), ("B", true));
        r.Questions[0].ExplainText.Should().Be("vì");
        r.Questions[1].MatchingPairs!.Select(p => p.LeftItem + p.RightItem).Should().Equal("x1", "y2");
        r.Questions[2].OrderingItems!.OrderBy(o => o.CorrectOrder).Select(o => o.Text).Should().Equal("một", "hai");
        r.Questions[3].ShortAnswer.Should().Be("đáp án");
        r.Questions.Select(q => q.Score).Should().Equal(2, 3, 4, 5);
        r.Questions[3].TextFormat.Should().Be("Markdown");
    }
}

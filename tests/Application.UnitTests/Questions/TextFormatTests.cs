using CleanArchitectureBase.Application.Questions.Utils;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.UnitTests.Questions;

public class TextFormatTests
{
    private static CreateUpdateQuestionDto Dto(string? textFormat) => new()
    {
        Type = "MultipleChoice",
        QuestionText = "Đoạn code sau in ra gì?\n```csharp\nint a = 5;\n```",
        TextFormat = textFormat,
        Score = 1,
        MultipleChoices = new List<CreateMultipleChoiceDto>
        {
            new() { Text = "`a`", IsAnswer = true },
            new() { Text = "`b`", IsAnswer = false }
        }
    };

    private static bool IsValid(CreateUpdateQuestionDto q) =>
        new CreateUpdateQuestionDto.QuestionCreateDtoValidator().Validate(q).IsValid;

    [TestCase(null)]
    [TestCase("")]
    [TestCase("PlainText")]
    [TestCase("Html")]
    [TestCase("Markdown")]
    [TestCase("markdown")]
    public void Validator_AcceptsMissingOrKnownTextFormat(string? format) => IsValid(Dto(format)).Should().BeTrue();

    [TestCase("Rtf")]
    [TestCase("1")]
    [TestCase("MarkDown2")]
    public void Validator_RejectsUnknownTextFormat(string format) => IsValid(Dto(format)).Should().BeFalse();

    [TestCase(null, TextFormat.Html, TextFormat.Html)]
    [TestCase(null, TextFormat.PlainText, TextFormat.PlainText)]
    [TestCase("", TextFormat.Html, TextFormat.Html)]
    [TestCase("Markdown", TextFormat.Html, TextFormat.Markdown)]
    [TestCase("markdown", TextFormat.PlainText, TextFormat.Markdown)]
    [TestCase("Html", TextFormat.PlainText, TextFormat.Html)]
    [TestCase("PlainText", TextFormat.Html, TextFormat.PlainText)]
    [TestCase("garbage", TextFormat.Html, TextFormat.Html)]
    [TestCase("1", TextFormat.Html, TextFormat.Html)]
    public void Resolve_UsesProvidedFormat_OtherwiseLegacyDefault(string? value, TextFormat fallback, TextFormat expected) =>
        TextFormatHelper.Resolve(value, fallback).Should().Be(expected);

    [Test]
    public void Normalize_CanonicalisesCase_AndKeepsUnknownForValidator()
    {
        TextFormatHelper.Normalize("markdown").Should().Be("Markdown");
        TextFormatHelper.Normalize("  HTML ").Should().Be("Html");
        TextFormatHelper.Normalize(" ").Should().BeNull();
        TextFormatHelper.Normalize(null).Should().BeNull();
        TextFormatHelper.Normalize("rtf").Should().Be("rtf");
    }

    [Test]
    public void EnumKeepsNumericMeaning_AndParsesStoredStrings()
    {
        ((int)TextFormat.Markdown).Should().Be(0);
        ((int)TextFormat.PlainText).Should().Be(1);
        ((int)TextFormat.Html).Should().Be(2);

        // Cùng biểu thức với EF value converter: dòng cũ "MarkDown" và dòng mới "Markdown" đều đọc được
        Enum.Parse(typeof(TextFormat), "MarkDown", true).Should().Be(TextFormat.Markdown);
        Enum.Parse(typeof(TextFormat), "Markdown", true).Should().Be(TextFormat.Markdown);
        Enum.Parse(typeof(TextFormat), "Html", true).Should().Be(TextFormat.Html);
        Enum.Parse(typeof(TextFormat), "PlainText", true).Should().Be(TextFormat.PlainText);
        TextFormat.Markdown.ToString().Should().Be("Markdown");
    }

    [Test]
    public void Deserializer_ReturnsTextFormatOfEntity()
    {
        var entity = new Question
        {
            Type = QuestionType.ShortText,
            QuestionText = "q",
            TextFormat = TextFormat.Markdown,
            DataJson = "{\"Answer\":\"x\"}"
        };

        var dto = CreateUpdateQuestionDto.Deserializer.Deserialize(entity);

        dto.TextFormat.Should().Be("Markdown");
        dto.ShortAnswer.Should().Be("x");
    }

    [Test]
    public void Compare_TreatsDifferentTextFormatAsDifferentQuestion()
    {
        var entity = new Question
        {
            Type = QuestionType.MultipleChoice,
            QuestionText = "Q",
            TextFormat = TextFormat.Html,
            DataJson = CreateUpdateQuestionDto.Serializer.Serialize(Dto(null))
        };
        var same = Dto("Html");
        same.QuestionText = "Q";
        var other = Dto("Markdown");
        other.QuestionText = "Q";
        var legacy = Dto(null);
        legacy.QuestionText = "Q";

        CreateUpdateQuestionDto.Compare.CompareQuestion(entity, same).Should().BeTrue();
        CreateUpdateQuestionDto.Compare.CompareQuestion(entity, other).Should().BeFalse();
        // Client cũ không gửi TextFormat: giữ hành vi so sánh cũ
        CreateUpdateQuestionDto.Compare.CompareQuestion(entity, legacy).Should().BeTrue();
    }
}

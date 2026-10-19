using CleanArchitectureBase.Application.QuestionSets.Dtos;

namespace CleanArchitectureBase.Application.UnitTests.Questions;

public class DuplicateOptionsValidationTests
{
    private static CreateUpdateQuestionDto Question(params string[] options) => new()
    {
        Type = "MultipleChoice",
        QuestionText = "q",
        Score = 1,
        MultipleChoices = options.Select((t, i) => new CreateMultipleChoiceDto { Text = t, IsAnswer = i == 0 }).ToList()
    };

    private static bool IsValid(CreateUpdateQuestionDto q) =>
        new CreateUpdateQuestionDto.QuestionCreateDtoValidator().Validate(q).IsValid;

    [Test]
    public void IdenticalOptions_AreRejected() => IsValid(Question("a", "b", "a")).Should().BeFalse();

    [Test]
    public void IdenticalOptions_IgnoringSurroundingWhitespace_AreRejected() => IsValid(Question("a", " a ")).Should().BeFalse();

    [Test]
    public void OptionsDifferingOnlyByCase_AreAccepted_BecauseCodeAnswersAreCaseSensitive()
    {
        IsValid(Question("a", "A", "Lỗi biên dịch")).Should().BeTrue();
        IsValid(Question("firstName", "FirstName", "FIRSTNAME", "first_name")).Should().BeTrue();
    }
}

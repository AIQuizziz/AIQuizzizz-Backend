using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Entities;
using FluentAssertions;
using NUnit.Framework;

namespace CleanArchitectureBase.Application.UnitTests.Tests;

/// <summary>Hash nội dung câu hỏi: ổn định khi lưu lại (id đáp án / thứ tự trộn sinh mới), đổi khi nội dung có nghĩa đổi</summary>
public class QuestionContentTests
{
    private static Question Build(CreateUpdateQuestionDto dto) => new()
    {
        Type = Enum.Parse<QuestionType>(dto.Type!),
        QuestionText = dto.QuestionText,
        ExplainText = dto.ExplainText,
        TextFormat = TextFormat.Markdown,
        Score = dto.Score,
        // mỗi lần Serialize sinh id + ShuffleOrder mới
        DataJson = CreateUpdateQuestionDto.Serializer.Serialize(dto)
    };

    private static CreateUpdateQuestionDto MultipleChoice(string correct = "A") => new()
    {
        Type = "MultipleChoice", QuestionText = "Câu?", ExplainText = "Vì", Score = 1,
        MultipleChoices = new() { new() { Text = "A", IsAnswer = correct == "A" }, new() { Text = "B", IsAnswer = correct == "B" }, new() { Text = "C" } }
    };

    private static CreateUpdateQuestionDto Matching(string right1 = "1") => new()
    {
        Type = "Matching", QuestionText = "Ghép", Score = 1,
        MatchingPairs = new() { new() { LeftItem = "a", RightItem = right1 }, new() { LeftItem = "b", RightItem = "2" } }
    };

    private static CreateUpdateQuestionDto Ordering(int firstOrder = 1) => new()
    {
        Type = "Ordering", QuestionText = "Xếp", Score = 1,
        OrderingItems = new() { new() { Text = "x", CorrectOrder = firstOrder }, new() { Text = "y", CorrectOrder = firstOrder == 1 ? 2 : 1 } }
    };

    [Test]
    public void Hash_IsStableAcrossReserialization()
    {
        foreach (var dto in new[] { MultipleChoice(), Matching(), Ordering() })
            QuestionContent.Hash(Build(dto)).Should().Be(QuestionContent.Hash(Build(dto)), dto.Type);
    }

    [Test]
    public void Hash_ChangesWhenMeaningfulContentChanges()
    {
        QuestionContent.Hash(Build(MultipleChoice("A"))).Should().NotBe(QuestionContent.Hash(Build(MultipleChoice("B"))));
        QuestionContent.Hash(Build(Matching("1"))).Should().NotBe(QuestionContent.Hash(Build(Matching("9"))));
        QuestionContent.Hash(Build(Ordering(1))).Should().NotBe(QuestionContent.Hash(Build(Ordering(2))));

        var text = MultipleChoice(); text.QuestionText = "Câu khác?";
        QuestionContent.Hash(Build(text)).Should().NotBe(QuestionContent.Hash(Build(MultipleChoice())));
        var explain = MultipleChoice(); explain.ExplainText = "Giải thích khác";
        QuestionContent.Hash(Build(explain)).Should().NotBe(QuestionContent.Hash(Build(MultipleChoice())));
    }

    [Test]
    public void Hash_IgnoresScore_AndKeepsOptionOrder()
    {
        var a = Build(MultipleChoice()); var b = Build(MultipleChoice());
        b.Score = 9;
        QuestionContent.Hash(a).Should().Be(QuestionContent.Hash(b));

        var reordered = MultipleChoice(); reordered.MultipleChoices!.Reverse();
        QuestionContent.Hash(Build(reordered)).Should().NotBe(QuestionContent.Hash(a), "tác giả đổi thứ tự đáp án là đổi nội dung");
    }

    [Test]
    public void Hash_FallsBackToRawJson_ForUnreadableData()
    {
        var broken = new Question { Type = QuestionType.MultipleChoice, QuestionText = "x", DataJson = "{not json" };
        var other = new Question { Type = QuestionType.MultipleChoice, QuestionText = "x", DataJson = "{not json either" };
        QuestionContent.Hash(broken).Should().NotBe(QuestionContent.Hash(other));
    }
}

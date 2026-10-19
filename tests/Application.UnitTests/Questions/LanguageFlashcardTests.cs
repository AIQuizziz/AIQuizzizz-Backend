using System.Text.Json;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.UnitTests.Questions;

public class LanguageFlashcardTests
{
    private static QTypeLanguageFlashcard Card(string definition, string? pos = "noun", string? translation = null) => new()
    {
        Back = new QFlashcardBack
        {
            Meanings = { new QFlashcardMeaning { Pos = pos, Definition = definition, Translation = translation } }
        }
    };

    private static Question Q(string front, QTypeLanguageFlashcard card) => new()
    {
        Id = Guid.NewGuid(),
        QuestionText = front,
        Type = QuestionType.LanguageFlashcard,
        DataJson = JsonSerializer.Serialize(card)
    };

    private static List<Question> Deck() => new()
    {
        Q("serendipity", Card("finding something good without looking for it")),
        Q("ephemeral", Card("lasting for a very short time", "adjective")),
        Q("ubiquitous", Card("present everywhere at the same time", "adjective")),
        Q("candid", Card("honest and direct in speech", "adjective")),
        Q("resilience", Card("ability to recover quickly from difficulty")),
    };

    private static QuestionResponseDto Quiz(Question q, IEnumerable<Question>? pool, bool shuffle = false, bool showAnswer = true) =>
        QuestionResponseDto.Mapper.FromEntity(q, null, shuffle, showAnswer, quizView: true, flashcardPool: pool);

    // ---- dựng đáp án trắc nghiệm ----

    [Test]
    public void BuildOptions_ContainsExactlyOneCorrect_WithQuestionIdAsId()
    {
        var deck = Deck();
        var dto = Quiz(deck[0], deck);

        dto.Type.Should().Be("MultipleChoice");
        dto.OriginalType.Should().Be("LanguageFlashcard");
        var options = dto.QuestionData.MultipleChoice!;
        options.Should().HaveCount(4);
        options.Count(o => o.IsAnswer == true).Should().Be(1);
        options.Single(o => o.IsAnswer == true).Id.Should().Be(deck[0].Id);
        options.Select(o => o.Text).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void BuildOptions_IsDeterministic_ForSameDeck_RegardlessOfPoolOrder()
    {
        var deck = Deck();
        var reversed = deck.AsEnumerable().Reverse().ToList();
        var first = Quiz(deck[1], deck).QuestionData.MultipleChoice!.Select(o => o.Id).ToList();
        var second = Quiz(deck[1], reversed).QuestionData.MultipleChoice!.Select(o => o.Id).ToList();
        second.Should().Equal(first);
    }

    [Test]
    public void BuildOptions_PrefersDistractorsWithSamePartOfSpeech()
    {
        var deck = Deck(); // thẻ [1] là adjective: còn 2 adjective khác + 2 noun
        var wrongIds = Quiz(deck[1], deck).QuestionData.MultipleChoice!
            .Where(o => o.IsAnswer == false).Select(o => o.Id).ToHashSet();
        wrongIds.Should().Contain(new[] { deck[2].Id, deck[3].Id });
    }

    [Test]
    public void BuildOptions_SkipsNearDuplicateMeanings()
    {
        var a = Q("big", Card("a large thing of great size"));
        var b = Q("huge", Card("a large thing of great size."));
        var c = Q("tiny", Card("very small in size"));
        var d = Q("fast", Card("moving at high speed"));

        Quiz(a, new List<Question> { a, b, c, d }).QuestionData.MultipleChoice!
            .Select(o => o.Id).Should().NotContain(b.Id);
    }

    [Test]
    public void BuildOptions_PrefersTranslationOverDefinition()
    {
        var deck = new List<Question>
        {
            Q("hello", Card("a greeting", translation: "xin chào")),
            Q("bye", Card("farewell", translation: "tạm biệt"))
        };
        Quiz(deck[0], deck).QuestionData.MultipleChoice!.Select(o => o.Text)
            .Should().BeEquivalentTo(new[] { "xin chào", "tạm biệt" });
    }

    [Test]
    public void BuildOptions_HidesAnswerFlags_WhenAnswersNotShown()
    {
        var deck = Deck();
        Quiz(deck[0], deck, shuffle: true, showAnswer: false).QuestionData.MultipleChoice!
            .Should().OnlyContain(o => o.IsAnswer == null);
    }

    [Test]
    public void SingleCardDeck_YieldsOnlyTheCorrectOption_NoCrash()
    {
        var only = Q("solo", Card("alone"));
        Quiz(only, new[] { only }).QuestionData.MultipleChoice.Should().ContainSingle();
    }

    [Test]
    public void DetailView_KeepsFlashcardType_AndExposesBackSide()
    {
        var deck = Deck();
        var dto = QuestionResponseDto.Mapper.FromEntity(deck[0], null, shuffle: false, quizView: false);
        dto.Type.Should().Be("LanguageFlashcard");
        dto.OriginalType.Should().BeNull();
        dto.QuestionData.Flashcard!.Back.Meanings.Should().ContainSingle();
        dto.QuestionData.MultipleChoice.Should().BeNull();
    }

    [Test]
    public void CardWithoutAnyMeaning_HasNoOptions()
    {
        var draft = Q("draft", new QTypeLanguageFlashcard());
        Quiz(draft, Deck()).QuestionData.MultipleChoice.Should().BeEmpty();
    }

    // ---- validator / serializer ----

    private static CreateUpdateQuestionDto Dto(QTypeLanguageFlashcard? card) => new()
    {
        Type = "LanguageFlashcard", QuestionText = "serendipity", Score = 1, Flashcard = card
    };

    private static bool IsValid(CreateUpdateQuestionDto q) =>
        new CreateUpdateQuestionDto.QuestionCreateDtoValidator().Validate(q).IsValid;

    [Test]
    public void Validator_AcceptsNormalCard_AndDraftCardWithoutMeanings()
    {
        IsValid(Dto(Card("a definition"))).Should().BeTrue();
        IsValid(Dto(new QTypeLanguageFlashcard())).Should().BeTrue();
    }

    [Test]
    public void Validator_RejectsMissingFlashcardData() => IsValid(Dto(null)).Should().BeFalse();

    [Test]
    public void Validator_RejectsMeaningWithoutDefinitionOrTranslation() => IsValid(Dto(Card(""))).Should().BeFalse();

    [TestCase("http://example.com/a.mp3")]
    [TestCase("javascript:alert(1)")]
    [TestCase("not a url")]
    public void Validator_RejectsNonHttpsUrls(string url)
    {
        var audio = Card("def");
        audio.Back.AudioUrl = url;
        IsValid(Dto(audio)).Should().BeFalse();

        var source = Card("def");
        source.Source = new QFlashcardSource { Url = url };
        IsValid(Dto(source)).Should().BeFalse();
    }

    [Test]
    public void Validator_EnforcesLimits()
    {
        var tooManyMeanings = new QTypeLanguageFlashcard();
        for (var i = 0; i < 6; i++) tooManyMeanings.Back.Meanings.Add(new QFlashcardMeaning { Definition = "d" + i });
        IsValid(Dto(tooManyMeanings)).Should().BeFalse();

        var tooManyExamples = Card("d");
        tooManyExamples.Back.Meanings[0].Examples.AddRange(new[] { "1", "2", "3", "4" });
        IsValid(Dto(tooManyExamples)).Should().BeFalse();

        IsValid(Dto(Card(new string('x', 1001)))).Should().BeFalse();
    }

    [Test]
    public void Serializer_TrimsAndForcesVersion_AndRoundTrips()
    {
        var card = Card("  a definition  ", " Noun ");
        card.V = 99;
        card.Back.Meanings[0].Examples.AddRange(new[] { " ex ", "  ", "ex2" });

        var json = CreateUpdateQuestionDto.Serializer.Serialize(Dto(card));
        var question = new Question { Id = Guid.NewGuid(), Type = QuestionType.LanguageFlashcard, QuestionText = "x", DataJson = json };
        var back = CreateUpdateQuestionDto.Deserializer.Deserialize(question).Flashcard!;

        back.V.Should().Be(1);
        back.Back.Meanings[0].Definition.Should().Be("a definition");
        back.Back.Meanings[0].Pos.Should().Be("noun");
        back.Back.Meanings[0].Examples.Should().Equal("ex", "ex2");
        json.Should().Contain("\"back\"").And.Contain("\"meanings\"").And.Contain("\"v\":1");
    }
}

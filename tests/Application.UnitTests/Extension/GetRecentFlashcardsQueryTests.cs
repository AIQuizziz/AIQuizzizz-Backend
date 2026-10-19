using System.Text.Json;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Extension;
using CleanArchitectureBase.Domain.Entities;
using CleanArchitectureBase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureBase.Application.UnitTests.Extension;

[TestFixture]
public class GetRecentFlashcardsQueryTests
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp() => _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [TearDown]
    public void TearDown() => _db.Dispose();

    private GetRecentFlashcardsQueryHandler Handler()
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.UserId).Returns(_me);
        return new GetRecentFlashcardsQueryHandler(_db, user.Object);
    }

    private QuestionSet AddSet(string name, QuestionSetUserShareMode mode = QuestionSetUserShareMode.Owner, bool deleted = false, Guid? member = null)
    {
        var set = new QuestionSet { Id = Guid.NewGuid(), Name = name, IsDeleted = deleted };
        _db.QuestionSets.Add(set);
        _db.QuestionSetUsers.Add(new QuestionSetUser { UserId = member ?? _me, QuestionSetId = set.Id, ShareMode = mode });
        return set;
    }

    private Question AddCard(QuestionSet set, string front, int minutesAgo, Guid? createdBy = null, bool deleted = false,
        string? definition = "a meaning", string? translation = null, QuestionType type = QuestionType.LanguageFlashcard)
    {
        var card = new QTypeLanguageFlashcard();
        if (definition != null || translation != null)
            card.Back.Meanings.Add(new QFlashcardMeaning { Definition = definition ?? "", Translation = translation });
        var q = new Question
        {
            Id = Guid.NewGuid(), QuestionSetId = set.Id, Type = type, QuestionText = front, IsDeleted = deleted,
            DataJson = type == QuestionType.LanguageFlashcard ? JsonSerializer.Serialize(card) : "{}",
            Created = T0.AddMinutes(-minutesAgo), CreatedBy = createdBy ?? _me
        };
        _db.Questions.Add(q);
        return q;
    }

    [Test]
    public async Task ReturnsMyCardsNewestFirst_WithSetNameAndPreview()
    {
        var set = AddSet("Flashcards 2026-10-19");
        AddCard(set, "old", 30, translation: "cũ");
        AddCard(set, "new", 1, definition: null, translation: "mới");
        AddCard(set, "mid", 10);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new GetRecentFlashcardsQuery(), default);

        result.Select(r => r.Front).Should().Equal("new", "mid", "old");
        result[0].Preview.Should().Be("mới");
        result[1].Preview.Should().Be("a meaning");
        result[2].Preview.Should().Be("cũ"); // bản dịch được ưu tiên hơn định nghĩa
        result.Should().OnlyContain(r => r.QuestionSetName == "Flashcards 2026-10-19");
        result[0].CreatedAt.Should().Be(T0.AddMinutes(-1));
    }

    [Test]
    public async Task ExcludesDeletedCards_DeletedSets_OtherUsersCards_AndOtherQuestionTypes()
    {
        var live = AddSet("Live");
        var gone = AddSet("Gone", deleted: true);
        AddCard(live, "keep", 1);
        AddCard(live, "deleted-card", 2, deleted: true);
        AddCard(gone, "in-deleted-set", 3);
        AddCard(live, "someone-elses", 4, createdBy: _other);
        AddCard(live, "short-text", 5, type: QuestionType.ShortText);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new GetRecentFlashcardsQuery(), default);

        result.Select(r => r.Front).Should().Equal("keep");
    }

    [Test]
    public async Task ExcludesSetsWhereIHaveLostEditAccess_ButKeepsEditableSharedSets()
    {
        var viewOnly = AddSet("ViewOnly", QuestionSetUserShareMode.ViewOnly);
        var editable = AddSet("Editable", QuestionSetUserShareMode.Editable);
        var notMine = AddSet("NotMine", member: _other); // mình không có hàng truy cập nào
        AddCard(viewOnly, "view-only", 1);
        AddCard(editable, "editable", 2);
        AddCard(notMine, "not-mine", 3);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new GetRecentFlashcardsQuery(), default);

        result.Select(r => r.Front).Should().Equal("editable");
    }

    [Test]
    public async Task HonoursLimit_DefaultTen()
    {
        var set = AddSet("S");
        for (var i = 0; i < 15; i++) AddCard(set, $"w{i}", i);
        await _db.SaveChangesAsync();

        (await Handler().Handle(new GetRecentFlashcardsQuery(), default)).Should().HaveCount(10);
        (await Handler().Handle(new GetRecentFlashcardsQuery { Limit = 3 }, default)).Select(r => r.Front).Should().Equal("w0", "w1", "w2");
    }

    [Test]
    public async Task DraftCard_HasNullPreview()
    {
        var set = AddSet("S");
        AddCard(set, "draft", 1, definition: null);
        await _db.SaveChangesAsync();

        (await Handler().Handle(new GetRecentFlashcardsQuery(), default)).Single().Preview.Should().BeNull();
    }

    [Test]
    public void Validator_LimitMustBeBetweenOneAndFifty()
    {
        var v = new GetRecentFlashcardsQueryValidator();
        v.Validate(new GetRecentFlashcardsQuery()).IsValid.Should().BeTrue();
        v.Validate(new GetRecentFlashcardsQuery { Limit = 50 }).IsValid.Should().BeTrue();
        v.Validate(new GetRecentFlashcardsQuery { Limit = 0 }).IsValid.Should().BeFalse();
        v.Validate(new GetRecentFlashcardsQuery { Limit = 51 }).IsValid.Should().BeFalse();
    }
}

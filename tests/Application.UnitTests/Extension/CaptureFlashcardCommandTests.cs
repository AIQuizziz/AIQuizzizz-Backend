using System.Text.Json;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Extension;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Domain.Entities;
using CleanArchitectureBase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureBase.Application.UnitTests.Extension;

[TestFixture]
public class CaptureFlashcardCommandTests
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _userId = Guid.NewGuid();
    private Mock<IDictionaryLookupService> _dictionary = null!;
    private Mock<ITranslationService> _translator = null!;
    private Mock<IQuestionSetService> _sets = null!;
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 20, 0, 0, TimeSpan.Zero); // 03:00 ngày 20/10 ở Asia/Ho_Chi_Minh

    private class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    [SetUp]
    public void SetUp()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _dictionary = new Mock<IDictionaryLookupService>();
        _translator = new Mock<ITranslationService>();
        _sets = new Mock<IQuestionSetService>();
        _sets.Setup(s => s.CanUserEditQuestionSet(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(true);

        _dictionary.Setup(d => d.LookupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DictionaryLookupResult.NotFound);
        _dictionary.Setup(d => d.LookupAsync("serendipity", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DictionaryLookupResult.Found(new DictionaryEntry
            {
                Phonetic = "/ˌsɛrənˈdɪpɪti/",
                AudioUrl = "https://example.com/a.mp3",
                Meanings = { new QFlashcardMeaning { Pos = "noun", Definition = "finding good things by chance" } }
            }));
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private CaptureFlashcardCommandHandler Handler(Guid? userId = null)
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.UserId).Returns(userId ?? _userId);
        return new CaptureFlashcardCommandHandler(_db, user.Object, _sets.Object, _dictionary.Object, _translator.Object, new FixedTime(Now));
    }

    private void Settings(ExtensionTargetMode mode, bool translate = true, Guid? fixedSet = null, string tz = "UTC", bool storeContext = true) =>
        _db.ExtensionSettings.Add(new ExtensionSetting
        {
            UserId = _userId, TargetMode = mode, TranslateEnabled = translate, TargetQuestionSetId = fixedSet,
            TimeZone = tz, StoreContext = storeContext
        });

    private async Task<QuestionSet> AddOwnedSet(string name, int count = 0)
    {
        var set = new QuestionSet { Id = Guid.NewGuid(), Name = name, QuestionCount = count };
        _db.QuestionSets.Add(set);
        _db.QuestionSetUsers.Add(new QuestionSetUser { UserId = _userId, QuestionSetId = set.Id, ShareMode = QuestionSetUserShareMode.Owner });
        await _db.SaveChangesAsync();
        return set;
    }

    private static CaptureFlashcardCommand Cmd(string front = "serendipity") => new() { Front = front };

    [Test]
    public async Task Daily_CreatesSetNamedByLocalDate_AndAddsFlashcardWithLookup()
    {
        Settings(ExtensionTargetMode.Daily, tz: "UTC");
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(Cmd(), default);

        result.QuestionSetName.Should().Be("Flashcards 2026-10-19");
        result.LookupStatus.Should().Be("found");
        result.Preview.Should().Be("finding good things by chance");
        var q = await _db.Questions.SingleAsync();
        q.Type.Should().Be(QuestionType.LanguageFlashcard);
        q.QuestionText.Should().Be("serendipity");
        var card = JsonSerializer.Deserialize<QTypeLanguageFlashcard>(q.DataJson!)!;
        card.Back.AudioUrl.Should().Be("https://example.com/a.mp3");
        card.Lookup!.Status.Should().Be("found");
        (await _db.QuestionSets.SingleAsync()).QuestionCount.Should().Be(1);
        (await _db.QuestionSetUsers.SingleAsync()).ShareMode.Should().Be(QuestionSetUserShareMode.Owner);
    }

    [Test]
    public async Task Daily_ReusesTheSameSetWithinTheSamePeriod()
    {
        Settings(ExtensionTargetMode.Daily);
        await _db.SaveChangesAsync();
        
        await Handler().Handle(Cmd(), default);
        await Handler().Handle(Cmd("ephemeral"), default);

        (await _db.QuestionSets.CountAsync()).Should().Be(1);
        (await _db.Questions.CountAsync()).Should().Be(2);
        (await _db.QuestionSets.SingleAsync()).QuestionCount.Should().Be(2);
        (await _db.Questions.OrderBy(q => q.Order).Select(q => q.Order).ToListAsync()).Should().Equal(0, 1);
    }

    [Test]
    public async Task SetName_FollowsUserTimeZone()
    {
        Settings(ExtensionTargetMode.Daily, tz: "Asia/Ho_Chi_Minh");
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(Cmd(), default);

        // Bỏ qua nếu môi trường thiếu dữ liệu múi giờ (khi đó dùng UTC)
        var expected = TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Ho_Chi_Minh", out _) ? "Flashcards 2026-10-20" : "Flashcards 2026-10-19";
        result.QuestionSetName.Should().Be(expected);
    }

    [Test]
    public async Task SameWord_InTheSameSet_IsNotDuplicated_CaseInsensitive()
    {
        var set = await AddOwnedSet("Mine");
        Settings(ExtensionTargetMode.Fixed, fixedSet: set.Id);
        await _db.SaveChangesAsync();

        var first = await Handler().Handle(Cmd("Serendipity "), default);
        var second = await Handler().Handle(Cmd("serendipity"), default);

        first.Duplicate.Should().BeFalse();
        second.Duplicate.Should().BeTrue();
        second.QuestionId.Should().Be(first.QuestionId);
        (await _db.Questions.CountAsync()).Should().Be(1);
        (await _db.QuestionSets.SingleAsync()).QuestionCount.Should().Be(1);
    }

    [Test]
    public async Task TranslationOff_SkipsAllLookups_AndLeavesTheBackEmpty()
    {
        Settings(ExtensionTargetMode.Daily, translate: false);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(Cmd(), default);

        result.Translated.Should().BeFalse();
        result.LookupStatus.Should().Be("skipped");
        result.Preview.Should().BeNull();
        var card = JsonSerializer.Deserialize<QTypeLanguageFlashcard>((await _db.Questions.SingleAsync()).DataJson!)!;
        card.Back.Meanings.Should().BeEmpty();
        _dictionary.Verify(d => d.LookupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _translator.Verify(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SameSourceAndTargetLanguage_UsesDictionaryOnly_WithoutTranslating()
    {
        Settings(ExtensionTargetMode.Daily, translate: true);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new CaptureFlashcardCommand { Front = "serendipity", TargetLang = "en" }, default);

        result.LookupStatus.Should().Be("found");
        result.Translated.Should().BeFalse();
        result.Preview.Should().Be("finding good things by chance");
        _translator.Verify(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task TranslationOn_PutsTranslationOnFirstMeaning_AndItBecomesThePreview()
    {
        Settings(ExtensionTargetMode.Daily, translate: true);
        await _db.SaveChangesAsync();
        _translator.Setup(t => t.TranslateAsync("serendipity", "en", "vi", It.IsAny<CancellationToken>())).ReturnsAsync("sự tình cờ may mắn");

        var result = await Handler().Handle(Cmd(), default);

        result.Translated.Should().BeTrue();
        result.Preview.Should().Be("sự tình cờ may mắn");
        var card = JsonSerializer.Deserialize<QTypeLanguageFlashcard>((await _db.Questions.SingleAsync()).DataJson!)!;
        card.Back.Meanings[0].Translation.Should().Be("sự tình cờ may mắn");
        card.Back.Meanings[0].Definition.Should().Be("finding good things by chance");
        card.Lang!.To.Should().Be("vi");
    }

    [Test]
    public async Task UntranslatableWord_EchoedBackByTranslator_IsNotStoredAsATranslation()
    {
        Settings(ExtensionTargetMode.Daily, translate: true);
        await _db.SaveChangesAsync();
        _translator.Setup(t => t.TranslateAsync("asdfqwerzx", "en", "vi", It.IsAny<CancellationToken>())).ReturnsAsync("asdfqwerzx");

        var result = await Handler().Handle(Cmd("asdfqwerzx"), default);

        result.Translated.Should().BeFalse();
        result.Preview.Should().BeNull();
    }

    [Test]
    public async Task WordNotInDictionary_StillCreatesCard_WithTranslationOnly()
    {
        Settings(ExtensionTargetMode.Daily, translate: true);
        await _db.SaveChangesAsync();
        _translator.Setup(t => t.TranslateAsync("zzzword", "en", "vi", It.IsAny<CancellationToken>())).ReturnsAsync("từ lạ");

        var result = await Handler().Handle(Cmd("zzzword"), default);

        result.LookupStatus.Should().Be("not_found");
        result.Preview.Should().Be("từ lạ");
        (await _db.Questions.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task WordNotFound_AndTranslationOff_CreatesDraftCardWithEmptyBack()
    {
        Settings(ExtensionTargetMode.Daily, translate: false);
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(Cmd("zzzword"), default);

        result.Preview.Should().BeNull();
        var card = JsonSerializer.Deserialize<QTypeLanguageFlashcard>((await _db.Questions.SingleAsync()).DataJson!)!;
        card.Back.Meanings.Should().BeEmpty();
    }

    [Test]
    public async Task DictionaryServiceError_IsRecordedAsError_NotAsNotFound_AndCardIsStillSaved()
    {
        Settings(ExtensionTargetMode.Daily, translate: true);
        await _db.SaveChangesAsync();
        _dictionary.Setup(d => d.LookupAsync("candid", "en", It.IsAny<CancellationToken>())).ReturnsAsync(DictionaryLookupResult.Error);

        var result = await Handler().Handle(Cmd("candid"), default);

        result.LookupStatus.Should().Be("error");
        (await _db.Questions.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task HttpSourceUrl_IsDropped_HttpsIsKept_ContextRespectsSetting()
    {
        Settings(ExtensionTargetMode.Daily, storeContext: false);
        await _db.SaveChangesAsync();

        await Handler().Handle(new CaptureFlashcardCommand
        {
            Front = "serendipity", SourceUrl = "http://insecure.example/page", SourceTitle = "T", Context = "a happy serendipity"
        }, default);
        await Handler().Handle(new CaptureFlashcardCommand
        {
            Front = "ephemeral", SourceUrl = "https://secure.example/page", Context = "ctx"
        }, default);

        var cards = (await _db.Questions.OrderBy(q => q.Order).ToListAsync())
            .Select(q => JsonSerializer.Deserialize<QTypeLanguageFlashcard>(q.DataJson!)!).ToList();
        cards[0].Source!.Url.Should().BeNull();
        cards[0].Source!.Context.Should().BeNull();
        cards[1].Source!.Url.Should().Be("https://secure.example/page");
        cards[1].Source!.Context.Should().BeNull();
    }

    [Test]
    public async Task FixedMode_RequiresEditPermission()
    {
        var set = await AddOwnedSet("Mine");
        Settings(ExtensionTargetMode.Fixed, fixedSet: set.Id);
        await _db.SaveChangesAsync();
        _sets.Setup(s => s.CanUserEditQuestionSet(_userId, set.Id)).ReturnsAsync(false);

        var act = () => Handler().Handle(Cmd(), default);

        await act.Should().ThrowAsync<ErrorCodeException>();
        (await _db.Questions.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task FixedMode_WithoutChosenSet_Fails()
    {
        Settings(ExtensionTargetMode.Fixed, fixedSet: null);
        await _db.SaveChangesAsync();

        var act = () => Handler().Handle(Cmd(), default);

        await act.Should().ThrowAsync<ErrorCodeException>();
    }

    [Test]
    public async Task FullSet_IsRejected()
    {
        var set = await AddOwnedSet("Full", count: 500);
        Settings(ExtensionTargetMode.Fixed, fixedSet: set.Id);
        await _db.SaveChangesAsync();

        var act = () => Handler().Handle(Cmd(), default);

        await act.Should().ThrowAsync<ErrorCodeException>();
    }

    [Test]
    public async Task NoSettingsRow_UsesDefaults_DailyEnglishToVietnamese()
    {
        var result = await Handler().Handle(Cmd(), default);

        result.QuestionSetName.Should().StartWith("Flashcards 2026-");
        result.Translated.Should().BeFalse();
    }

    [Test]
    public void Validator_RejectsEmptyAndTooLongFront()
    {
        var v = new CaptureFlashcardCommandValidator();
        v.Validate(new CaptureFlashcardCommand { Front = "  " }).IsValid.Should().BeFalse();
        v.Validate(new CaptureFlashcardCommand { Front = new string('a', 201) }).IsValid.Should().BeFalse();
        v.Validate(new CaptureFlashcardCommand { Front = "ok" }).IsValid.Should().BeTrue();
        v.Validate(new CaptureFlashcardCommand { Front = "ok", SourceLang = "xx_yy" }).IsValid.Should().BeFalse();
    }

    // ---- hoàn tác ----

    private UndoCaptureFlashcardCommandHandler UndoHandler(DateTimeOffset? now = null)
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.UserId).Returns(_userId);
        return new UndoCaptureFlashcardCommandHandler(_db, user.Object, _sets.Object, new FixedTime(now ?? Now));
    }

    [Test]
    public async Task Undo_RemovesOnlyThatCard_AndUpdatesCount()
    {
        Settings(ExtensionTargetMode.Daily);
        await _db.SaveChangesAsync();
        var a = await Handler().Handle(Cmd(), default);
        var b = await Handler().Handle(Cmd("ephemeral"), default);
        // Created do interceptor đặt; InMemory không có interceptor nên gán tay
        foreach (var q in _db.Questions) q.Created = Now;
        await _db.SaveChangesAsync();

        await UndoHandler().Handle(new UndoCaptureFlashcardCommand { QuestionId = a.QuestionId }, default);

        (await _db.Questions.SingleAsync(q => q.Id == a.QuestionId)).IsDeleted.Should().BeTrue();
        (await _db.Questions.SingleAsync(q => q.Id == b.QuestionId)).IsDeleted.Should().BeFalse();
        (await _db.QuestionSets.SingleAsync()).QuestionCount.Should().Be(1);
    }

    [Test]
    public async Task Undo_IsRejected_AfterTheUndoWindow_OrWithoutEditPermission()
    {
        Settings(ExtensionTargetMode.Daily);
        await _db.SaveChangesAsync();
        var a = await Handler().Handle(Cmd(), default);
        foreach (var q in _db.Questions) q.Created = Now;
        await _db.SaveChangesAsync();

        var late = () => UndoHandler(Now.AddMinutes(11)).Handle(new UndoCaptureFlashcardCommand { QuestionId = a.QuestionId }, default);
        await late.Should().ThrowAsync<ErrorCodeException>();

        _sets.Setup(s => s.CanUserEditQuestionSet(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(false);
        var forbidden = () => UndoHandler().Handle(new UndoCaptureFlashcardCommand { QuestionId = a.QuestionId }, default);
        await forbidden.Should().ThrowAsync<ErrorCodeException>();

        (await _db.Questions.SingleAsync()).IsDeleted.Should().BeFalse();
    }

    [Test]
    public async Task Undo_OnlyAppliesToFlashcards()
    {
        var set = await AddOwnedSet("Mine");
        var mc = new Question { Id = Guid.NewGuid(), QuestionSetId = set.Id, Type = QuestionType.ShortText, QuestionText = "q", DataJson = "{}", Created = Now };
        _db.Questions.Add(mc);
        await _db.SaveChangesAsync();

        var act = () => UndoHandler().Handle(new UndoCaptureFlashcardCommand { QuestionId = mc.Id }, default);

        await act.Should().ThrowAsync<ErrorCodeException>();
    }

    // ---- đặt tên bộ theo kỳ ----

    [TestCase(ExtensionTargetMode.Daily, "UTC", "Flashcards 2026-10-19")]
    [TestCase(ExtensionTargetMode.Weekly, "UTC", "Flashcards 2026-W43")]
    [TestCase(ExtensionTargetMode.Monthly, "UTC", "Flashcards 2026-10")]
    public void PeriodSetNamer_NamesByPeriod(ExtensionTargetMode mode, string tz, string expected) =>
        PeriodSetNamer.NameFor(mode, Now, tz).Should().Be(expected);

    [Test]
    public void PeriodSetNamer_UsesIsoWeekYear_AtYearBoundary()
    {
        // 31/12/2026 là thứ Năm thuộc tuần 53 của năm ISO 2026; 01/01/2027 (thứ Sáu) vẫn thuộc tuần đó
        PeriodSetNamer.NameFor(ExtensionTargetMode.Weekly, new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero), "UTC")
            .Should().Be("Flashcards 2026-W53");
    }

    [Test]
    public void PeriodSetNamer_UnknownTimeZone_FallsBackToUtc() =>
        PeriodSetNamer.NameFor(ExtensionTargetMode.Daily, Now, "Not/AZone").Should().Be("Flashcards 2026-10-19");
}

using System.Net;
using System.Text;
using System.Text.Json;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Extension;
using CleanArchitectureBase.Domain.Entities;
using CleanArchitectureBase.Infrastructure.Data;
using CleanArchitectureBase.Infrastructure.Flashcards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanArchitectureBase.Application.UnitTests.Extension;

[TestFixture]
public class TranslateTextQueryTests
{
    private ApplicationDbContext _db = null!;
    private readonly Guid _userId = Guid.NewGuid();
    private Mock<IDictionaryLookupService> _dictionary = null!;
    private Mock<ITranslationService> _wordTranslator = null!;
    private Mock<ISentenceTranslationService> _sentenceTranslator = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _dictionary = new Mock<IDictionaryLookupService>();
        _wordTranslator = new Mock<ITranslationService>();
        _sentenceTranslator = new Mock<ISentenceTranslationService>();
        _dictionary.Setup(d => d.LookupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DictionaryLookupResult.NotFound);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private TranslateTextQueryHandler Handler()
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.UserId).Returns(_userId);
        return new TranslateTextQueryHandler(_db, user.Object, _dictionary.Object, _wordTranslator.Object, _sentenceTranslator.Object);
    }

    private static DictionaryLookupResult Found(string definition) => DictionaryLookupResult.Found(new DictionaryEntry
    {
        Phonetic = "/x/",
        AudioUrl = "https://example.com/a.mp3",
        Meanings = { new QFlashcardMeaning { Pos = "noun", Definition = definition, Examples = { "an example" } } }
    });

    // ---- phân loại từ / câu ----

    [TestCase("documentation", true)]
    [TestCase("well-known", true)]
    [TestCase("ice cream", false)]
    [TestCase("This is a sentence.", false)]
    [TestCase("これは本です。", true)] // viết liền không có khoảng trắng và ngắn: coi là từ
    public void IsWord_ClassifiesByWhitespaceAndLength(string text, bool expected) =>
        TranslateTextQuery.IsWord(TranslateTextQuery.Normalize(text)).Should().Be(expected);

    [Test]
    public void IsWord_LongTextWithoutSpaces_IsASentence() =>
        TranslateTextQuery.IsWord(new string('a', TranslateTextQuery.MaxWordLength + 1)).Should().BeFalse();

    [Test]
    public void TrimPunctuation_RemovesSurroundingPunctuationOnly()
    {
        TranslateTextQuery.TrimPunctuation("“documentation,”").Should().Be("documentation");
        TranslateTextQuery.TrimPunctuation("don't").Should().Be("don't");
    }

    // ---- từ ----

    [Test]
    public async Task Word_UsesDictionaryAndWordTranslation_AndBuildsInsertableBack()
    {
        _dictionary.Setup(d => d.LookupAsync("documentation", "en", It.IsAny<CancellationToken>())).ReturnsAsync(Found("written material"));
        _wordTranslator.Setup(t => t.TranslateAsync("documentation", "en", "vi", It.IsAny<CancellationToken>())).ReturnsAsync("tài liệu");

        var r = await Handler().Handle(new TranslateTextQuery { Text = "  documentation, " }, default);

        r.Kind.Should().Be("word");
        r.Text.Should().Be("documentation");
        r.Translation.Should().Be("tài liệu");
        r.TranslationStatus.Should().Be("ok");
        r.LookupStatus.Should().Be("found");
        r.CanInsert.Should().BeTrue();
        r.Back.Back.Phonetic.Should().Be("/x/");
        r.Back.Back.Meanings[0].Definition.Should().Be("written material");
        r.Back.Back.Meanings[0].Translation.Should().Be("tài liệu");
        r.Back.Lang!.To.Should().Be("vi");
        _sentenceTranslator.Verify(s => s.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Word_NotInDictionary_StillReturnsTranslation_AsTheOnlyMeaning()
    {
        _wordTranslator.Setup(t => t.TranslateAsync("zzz", "en", "vi", It.IsAny<CancellationToken>())).ReturnsAsync("từ lạ");

        var r = await Handler().Handle(new TranslateTextQuery { Text = "zzz" }, default);

        r.LookupStatus.Should().Be("not_found");
        r.Back.Back.Meanings.Should().ContainSingle().Which.Translation.Should().Be("từ lạ");
    }

    [Test]
    public async Task Word_TranslatorEchoingTheWord_WithNoDictionaryEntry_IsUnavailable()
    {
        _wordTranslator.Setup(t => t.TranslateAsync("asdfqwerzx", "en", "vi", It.IsAny<CancellationToken>())).ReturnsAsync("asdfqwerzx");

        var r = await Handler().Handle(new TranslateTextQuery { Text = "asdfqwerzx" }, default);

        r.TranslationStatus.Should().Be("unavailable");
        r.Translation.Should().BeNull();
        r.Back.Back.Meanings.Should().BeEmpty();
    }

    [Test]
    public async Task Word_SameSourceAndTarget_OnlyLooksUp()
    {
        _dictionary.Setup(d => d.LookupAsync("book", "en", It.IsAny<CancellationToken>())).ReturnsAsync(Found("pages"));

        var r = await Handler().Handle(new TranslateTextQuery { Text = "book", TargetLang = "en" }, default);

        r.TranslationStatus.Should().Be("skipped");
        r.LookupStatus.Should().Be("found");
        _wordTranslator.Verify(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UsesTheUsersSavedLanguages_WhenRequestDoesNotOverride()
    {
        _db.ExtensionSettings.Add(new ExtensionSetting { UserId = _userId, SourceLang = "fr", TargetLang = "en" });
        await _db.SaveChangesAsync();
        _wordTranslator.Setup(t => t.TranslateAsync("livre", "fr", "en", It.IsAny<CancellationToken>())).ReturnsAsync("book");

        var r = await Handler().Handle(new TranslateTextQuery { Text = "livre" }, default);

        r.Translation.Should().Be("book");
        _dictionary.Verify(d => d.LookupAsync("livre", "fr", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- câu ----

    [Test]
    public async Task Sentence_UsesLibreTranslate_AndSkipsTheDictionary()
    {
        _sentenceTranslator.Setup(s => s.TranslateAsync("The quick brown fox.", "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SentenceTranslationResult.Ok("Con cáo nâu nhanh nhẹn."));

        var r = await Handler().Handle(new TranslateTextQuery { Text = "The quick   brown fox." }, default);

        r.Kind.Should().Be("sentence");
        r.Translation.Should().Be("Con cáo nâu nhanh nhẹn.");
        r.TranslationStatus.Should().Be("ok");
        r.LookupStatus.Should().Be("skipped");
        r.Back.Back.Meanings.Should().ContainSingle().Which.Translation.Should().Be("Con cáo nâu nhanh nhẹn.");
        _dictionary.Verify(d => d.LookupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Sentence_WhenLibreTranslateUnavailable_ReportsUnavailable()
    {
        _sentenceTranslator.Setup(s => s.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SentenceTranslationResult.Unavailable);

        var r = await Handler().Handle(new TranslateTextQuery { Text = "Two words" }, default);

        r.TranslationStatus.Should().Be("unavailable");
        r.Translation.Should().BeNull();
    }

    [Test]
    public async Task LongSentence_CannotBeInsertedAsAFlashcard()
    {
        _sentenceTranslator.Setup(s => s.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SentenceTranslationResult.Ok("x"));

        var r = await Handler().Handle(new TranslateTextQuery { Text = string.Join(' ', Enumerable.Repeat("word", 60)) }, default);

        r.CanInsert.Should().BeFalse();
    }

    [Test]
    public void Validator_RejectsEmptyOverlongAndBadLanguage()
    {
        var v = new TranslateTextQueryValidator();
        v.Validate(new TranslateTextQuery { Text = " " }).IsValid.Should().BeFalse();
        v.Validate(new TranslateTextQuery { Text = new string('a', TranslateTextQuery.MaxLength + 1) }).IsValid.Should().BeFalse();
        v.Validate(new TranslateTextQuery { Text = "ok", SourceLang = "xx_yy" }).IsValid.Should().BeFalse();
        v.Validate(new TranslateTextQuery { Text = "ok" }).IsValid.Should().BeTrue();
    }

    // ---- LibreTranslateService ----

    private class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public Func<HttpResponseMessage> Respond = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond();
        }
    }

    private static LibreTranslateService Libre(StubHandler handler, string? baseUrl = "http://lt:5000", string? apiKey = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LibreTranslate:BaseUrl"] = baseUrl, ["LibreTranslate:ApiKey"] = apiKey
        }).Build();
        return new LibreTranslateService(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()), config, NullLogger<LibreTranslateService>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Test]
    public async Task Libre_PostsToTranslate_WithShortLanguageCodes_AndCachesTheResult()
    {
        var handler = new StubHandler { Respond = () => Json("{\"translatedText\":\"Olá mundo\"}") };
        var libre = Libre(handler, "http://lt:5000/");

        var first = await libre.TranslateAsync("Hello world", "en", "pt-BR");
        var second = await libre.TranslateAsync("Hello world", "en", "pt-BR");

        first.Status.Should().Be("ok");
        first.Text.Should().Be("Olá mundo");
        second.Text.Should().Be("Olá mundo");
        handler.Request!.RequestUri!.ToString().Should().Be("http://lt:5000/translate");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("q").GetString().Should().Be("Hello world");
        body.RootElement.GetProperty("source").GetString().Should().Be("en");
        body.RootElement.GetProperty("target").GetString().Should().Be("pt");
        body.RootElement.GetProperty("format").GetString().Should().Be("text");
        body.RootElement.TryGetProperty("api_key", out _).Should().BeFalse();
    }

    [Test]
    public async Task Libre_SendsApiKeyWhenConfigured()
    {
        var handler = new StubHandler { Respond = () => Json("{\"translatedText\":\"x\"}") };
        await Libre(handler, apiKey: "secret").TranslateAsync("Hello there", "en", "vi");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("api_key").GetString().Should().Be("secret");
    }

    [Test]
    public async Task Libre_NotConfigured_OrServerError_OrBadPayload_IsUnavailable()
    {
        (await Libre(new StubHandler(), baseUrl: null).TranslateAsync("a b", "en", "vi")).Status.Should().Be("unavailable");
        (await Libre(new StubHandler { Respond = () => Json("{\"error\":\"vi is not supported\"}", HttpStatusCode.BadRequest) }).TranslateAsync("a b", "en", "vi")).Status.Should().Be("unavailable");
        (await Libre(new StubHandler { Respond = () => Json("not json") }).TranslateAsync("a b", "en", "vi")).Status.Should().Be("unavailable");
        (await Libre(new StubHandler { Respond = () => Json("{\"translatedText\":\"\"}") }).TranslateAsync("a b", "en", "vi")).Status.Should().Be("unavailable");
    }

    [Test]
    public async Task Libre_SameLanguage_IsUnavailable_WithoutCallingTheServer()
    {
        var handler = new StubHandler();
        (await Libre(handler).TranslateAsync("a b", "en", "en-GB")).Status.Should().Be("unavailable");
        handler.Request.Should().BeNull();
    }
}

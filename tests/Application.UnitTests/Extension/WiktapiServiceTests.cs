using System.Net;
using System.Text;
using CleanArchitectureBase.Infrastructure.Flashcards;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Application.UnitTests.Extension;

/// <summary>WiktAPI tự host: tra từ (định nghĩa, IPA, âm thanh) + dịch từ. JSON rút gọn từ phản hồi thật của api.wiktapi.dev.</summary>
[TestFixture]
public class WiktapiServiceTests
{
    private const string HouseDefinitions = """
        {"word":"house","edition":"en","definitions":[
          {"pos":"noun","lang_code":"en","senses":[
            {"glosses":["A structure built or serving as an abode of human beings."],
             "examples":[{"text":"The big houses […] lie for the most part in the valleys.","type":"quotation"},
                         {"text":"This is my house and my family's ancestral home.","type":"example"}]},
            {"glosses":["A container; a thing which houses another."],"examples":[]}]},
          {"pos":"verb","lang_code":"en","senses":[{"glosses":["To keep within a structure or container."],"examples":[]}]},
          {"pos":"noun","lang_code":"en","senses":[{"glosses":["A structure built or serving as an abode of human beings."],"examples":[]}]}]}
        """;

    private const string HousePronunciations = """
        {"word":"house","edition":"en","pronunciations":[{"pos":"noun","lang_code":"en","sounds":[
          {"ipa":"/haʊ̯s/","audio":null,"tags":["General-American"]},
          {"ipa":null,"audio":"En-uk-house.ogg","tags":["UK"]}]}]}
        """;

    private const string HouseTranslations = """
        {"word":"house","edition":"en","translations":[
          {"pos":"noun","lang_code":"en","translations":[
            {"lang":"Afrikaans","code":"af","sense":"human abode","word":"huis"},
            {"lang":"Vietnamese","code":"vi","sense":"human abode","word":"nhà"},
            {"lang":"Vietnamese","code":"vi","sense":"human abode","word":"nhà ở"},
            {"lang":"Vietnamese","code":"vi","sense":"debating chamber","word":"nghị viện"}]}]}
        """;

    private const string NhaDefinitionsInEnglish = """
        {"word":"nhà","edition":"en","definitions":[
          {"pos":"adj","lang_code":"vi","senses":[{"glosses":["domestic"]}]},
          {"pos":"noun","lang_code":"vi","senses":[{"glosses":["house; home; dwelling"]},{"glosses":["spouse"]}]}]}
        """;

    /// <summary>Trả lời theo đường dẫn; ghi lại các request</summary>
    private class RouteHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _route;
        public readonly List<string> Paths = new();
        public RouteHandler(Func<string, HttpResponseMessage> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            return Task.FromResult(_route(Uri.UnescapeDataString(path)));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static WiktapiService Service(RouteHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://wiktapi:3000/") },
            new MemoryCache(new MemoryCacheOptions()), NullLogger<WiktapiService>.Instance,
            Options.Create(new WiktapiSettings { BaseUrl = "http://wiktapi:3000", Editions = "en,vi" }));

    [Test]
    public async Task Lookup_MapsDefinitionsExamplesIpaAndAudio()
    {
        var handler = new RouteHandler(path => path.Contains("/definitions") ? Json(HouseDefinitions) : Json(HousePronunciations));

        var result = await Service(handler).LookupAsync("house", "en");

        result.Status.Should().Be("found");
        var entry = result.Entry!;
        entry.Phonetic.Should().Be("/haʊ̯s/");
        entry.AudioUrl.Should().Be("https://commons.wikimedia.org/wiki/Special:FilePath/En-uk-house.ogg");
        // một nghĩa cho mỗi nhóm từ loại; nhóm thứ 3 trùng (noun, cùng gloss) bị bỏ
        entry.Meanings.Select(m => (m.Pos, m.Definition)).Should().Equal(
            ("noun", "A structure built or serving as an abode of human beings."),
            ("verb", "To keep within a structure or container."));
        // ví dụ ngắn dạng "example", bỏ trích dẫn dài
        entry.Meanings[0].Examples.Should().Equal("This is my house and my family's ancestral home.");
        handler.Paths.Should().Contain(p => p.StartsWith("/v1/en/word/house/definitions?lang=en"));
    }

    [Test]
    public async Task Lookup_VietnameseWord_UsesVietnameseEdition_AndNotFoundIsCached()
    {
        var handler = new RouteHandler(_ => Json("""{"error":true,"status":404}""", HttpStatusCode.NotFound));
        var service = Service(handler);

        (await service.LookupAsync("nhà", "vi")).Status.Should().Be("not_found");
        (await service.LookupAsync("nhà", "vi")).Status.Should().Be("not_found");

        handler.Paths.Should().ContainSingle("lần hai lấy từ cache");
        Uri.UnescapeDataString(handler.Paths[0]).Should().StartWith("/v1/vi/word/nhà/definitions?lang=vi");
    }

    [Test]
    public async Task Lookup_ServerError_IsErrorAndNotCached()
    {
        var handler = new RouteHandler(_ => Json("{}", HttpStatusCode.ServiceUnavailable));
        var service = Service(handler);

        (await service.LookupAsync("house", "en")).Status.Should().Be("error");
        (await service.LookupAsync("house", "en")).Status.Should().Be("error");
        handler.Paths.Should().HaveCount(2, "lỗi tạm thời không được cache");
    }

    [Test]
    public async Task Translate_EnglishToVietnamese_UsesTranslationTable_FirstSenseOnly()
    {
        var handler = new RouteHandler(_ => Json(HouseTranslations));

        var text = await Service(handler).TranslateAsync("house", "en", "vi");

        text.Should().Be("nhà, nhà ở");
        handler.Paths.Should().ContainSingle().Which.Should().StartWith("/v1/en/word/house/translations?lang=en");
    }

    [Test]
    public async Task Translate_VietnameseToEnglish_UsesEnglishGloss_PreferringNoun()
    {
        var handler = new RouteHandler(_ => Json(NhaDefinitionsInEnglish));

        var text = await Service(handler).TranslateAsync("nhà", "vi", "en");

        text.Should().Be("house; home; dwelling");
        Uri.UnescapeDataString(handler.Paths.Single()).Should().StartWith("/v1/en/word/nhà/definitions?lang=vi");
    }

    [Test]
    public async Task Translate_UnsupportedPairOrNoMatch_ReturnsNull()
    {
        var handler = new RouteHandler(_ => Json(HouseTranslations));
        var service = Service(handler);

        (await service.TranslateAsync("nhà", "vi", "ja")).Should().BeNull("không có dữ liệu cho cặp này");
        handler.Paths.Should().BeEmpty();
        (await service.TranslateAsync("house", "en", "ko")).Should().BeNull("bảng dịch không có tiếng Hàn");
        (await service.TranslateAsync("house", "en", "en")).Should().BeNull();
    }

    private class FakeMachine : CleanArchitectureBase.Application.Common.Interfaces.ISentenceTranslationService
    {
        public int Calls;
        public string? Reply;
        public Task<CleanArchitectureBase.Application.Common.Interfaces.SentenceTranslationResult> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Reply == null
                ? new CleanArchitectureBase.Application.Common.Interfaces.SentenceTranslationResult { Status = "unavailable" }
                : new CleanArchitectureBase.Application.Common.Interfaces.SentenceTranslationResult { Status = "ok", Text = Reply });
        }
    }

    [Test]
    public async Task WordTranslation_UsesWiktionaryFirst_ThenLibreTranslate()
    {
        var machine = new FakeMachine { Reply = "sự tình cờ may mắn" };
        var wiktionaryHas = new WordTranslationService(Service(new RouteHandler(_ => Json(HouseTranslations))), machine);
        (await wiktionaryHas.TranslateAsync("house", "en", "vi")).Should().Be("nhà, nhà ở");
        machine.Calls.Should().Be(0, "Wiktionary có bản dịch thì không gọi máy dịch");

        var noTranslation = """{"word":"serendipity","edition":"en","translations":[]}""";
        // từ có trong từ điển (definitions) nhưng không có bản dịch tiếng Việt
        var wiktionaryMissing = new WordTranslationService(Service(new RouteHandler(path =>
            path.Contains("/translations") ? Json(noTranslation) : path.Contains("/definitions") ? Json(HouseDefinitions) : Json(HousePronunciations))), machine);
        (await wiktionaryMissing.TranslateAsync("serendipity", "en", "vi")).Should().Be("sự tình cờ may mắn");
        machine.Calls.Should().Be(1);

        // từ không có trong từ điển: không hỏi máy dịch (tránh "dịch" chuỗi vô nghĩa)
        var notAWord = new WordTranslationService(Service(new RouteHandler(path =>
            path.Contains("/translations") ? Json(noTranslation) : Json("""{"error":true}""", HttpStatusCode.NotFound))), machine);
        (await notAWord.TranslateAsync("zzqqxxw", "en", "vi")).Should().BeNull();
        machine.Calls.Should().Be(1, "không gọi máy dịch cho từ không tồn tại");
    }
}

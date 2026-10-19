using System.Net;
using System.Text;
using CleanArchitectureBase.Infrastructure.Flashcards;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleanArchitectureBase.Application.UnitTests.Extension;

[TestFixture]
public class FreeDictionaryServiceTests
{
    private const string Entry =
        "[{\"word\":\"create\",\"phonetic\":\"/x/\",\"phonetics\":[],\"meanings\":[{\"partOfSpeech\":\"verb\",\"definitions\":[{\"definition\":\"to make\"}]}]}]";

    private class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _replies;
        public int Calls;
        public SequenceHandler(params Func<HttpResponseMessage>[] replies) => _replies = new Queue<Func<HttpResponseMessage>>(replies);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var next = _replies.Count > 1 ? _replies.Dequeue() : _replies.Peek();
            return Task.FromResult(next());
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode code, string body = "") =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static FreeDictionaryService Service(SequenceHandler handler) =>
        new(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()), NullLogger<FreeDictionaryService>.Instance);

    [Test]
    public async Task TransientServerError_IsRetried_ThenFound()
    {
        var handler = new SequenceHandler(() => Status((HttpStatusCode)530, "error code: 1033"), () => Status(HttpStatusCode.OK, Entry));

        var result = await Service(handler).LookupAsync("create", "en");

        result.Status.Should().Be("found");
        result.Entry!.Meanings[0].Definition.Should().Be("to make");
        handler.Calls.Should().Be(2);
    }

    [Test]
    public async Task PersistentServerError_GivesUpAfterTheMaxAttempts_AsError_AndIsNotCached()
    {
        var handler = new SequenceHandler(() => Status((HttpStatusCode)530, "error code: 1033"));
        var service = Service(handler);

        (await service.LookupAsync("create", "en")).Status.Should().Be("error");
        handler.Calls.Should().Be(3);

        // lỗi tạm thời không được cache: lần sau gọi lại
        (await service.LookupAsync("create", "en")).Status.Should().Be("error");
        handler.Calls.Should().Be(6);
    }

    [Test]
    public async Task NotFound_IsNotRetried_AndIsCached()
    {
        var handler = new SequenceHandler(() => Status(HttpStatusCode.NotFound, "{\"title\":\"No Definitions Found\"}"));
        var service = Service(handler);

        (await service.LookupAsync("zzzz", "en")).Status.Should().Be("not_found");
        (await service.LookupAsync("zzzz", "en")).Status.Should().Be("not_found");
        handler.Calls.Should().Be(1);
    }

    [Test]
    public async Task ClientError_IsNotRetried()
    {
        var handler = new SequenceHandler(() => Status(HttpStatusCode.TooManyRequests));
        (await Service(handler).LookupAsync("create", "en")).Status.Should().Be("error");
        handler.Calls.Should().Be(1);
    }
}

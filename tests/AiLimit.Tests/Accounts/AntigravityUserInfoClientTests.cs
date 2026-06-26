using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AntigravityUserInfoClientTests
{
    [Fact]
    public async Task FetchEmailReturnsValueOnSuccess()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"id":"123","email":"alice@example.com","verified_email":true,"name":"Alice"}""")
        });
        using var http = new HttpClient(handler);
        var client = new AntigravityUserInfoClient(http);

        var email = await client.FetchEmailAsync("ya29.test", CancellationToken.None);

        Assert.Equal("alice@example.com", email);
    }

    [Fact]
    public async Task FetchEmailReturnsNullOnNon200()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        var client = new AntigravityUserInfoClient(http);

        var email = await client.FetchEmailAsync("ya29.bad", CancellationToken.None);

        Assert.Null(email);
    }

    [Fact]
    public async Task FetchEmailReturnsNullOnMissingEmailField()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"123","verified_email":true}""")
        });
        using var http = new HttpClient(handler);
        var client = new AntigravityUserInfoClient(http);

        var email = await client.FetchEmailAsync("ya29.test", CancellationToken.None);

        Assert.Null(email);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_factory(request));
    }
}

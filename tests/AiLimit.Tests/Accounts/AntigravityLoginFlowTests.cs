using System.Net;
using System.Net.Http;
using System.Text;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class AntigravityLoginFlowTests
{
    private sealed class FakeListener : ILoopbackOAuthListener
    {
        private readonly LoopbackCallback _cb;
        public FakeListener(LoopbackCallback cb) => _cb = cb;
        public string RedirectUri => "http://127.0.0.1:5999/oauth-callback";
        public Task<LoopbackCallback> WaitForCallbackAsync(CancellationToken ct) => Task.FromResult(_cb);
        public void Dispose() { }
    }

    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public CannedHandler(string json, HttpStatusCode status = HttpStatusCode.OK) { _json = json; _status = status; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"));
    }

    private sealed class CancelableListener : ILoopbackOAuthListener
    {
        public string RedirectUri => "http://127.0.0.1:5999/oauth-callback";
        public async Task<LoopbackCallback> WaitForCallbackAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new LoopbackCallback("", "");
        }
        public void Dispose() { }
    }

    private static AntigravityLoginFlow Flow(
        LoopbackCallback cb,
        string tokenJson,
        Func<string, CancellationToken, Task<string?>> userInfo,
        List<(string email, string refresh)> sink,
        HttpStatusCode tokenStatus = HttpStatusCode.OK,
        AntigravityOAuthClientConfig? client = null)
        => new AntigravityLoginFlow(
            activeClient: () => client ?? new AntigravityOAuthClientConfig("cid", "csecret"),
            listenerFactory: () => new FakeListener(cb),
            openBrowser: _ => { },
            httpClient: new HttpClient(new CannedHandler(tokenJson, tokenStatus)),
            fetchEmail: userInfo,
            addAccount: (email, refresh) => { sink.Add((email, refresh)); return false; },
            stateFactory: () => "STATE",
            timeout: TimeSpan.FromSeconds(5));

    [Fact]
    public async Task HappyPath_StoresAccount()
    {
        var sink = new List<(string, string)>();
        var flow = Flow(new LoopbackCallback("code1", "STATE"),
            "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}",
            (_, _) => Task.FromResult<string?>("alice@example.com"), sink);
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.Added, result.Outcome);
        Assert.Equal("alice@example.com", result.Email);
        Assert.Single(sink);
        Assert.Equal("rt", sink[0].Item2);
    }

    [Fact]
    public async Task StateMismatch_Fails()
    {
        var flow = Flow(new LoopbackCallback("code1", "WRONG"),
            "{}", (_, _) => Task.FromResult<string?>("x"), new());
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.AuthFailed, result.Outcome);
    }

    [Fact]
    public async Task TokenExchangeError_Fails()
    {
        var flow = Flow(new LoopbackCallback("c", "STATE"),
            "{\"error\":\"invalid_client\"}", (_, _) => Task.FromResult<string?>("x"), new(),
            tokenStatus: HttpStatusCode.BadRequest);
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.ExchangeFailed, result.Outcome);
    }

    [Fact]
    public async Task NoActiveClient_Fails()
    {
        var flow = Flow(new LoopbackCallback("c", "STATE"),
            "{}", (_, _) => Task.FromResult<string?>("x"), new(),
            client: new AntigravityOAuthClientConfig(null, null));
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.NoActiveClient, result.Outcome);
    }

    [Fact]
    public async Task UserInfoFailure_WritesNoAccount()
    {
        var sink = new List<(string, string)>();
        var flow = Flow(new LoopbackCallback("c", "STATE"),
            "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}",
            (_, _) => Task.FromResult<string?>(null), sink);
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.UserInfoFailed, result.Outcome);
        Assert.Empty(sink);
    }

    [Fact]
    public async Task CallbackDeadlineDoesNotCancelAccountLookupAfterCallbackArrives()
    {
        var sink = new List<(string, string)>();
        var flow = new AntigravityLoginFlow(
            activeClient: () => new AntigravityOAuthClientConfig("cid", "csecret"),
            listenerFactory: () => new FakeListener(new LoopbackCallback("code1", "STATE")),
            openBrowser: _ => { },
            httpClient: new HttpClient(new CannedHandler(
                "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}")),
            fetchEmail: async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                return "alice@example.com";
            },
            addAccount: (email, refresh) => { sink.Add((email, refresh)); return false; },
            stateFactory: () => "STATE",
            timeout: TimeSpan.FromMilliseconds(25));

        var result = await flow.SignInAsync(CancellationToken.None);

        Assert.Equal(AntigravityLoginOutcome.Added, result.Outcome);
        Assert.Single(sink);
    }

    [Fact]
    public async Task TokenExchangeError_SurfacesErrorBody()
    {
        var flow = Flow(new LoopbackCallback("c", "STATE"),
            "{\"error\":\"invalid_grant\",\"error_description\":\"Bad Request\"}",
            (_, _) => Task.FromResult<string?>("x"), new(),
            tokenStatus: HttpStatusCode.BadRequest);
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.ExchangeFailed, result.Outcome);
        Assert.Contains("invalid_grant", result.ErrorMessage);
    }

    [Fact]
    public async Task MissingRefreshToken_MapsToExchangeFailedWithReason()
    {
        var flow = Flow(new LoopbackCallback("c", "STATE"),
            "{\"access_token\":\"at\",\"expires_in\":3600}",
            (_, _) => Task.FromResult<string?>("x"), new());
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.ExchangeFailed, result.Outcome);
        Assert.Contains("refresh", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserOpenFailure_MapsToAuthFailed()
    {
        var flow = new AntigravityLoginFlow(
            activeClient: () => new AntigravityOAuthClientConfig("cid", "csecret"),
            listenerFactory: () => new FakeListener(new LoopbackCallback("c", "STATE")),
            openBrowser: _ => throw new InvalidOperationException("no browser"),
            httpClient: new HttpClient(new CannedHandler("{}")),
            fetchEmail: (_, _) => Task.FromResult<string?>("x"),
            addAccount: (_, _) => false,
            stateFactory: () => "STATE");
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.AuthFailed, result.Outcome);
        Assert.Contains("browser", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListenerStartFailure_MapsToAuthFailed()
    {
        var flow = new AntigravityLoginFlow(
            activeClient: () => new AntigravityOAuthClientConfig("cid", "csecret"),
            listenerFactory: () => throw new InvalidOperationException("port in use"),
            openBrowser: _ => { },
            httpClient: new HttpClient(new CannedHandler("{}")),
            fetchEmail: (_, _) => Task.FromResult<string?>("x"),
            addAccount: (_, _) => false,
            stateFactory: () => "STATE");
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.AuthFailed, result.Outcome);
        Assert.Contains("listener", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExchangeTimeout_MapsToExchangeFailed_NoExceptionEscapes()
    {
        var flow = new AntigravityLoginFlow(
            activeClient: () => new AntigravityOAuthClientConfig("cid", "csecret"),
            listenerFactory: () => new FakeListener(new LoopbackCallback("code1", "STATE")),
            openBrowser: _ => { },
            httpClient: new HttpClient(new ThrowingHandler()),
            fetchEmail: (_, _) => Task.FromResult<string?>("x"),
            addAccount: (_, _) => false,
            stateFactory: () => "STATE");
        var result = await flow.SignInAsync(CancellationToken.None);
        Assert.Equal(AntigravityLoginOutcome.ExchangeFailed, result.Outcome);
    }

    [Fact]
    public async Task CallerCancellation_MapsToCancelled()
    {
        using var cts = new CancellationTokenSource();
        var flow = new AntigravityLoginFlow(
            activeClient: () => new AntigravityOAuthClientConfig("cid", "csecret"),
            listenerFactory: () => new CancelableListener(),
            openBrowser: _ => { },
            httpClient: new HttpClient(new CannedHandler("{}")),
            fetchEmail: (_, _) => Task.FromResult<string?>("x"),
            addAccount: (_, _) => false,
            stateFactory: () => "STATE");
        var task = flow.SignInAsync(cts.Token);
        cts.Cancel();
        var result = await task;
        Assert.Equal(AntigravityLoginOutcome.Cancelled, result.Outcome);
    }
}

using System.Net;
using System.Net.Http;
using System.Text;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class ClaudeLoginFlowTests
{
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public CannedHandler(string json, HttpStatusCode status = HttpStatusCode.OK) { _json = json; _status = status; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
    }

    private static ClaudeLoginFlow Flow(
        HttpStatusCode tokenStatus, string tokenJson, List<ClaudeCredential> sink,
        Action<string>? openBrowser = null, Func<ClaudeCredential, string>? write = null)
        => new ClaudeLoginFlow(
            openBrowser: openBrowser ?? (_ => { }),
            credentials: new ClaudeOAuthCredentialStore(new HttpClient(new CannedHandler(tokenJson, tokenStatus))),
            writeProfile: write ?? (c => { sink.Add(c); return @"C:\home\.claude2\.credentials.json"; }),
            stateFactory: () => "STATE");

    [Fact]
    public void BeginSignIn_ReturnsAuthUrl_AndReportsBrowserOpened()
    {
        var flow = Flow(HttpStatusCode.OK, "{}", new());
        var begin = flow.BeginSignIn();
        Assert.StartsWith(ClaudeOAuthCredentialStore.AuthorizeEndpoint, begin.AuthUrl);
        Assert.Contains("code_challenge_method=S256", begin.AuthUrl);
        Assert.True(begin.BrowserOpened);
    }

    [Fact]
    public void BeginSignIn_BrowserThrows_StillReturnsUrlWithBrowserNotOpened()
    {
        var flow = Flow(HttpStatusCode.OK, "{}", new(), openBrowser: _ => throw new InvalidOperationException("no browser"));
        var begin = flow.BeginSignIn();
        Assert.StartsWith(ClaudeOAuthCredentialStore.AuthorizeEndpoint, begin.AuthUrl);
        Assert.False(begin.BrowserOpened);
    }

    [Fact]
    public async Task CompleteSignIn_HappyPath_WritesProfileAndReturnsAdded()
    {
        var sink = new List<ClaudeCredential>();
        var flow = Flow(HttpStatusCode.OK, "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}", sink);
        flow.BeginSignIn();
        var result = await flow.CompleteSignInAsync("auth-code#STATE", CancellationToken.None);
        Assert.Equal(ClaudeLoginOutcome.Added, result.Outcome);
        Assert.Single(sink);
        Assert.Equal("at", sink[0].AccessToken);
    }

    [Fact]
    public async Task CompleteSignIn_StateMismatch_Fails()
    {
        var flow = Flow(HttpStatusCode.OK, "{\"access_token\":\"at\"}", new());
        flow.BeginSignIn();
        var result = await flow.CompleteSignInAsync("auth-code#WRONG", CancellationToken.None);
        Assert.Equal(ClaudeLoginOutcome.AuthFailed, result.Outcome);
    }

    [Fact]
    public async Task CompleteSignIn_EmptyCode_Fails()
    {
        var flow = Flow(HttpStatusCode.OK, "{}", new());
        flow.BeginSignIn();
        var result = await flow.CompleteSignInAsync("   ", CancellationToken.None);
        Assert.Equal(ClaudeLoginOutcome.AuthFailed, result.Outcome);
    }

    [Fact]
    public async Task CompleteSignIn_ExchangeError_SurfacesMessage()
    {
        var flow = Flow(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", new());
        flow.BeginSignIn();
        var result = await flow.CompleteSignInAsync("code#STATE", CancellationToken.None);
        Assert.Equal(ClaudeLoginOutcome.ExchangeFailed, result.Outcome);
        Assert.Contains("invalid_grant", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteSignIn_WriteThrows_ReturnsWriteFailed()
    {
        var flow = Flow(HttpStatusCode.OK, "{\"access_token\":\"at\"}", new(),
            write: _ => throw new IOException("disk full"));
        flow.BeginSignIn();
        var result = await flow.CompleteSignInAsync("code#STATE", CancellationToken.None);
        Assert.Equal(ClaudeLoginOutcome.WriteFailed, result.Outcome);
    }

    [Fact]
    public async Task CompleteSignIn_BeforeBegin_Fails()
    {
        var flow = Flow(HttpStatusCode.OK, "{}", new());
        var result = await flow.CompleteSignInAsync("code#STATE", CancellationToken.None);
        Assert.Equal(ClaudeLoginOutcome.AuthFailed, result.Outcome);
    }
}

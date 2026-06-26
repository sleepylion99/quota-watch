using System.Net;
using System.Net.Http;
using System.Text;
using AiLimit.Core.Providers;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class ClaudeOAuthCredentialStoreTests
{
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public CannedHandler(string json, HttpStatusCode status = HttpStatusCode.OK) { _json = json; _status = status; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task ExchangeCodeAsync_Success_ReturnsTokensAndExpiry()
    {
        var store = new ClaudeOAuthCredentialStore(new HttpClient(new CannedHandler(
            "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}")));

        var result = await store.ExchangeCodeAsync("code", "state", "verifier", "https://redirect", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("at", result.AccessToken);
        Assert.Equal("rt", result.RefreshToken);
        Assert.NotNull(result.ExpiresAtUnixMs);
    }

    [Fact]
    public async Task ExchangeCodeAsync_HttpError_SurfacesErrorBody()
    {
        var store = new ClaudeOAuthCredentialStore(new HttpClient(new CannedHandler(
            "{\"error\":\"invalid_grant\",\"error_description\":\"bad code\"}", HttpStatusCode.BadRequest)));

        var result = await store.ExchangeCodeAsync("code", "state", "verifier", "https://redirect", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("invalid_grant", result.ErrorMessage);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ObjectShapedError_ReturnsFailureWithoutThrowing()
    {
        // Anthropic returns errors as a nested object, not a string.
        var store = new ClaudeOAuthCredentialStore(new HttpClient(new CannedHandler(
            "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"grant_type unsupported\"}}",
            HttpStatusCode.BadRequest)));

        var result = await store.ExchangeCodeAsync("code", "state", "verifier", "https://redirect", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("grant_type unsupported", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchAccountAsync_ParsesEmailAndPlan()
    {
        var store = new ClaudeOAuthCredentialStore(new HttpClient(new CannedHandler(
            "{\"account\":{\"email\":\"a@b.com\",\"display_name\":\"Al\",\"has_claude_max\":true,\"has_claude_pro\":false}}")));

        var account = await store.FetchAccountAsync("token", CancellationToken.None);

        Assert.NotNull(account);
        Assert.Equal("a@b.com", account!.Email);
        Assert.Equal("Al", account.DisplayName);
        Assert.True(account.HasMax);
        Assert.False(account.HasPro);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_Success_ReturnsNewToken()
    {
        var store = new ClaudeOAuthCredentialStore(new HttpClient(new CannedHandler(
            "{\"access_token\":\"at2\",\"refresh_token\":\"rt2\",\"expires_in\":60}")));

        var result = await store.RefreshAccessTokenAsync("old-refresh", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("at2", result.AccessToken);
    }
}

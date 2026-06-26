using System.Net;
using System.Net.Http;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class LoopbackOAuthListenerTests
{
    [Fact]
    public async Task ReceivesCodeAndState()
    {
        using var listener = new LoopbackOAuthListener();
        Assert.StartsWith("http://127.0.0.1:", listener.RedirectUri);

        var wait = listener.WaitForCallbackAsync(CancellationToken.None);
        using (var http = new HttpClient())
        {
            await http.GetAsync($"{listener.RedirectUri}?code=abc&state=xyz");
        }

        var cb = await wait;
        Assert.Equal("abc", cb.Code);
        Assert.Equal("xyz", cb.State);
    }

    [Fact]
    public async Task SurfacesErrorParam()
    {
        using var listener = new LoopbackOAuthListener();
        var wait = listener.WaitForCallbackAsync(CancellationToken.None);
        using (var http = new HttpClient())
        {
            await http.GetAsync($"{listener.RedirectUri}?error=access_denied");
        }
        await Assert.ThrowsAsync<OAuthCallbackException>(async () => await wait);
    }

    [Fact]
    public async Task IgnoresNonCallbackRequestsUntilRealCallback()
    {
        using var listener = new LoopbackOAuthListener();
        var wait = listener.WaitForCallbackAsync(CancellationToken.None);
        using (var http = new HttpClient())
        {
            var baseUri = listener.RedirectUri.Replace("/oauth-callback", "");
            var favicon = await http.GetAsync($"{baseUri}/favicon.ico");
            Assert.Equal(HttpStatusCode.NoContent, favicon.StatusCode);
            await http.GetAsync($"{listener.RedirectUri}?code=abc&state=xyz");
        }

        var cb = await wait;
        Assert.Equal("abc", cb.Code);
        Assert.Equal("xyz", cb.State);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceled()
    {
        using var listener = new LoopbackOAuthListener();
        using var cts = new CancellationTokenSource();
        var wait = listener.WaitForCallbackAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);
    }
}

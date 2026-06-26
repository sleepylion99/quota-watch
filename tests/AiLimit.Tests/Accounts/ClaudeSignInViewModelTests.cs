using System.Net;
using System.Net.Http;
using System.Text;
using AiLimit.App.ViewModels.Accounts;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class ClaudeSignInViewModelTests
{
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public CannedHandler(string json, HttpStatusCode status = HttpStatusCode.OK) { _json = json; _status = status; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
    }

    private static ClaudeSignInViewModel ViewModel(List<ClaudeCredential> sink, HttpStatusCode status, string json)
        => new ClaudeSignInViewModel(new ClaudeLoginFlow(
            openBrowser: _ => { },
            credentials: new ClaudeOAuthCredentialStore(new HttpClient(new CannedHandler(json, status))),
            writeProfile: c => { sink.Add(c); return "path"; },
            stateFactory: () => "STATE"));

    [Fact]
    public void Begin_SetsAwaitingCodeAndAuthUrl()
    {
        var vm = ViewModel(new(), HttpStatusCode.OK, "{}");
        vm.SetLanguage(AppLanguage.English);

        vm.Begin();

        Assert.True(vm.AwaitingCode);
        Assert.StartsWith(ClaudeOAuthCredentialStore.AuthorizeEndpoint, vm.AuthUrl);
        Assert.Equal("Approve in the browser, then paste the code here.", vm.StatusMessage);
    }

    [Fact]
    public void Begin_UsesConfiguredLanguage()
    {
        var vm = ViewModel(new(), HttpStatusCode.OK, "{}");
        vm.SetLanguage(AppLanguage.Korean);

        vm.Begin();

        Assert.Equal("브라우저에서 승인한 뒤 코드를 붙여넣으세요.", vm.StatusMessage);
    }

    [Fact]
    public async Task Complete_HappyPath_WritesAndClearsAwaiting()
    {
        var sink = new List<ClaudeCredential>();
        var vm = ViewModel(sink, HttpStatusCode.OK, "{\"access_token\":\"at\",\"expires_in\":3600}");
        vm.Begin();
        vm.PastedCode = "code#STATE";
        await vm.CompleteAsync(CancellationToken.None);
        Assert.Single(sink);
        Assert.False(vm.AwaitingCode);
    }

    [Fact]
    public async Task Complete_Error_KeepsAwaitingAndShowsMessage()
    {
        var vm = ViewModel(new(), HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        vm.Begin();
        vm.PastedCode = "code#STATE";
        await vm.CompleteAsync(CancellationToken.None);
        Assert.True(vm.AwaitingCode);
        Assert.Contains("invalid_grant", vm.StatusMessage);
    }
}

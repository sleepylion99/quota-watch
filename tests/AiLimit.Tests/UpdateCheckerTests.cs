using System.Net;
using System.Net.Http;
using AiLimit.App.Services;

namespace AiLimit.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("v0.1.5", "0.1.4", true)]
    [InlineData("0.1.4", "0.1.4", false)]
    [InlineData("v0.1.4+build.1", "0.1.3", true)]
    public void IsNewerVersionComparesReleaseTags(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewerVersion(candidate, current));
    }

    [Fact]
    public async Task CheckAsyncReadsLatestGitHubRelease()
    {
        var handler = new StaticJsonHandler(
            """
            {
              "tag_name": "v0.1.5",
              "html_url": "https://github.com/sleepylion99/quota-watch/releases/tag/v0.1.5"
            }
            """);
        var checker = new UpdateChecker(new HttpClient(handler), () => "0.1.4");

        var result = await checker.CheckAsync(CancellationToken.None);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.1.4", result.CurrentVersion);
        Assert.Equal("0.1.5", result.LatestVersion);
        Assert.Equal("https://github.com/sleepylion99/quota-watch/releases/tag/v0.1.5", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckAsyncFallsBackToLatestReleasePageWhenGitHubApiIsHidden()
    {
        var handler = new ReleasePageFallbackHandler();
        var checker = new UpdateChecker(new HttpClient(handler), () => "0.1.4");

        var result = await checker.CheckAsync(CancellationToken.None);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.1.5", result.LatestVersion);
        Assert.Equal("https://github.com/sleepylion99/quota-watch/releases/tag/v0.1.5", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckAsyncExplainsReleaseVisibilityWhenNoUpdateSourceIsReadable()
    {
        var handler = new HiddenReleaseHandler();
        var checker = new UpdateChecker(new HttpClient(handler), () => "0.1.5");

        var exception = await Assert.ThrowsAsync<UpdateCheckException>(
            () => checker.CheckAsync(CancellationToken.None));

        Assert.Contains("GitHub 릴리즈 정보를 볼 수 없습니다.", exception.UserMessage);
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.github.com/repos/sleepylion99/quota-watch/releases/latest", request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private sealed class ReleasePageFallbackHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.ToString() == "https://api.github.com/repos/sleepylion99/quota-watch/releases/latest")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            Assert.Equal("https://github.com/sleepylion99/quota-watch/releases/latest", request.RequestUri?.ToString());
            var finalRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "https://github.com/sleepylion99/quota-watch/releases/tag/v0.1.5");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = finalRequest
            });
        }
    }

    private sealed class HiddenReleaseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            });
        }
    }
}

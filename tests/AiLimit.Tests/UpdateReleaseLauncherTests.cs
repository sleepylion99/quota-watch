using System.Diagnostics;
using AiLimit.App.Services;

namespace AiLimit.Tests;

public sealed class UpdateReleaseLauncherTests
{
    [Fact]
    public void OpenUsesValidatedGitHubReleaseUrl()
    {
        ProcessStartInfo? captured = null;
        var launcher = new UpdateReleaseLauncher(startInfo => captured = startInfo);

        launcher.Open("https://github.com/sleepylion99/quota-watch/releases/tag/v0.1.5");

        Assert.NotNull(captured);
        Assert.Equal(
            "https://github.com/sleepylion99/quota-watch/releases/tag/v0.1.5",
            captured!.FileName);
        Assert.True(captured.UseShellExecute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://github.com/sleepylion99/quota-watch/releases/tag/v0.1.5")]
    [InlineData("https://example.com/releases/tag/v0.1.5")]
    [InlineData("https://github.com/another-owner/another-repo/releases/tag/v0.1.5")]
    [InlineData("https://github.com/sleepylion99/quota-watch/issues/1")]
    public void OpenFallsBackForMissingOrUnsafeUrl(string? releaseUrl)
    {
        ProcessStartInfo? captured = null;
        var launcher = new UpdateReleaseLauncher(startInfo => captured = startInfo);

        launcher.Open(releaseUrl);

        Assert.NotNull(captured);
        Assert.Equal(UpdateReleaseLauncher.LatestReleasePageUrl, captured!.FileName);
    }

    [Fact]
    public void OpenPropagatesBrowserLaunchFailure()
    {
        var launcher = new UpdateReleaseLauncher(_ => throw new InvalidOperationException("launch failed"));

        var exception = Assert.Throws<InvalidOperationException>(() => launcher.Open(null));

        Assert.Equal("launch failed", exception.Message);
    }
}

using AiLimit.Core.Domain;
using AiLimit.Core.Providers;

namespace AiLimit.Tests;

public sealed class ClaudeUsageProviderTests
{
    [Fact]
    public async Task RefreshAsyncMapsOAuthUsageToUnifiedClaudeSnapshot()
    {
        var client = new StubClaudeUsageClient(new ClaudeOAuthUsageResponse(
            new ClaudeOAuthUsageWindow(42, "2026-05-30T12:08:09Z"),
            new ClaudeOAuthUsageWindow(64, "2026-06-02T00:33:16Z"),
            new ClaudeOAuthUsageWindow(35, "2026-06-02T00:33:16Z"),
            new ClaudeOAuthUsageWindow(81, "2026-06-02T00:33:16Z"),
            AccountKey: "claude:token-sha256:test"));
        var provider = new ClaudeUsageProvider("claude", "Claude Code", client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal("claude", snapshot.ProviderId);
        Assert.Equal("Claude Code", snapshot.DisplayName);
        Assert.Equal("claude:token-sha256:test", snapshot.AccountKey);
        Assert.Equal(UsageSource.Agent, snapshot.Source);
        Assert.Equal(UsageStatus.Fresh, snapshot.Status);
        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("five-hour", window.Id);
                Assert.Equal("Current session", window.Label);
                Assert.Equal(42, window.PercentRemaining);
                Assert.True(window.IsUsedPercent);
                Assert.Equal(DateTimeOffset.Parse("2026-05-30T12:08:09Z"), window.ResetAt);
            },
            window =>
            {
                Assert.Equal("weekly", window.Id);
                Assert.Equal("Current week (all models)", window.Label);
                Assert.Equal(64, window.PercentRemaining);
                Assert.True(window.IsUsedPercent);
                Assert.Equal(DateTimeOffset.Parse("2026-06-02T00:33:16Z"), window.ResetAt);
            },
            window =>
            {
                Assert.Equal("weekly-sonnet", window.Id);
                Assert.Equal("Current week (Sonnet only)", window.Label);
                Assert.Equal(35, window.PercentRemaining);
                Assert.True(window.IsUsedPercent);
                Assert.Equal(DateTimeOffset.Parse("2026-06-02T00:33:16Z"), window.ResetAt);
            },
            window =>
            {
                Assert.Equal("weekly-opus", window.Id);
                Assert.Equal("Current week (Opus only)", window.Label);
                Assert.Equal(81, window.PercentRemaining);
                Assert.True(window.IsUsedPercent);
                Assert.Equal(DateTimeOffset.Parse("2026-06-02T00:33:16Z"), window.ResetAt);
            });
    }

    [Fact]
    public async Task RefreshAsyncMapsOAuthUsageToOpusSpecificWeeklyWindow()
    {
        var client = new StubClaudeUsageClient(new ClaudeOAuthUsageResponse(
            new ClaudeOAuthUsageWindow(20, null),
            new ClaudeOAuthUsageWindow(60, null),
            new ClaudeOAuthUsageWindow(35, null),
            new ClaudeOAuthUsageWindow(81, null)));
        var provider = new ClaudeUsageProvider("claude-opus", "Claude Opus", client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window => Assert.Equal(20, window.PercentRemaining),
            window => Assert.Equal(81, window.PercentRemaining));
    }

    [Fact]
    public async Task RefreshAsyncDoesNotDuplicateAccountLevelExtraWindowsOnModelCards()
    {
        var client = new StubClaudeUsageClient(new ClaudeOAuthUsageResponse(
            new ClaudeOAuthUsageWindow(20, null),
            new ClaudeOAuthUsageWindow(60, null),
            new ClaudeOAuthUsageWindow(35, null),
            new ClaudeOAuthUsageWindow(81, null),
            new ClaudeOAuthUsageWindow(10, "2026-06-03T00:00:00Z"),
            new ClaudeOAuthUsageWindow(25, "2026-06-04T00:00:00Z")));
        var provider = new ClaudeUsageProvider("claude-sonnet", "Claude Sonnet", client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.DoesNotContain(snapshot.Windows, window => window.Id == "weekly-routines");
        Assert.DoesNotContain(snapshot.Windows, window => window.Id == "weekly-cowork");
        Assert.Collection(
            snapshot.Windows,
            window => Assert.Equal("five-hour", window.Id),
            window => Assert.Equal("weekly", window.Id));
    }

    [Fact]
    public async Task RefreshAsyncShowsAllReturnedClaudeRowsOnUnifiedCard()
    {
        var client = new StubClaudeUsageClient(new ClaudeOAuthUsageResponse(
            new ClaudeOAuthUsageWindow(20, null),
            new ClaudeOAuthUsageWindow(60, null),
            new ClaudeOAuthUsageWindow(35, null),
            new ClaudeOAuthUsageWindow(81, null),
            new ClaudeOAuthUsageWindow(10, "2026-06-03T00:00:00Z"),
            new ClaudeOAuthUsageWindow(25, "2026-06-04T00:00:00Z")));
        var provider = new ClaudeUsageProvider("claude", "Claude Code", client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window => Assert.Equal("five-hour", window.Id),
            window => Assert.Equal("weekly", window.Id),
            window => Assert.Equal("weekly-sonnet", window.Id),
            window => Assert.Equal("weekly-opus", window.Id),
            window => Assert.Equal("weekly-routines", window.Id),
            window => Assert.Equal("weekly-cowork", window.Id));
    }

    [Fact]
    public async Task RefreshAsyncOmitsAllModelsWeeklyWhenApiDoesNotReturnIt()
    {
        var client = new StubClaudeUsageClient(new ClaudeOAuthUsageResponse(
            new ClaudeOAuthUsageWindow(20, null),
            null,
            new ClaudeOAuthUsageWindow(35, null),
            new ClaudeOAuthUsageWindow(81, null)));
        var provider = new ClaudeUsageProvider("claude", "Claude Code", client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window => Assert.Equal("five-hour", window.Id),
            window =>
            {
                Assert.Equal("weekly-sonnet", window.Id);
                Assert.Equal("Current week (Sonnet only)", window.Label);
            },
            window =>
            {
                Assert.Equal("weekly-opus", window.Id);
                Assert.Equal("Current week (Opus only)", window.Label);
            });
    }

    private sealed class StubClaudeUsageClient(ClaudeOAuthUsageResponse usage) : IClaudeUsageClient
    {
        public Task<ClaudeOAuthUsageResponse> ReadUsageAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(usage);
        }
    }
}

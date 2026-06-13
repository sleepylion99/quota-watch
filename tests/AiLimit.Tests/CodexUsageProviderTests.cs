using AiLimit.Core.Domain;
using AiLimit.Core.Providers;

namespace AiLimit.Tests;

public sealed class CodexUsageProviderTests
{
    [Fact]
    public async Task RefreshAsyncMapsRpcRateLimitsToUsageSnapshot()
    {
        var client = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(44, 300, 1780142889),
            new CodexRpcRateLimitWindow(31, 10080, 1780360396),
            "plus",
            AccountKey: "codex:token-sha256:test"));
        var provider = new CodexUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal("codex", snapshot.ProviderId);
        Assert.Equal("ChatGPT Codex", snapshot.DisplayName);
        Assert.Equal("codex:token-sha256:test", snapshot.AccountKey);
        Assert.Equal(UsageSource.Agent, snapshot.Source);
        Assert.Equal(UsageStatus.Fresh, snapshot.Status);
        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("five-hour", window.Id);
                Assert.Equal(56, window.PercentRemaining);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1780142889), window.ResetAt);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("weekly", window.Id);
                Assert.Equal(69, window.PercentRemaining);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1780360396), window.ResetAt);
                Assert.Equal("high", window.Confidence);
            });
    }

    [Fact]
    public async Task RefreshAsyncClampsRemainingPercent()
    {
        var client = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(104, 300, null),
            new CodexRpcRateLimitWindow(-2, 10080, null),
            "plus"));
        var provider = new CodexUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window => Assert.Equal(0, window.PercentRemaining),
            window => Assert.Equal(100, window.PercentRemaining));
    }

    [Fact]
    public async Task RefreshAsyncUsesPrimaryAndSecondaryLabelsWhenDurationIsMissing()
    {
        var client = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(99, null, 1780142889),
            new CodexRpcRateLimitWindow(86, null, 1780360396),
            "pro"));
        var provider = new CodexUsageProvider(client, CodexUsageMode.ProDetailed);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("five-hour", window.Id);
                Assert.Equal("5h limit", window.Label);
                Assert.Equal(1, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("weekly", window.Id);
                Assert.Equal("Weekly limit", window.Label);
                Assert.Equal(14, window.PercentRemaining);
            });
    }

    [Fact]
    public async Task RefreshAsyncMapsProDetailedLimitBuckets()
    {
        var client = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(44, 300, 1780142889),
            new CodexRpcRateLimitWindow(31, 10080, 1780360396),
            "pro",
            new Dictionary<string, CodexRpcRateLimitBucket>
            {
                ["codex-spark"] = new(
                    "codex-spark",
                    "GPT-5.3-Codex-Spark",
                    new CodexRpcRateLimitWindow(12, 300, 1780142889),
                    new CodexRpcRateLimitWindow(4, 10080, 1780360396),
                    null,
                    "pro"),
                ["code-review"] = new(
                    "code-review",
                    "Code review",
                    new CodexRpcRateLimitWindow(0, 300, null),
                    null,
                    new CodexRpcCredits(7),
                    "pro")
            }));
        var provider = new CodexUsageProvider(client, CodexUsageMode.ProDetailed);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("five-hour", window.Id);
                Assert.Equal("5h limit", window.Label);
                Assert.Equal(56, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("weekly", window.Id);
                Assert.Equal("Weekly limit", window.Label);
                Assert.Equal(69, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("codex-spark-primary", window.Id);
                Assert.Equal("GPT-5.3-Codex-Spark 5h limit", window.Label);
                Assert.Equal(88, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("codex-spark-secondary", window.Id);
                Assert.Equal("GPT-5.3-Codex-Spark weekly limit", window.Label);
                Assert.Equal(96, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("code-review-primary", window.Id);
                Assert.Equal("Code review 5h limit", window.Label);
                Assert.Equal(100, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("code-review-credits", window.Id);
                Assert.Equal("Code review credits", window.Label);
                Assert.Equal(100, window.PercentRemaining);
                Assert.Equal("Remaining credits: 7", window.ResetLabel);
            });
    }

    [Fact]
    public void ParseWhamUsageMapsPrimaryWeeklyAndAdditionalRateLimits()
    {
        var json = """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 25,
                  "window_duration_mins": 300,
                  "resets_at": 1780142889
                },
                "secondary_window": {
                  "used_percent": 40,
                  "window_duration_mins": 10080,
                  "resets_at": 1780360396
                },
                "additional_rate_limits": [
                  {
                    "limit_id": "codex-spark",
                    "title": "GPT-5.3-Codex-Spark",
                    "primary_window": {
                      "used_percent": 12,
                      "window_duration_mins": 300,
                      "resets_at": 1780142889
                    },
                    "secondary_window": {
                      "used_percent": 4,
                      "window_duration_mins": 10080,
                      "resets_at": 1780360396
                    }
                  },
                  {
                    "limit_id": "code-review",
                    "name": "Code review",
                    "primary_window": {
                      "used_percent": 0,
                      "window_duration_mins": 300
                    },
                    "credits": {
                      "balance": 3
                    }
                  }
                ]
              },
              "plan_type": "pro"
            }
            """;

        var rateLimits = CodexWhamRateLimitClient.ParseWhamUsage(json);

        Assert.Equal(25, rateLimits.Primary?.UsedPercent);
        Assert.Equal(40, rateLimits.Secondary?.UsedPercent);
        Assert.Equal("pro", rateLimits.PlanType);
        Assert.NotNull(rateLimits.RateLimitsByLimitId);
        Assert.True(rateLimits.RateLimitsByLimitId!.ContainsKey("codex-spark"));
        Assert.Equal("GPT-5.3-Codex-Spark", rateLimits.RateLimitsByLimitId["codex-spark"].LimitName);
        Assert.Equal(3, rateLimits.RateLimitsByLimitId["code-review"].Credits?.Balance);
    }

    [Fact]
    public void AppServerStartInfoRunsWithoutVisibleConsoleWindow()
    {
        var startInfo = CodexAppServerRateLimitClient.CreateAppServerStartInfo("codex.cmd");

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void BasicModePrefersOAuthCloudUsageBeforeStartingCodexAppServer()
    {
        var client = CodexCompositeRateLimitClient.CreateDefault(CodexUsageMode.Basic);

        var composite = Assert.IsType<CodexCompositeRateLimitClient>(client);
        Assert.Collection(
            composite.Clients,
            item => Assert.IsType<CodexWhamRateLimitClient>(item),
            item => Assert.IsType<CodexAppServerRateLimitClient>(item));
    }

    private sealed class StubCodexRateLimitClient(CodexRpcRateLimits rateLimits) : ICodexRateLimitClient
    {
        public Task<CodexRpcRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(rateLimits);
        }
    }
}

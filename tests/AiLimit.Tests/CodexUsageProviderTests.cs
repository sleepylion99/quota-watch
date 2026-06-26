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
    public async Task RefreshAsyncLabelsMonthlyWindowByDuration()
    {
        // Some responses provide an explicit ~30-day window duration (43200 mins).
        var client = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(40, 43200, 1780142889),
            null,
            "free"));
        var provider = new CodexUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        var window = Assert.Single(snapshot.Windows);
        Assert.Equal("monthly", window.Id);
        Assert.Equal(60, window.PercentRemaining);
    }

    [Fact]
    public async Task RefreshAsyncLabelsMonthlyByFarResetWhenDurationMissing()
    {
        // Real Free / Go behaviour: window_duration_mins is omitted, but the single
        // window resets ~30 days out. The reset distance alone identifies it as monthly.
        var resetUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var client = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(5, null, resetUnix),
            null,
            "free"));
        var provider = new CodexUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        var window = Assert.Single(snapshot.Windows);
        Assert.Equal("monthly", window.Id);
        Assert.Equal(95, window.PercentRemaining);
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

    [Fact]
    public void ExplicitProfileConfiguresBothCodexClientsForTheSameAccount()
    {
        var authPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codex-work", "auth.json"));

        var client = CodexCompositeRateLimitClient.CreateDefault(CodexUsageMode.Auto, authPath);

        var composite = Assert.IsType<CodexCompositeRateLimitClient>(client);
        var wham = Assert.IsType<CodexWhamRateLimitClient>(composite.Clients[0]);
        var appServer = Assert.IsType<CodexAppServerRateLimitClient>(composite.Clients[1]);
        Assert.Equal(authPath, ReadPrivateField<string>(wham, "_authPath"));
        Assert.Equal(Path.GetDirectoryName(authPath), ReadPrivateField<string>(appServer, "_codexHome"));
    }

    [Fact]
    public void AppServerStartInfoUsesSelectedProfileAsCodexHome()
    {
        var codexHome = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codex-work"));

        var startInfo = CodexAppServerRateLimitClient.CreateAppServerStartInfo("codex.cmd", codexHome);

        Assert.Equal(codexHome, startInfo.Environment["CODEX_HOME"]);
    }

    private static T ReadPrivateField<T>(object instance, string fieldName)
    {
        return Assert.IsType<T>(instance.GetType()
            .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(instance));
    }

    [Fact]
    public void BuildResetCreditSummaryPicksCountAndNearestExpiryOfAvailable()
    {
        var credits = new CodexResetCredits(3, new[]
        {
            new CodexResetCreditEntry("a", "available", DateTimeOffset.Parse("2026-07-10T00:00:00Z"), null, null, null),
            new CodexResetCreditEntry("b", "available", DateTimeOffset.Parse("2026-07-02T00:00:00Z"), null, null, null),
            new CodexResetCreditEntry("c", "redeemed", DateTimeOffset.Parse("2026-06-01T00:00:00Z"), null, null, null),
        });

        var summary = CodexUsageProvider.BuildResetCreditSummary(credits);

        Assert.NotNull(summary);
        Assert.Equal(3, summary!.AvailableCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-02T00:00:00Z"), summary.NearestExpiry);
    }

    [Fact]
    public void BuildResetCreditSummaryReturnsNullWhenNoneAvailable()
    {
        var credits = new CodexResetCredits(0, new[]
        {
            new CodexResetCreditEntry("c", "redeemed", DateTimeOffset.Parse("2026-06-01T00:00:00Z"), null, null, null),
        });

        Assert.Null(CodexUsageProvider.BuildResetCreditSummary(credits));
    }

    [Fact]
    public void BuildResetCreditSummaryAllowsMissingExpiry()
    {
        var credits = new CodexResetCredits(0, new[]
        {
            new CodexResetCreditEntry("a", "available", null, null, null, null),
        });

        var summary = CodexUsageProvider.BuildResetCreditSummary(credits);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.AvailableCount);
        Assert.Null(summary.NearestExpiry);
    }

    [Fact]
    public void ParseResetCreditsReadsAvailableCountAndEntries()
    {
        var json = """
            {
              "available_count": 2,
              "credits": [
                { "id": "a", "status": "available", "granted_at": "2026-06-01T00:00:00Z", "expires_at": "2026-06-30T00:00:00Z", "reset_type": "weekly", "title": "Weekly reset" },
                { "id": "b", "status": "available", "expires_at": "2026-06-28T00:00:00Z" },
                { "id": "c", "status": "redeemed", "expires_at": "2026-06-10T00:00:00Z" }
              ]
            }
            """;

        var result = CodexWhamResetCreditsClient.ParseResetCredits(json);

        Assert.Equal(2, result.AvailableCount);
        Assert.Equal(3, result.Credits.Count);
        Assert.True(result.Credits[0].IsAvailable);
        Assert.Equal(DateTimeOffset.Parse("2026-06-30T00:00:00Z"), result.Credits[0].ExpiresAt);
        Assert.False(result.Credits[2].IsAvailable);
    }

    [Fact]
    public void ParseResetCreditsFallsBackToAvailableEntryCountWhenCountMissing()
    {
        var json = """
            { "credits": [ { "id": "a", "status": "available" }, { "id": "b", "status": "redeemed" } ] }
            """;

        var result = CodexWhamResetCreditsClient.ParseResetCredits(json);

        Assert.Equal(1, result.AvailableCount);
    }

    [Fact]
    public async Task RefreshAsyncAttachesResetCreditSummary()
    {
        var rateClient = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(44, 300, 1780142889), null, "plus"));
        var resetClient = new StubResetCreditsClient(new CodexResetCredits(2, new[]
        {
            new CodexResetCreditEntry("a", "available", DateTimeOffset.Parse("2026-07-02T00:00:00Z"), null, null, null),
            new CodexResetCreditEntry("b", "available", DateTimeOffset.Parse("2026-07-10T00:00:00Z"), null, null, null),
        }));
        var provider = new CodexUsageProvider(rateClient, CodexUsageMode.Auto, resetClient);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.NotNull(snapshot.ResetCredits);
        Assert.Equal(2, snapshot.ResetCredits!.AvailableCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-02T00:00:00Z"), snapshot.ResetCredits.NearestExpiry);
        Assert.NotEmpty(snapshot.Windows);
    }

    [Fact]
    public async Task RefreshAsyncKeepsWindowsWhenResetCreditsFail()
    {
        var rateClient = new StubCodexRateLimitClient(new CodexRpcRateLimits(
            new CodexRpcRateLimitWindow(44, 300, 1780142889), null, "plus"));
        var resetClient = new ThrowingResetCreditsClient();
        var provider = new CodexUsageProvider(rateClient, CodexUsageMode.Auto, resetClient);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Null(snapshot.ResetCredits);
        Assert.Equal(UsageStatus.Fresh, snapshot.Status);
        Assert.NotEmpty(snapshot.Windows);
    }

    private sealed class StubCodexRateLimitClient(CodexRpcRateLimits rateLimits) : ICodexRateLimitClient
    {
        public Task<CodexRpcRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(rateLimits);
        }
    }

    private sealed class StubResetCreditsClient(CodexResetCredits? credits) : ICodexResetCreditsClient
    {
        public Task<CodexResetCredits?> ReadResetCreditsAsync(CancellationToken cancellationToken)
            => Task.FromResult(credits);
    }

    private sealed class ThrowingResetCreditsClient : ICodexResetCreditsClient
    {
        public Task<CodexResetCredits?> ReadResetCreditsAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }
}

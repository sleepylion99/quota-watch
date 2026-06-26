using AiLimit.Core.Domain;
using AiLimit.Core.Providers.Accounts;

namespace AiLimit.Tests.Accounts;

public sealed class ClaudeAccountProviderUsageTests
{
    [Fact]
    public async Task PollUsageAsyncStampsAccountLabelOntoDisplayName()
    {
        var home = Path.Combine(Path.GetTempPath(), "claude-acct-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(home, ".claude2");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, ".credentials.json"),
            """{"claudeAiOauth":{"accessToken":"t"}}""");
        try
        {
            var scanner = new ClaudeProfileScanner(home);
            var record = scanner.Scan().Single();
            var provider = new ClaudeAccountProvider(
                scanner,
                new ClaudeActiveSelection(Path.Combine(home, "active.json")),
                poll: (_, _) => Task.FromResult(AccountSnapshot.Failure("unused")),
                pollUsage: (_, _) => Task.FromResult(new UsageSnapshot(
                    "claude", "Claude Code", DateTimeOffset.UtcNow, UsageSource.Agent, UsageStatus.Fresh,
                    [new UsageWindow("five-hour", "Current session", 95, null, null, "high", IsUsedPercent: true)],
                    AccountKey: "acct-1")));

            var snapshot = await provider.PollUsageAsync(record, CancellationToken.None);

            Assert.Equal(UsageStatus.Fresh, snapshot.Status);
            Assert.Equal("acct-1", snapshot.AccountKey);
            Assert.Contains(".claude2", snapshot.DisplayName);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}

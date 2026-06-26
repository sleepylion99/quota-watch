using System.IO;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class ClaudeAccountProviderTests
{
    private static (ClaudeAccountProvider provider, ClaudeProfileScanner scanner, string home) Make(
        Func<string, CancellationToken, Task<AccountSnapshot>>? poll = null)
    {
        var home = Path.Combine(Path.GetTempPath(), "claude-prov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        File.WriteAllText(Path.Combine(home, ".claude", ".credentials.json"), "{\"claudeAiOauth\":{\"subscriptionType\":\"max\"}}");
        Directory.CreateDirectory(Path.Combine(home, ".claude2"));
        File.WriteAllText(Path.Combine(home, ".claude2", ".credentials.json"), "{\"claudeAiOauth\":{\"subscriptionType\":\"pro\"}}");

        var scanner = new ClaudeProfileScanner(home);
        var selection = new ClaudeActiveSelection(Path.Combine(home, "active.json"));
        var trash = new AccountTrashStore(Path.Combine(home, "trash.json"));
        var provider = new ClaudeAccountProvider(
            scanner,
            selection,
            poll ?? ((_, _) => Task.FromResult(AccountSnapshot.Success(Array.Empty<QuotaBucket>(), AccountPlan.Unknown))),
            trash);
        return (provider, scanner, home);
    }

    [Fact]
    public void LoadAccounts_ReturnsScannedProfiles()
    {
        var (provider, _, home) = Make();
        try
        {
            var accounts = provider.LoadAccounts();
            Assert.Equal(2, accounts.Count);
            Assert.All(accounts, a => Assert.Equal("claude", a.ProviderKey));
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void MarkActive_RoundTrips()
    {
        var (provider, _, home) = Make();
        try
        {
            var id = provider.LoadAccounts().First().Id;
            provider.MarkActive(id);
            Assert.Equal(id, provider.GetActiveId());
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public async Task PollAsync_DelegatesToInjectedPoll()
    {
        var polled = new List<string>();
        var (provider, _, home) = Make(poll: (path, _) =>
        {
            polled.Add(path);
            return Task.FromResult(AccountSnapshot.Success(Array.Empty<QuotaBucket>(), AccountPlan.Max));
        });
        try
        {
            var record = provider.LoadAccounts().First(r => r.DisplayName == ".claude2");
            var snapshot = await provider.PollAsync(record, CancellationToken.None);

            Assert.True(snapshot.IsSuccess);
            Assert.Equal(AccountPlan.Max, snapshot.Plan);
            Assert.Single(polled);
            Assert.EndsWith(Path.Combine(".claude2", ".credentials.json"), polled[0]);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void CanTrash_TrueForNumberedProfileOnly()
    {
        var (provider, _, home) = Make();
        try
        {
            var primary = provider.LoadAccounts().First(r => r.DisplayName == ".claude");
            var numbered = provider.LoadAccounts().First(r => r.DisplayName == ".claude2");
            Assert.False(provider.CanTrash(primary));
            Assert.True(provider.CanTrash(numbered));
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}

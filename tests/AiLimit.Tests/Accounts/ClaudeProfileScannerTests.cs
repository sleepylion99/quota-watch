using System.IO;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class ClaudeProfileScannerTests
{
    private static string MakeHome(out string home)
    {
        home = Path.Combine(Path.GetTempPath(), "claude-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        File.WriteAllText(Path.Combine(home, ".claude", ".credentials.json"),
            "{\"claudeAiOauth\":{\"subscriptionType\":\"max\"}}");
        Directory.CreateDirectory(Path.Combine(home, ".claude2"));
        File.WriteAllText(Path.Combine(home, ".claude2", ".credentials.json"),
            "{\"claudeAiOauth\":{\"subscriptionType\":\"pro\"}}");
        Directory.CreateDirectory(Path.Combine(home, ".claude3"));   // no credentials -> ignored
        Directory.CreateDirectory(Path.Combine(home, ".other"));     // not a claude profile
        return home;
    }

    [Fact]
    public void Scan_FindsOnlyClaudeProfilesWithCredentials()
    {
        var home = MakeHome(out _);
        try
        {
            var records = new ClaudeProfileScanner(home).Scan();

            Assert.Equal(2, records.Count);
            Assert.All(records, r => Assert.Equal("claude", r.ProviderKey));
            // Claude credentials carry no email; the directory name identifies the profile.
            Assert.Contains(records, r => r.DisplayName == ".claude" && r.Email is null);
            Assert.Contains(records, r => r.DisplayName == ".claude2" && r.Email is null);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void ResolveCredentialsPath_RoundTripsById()
    {
        var home = MakeHome(out _);
        try
        {
            var scanner = new ClaudeProfileScanner(home);
            var record = scanner.Scan().First(r => r.DisplayName == ".claude2");

            var path = scanner.ResolveCredentialsPath(record.Id);

            Assert.Equal(Path.Combine(home, ".claude2", ".credentials.json"), path);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void Scan_UsesCachedEmailWhenPresent()
    {
        var home = MakeHome(out _);
        try
        {
            ClaudeAccountCache.Write(Path.Combine(home, ".claude2"), "user@example.com");

            var records = new ClaudeProfileScanner(home).Scan();

            Assert.Contains(records, r => r.DisplayName == ".claude2" && r.Email == "user@example.com");
            Assert.Contains(records, r => r.DisplayName == ".claude" && r.Email is null);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void MapPlan_MapsSubscriptionType()
    {
        Assert.Equal(AccountPlan.Max, ClaudeProfileScanner.MapPlan("max"));
        Assert.Equal(AccountPlan.Pro, ClaudeProfileScanner.MapPlan("pro"));
        Assert.Equal(AccountPlan.Free, ClaudeProfileScanner.MapPlan("free"));
        Assert.Equal(AccountPlan.Unknown, ClaudeProfileScanner.MapPlan(null));
    }
}

using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using Xunit.Abstractions;

namespace AiLimit.Tests;

public sealed class AntigravityLiveProbeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LiveAntigravityIdeProbeWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("AILIMIT_LIVE_ANTIGRAVITY"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var provider = new AntigravityUsageProvider();
        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        output.WriteLine($"Status: {snapshot.Status}");
        output.WriteLine($"Source channel: {snapshot.SourceChannel ?? "missing"}");
        output.WriteLine($"Window count: {snapshot.Windows.Count}");
        foreach (var window in snapshot.Windows)
        {
            output.WriteLine($"{window.Label}: {window.PercentRemaining}% used, confidence={window.Confidence}");
        }

        Assert.Equal(UsageStatus.Fresh, snapshot.Status);
        Assert.Equal("ide-fallback", snapshot.SourceChannel);
        Assert.NotEmpty(snapshot.Windows);
        Assert.All(snapshot.Windows, window => Assert.Equal("high", window.Confidence));
    }

    [Fact]
    public async Task LiveAntigravityCloudProbeWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("AILIMIT_LIVE_ANTIGRAVITY"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var runningProcesses = System.Diagnostics.Process.GetProcesses()
            .Where(process => process.ProcessName.Contains("Antigravity", StringComparison.OrdinalIgnoreCase))
            .Select(process => process.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        output.WriteLine(runningProcesses.Length == 0
            ? "Antigravity process: not running"
            : $"Antigravity process: {string.Join(", ", runningProcesses)}");

        var credentials = AntigravityOAuthCredentialStore.Load();
        output.WriteLine(credentials is null
            ? "Stored credentials: missing"
            : $"Stored credentials: access={Has(credentials.AccessToken)}, refresh={Has(credentials.RefreshToken)}, clientId={Has(credentials.ClientId)}, clientSecret={Has(credentials.ClientSecret)}");

        var cloudClient = new AntigravityOAuthUsageClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        var cloudUsage = await cloudClient.ReadUsageAsync(CancellationToken.None);
        output.WriteLine($"Cloud-only window count: {cloudUsage.Buckets.Count}");

        var provider = new AntigravityUsageProvider(new StubAntigravityUsageClient(cloudUsage));
        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        output.WriteLine($"Status: {snapshot.Status}");
        output.WriteLine($"Window count: {snapshot.Windows.Count}");
        foreach (var window in snapshot.Windows)
        {
            output.WriteLine($"{window.Label}: {100 - window.PercentRemaining}% used");
        }

        Assert.Equal(UsageStatus.Fresh, snapshot.Status);
        Assert.NotEmpty(snapshot.Windows);
    }

    private static string Has(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "missing" : "present";
    }

    private sealed class StubAntigravityUsageClient(AntigravityUsageReadResult result) : IAntigravityUsageClient
    {
        public Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}

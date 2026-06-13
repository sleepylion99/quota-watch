using AiLimit.App.Services;

namespace AiLimit.Tests;

public sealed class AppLogTests
{
    [Fact]
    public void IsEnabledDefaultsToFalse()
    {
        var previous = Environment.GetEnvironmentVariable("AILIMIT_DEBUG_LOG");
        try
        {
            Environment.SetEnvironmentVariable("AILIMIT_DEBUG_LOG", null);

            Assert.False(AppLog.IsEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AILIMIT_DEBUG_LOG", previous);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void IsEnabledAcceptsExplicitDebugValues(string value)
    {
        var previous = Environment.GetEnvironmentVariable("AILIMIT_DEBUG_LOG");
        try
        {
            Environment.SetEnvironmentVariable("AILIMIT_DEBUG_LOG", value);

            Assert.True(AppLog.IsEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AILIMIT_DEBUG_LOG", previous);
        }
    }

    [Fact]
    public void FormatEntryIncludesAreaAndMessage()
    {
        var entry = AppLog.FormatEntry(
            DateTimeOffset.Parse("2026-05-31T16:10:00+09:00"),
            AppLogLevel.Warning,
            "Dashboard",
            "Provider settings checked");

        Assert.StartsWith("2026-05-31 16:10:00 [Warning] [Dashboard]", entry);
        Assert.Contains("[Dashboard]", entry);
        Assert.Contains("Provider settings checked", entry);
    }

    [Fact]
    public void FormatEntryRedactsCommonSecretsBeforeWritingLogFile()
    {
        var entry = AppLog.FormatEntry(
            DateTimeOffset.Parse("2026-05-31T16:10:00+09:00"),
            AppLogLevel.Warning,
            "Provider",
            "Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789 access_token=fresh-token-value-123456 --csrf_token csrf-token-value-123456");

        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123456789", entry);
        Assert.DoesNotContain("fresh-token-value-123456", entry);
        Assert.DoesNotContain("csrf-token-value-123456", entry);
        Assert.Contains("Authorization: Bearer [redacted]", entry);
        Assert.Contains("access_token=[redacted]", entry);
        Assert.Contains("--csrf_token [redacted]", entry);
    }

    [Fact]
    public void FormatCopyTextUsesFriendlyEmptyMessage()
    {
        var text = AppLog.FormatCopyText(string.Empty);

        Assert.Equal("Quota Watch diagnostic log is empty.", text);
    }

    [Fact]
    public void FormatCopyTextRedactsCommonSecrets()
    {
        const string content = """
        Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789
        access_token=fresh-token-value-123456
        refresh_token: "refresh-token-value-123456"
        client_secret=test-client-secret-123456
        --csrf_token csrf-token-value-123456
        """;

        var text = AppLog.FormatCopyText(content);

        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123456789", text);
        Assert.DoesNotContain("fresh-token-value-123456", text);
        Assert.DoesNotContain("refresh-token-value-123456", text);
        Assert.DoesNotContain("test-client-secret-123456", text);
        Assert.DoesNotContain("csrf-token-value-123456", text);
        Assert.Contains("Authorization: Bearer [redacted]", text);
        Assert.Contains("access_token=[redacted]", text);
        Assert.Contains("refresh_token: [redacted]", text);
        Assert.Contains("client_secret=[redacted]", text);
        Assert.Contains("--csrf_token [redacted]", text);
    }
}

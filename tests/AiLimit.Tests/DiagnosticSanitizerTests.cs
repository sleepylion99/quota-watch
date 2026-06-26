using AiLimit.Core;

namespace AiLimit.Tests;

public sealed class DiagnosticSanitizerTests
{
    [Fact]
    public void RedactsBareGoogleClientId()
    {
        var clientId = string.Concat(
            "1071006060591-",
            "abcdef1234567890",
            ".apps.googleusercontent.com");
        var input = $"Scanning {clientId} for client";
        var redacted = DiagnosticSanitizer.Redact(input);
        Assert.DoesNotContain("googleusercontent.com", redacted);
        Assert.Contains("[redacted]", redacted);
    }

    [Fact]
    public void DoesNotRedactNonClientIdLookalikes()
    {
        var input = "Check 123456-short.apps.googleusercontent.com (not a client id)";
        var redacted = DiagnosticSanitizer.Redact(input);
        Assert.Contains("123456-short", redacted);
    }
}

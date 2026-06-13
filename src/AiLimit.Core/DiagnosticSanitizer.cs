using System.Text.RegularExpressions;

namespace AiLimit.Core;

public static class DiagnosticSanitizer
{
    private static readonly Regex BearerTokenPattern = new(
        @"(?i)\b(Authorization\s*:\s*Bearer\s+)[A-Za-z0-9._~+/=-]{12,}",
        RegexOptions.CultureInvariant);
    private static readonly Regex KeyValueSecretPattern = new(
        @"(?i)\b(access_token|refresh_token|id_token|client_secret|api_key|apikey|password)\b(\s*[:=]\s*)(""[^""\r\n]*""|'[^'\r\n]*'|[^\s,;]+)",
        RegexOptions.CultureInvariant);
    private static readonly Regex CsrfArgumentPattern = new(
        @"(?i)(--csrf_token(?:=|\s+))(""[^""\r\n]*""|'[^'\r\n]*'|[^\s]+)",
        RegexOptions.CultureInvariant);
    // Bare Google OAuth artifacts have distinctive prefixes, so they can be redacted even
    // when they appear in a log line without an adjacent key (e.g. inside an exception message).
    private static readonly Regex BareGoogleTokenPattern = new(
        @"\b(?:ya29\.|1//|GOCSPX-)[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.CultureInvariant);

    public static string Redact(string content)
    {
        var redacted = BearerTokenPattern.Replace(content, "$1[redacted]");
        redacted = KeyValueSecretPattern.Replace(redacted, "$1$2[redacted]");
        redacted = CsrfArgumentPattern.Replace(redacted, "$1[redacted]");
        return BareGoogleTokenPattern.Replace(redacted, "[redacted]");
    }
}

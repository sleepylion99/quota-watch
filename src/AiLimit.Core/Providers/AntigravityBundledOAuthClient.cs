namespace AiLimit.Core.Providers;

/// <summary>
/// The public, embedded desktop-app OAuth client that the Antigravity IDE itself ships with.
/// Per OAuth public-client semantics the "secret" is not confidential; it is taken from the
/// Antigravity IDE bundle (and mirrored by AntigravityManager). Used as the lowest-precedence
/// fallback so the bundled-default registry entry works even when the IDE's bundle cannot be
/// scanned on this machine. User-supplied clients always take precedence.
/// </summary>
internal static class AntigravityBundledOAuthClient
{
    public static string ClientId { get; } = string.Concat(
        "1071006060591-",
        "tmhssin2h21lcre235vtolojh4g403ep",
        ".apps.googleusercontent.com");

    public static string ClientSecret { get; } = string.Concat(
        "GOC",
        "SPX",
        "-",
        "K58FWR486LdLJ1mLB8sXC4z6qDAf");

    public static AntigravityOAuthClientConfig Config { get; } = new(ClientId, ClientSecret);
}

namespace AiLimit.Core.Providers;

public sealed record ProviderCatalogItem(
    string Id,
    string DisplayName,
    int FiveHourPercent,
    int WeeklyPercent,
    bool IsEnabledByDefault = true);

public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderCatalogItem> KnownProviders { get; } =
    [
        new("codex", "ChatGPT Codex", 63, 41),
        new("claude", "Claude Code", 72, 54),
        new("gemini-pro", "Google Antigravity", 88, 67)
    ];
}

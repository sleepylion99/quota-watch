using System.Text.Json;

namespace AiLimit.Core.Providers.Accounts;

public sealed record ClaudeCredential(
    string AccessToken,
    string? RefreshToken,
    long? ExpiresAtUnixMs,
    string? SubscriptionType = null,
    string? RateLimitTier = null,
    IReadOnlyList<string>? Scopes = null);

/// <summary>
/// Writes a Claude login into a NEW profile directory (.claude2, .claude3, ...). Never overwrites the
/// primary ~/.claude or an existing numbered profile, so sign-in is non-destructive.
/// </summary>
public sealed class ClaudeProfileWriter
{
    private readonly string _homeRoot;

    public ClaudeProfileWriter()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    public ClaudeProfileWriter(string homeRoot) => _homeRoot = homeRoot;

    public string WriteNewProfile(ClaudeCredential credential)
    {
        var dir = NextFreeProfileDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ".credentials.json");
        File.WriteAllText(path, BuildJson(credential));
        return path;
    }

    private string NextFreeProfileDir()
    {
        for (var i = 2; ; i++)
        {
            var dir = Path.Combine(_homeRoot, $".claude{i}");
            if (!Directory.Exists(dir))
            {
                return dir;
            }
        }
    }

    private static string BuildJson(ClaudeCredential c)
    {
        var oauth = new Dictionary<string, object?>
        {
            ["accessToken"] = c.AccessToken,
            ["refreshToken"] = c.RefreshToken,
            ["expiresAt"] = c.ExpiresAtUnixMs,
            ["scopes"] = c.Scopes,
            ["subscriptionType"] = c.SubscriptionType,
            ["rateLimitTier"] = c.RateLimitTier,
        };
        var root = new Dictionary<string, object?> { ["claudeAiOauth"] = oauth };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }
}

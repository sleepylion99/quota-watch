using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// Discovers Claude Code profiles: directories under the home root named ".claude" or ".claudeN"
/// that contain a .credentials.json. The AccountRecord.Id is a deterministic Guid derived from the
/// normalized profile path so an active-selection pointer survives restarts. Claude credentials carry
/// no email, so Email is left null and the directory name (DisplayName) identifies the profile; the
/// subscriptionType is surfaced only as the plan via <see cref="MapPlan"/>.
/// </summary>
public sealed class ClaudeProfileScanner
{
    private readonly string _homeRoot;

    public ClaudeProfileScanner()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    public ClaudeProfileScanner(string homeRoot) => _homeRoot = homeRoot;

    public string HomeRoot => _homeRoot;

    public IReadOnlyList<AccountRecord> Scan()
    {
        var result = new List<AccountRecord>();
        if (!Directory.Exists(_homeRoot)) { return result; }

        foreach (var dir in Directory.EnumerateDirectories(_homeRoot))
        {
            var name = Path.GetFileName(dir);
            if (!IsClaudeProfileName(name)) { continue; }
            var credPath = Path.Combine(dir, ".credentials.json");
            if (!File.Exists(credPath)) { continue; }

            result.Add(new AccountRecord(
                Id: DeterministicId(dir),
                ProviderKey: "claude",
                DisplayName: name,
                Email: ClaudeAccountCache.ReadEmail(dir),
                LastSyncedAt: File.GetLastWriteTimeUtc(credPath)));
        }

        return result;
    }

    public string? ResolveProfilePath(Guid id)
    {
        if (!Directory.Exists(_homeRoot)) { return null; }
        foreach (var dir in Directory.EnumerateDirectories(_homeRoot))
        {
            if (IsClaudeProfileName(Path.GetFileName(dir)) && DeterministicId(dir) == id)
            {
                return dir;
            }
        }
        return null;
    }

    public string? ResolveCredentialsPath(Guid id)
    {
        var profile = ResolveProfilePath(id);
        if (profile is null) { return null; }
        var credPath = Path.Combine(profile, ".credentials.json");
        return File.Exists(credPath) ? credPath : null;
    }

    public static string? ReadSubscriptionType(string credPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(credPath));
            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                && oauth.TryGetProperty("subscriptionType", out var s)
                && s.ValueKind == JsonValueKind.String)
            {
                return s.GetString();
            }
        }
        catch
        {
            // unreadable -> no subscription type
        }
        return null;
    }

    public static string? ReadAccessToken(string credPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(credPath));
            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                && oauth.TryGetProperty("accessToken", out var a)
                && a.ValueKind == JsonValueKind.String)
            {
                return a.GetString();
            }
        }
        catch
        {
            // unreadable -> no token
        }
        return null;
    }

    public static AccountPlan MapPlan(string? subscriptionType) => subscriptionType?.ToLowerInvariant() switch
    {
        "free" => AccountPlan.Free,
        "pro" => AccountPlan.Pro,
        "max" => AccountPlan.Max,
        _ => AccountPlan.Unknown
    };

    private static bool IsClaudeProfileName(string name)
    {
        if (!name.StartsWith(".claude", StringComparison.Ordinal)) { return false; }
        var suffix = name[".claude".Length..];
        return suffix.Length == 0 || suffix.All(char.IsDigit);
    }

    private static Guid DeterministicId(string dir)
    {
        var normalized = Path.GetFullPath(dir).TrimEnd('\\', '/').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("claude|" + normalized));
        return new Guid(hash.AsSpan(0, 16));
    }
}

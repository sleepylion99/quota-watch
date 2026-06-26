using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// Discovers Codex CLI profiles. A profile is a directory under the home root
/// named ".codex" or ".codexN" that contains an auth.json. Identity (email +
/// plan) is read from the id_token JWT inside auth.json; unreadable auth falls
/// back to the directory name. The AccountRecord.Id is a deterministic Guid
/// derived from the normalized profile path so an active-selection pointer
/// survives restarts.
/// </summary>
public sealed class CodexProfileScanner
{
    private readonly string _homeRoot;

    public CodexProfileScanner()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public CodexProfileScanner(string homeRoot)
    {
        _homeRoot = homeRoot;
    }

    public string HomeRoot => _homeRoot;

    public IReadOnlyList<AccountRecord> Scan()
    {
        var result = new List<AccountRecord>();
        if (!Directory.Exists(_homeRoot))
        {
            return result;
        }

        foreach (var dir in Directory.EnumerateDirectories(_homeRoot))
        {
            var name = Path.GetFileName(dir);
            if (!IsCodexProfileName(name))
            {
                continue;
            }

            var authPath = Path.Combine(dir, "auth.json");
            if (!File.Exists(authPath))
            {
                continue;
            }

            var (email, _) = ReadIdentity(authPath);
            result.Add(new AccountRecord(
                Id: DeterministicId(dir),
                ProviderKey: "codex",
                DisplayName: name,
                Email: email,
                LastSyncedAt: File.GetLastWriteTimeUtc(authPath)));
        }

        return result;
    }

    /// <summary>Maps a record id back to its profile auth.json path, or null when the profile is gone.</summary>
    public string? ResolveAuthPath(Guid id)
    {
        var profilePath = ResolveProfilePath(id);
        if (profilePath is null)
        {
            return null;
        }

        var authPath = Path.Combine(profilePath, "auth.json");
        return File.Exists(authPath) ? authPath : null;
    }

    public string? ResolveProfilePath(Guid id)
    {
        if (!Directory.Exists(_homeRoot))
        {
            return null;
        }

        foreach (var dir in Directory.EnumerateDirectories(_homeRoot))
        {
            if (IsCodexProfileName(Path.GetFileName(dir)) && DeterministicId(dir) == id)
            {
                return dir;
            }
        }

        return null;
    }

    /// <summary>Reads (email, planType) from the id_token JWT in auth.json. Returns (null, null) on any failure.</summary>
    public static (string? Email, string? PlanType) ReadIdentity(string authPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(authPath));
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens)
                || !tokens.TryGetProperty("id_token", out var idTokenEl)
                || idTokenEl.GetString() is not { } jwt)
            {
                return (null, null);
            }

            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return (null, null);
            }

            var json = Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
            using var payload = JsonDocument.Parse(json);
            var root = payload.RootElement;

            string? email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            string? plan = null;
            if (root.TryGetProperty("https://api.openai.com/auth", out var auth)
                && auth.TryGetProperty("chatgpt_plan_type", out var p))
            {
                plan = p.GetString();
            }

            return (email, plan);
        }
        catch
        {
            return (null, null);
        }
    }

    public static AccountPlan MapPlan(string? planType) => planType?.ToLowerInvariant() switch
    {
        "free" => AccountPlan.Free,
        "plus" or "pro" => AccountPlan.Pro,
        "team" or "business" or "enterprise" => AccountPlan.Max,
        _ => AccountPlan.Unknown
    };

    private static bool IsCodexProfileName(string name)
    {
        if (!name.StartsWith(".codex", StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = name[".codex".Length..];
        return suffix.Length == 0 || suffix.All(char.IsDigit);
    }

    private static Guid DeterministicId(string dir)
    {
        var normalized = Path.GetFullPath(dir).TrimEnd('\\', '/').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("codex|" + normalized));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

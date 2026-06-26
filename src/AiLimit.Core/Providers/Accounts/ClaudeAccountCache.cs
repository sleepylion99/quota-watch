using System.Text.Json;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// Sidecar cache (.account-cache.json) inside a Claude profile directory holding the account email
/// resolved from the OAuth profile endpoint. Lets the (synchronous) scanner show a real account name
/// without a network call on every list; a poll refreshes it.
/// </summary>
public static class ClaudeAccountCache
{
    private const string FileName = ".account-cache.json";

    public static string? ReadEmail(string profileDir)
    {
        try
        {
            var path = Path.Combine(profileDir, FileName);
            if (!File.Exists(path)) { return null; }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string profileDir, string? email)
    {
        try
        {
            Directory.CreateDirectory(profileDir);
            var path = Path.Combine(profileDir, FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(
                new Dictionary<string, object?> { ["email"] = email },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best-effort cache; failure just means the dir name is shown until the next poll
        }
    }
}

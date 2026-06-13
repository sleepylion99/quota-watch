using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AiLimit.Core.Providers;

public sealed record AntigravityOAuthClientConfig(string? ClientId, string? ClientSecret);

[SupportedOSPlatform("windows")]
public sealed class AntigravityOAuthClientStore
{
    private const string RegistryKeyPath = @"Software\AiLimit";
    private const string RegistryValueName = "AntigravityOAuthEntropy";
    private readonly string _path;

    public AntigravityOAuthClientStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiLimit",
            "antigravity-oauth-client.json");
    }

    public AntigravityOAuthClientConfig Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AntigravityOAuthClientConfig(null, null);
            }

            var payload = JsonSerializer.Deserialize<StoredClientPayload>(File.ReadAllText(_path));
            if (payload is null)
            {
                return new AntigravityOAuthClientConfig(null, null);
            }

            var entropy = LoadEntropy();
            return new AntigravityOAuthClientConfig(
                Unprotect(payload.ClientId, entropy),
                Unprotect(payload.ClientSecret, entropy));
        }
        catch
        {
            return new AntigravityOAuthClientConfig(null, null);
        }
    }

    public void Save(string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client ID is required.", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Client secret is required.", nameof(clientSecret));
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var entropy = EnsureEntropy();
        var payload = new StoredClientPayload(
            Protect(clientId.Trim(), entropy),
            Protect(clientSecret.Trim(), entropy));
        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // Clearing credentials is best-effort.
        }
    }

    private static byte[] EnsureEntropy()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        if (key.GetValue(RegistryValueName) is string existing && !string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                return Convert.FromBase64String(existing);
            }
            catch
            {
                // Regenerate if stored value is corrupt.
            }
        }

        var entropy = RandomNumberGenerator.GetBytes(32);
        key.SetValue(RegistryValueName, Convert.ToBase64String(entropy));
        return entropy;
    }

    private static byte[]? LoadEntropy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        if (key?.GetValue(RegistryValueName) is not string value || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch
        {
            return null;
        }
    }

    private static string Protect(string value, byte[]? entropy)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string? Unprotect(string? value, byte[]? entropy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var bytes = ProtectedData.Unprotect(
            Convert.FromBase64String(value),
            entropy,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed record StoredClientPayload(
        [property: JsonPropertyName("client_id")] string? ClientId,
        [property: JsonPropertyName("client_secret")] string? ClientSecret);
}

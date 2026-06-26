using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AiLimit.Core.Providers;

public sealed record AntigravityOAuthClientConfig(string? ClientId, string? ClientSecret);

/// <summary>
/// Vestige of the retired single-client OAuth store. Antigravity OAuth clients are now
/// managed by <see cref="AntigravityOAuthClientRegistry"/>; this type survives only to name
/// the legacy on-disk file the registry migrates from on first run and to decrypt it (the
/// old format used per-machine DPAPI with a registry-stored entropy blob). Once the one-time
/// migration is no longer needed, this class and its <see cref="DefaultPath"/> can be removed.
/// </summary>
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

    /// <summary>
    /// Reads the old entropy-DPAPI format and returns the DECRYPTED plaintext client config,
    /// or <c>null</c> if the file is absent or cannot be decrypted (wrong user, missing
    /// registry entropy, or corrupted). Only available on Windows because DPAPI is Windows-only.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public AntigravityOAuthClientConfig? LoadLegacyPlaintext()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var payload = JsonSerializer.Deserialize<StoredClientPayload>(File.ReadAllText(_path));
            if (payload is null) return null;
            var entropy = LoadEntropy();
            return new AntigravityOAuthClientConfig(
                Unprotect(payload.ClientId, entropy),
                Unprotect(payload.ClientSecret, entropy));
        }
        catch { return null; }
    }

    [SupportedOSPlatform("windows")]
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

    [SupportedOSPlatform("windows")]
    private static string? Unprotect(string? value, byte[]? entropy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(value),
                entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException) { return null; }
        catch (CryptographicException) { return null; }
    }

    private sealed record StoredClientPayload(
        [property: JsonPropertyName("client_id")] string? ClientId,
        [property: JsonPropertyName("client_secret")] string? ClientSecret);
}

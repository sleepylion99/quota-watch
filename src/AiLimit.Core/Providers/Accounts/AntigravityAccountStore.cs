using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>Outcome of reading a stored refresh token.</summary>
public enum RefreshTokenStatus
{
    /// <summary>The token was present and successfully decrypted.</summary>
    Available,

    /// <summary>No account or token exists for the requested id.</summary>
    NotStored,

    /// <summary>An encrypted token exists but could not be decrypted.</summary>
    Undecryptable
}

/// <summary>A stored refresh-token lookup result.</summary>
public readonly record struct RefreshTokenLookup(string? Token, RefreshTokenStatus Status);

[SupportedOSPlatform("windows")]
public sealed class AntigravityAccountStore
{
    private const string RegistryKeyPath = @"Software\AiLimit";
    private const string RegistryValueName = "AntigravityAccountStoreEntropy";
    private readonly string _path;

    public AntigravityAccountStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiLimit",
            "antigravity-accounts.json");
    }

    public IReadOnlyList<AccountRecord> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var payload = JsonSerializer.Deserialize<StoredAccountListPayload>(File.ReadAllText(_path));
            if (payload?.Accounts is null)
            {
                return [];
            }

            return payload.Accounts
                .Select(a => new AccountRecord(
                    Id: a.Id,
                    ProviderKey: "gemini-pro",
                    DisplayName: a.Name,
                    Email: a.Email,
                    LastSyncedAt: a.LastSyncedAt))
                .ToList()
                .AsReadOnly();
        }
        catch
        {
            return [];
        }
    }

    public AccountRecord Add(string displayName, string? email, string refreshToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var existing = LoadRaw();
        if (existing.Any(a => string.Equals(
                TryUnprotect(a.RefreshToken, ResolveEntropy(a)),
                refreshToken,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "An account with the same refresh token is already stored.");
        }

        var now = DateTimeOffset.UtcNow;
        var salt = RandomNumberGenerator.GetBytes(32);
        var newAccount = new StoredAccountPayload
        {
            Id = Guid.NewGuid(),
            Name = displayName,
            Email = email,
            RefreshToken = Protect(refreshToken, salt),
            TokenSalt = Convert.ToBase64String(salt),
            CreatedAt = now,
            LastSyncedAt = now
        };

        existing.Add(newAccount);
        WriteRaw(existing);

        return new AccountRecord(
            Id: newAccount.Id,
            ProviderKey: "gemini-pro",
            DisplayName: displayName,
            Email: email,
            LastSyncedAt: now);
    }

    public void Remove(Guid id)
    {
        var existing = LoadRaw();
        var filtered = existing.Where(a => a.Id != id).ToList();
        if (filtered.Count == existing.Count)
        {
            return;
        }

        WriteRaw(filtered);
    }

    public string? ReadRefreshToken(Guid id) => ReadRefreshTokenLookup(id).Token;

    /// <summary>
    /// Reads a stored refresh token and reports whether it is missing entirely
    /// (<see cref="RefreshTokenStatus.NotStored"/>) versus present but no longer
    /// decryptable (<see cref="RefreshTokenStatus.Undecryptable"/>). Callers use the
    /// distinction to tell "account removed" apart from "sign-in needs renewing".
    /// </summary>
    public RefreshTokenLookup ReadRefreshTokenLookup(Guid id)
    {
        var existing = LoadRaw();
        var account = existing.FirstOrDefault(a => a.Id == id);
        if (account is null || string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            return new RefreshTokenLookup(null, RefreshTokenStatus.NotStored);
        }

        var token = TryUnprotect(account.RefreshToken, ResolveEntropy(account));
        return string.IsNullOrWhiteSpace(token)
            ? new RefreshTokenLookup(null, RefreshTokenStatus.Undecryptable)
            : new RefreshTokenLookup(token, RefreshTokenStatus.Available);
    }

    private List<StoredAccountPayload> LoadRaw()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var payload = JsonSerializer.Deserialize<StoredAccountListPayload>(File.ReadAllText(_path));
            return payload?.Accounts ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteRaw(List<StoredAccountPayload> accounts)
    {
        var payload = new StoredAccountListPayload { Accounts = accounts };
        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, null);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Resolves the DPAPI entropy for an account. New accounts carry their own
    /// per-account salt inside the file, removing the legacy dependency on the shared
    /// registry entropy whose desync produced spurious "Account removed." failures.
    /// Pre-salt entries fall back to the registry entropy.
    /// </summary>
    private static byte[]? ResolveEntropy(StoredAccountPayload account)
    {
        if (!string.IsNullOrWhiteSpace(account.TokenSalt))
        {
            try
            {
                return Convert.FromBase64String(account.TokenSalt);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return LoadEntropy();
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

    private static string? TryUnprotect(string? value, byte[]? entropy)
    {
        try
        {
            return Unprotect(value, entropy);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private sealed class StoredAccountListPayload
    {
        [JsonPropertyName("accounts")]
        public List<StoredAccountPayload> Accounts { get; set; } = [];
    }

    private sealed class StoredAccountPayload
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_salt")]
        public string? TokenSalt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("last_synced_at")]
        public DateTimeOffset LastSyncedAt { get; set; }
    }
}

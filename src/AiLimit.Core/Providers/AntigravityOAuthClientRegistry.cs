using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLimit.Core.Providers;

public interface ISecretProtector
{
    string Protect(string value);
    string? Unprotect(string? value);
}

public sealed record AntigravityOAuthClientEntry(
    string Key, string Label, string? ClientId, string? ClientSecret, bool IsBuiltIn);

public sealed class AntigravityOAuthClientRegistry
{
    public const string BuiltInKey = "antigravity_bundled";

    private readonly string _path;
    private readonly string _legacySingleClientPath;
    private readonly AntigravityOAuthClientConfig _bundled;
    private readonly ISecretProtector _protector;
    private readonly Func<AntigravityOAuthClientConfig?>? _legacyLoader;

    public AntigravityOAuthClientRegistry(
        string path,
        string legacySingleClientPath,
        AntigravityOAuthClientConfig bundled,
        ISecretProtector protector,
        Func<AntigravityOAuthClientConfig?>? legacyLoader = null)
    {
        _path = path;
        _legacySingleClientPath = legacySingleClientPath;
        _bundled = bundled;
        _protector = protector;
        _legacyLoader = legacyLoader;
        MigrateLegacyIfNeeded();
    }

    public IReadOnlyList<AntigravityOAuthClientEntry> List()
    {
        var file = LoadFile();
        var entries = new List<AntigravityOAuthClientEntry>
        {
            new(BuiltInKey, "Bundled default", _bundled.ClientId, _bundled.ClientSecret, IsBuiltIn: true)
        };
        foreach (var row in file.Clients)
        {
            string? secret = null;
            try { secret = _protector.Unprotect(row.ClientSecretProtected); }
            catch { /* corrupted — leave null */ }
            entries.Add(new AntigravityOAuthClientEntry(
                row.Key, row.Label, row.ClientId, secret, IsBuiltIn: false));
        }
        return entries;
    }

    public AntigravityOAuthClientEntry? GetActive()
    {
        var file = LoadFile();
        var key = string.IsNullOrEmpty(file.ActiveKey) ? BuiltInKey : file.ActiveKey;
        var all = List();
        return all.FirstOrDefault(c => c.Key == key) ?? all.First(c => c.IsBuiltIn);
    }

    public void SetActive(string key)
    {
        var file = LoadFile();
        file.ActiveKey = key;
        SaveFile(file);
    }

    public AntigravityOAuthClientEntry Add(string label, string clientId, string clientSecret)
    {
        var file = LoadFile();
        var entry = new ClientRow
        {
            Key = "client_" + Guid.NewGuid().ToString("N")[..8],
            Label = label,
            ClientId = clientId,
            ClientSecretProtected = _protector.Protect(clientSecret),
        };
        file.Clients.Add(entry);
        SaveFile(file);
        return new AntigravityOAuthClientEntry(entry.Key, label, clientId, clientSecret, IsBuiltIn: false);
    }

    public void Remove(string key)
    {
        if (key == BuiltInKey)
        {
            throw new InvalidOperationException("The bundled default client cannot be removed.");
        }
        var file = LoadFile();
        file.Clients.RemoveAll(c => c.Key == key);
        if (file.ActiveKey == key)
        {
            file.ActiveKey = BuiltInKey;
        }
        SaveFile(file);
    }

    private void MigrateLegacyIfNeeded()
    {
        var file = LoadFile();
        if (file.MigratedLegacy)
        {
            return;
        }

        // Only bother reading from disk if there is no injected loader.
        if (_legacyLoader is null && !File.Exists(_legacySingleClientPath))
        {
            file.MigratedLegacy = true;
            SaveFile(file);
            return;
        }

        try
        {
            AntigravityOAuthClientConfig? legacy;
            if (_legacyLoader is not null)
            {
                // Injected loader already returns plaintext (e.g. the Windows DPAPI-decrypt path).
                legacy = _legacyLoader();
            }
            else
            {
                // Existing single-client file written by the *registry* itself: PascalCase JSON,
                // secret already protected by _protector — unprotect to get plaintext first.
                var parsed = JsonSerializer.Deserialize<LegacyClient>(File.ReadAllText(_legacySingleClientPath));
                legacy = parsed is not null
                    ? new AntigravityOAuthClientConfig(parsed.ClientId, _protector.Unprotect(parsed.ClientSecret))
                    : null;
            }

            if (!string.IsNullOrWhiteSpace(legacy?.ClientId))
            {
                file.Clients.Add(new ClientRow
                {
                    Key = "user_saved",
                    Label = "Imported",
                    ClientId = legacy.ClientId,
                    // Always re-protect: the plaintext secret (from either path) is
                    // protected here so it round-trips correctly on load.
                    ClientSecretProtected = legacy.ClientSecret is null
                        ? null
                        : _protector.Protect(legacy.ClientSecret),
                });
                file.ActiveKey = "user_saved";
            }
        }
        catch { /* corrupt legacy file — skip */ }

        file.MigratedLegacy = true;
        SaveFile(file);
    }

    private RegistryFile LoadFile()
    {
        if (!File.Exists(_path))
        {
            return new RegistryFile();
        }
        try
        {
            return JsonSerializer.Deserialize<RegistryFile>(File.ReadAllText(_path)) ?? new RegistryFile();
        }
        catch { return new RegistryFile(); }
    }

    private void SaveFile(RegistryFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class RegistryFile
    {
        [JsonPropertyName("activeKey")] public string ActiveKey { get; set; } = "";
        [JsonPropertyName("migratedLegacy")] public bool MigratedLegacy { get; set; }
        [JsonPropertyName("clients")] public List<ClientRow> Clients { get; set; } = new();
    }

    private sealed class ClientRow
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
        [JsonPropertyName("clientSecretProtected")] public string? ClientSecretProtected { get; set; }
    }

    private sealed class LegacyClient
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}

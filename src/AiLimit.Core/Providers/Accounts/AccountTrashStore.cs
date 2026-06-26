using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLimit.Core.Providers.Accounts;

public sealed class AccountTrashStore
{
    private readonly string _path;

    public AccountTrashStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiLimit",
            "account-trash.json");
    }

    public IReadOnlyList<TrashedAccountRecord> List(string? providerKey = null)
    {
        var items = LoadRaw()
            .Select(i => new TrashedAccountRecord(
                i.Id,
                i.ProviderKey,
                i.DisplayName,
                i.Email,
                i.DeletedAt,
                i.ResourcePath));

        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            items = items.Where(i => string.Equals(i.ProviderKey, providerKey, StringComparison.Ordinal));
        }

        return items
            .OrderByDescending(i => i.DeletedAt)
            .ToList()
            .AsReadOnly();
    }

    public bool Contains(string providerKey, Guid id)
    {
        return LoadRaw().Any(i => i.Id == id && string.Equals(i.ProviderKey, providerKey, StringComparison.Ordinal));
    }

    public void Put(TrashedAccountRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ProviderKey))
        {
            throw new ArgumentException("Provider key is required.", nameof(record));
        }

        var existing = LoadRaw();
        existing.RemoveAll(i => i.Id == record.Id && string.Equals(i.ProviderKey, record.ProviderKey, StringComparison.Ordinal));
        existing.Add(new StoredTrashItem
        {
            Id = record.Id,
            ProviderKey = record.ProviderKey,
            DisplayName = record.DisplayName,
            Email = record.Email,
            DeletedAt = record.DeletedAt,
            ResourcePath = record.ResourcePath
        });
        WriteRaw(existing);
    }

    public void Remove(string providerKey, Guid id)
    {
        var existing = LoadRaw();
        var removed = existing.RemoveAll(i => i.Id == id && string.Equals(i.ProviderKey, providerKey, StringComparison.Ordinal));
        if (removed > 0)
        {
            WriteRaw(existing);
        }
    }

    private List<StoredTrashItem> LoadRaw()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var payload = JsonSerializer.Deserialize<StoredTrashPayload>(File.ReadAllText(_path));
            return payload?.Items?
                .Where(i => i.Id != Guid.Empty && !string.IsNullOrWhiteSpace(i.ProviderKey))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteRaw(List<StoredTrashItem> items)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new StoredTrashPayload { Items = items };
        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
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

    private sealed class StoredTrashPayload
    {
        [JsonPropertyName("items")]
        public List<StoredTrashItem> Items { get; set; } = [];
    }

    private sealed class StoredTrashItem
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("provider_key")]
        public string ProviderKey { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("deleted_at")]
        public DateTimeOffset DeletedAt { get; set; }

        [JsonPropertyName("resource_path")]
        public string? ResourcePath { get; set; }
    }
}

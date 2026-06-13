using AiLimit.Core.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLimit.Core.Storage;

public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly JsonFileStore<UsageSnapshot> _store;
    private readonly JsonFileStore<IReadOnlyList<UsageSnapshot>> _allStore;

    public SnapshotStore(string path)
    {
        _path = path;
        _store = new JsonFileStore<UsageSnapshot>(path);
        _allStore = new JsonFileStore<IReadOnlyList<UsageSnapshot>>(path);
    }

    public Task<UsageSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        return _store.LoadAsync(cancellationToken);
    }

    public Task SaveAsync(UsageSnapshot snapshot, CancellationToken cancellationToken)
    {
        return _store.SaveAsync(snapshot, cancellationToken);
    }

    public async Task<IReadOnlyList<UsageSnapshot>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var info = new FileInfo(_path);
            if (info.Length == 0)
            {
                return [];
            }

            await using var stream = File.OpenRead(_path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var json = document.RootElement.GetRawText();
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<IReadOnlyList<UsageSnapshot>>(json, Options) ?? [];
            }

            var snapshot = JsonSerializer.Deserialize<UsageSnapshot>(json, Options);
            return snapshot is null ? [] : [snapshot];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public Task SaveAllAsync(IReadOnlyList<UsageSnapshot> snapshots, CancellationToken cancellationToken)
    {
        return _allStore.SaveAsync(snapshots, cancellationToken);
    }
}

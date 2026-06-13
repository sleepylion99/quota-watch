using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLimit.Core.Storage;

public sealed class JsonFileStore<T>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public JsonFileStore(string path)
    {
        _path = path;
    }

    public async Task<T?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return default;
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? "." : directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // A leftover temp file is safer than losing the existing settings file.
            }
        }
    }
}

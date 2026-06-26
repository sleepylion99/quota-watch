using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLimit.Core.Providers.Accounts;

public sealed class AntigravityActiveSelection
{
    private readonly string _path;

    public AntigravityActiveSelection(string path)
    {
        _path = path;
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiLimit",
            "antigravity-active-account.json");
    }

    public Guid? Get()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(_path));
            return payload?.ActiveId;
        }
        catch
        {
            return null;
        }
    }

    public void Set(Guid? id)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new Payload { ActiveId = id };
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

    private sealed class Payload
    {
        [JsonPropertyName("active_id")]
        public Guid? ActiveId { get; set; }
    }
}

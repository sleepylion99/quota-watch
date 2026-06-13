using AiLimit.Core.Storage;

namespace AiLimit.Core.Settings;

public sealed class SettingsStore
{
    private readonly JsonFileStore<AppSettings> _store;

    public SettingsStore(string path)
    {
        _store = new JsonFileStore<AppSettings>(path);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? AppSettings.Default;
            return settings.Normalize();
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        return _store.SaveAsync(settings, cancellationToken);
    }
}

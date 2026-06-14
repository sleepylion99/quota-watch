using System.IO;

namespace AiLimit.App.Services;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiLimit");

    public static string SettingsFile => Path.Combine(AppDataDirectory, "settings.json");

    public static string SnapshotsFile => Path.Combine(AppDataDirectory, "snapshots.json");

    public static string UsageHistoryFile => Path.Combine(AppDataDirectory, "history.json");
}

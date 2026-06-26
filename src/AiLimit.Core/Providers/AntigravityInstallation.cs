using Microsoft.Win32;
using System.Runtime.Versioning;

namespace AiLimit.Core.Providers;

internal static class AntigravityInstallation
{
    public static bool IsProbablyInstalled()
    {
        return CandidatePaths().Any(path => File.Exists(path) || Directory.Exists(path))
            || (OperatingSystem.IsWindows() && HasUninstallRegistryEntry());
    }

    internal static IEnumerable<string> InstallRootCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Antigravity");
            yield return Path.Combine(localAppData, "Programs", "Antigravity IDE");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Antigravity");
            yield return Path.Combine(programFiles, "Antigravity IDE");
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Antigravity");
            yield return Path.Combine(localAppData, "Programs", "Antigravity IDE");
            yield return Path.Combine(localAppData, "Programs", "Antigravity", "Antigravity.exe");
            yield return Path.Combine(localAppData, "Programs", "Antigravity IDE", "Antigravity.exe");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "Antigravity");
            yield return Path.Combine(appData, "Antigravity IDE");
        }

        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        if (!string.IsNullOrWhiteSpace(startMenu))
        {
            yield return Path.Combine(startMenu, "Programs", "Antigravity.lnk");
            yield return Path.Combine(startMenu, "Programs", "Antigravity IDE.lnk");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool HasUninstallRegistryEntry()
    {
        return HasUninstallRegistryEntry(Registry.CurrentUser)
            || HasUninstallRegistryEntry(Registry.LocalMachine);
    }

    [SupportedOSPlatform("windows")]
    private static bool HasUninstallRegistryEntry(RegistryKey root)
    {
        foreach (var subkeyPath in new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        })
        {
            try
            {
                using var uninstall = root.OpenSubKey(subkeyPath);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(name);
                    var displayName = app?.GetValue("DisplayName") as string;
                    if (displayName?.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Installation detection is best-effort only.
            }
        }

        return false;
    }
}

using Microsoft.Win32;

namespace AiLimit.App.Services;

public sealed class RegistrySystemThemeProbe : ISystemThemeProbe
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    public bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }
}

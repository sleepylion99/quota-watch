using AiLimit.App.Localization;
using AiLimit.App.ViewModels;
using AiLimit.Core.Domain;
using AiLimit.Core.Settings;

namespace AiLimit.App.Tray;

public static class LimitWarningText
{
    public static string Title(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.LimitWarningTitle);
    }

    public static string WeeklyTitle(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.LimitWarningWeeklyTitle);
    }

    public static string SuppressWeeklyButton(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.LimitWarningSuppressWeekly);
    }

    public static string DismissButton(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.LimitWarningDismiss);
    }

    public static string BuildBody(LimitWarning warning, AppLanguage language)
    {
        var label = LocalizeWindowLabel(warning, language);
        return warning.IsUsedPercent
            ? AppText.Get(language, AppStringKeys.LimitWarningBodyUsedPercent, warning.ProviderName, label, warning.Percent)
            : AppText.Get(language, AppStringKeys.LimitWarningBodyRemaining, warning.ProviderName, label, warning.Percent);
    }

    private static string LocalizeWindowLabel(LimitWarning warning, AppLanguage language)
    {
        var window = new UsageWindow(warning.WindowId, warning.WindowLabel, warning.Percent, warning.ResetAt, null, "high");
        return UsageWindowLabelText.For(window, language);
    }
}

using System.Globalization;
using System.Resources;
using AiLimit.Core.Settings;

namespace AiLimit.App.Localization;

internal static class AppText
{
    private static readonly ResourceManager Resources = new(
        "AiLimit.App.Localization.AppStrings",
        typeof(AppText).Assembly);

    public static string Get(AppLanguage language, string name, params object[] args)
    {
        var culture = CultureFor(AppLanguageResolver.Resolve(language));
        var text = Resources.GetString(name, culture)
            ?? Resources.GetString(name, CultureInfo.InvariantCulture)
            ?? name;

        return args.Length == 0
            ? text
            : string.Format(culture, text, args);
    }

    private static CultureInfo CultureFor(AppLanguage language)
    {
        return language switch
        {
            AppLanguage.Korean => CultureInfo.GetCultureInfo("ko"),
            AppLanguage.Japanese => CultureInfo.GetCultureInfo("ja"),
            AppLanguage.Chinese => CultureInfo.GetCultureInfo("zh-Hans"),
            _ => CultureInfo.InvariantCulture
        };
    }
}

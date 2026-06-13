using AiLimit.Core.Settings;
using AiLimit.App.ViewModels;

namespace AiLimit.App.Localization;

internal static class AntigravityLocalizedText
{
    public static string OAuthDetail(AppLanguage language)
    {
        return AppText.Get(
            language,
            AppStringKeys.AntigravityOAuthDetail,
            ClientIdLabel(language),
            ClientSecretLabel(language));
    }

    public static string ClientIdLabel(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.AntigravityOAuthClientIdLabel);
    }

    public static string ClientSecretLabel(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.AntigravityOAuthClientSecretLabel);
    }

    public static string OAuthStatusMessage(AppLanguage language, AntigravityOAuthStatusKind status)
    {
        return status switch
        {
            AntigravityOAuthStatusKind.MissingInput => AppText.Get(
                language,
                AppStringKeys.AntigravityOAuthMissingInput,
                ClientIdLabel(language),
                ClientSecretLabel(language)),
            AntigravityOAuthStatusKind.Saved => AppText.Get(language, AppStringKeys.AntigravityOAuthSaved),
            AntigravityOAuthStatusKind.SaveFailed => AppText.Get(language, AppStringKeys.AntigravityOAuthSaveFailed),
            AntigravityOAuthStatusKind.Cleared => AppText.Get(language, AppStringKeys.AntigravityOAuthCleared),
            AntigravityOAuthStatusKind.ClearFailed => AppText.Get(language, AppStringKeys.AntigravityOAuthClearFailed),
            AntigravityOAuthStatusKind.LoadFailed => AppText.Get(language, AppStringKeys.AntigravityOAuthLoadFailed),
            AntigravityOAuthStatusKind.SavedExists => AppText.Get(language, AppStringKeys.AntigravityOAuthSavedExists),
            _ => AppText.Get(language, AppStringKeys.AntigravityOAuthMissing)
        };
    }

    public static string MissingOAuthClientError(AppLanguage language)
    {
        return AppText.Get(
            language,
            AppStringKeys.AntigravityMissingOAuthClientError,
            AppText.Get(language, AppStringKeys.AntigravityOAuthSettingsLabel),
            ClientIdLabel(language),
            ClientSecretLabel(language));
    }

    public static string CloudSetupError(AppLanguage language)
    {
        return AppText.Get(language, AppStringKeys.AntigravityCloudSetupError);
    }
}

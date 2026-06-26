using AiLimit.Core.Providers;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class ProviderSettingsTests
{
    [Fact]
    public void LanguageCatalogUsesReadableLocalizedLabels()
    {
        var system = AppLanguageCatalog.SupportedLanguages.Single(item => item.Language == AppLanguage.System);
        var korean = AppLanguageCatalog.SupportedLanguages.Single(item => item.Language == AppLanguage.Korean);
        var english = AppLanguageCatalog.SupportedLanguages.Single(item => item.Language == AppLanguage.English);
        var japanese = AppLanguageCatalog.SupportedLanguages.Single(item => item.Language == AppLanguage.Japanese);
        var chinese = AppLanguageCatalog.SupportedLanguages.Single(item => item.Language == AppLanguage.Chinese);

        Assert.Equal("시스템 기본", AppLanguageCatalog.LabelFor(system, AppLanguage.Korean));
        Assert.Equal("システム既定", AppLanguageCatalog.LabelFor(system, AppLanguage.Japanese));
        Assert.Equal("系统默认", AppLanguageCatalog.LabelFor(system, AppLanguage.Chinese));
        Assert.Equal("한국어", AppLanguageCatalog.LabelFor(korean, AppLanguage.Korean));
        Assert.Equal("영어", AppLanguageCatalog.LabelFor(english, AppLanguage.Korean));
        Assert.Equal("日本語", AppLanguageCatalog.LabelFor(japanese, AppLanguage.Japanese));
        Assert.Equal("中文", AppLanguageCatalog.LabelFor(chinese, AppLanguage.Chinese));
    }

    [Fact]
    public void DefaultSettingsEnableEveryKnownProvider()
    {
        var settings = AppSettings.Default;

        var effectiveProviders = settings.GetEffectiveProviders();

        Assert.All(ProviderCatalog.KnownProviders, provider =>
        {
            var providerSetting = Assert.Single(effectiveProviders, item => item.Id == provider.Id);
            Assert.Equal(provider.DisplayName, providerSetting.DisplayName);
            Assert.True(providerSetting.IsEnabled);
        });
    }

    [Fact]
    public void EffectiveProvidersAddsMissingCatalogDefaults()
    {
        var settings = AppSettings.Default with
        {
            Providers =
            [
                new ProviderSetting("codex", "ChatGPT Codex", false)
            ]
        };

        var effective = settings.GetEffectiveProviders();

        Assert.False(effective.Single(item => item.Id == "codex").IsEnabled);
        Assert.Contains(effective, item => item.Id == "claude" && item.IsEnabled);
        Assert.Contains(effective, item => item.Id == "gemini-pro" && item.IsEnabled);
    }

    [Fact]
    public void EffectiveProvidersPreserveSupportedCodexModes()
    {
        var settings = AppSettings.Default with
        {
            Providers =
            [
                new ProviderSetting("codex", "ChatGPT Codex", true, "pro"),
                new ProviderSetting("gemini-pro", "Google Antigravity", true, "whatever")
            ]
        };

        var effective = settings.GetEffectiveProviders();

        Assert.Equal("pro", effective.Single(provider => provider.Id == "codex").Mode);
        Assert.Null(effective.Single(provider => provider.Id == "gemini-pro").Mode);
    }

    [Fact]
    public void EffectiveProvidersNormalizesUnknownModes()
    {
        var settings = AppSettings.Default with
        {
            Providers =
            [
                new ProviderSetting("codex", "ChatGPT Codex", true, "whatever"),
                new ProviderSetting("gemini-pro", "Google Antigravity", true, "whatever")
            ]
        };

        var effective = settings.GetEffectiveProviders();

        Assert.Null(effective.Single(provider => provider.Id == "codex").Mode);
        Assert.Null(effective.Single(provider => provider.Id == "gemini-pro").Mode);
    }

    [Fact]
    public void NormalizePersistsCatalogNamesAndSupportedProviderModes()
    {
        var settings = AppSettings.Default with
        {
            Providers =
            [
                new ProviderSetting("codex", "ChatGPT Codex", true, "pro"),
                new ProviderSetting("gemini-pro", "Gemini Pro", true)
            ]
        };

        var normalized = settings.Normalize();

        Assert.Equal("pro", normalized.Providers!.Single(provider => provider.Id == "codex").Mode);
        var antigravity = normalized.Providers!.Single(provider => provider.Id == "gemini-pro");
        Assert.Equal("Google Antigravity", antigravity.DisplayName);
        Assert.Null(antigravity.Mode);
    }

}

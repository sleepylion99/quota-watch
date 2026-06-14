using AiLimit.App.ViewModels;
using AiLimit.App.Services;
using AiLimit.App.Theming;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class UsageOverviewViewModelTests
{
    [Theory]
    [InlineData(100, false, BrushKey.UrgencyLow)]
    [InlineData(49, false, BrushKey.UrgencyMedium)]
    [InlineData(20, false, BrushKey.UrgencyHigh)]
    [InlineData(10, false, BrushKey.UrgencyCritical)]
    [InlineData(10, true, BrushKey.UrgencyLow)]
    [InlineData(50, true, BrushKey.UrgencyMedium)]
    [InlineData(80, true, BrushKey.UrgencyHigh)]
    [InlineData(90, true, BrushKey.UrgencyCritical)]
    public void ComputeUrgencyBrushUsesCorrectBandsForRemainingAndUsedPercent(
        int percent,
        bool isUsedPercent,
        string expectedBrush)
    {
        Assert.Equal(expectedBrush, ProviderUsageItemViewModel.ComputeUrgencyBrush(percent, isUsedPercent));
    }

    [Fact]
    public void UpdateExposesSemanticBrushKeysForDashboardBindings()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-12T13:30:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("weekly", "Current week", 90, null, null, "high", IsUsedPercent: true),
                new UsageWindow("five-hour", "Current session", 10, null, null, "high", IsUsedPercent: true)
            ],
            SourceChannel: "cloud");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.English);

        Assert.Equal(BrushKey.StatusFresh, viewModel.StatusBrush);
        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal(BrushKey.StatusFresh, provider.StatusBrush);
        Assert.Equal(BrushKey.BadgeOkFg, provider.StatusBadgeForeground);
        Assert.Equal(BrushKey.BadgeOkBg, provider.StatusBadgeBackground);
        Assert.Equal(BrushKey.BadgeOkBorder, provider.StatusBadgeBorderBrush);
        Assert.Equal(BrushKey.BadgeCloudFg, provider.SourceBadgeForeground);
        Assert.Equal(BrushKey.BadgeCloudBg, provider.SourceBadgeBackground);
        Assert.Equal(BrushKey.BadgeCloudBorder, provider.SourceBadgeBorderBrush);
        Assert.Equal(BrushKey.BrandClaude, provider.BrandColorBrush);
        Assert.Equal(BrushKey.UrgencyLow, provider.UrgencyBrush);
        Assert.Equal(BrushKey.UrgencyCritical, Assert.Single(provider.Windows).UrgencyBrush);
    }

    [Fact]
    public void RaiseColorPropertiesRefreshesRootProviderAndWindowBrushBindings()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-12T13:30:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("weekly", "Current week", 90, null, null, "high", IsUsedPercent: true),
                new UsageWindow("five-hour", "Current session", 10, null, null, "high", IsUsedPercent: true)
            ],
            SourceChannel: "cloud");
        var viewModel = new UsageViewModel();
        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.English);
        var provider = Assert.Single(viewModel.Providers);
        var window = Assert.Single(provider.Windows);
        var rootChanges = new List<string?>();
        var providerChanges = new List<string?>();
        var windowChanges = new List<string?>();
        viewModel.PropertyChanged += (_, args) => rootChanges.Add(args.PropertyName);
        provider.PropertyChanged += (_, args) => providerChanges.Add(args.PropertyName);
        window.PropertyChanged += (_, args) => windowChanges.Add(args.PropertyName);

        viewModel.RaiseColorProperties();

        Assert.Contains(nameof(UsageViewModel.StatusBrush), rootChanges);
        Assert.Contains(nameof(ProviderUsageItemViewModel.StatusBrush), providerChanges);
        Assert.Contains(nameof(ProviderUsageItemViewModel.BrandColorBrush), providerChanges);
        Assert.Contains(nameof(ProviderUsageItemViewModel.UrgencyBrush), providerChanges);
        Assert.Contains(nameof(UsageWindowItemViewModel.UrgencyBrush), windowChanges);
    }

    [Fact]
    public void UpdateBuildsLocalizedThemeOptionsAndSelectsCurrentMode()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [],
            isRefreshing: false,
            LimitDisplayMode.Bars,
            AppLanguage.Korean,
            themeMode: AppThemeMode.Light);

        Assert.Equal("테마", viewModel.ThemeSettingsTitleText);
        Assert.Equal("대시보드 외형을 선택하세요.", viewModel.ThemeSettingsDetailText);
        Assert.Equal("자동은 Windows 앱 테마를 따릅니다.", viewModel.ThemeSystemHintText);
        Assert.Collection(
            viewModel.ThemeOptions,
            option =>
            {
                Assert.Equal(AppThemeMode.Dark, option.Mode);
                Assert.Equal("다크", option.Label);
                Assert.False(option.IsSelected);
            },
            option =>
            {
                Assert.Equal(AppThemeMode.Light, option.Mode);
                Assert.Equal("라이트", option.Label);
                Assert.True(option.IsSelected);
            },
            option =>
            {
                Assert.Equal(AppThemeMode.System, option.Mode);
                Assert.Equal("자동", option.Label);
                Assert.False(option.IsSelected);
            });
    }

    [Fact]
    public void UpdateShowsEmptyEnglishGuidanceWhenNoSnapshotsExist()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update([], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.English);

        Assert.Equal("Refresh to load usage", viewModel.GuidanceTitle);
        Assert.Equal("No usage data is loaded yet.", viewModel.GuidanceDetail);
        Assert.Equal("Use Refresh All to check your current limits.", viewModel.GuidanceAction);
        Assert.Equal("Refresh to load usage", viewModel.WidgetGuidanceText);
    }

    [Fact]
    public void UpdateShowsRefreshingKoreanGuidance()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update([], isRefreshing: true, LimitDisplayMode.BothText, AppLanguage.Korean);

        Assert.Equal("최신 사용량 확인 중", viewModel.GuidanceTitle);
        Assert.Equal("잠시만 기다리면 한도 상태가 업데이트됩니다.", viewModel.GuidanceDetail);
        Assert.Equal("이전 데이터가 있으면 계속 표시합니다.", viewModel.GuidanceAction);
        Assert.Equal("최신 사용량 확인 중", viewModel.WidgetGuidanceText);
    }

    [Fact]
    public void UpdateShowsFreshEnglishGuidanceForSnapshots()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-05-18T02:00:00+09:00"),
            UsageSource.Mock,
            UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5-hour limit", 63, null, null, "high")]);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.English);

        Assert.Equal("Usage is up to date", viewModel.GuidanceTitle);
        Assert.Equal("All tracked models are reporting normally.", viewModel.GuidanceDetail);
        Assert.Equal("You can keep working.", viewModel.GuidanceAction);
        Assert.Equal("Up to date · keep working", viewModel.WidgetGuidanceText);
        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Fresh", ReadStringProperty(provider, "StatusBadgeText"));
    }

    [Fact]
    public void UpdateUsesFriendlyDisplayModeLabels()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update([], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        Assert.Equal("5시간 한도", viewModel.FiveHourModeText);
        Assert.Equal("주간 한도", viewModel.WeeklyModeText);
        Assert.Equal("5시간 + 주간", viewModel.BothModeText);
        Assert.Equal("그래프로 보기", viewModel.BarsModeText);

        viewModel.Update([], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.English);

        Assert.Equal("5h limit", viewModel.FiveHourModeText);
        Assert.Equal("Weekly limit", viewModel.WeeklyModeText);
        Assert.Equal("5h + weekly", viewModel.BothModeText);
        Assert.Equal("Graph view", viewModel.BarsModeText);
    }

    [Fact]
    public void UpdateLocalizesKoreanUsageWindowLabels()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-05-18T02:00:00+09:00"),
            UsageSource.Mock,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "5-hour limit", 63, null, null, "high"),
                new UsageWindow("weekly", "Weekly limit", 41, null, null, "medium")
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("5시간 한도", provider.PrimaryLabel);
        var detail = Assert.Single(provider.Windows);
        Assert.Equal("주간 한도", detail.Label);
        Assert.Equal("41% 남음", detail.PercentText);
    }

    [Fact]
    public void UpdateFormatsAntigravityWindowsAsUsedPercent()
    {
        var snapshot = AntigravitySnapshot();
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.False(provider.ShowPrimarySummary);
        Assert.False(provider.ShowPrimaryProgress);
        Assert.Collection(
            provider.Windows,
            window =>
            {
                Assert.Equal("Claude Sonnet (Thinking)", window.Label);
                Assert.Equal("20% used", window.PercentText);
            },
            window =>
            {
                Assert.Equal("Gemini Flash (Medium)", window.Label);
                Assert.Equal("0% used", window.PercentText);
            });
    }

    [Fact]
    public void UpdateDoesNotDuplicatePrimaryPredictionForAntigravity()
    {
        var snapshot = AntigravitySnapshot();
        var firstWindow = snapshot.Windows.OrderByDescending(window => window.PercentRemaining).First();
        var prediction = new DepletionPrediction(
            PredictionState.WaitingForChange,
            null,
            null,
            0,
            [
                new UsageSample(snapshot.ProviderId, firstWindow.Id, snapshot.CheckedAt.AddMinutes(-3), 0),
                new UsageSample(snapshot.ProviderId, firstWindow.Id, snapshot.CheckedAt, 0)
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [snapshot],
            isRefreshing: false,
            LimitDisplayMode.Bars,
            AppLanguage.Korean,
            predictions: new Dictionary<UsageWindowKey, DepletionPrediction>
            {
                [new(snapshot.ProviderId, firstWindow.Id)] = prediction
            });

        var provider = Assert.Single(viewModel.Providers);
        Assert.False(provider.ShowPrediction);
        Assert.True(provider.Windows[0].ShowPrediction);
    }

    [Fact]
    public void UpdateFormatsClaudeWindowsAsUsedPercentWithUsageBasedColors()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-12T13:30:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "Current session", 10, null, null, "high", IsUsedPercent: true),
                new UsageWindow("weekly", "Current week (all models)", 90, null, null, "high", IsUsedPercent: true)
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowPrimarySummary);
        Assert.Equal("5시간 한도", provider.PrimaryLabel);
        Assert.Equal("10% 사용", provider.PrimaryPercentText);
        Assert.Collection(
            provider.Windows,
            window =>
            {
                Assert.Equal("주간 한도", window.Label);
                Assert.Equal("90% 사용", window.PercentText);
                Assert.Equal(BrushKey.UrgencyCritical, window.UrgencyBrush);
            });
    }

    [Fact]
    public void UpdateKeepsAntigravityStrengthVisibleForRepeatedModelFamilies()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("antigravity-gemini-3-5-flash-medium", "Gemini 3.5 Flash (Medium)", 0, null, null, "medium", IsUsedPercent: true),
                new UsageWindow("antigravity-gemini-3-5-flash-high", "Gemini 3.5 Flash (High)", 78, null, null, "high", IsUsedPercent: true),
                new UsageWindow("antigravity-gemini-3-5-flash-low", "Gemini 3.5 Flash (Low)", 0, null, null, "low", IsUsedPercent: true),
                new UsageWindow("antigravity-claude-sonnet-4-6-thinking", "Claude Sonnet 4.6 (Thinking)", 0, null, null, "medium", IsUsedPercent: true)
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal(
            ["Gemini Flash (High)", "Gemini Flash (Medium)", "Gemini Flash (Low)", "Claude Sonnet (Thinking)"],
            provider.Windows.Select(window => window.Label).ToArray());
    }

    [Theory]
    [InlineData(LimitDisplayMode.FiveHourOnly)]
    [InlineData(LimitDisplayMode.WeeklyOnly)]
    [InlineData(LimitDisplayMode.BothText)]
    [InlineData(LimitDisplayMode.Bars)]
    public void UpdateKeepsAntigravityModelSelectionConsistentAcrossDisplayModes(LimitDisplayMode displayMode)
    {
        var viewModel = new UsageViewModel();

        viewModel.Update([AntigravitySnapshot()], isRefreshing: false, displayMode, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.False(provider.ShowPrimarySummary);
        Assert.False(provider.ShowPrimaryProgress);
    }

    [Fact]
    public void FailedProviderWithoutWindowsDoesNotShowEmptyPrimaryProgress()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Claude OAuth credentials were not found. Run Claude Code login first.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowPrimarySummary);
        Assert.False(provider.ShowPrimaryProgress);
    }

    [Fact]
    public void UpdateSortsProvidersByMostStressedFirst()
    {
        var snapshots = new[]
        {
            new UsageSnapshot(
                "codex",
                "ChatGPT Codex",
                DateTimeOffset.Parse("2026-05-30T02:00:00+09:00"),
                UsageSource.Agent,
                UsageStatus.Fresh,
                [new UsageWindow("five-hour", "5-hour limit", 91, null, null, "high")]),
            AntigravitySnapshot()
        };
        var viewModel = new UsageViewModel();

        viewModel.Update(snapshots, isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.English);

        Assert.Equal(
            ["Google Antigravity", "ChatGPT Codex"],
            viewModel.Providers.Select(provider => provider.ProviderName).ToArray());
    }

    [Fact]
    public void BothTextModeShowsOnlySecondaryWindowDetailsWithResetText()
    {
        var snapshot = SnapshotWithResetLabels();
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("5시간 한도", provider.PrimaryLabel);
        var detail = Assert.Single(provider.Windows);
        Assert.Equal("주간 한도", detail.Label);
        Assert.Equal("주간 초기화", detail.ResetText);
        Assert.Equal("41% 남음", detail.PercentText);
    }

    [Fact]
    public void WeeklyModeSelectsProDetailedSecondaryWindow()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-05-31T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "5-hour limit", 70, null, null, "high"),
                new UsageWindow("weekly", "Weekly limit", 60, null, null, "high"),
                new UsageWindow("codex-spark-primary", "GPT-5.3-Codex-Spark 5h limit", 88, null, null, "high"),
                new UsageWindow("codex-spark-secondary", "GPT-5.3-Codex-Spark weekly limit", 96, null, null, "high")
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.WeeklyOnly, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("GPT-5.3-Codex-Spark weekly limit", provider.PrimaryLabel);
        Assert.Equal("96% left", provider.PrimaryPercentText);
    }

    [Fact]
    public void BarsModeSkipsFirstWindowForNonUsedPercentWithResetText()
    {
        var snapshot = SnapshotWithResetLabels();
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        var detail = Assert.Single(provider.Windows);
        Assert.Equal("주간 한도", detail.Label);
        Assert.Equal("주간 초기화", detail.ResetText);
    }

    [Fact]
    public void UpdateShowsLocalizedProviderSettingsRows()
    {
        var viewModel = new UsageViewModel();
        var providers = AppSettings.Default.GetEffectiveProviders();

        viewModel.Update([], false, LimitDisplayMode.BothText, AppLanguage.Korean, providers);

        Assert.Equal("모델 설정", viewModel.ModelSettingsButtonText);
        Assert.Equal("대시보드 동작과 지원 기능을 한곳에서 조정합니다.", viewModel.SettingsPanelDetailText);
        Assert.Equal("대시보드에 표시할 공급자를 선택합니다.", viewModel.ModelSettingsDetailText);
        Assert.Equal("언어", viewModel.LanguageSettingsTitleText);
        Assert.Equal("한도 제한 알림", viewModel.LimitWarningSettingsTitleText);
        Assert.Equal("한도 제한 알림 사용", viewModel.LimitWarningEnabledText);
        Assert.Equal("알림 기준", viewModel.LimitWarningThresholdText);
        Assert.True(viewModel.IsLimitWarningEnabled);
        Assert.Collection(
            viewModel.LimitWarningProviderSettings,
            provider =>
            {
                Assert.Equal("codex", provider.ProviderId);
                Assert.Equal("ChatGPT Codex", provider.ProviderName);
                Assert.Equal("남은 10% 이하", provider.ValueText);
                Assert.False(provider.IsCustom);
                Assert.False(provider.IsSliderEnabled);
                Assert.Equal(
                    ["낮음 · 남은 10% 이하", "보통 · 남은 20% 이하", "여유 · 남은 30% 이하"],
                    provider.Recommendations.Select(option => option.Label).ToArray());
            },
            provider =>
            {
                Assert.Equal("claude", provider.ProviderId);
                Assert.Equal("Claude Code", provider.ProviderName);
                Assert.Equal("90% 사용 이상", provider.ValueText);
                Assert.Equal(
                    ["낮음 · 90% 사용 이상", "보통 · 80% 사용 이상", "여유 · 70% 사용 이상"],
                    provider.Recommendations.Select(option => option.Label).ToArray());
            },
            provider =>
            {
                Assert.Equal("gemini-pro", provider.ProviderId);
                Assert.Equal("Google Antigravity", provider.ProviderName);
                Assert.Equal("90% 사용 이상", provider.ValueText);
            });
        Assert.Equal("진단 로그", viewModel.DiagnosticLogTitleText);
        Assert.Equal("추적", viewModel.TrackProviderLabel);
        Assert.Collection(
            viewModel.LanguageOptions,
            option =>
            {
                Assert.Equal(AppLanguage.System, option.Language);
                Assert.Equal("AUTO", option.Code);
                Assert.False(option.IsSelected);
            },
            option =>
            {
                Assert.Equal(AppLanguage.Korean, option.Language);
                Assert.Equal("KO", option.Code);
                Assert.True(option.IsSelected);
            },
            option => Assert.Equal(AppLanguage.English, option.Language),
            option => Assert.Equal(AppLanguage.Japanese, option.Language),
            option => Assert.Equal(AppLanguage.Chinese, option.Language));
        Assert.Equal(providers.Count, viewModel.ProviderSettings.Count);
        Assert.All(viewModel.ProviderSettings, row => Assert.True(row.IsEnabled));
    }

    [Fact]
    public void UpdateShowsSelectedLimitWarningSettings()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [],
            false,
            LimitDisplayMode.BothText,
            AppLanguage.Korean,
            isLimitWarningEnabled: false,
            limitWarningThresholdPercent: 20,
            limitWarningSettings:
            [
                new ProviderLimitWarningSetting("codex", 17, IsCustom: true),
                new ProviderLimitWarningSetting("claude", 83),
                new ProviderLimitWarningSetting("gemini-pro", 76, IsCustom: true)
            ]);

        Assert.False(viewModel.IsLimitWarningEnabled);
        Assert.Collection(
            viewModel.LimitWarningProviderSettings,
            provider =>
            {
                Assert.Equal(17, provider.ThresholdPercent);
                Assert.True(provider.IsCustom);
                Assert.True(provider.IsSliderEnabled);
                Assert.Equal("남은 17% 이하", provider.ValueText);
            },
            provider =>
            {
                Assert.Equal(83, provider.ThresholdPercent);
                Assert.True(provider.IsCustom);
                Assert.True(provider.IsSliderEnabled);
                Assert.Equal("83% 사용 이상", provider.ValueText);
            },
            provider =>
            {
                Assert.Equal(76, provider.ThresholdPercent);
                Assert.True(provider.IsCustom);
                Assert.True(provider.IsSliderEnabled);
                Assert.Equal("76% 사용 이상", provider.ValueText);
            });
    }

    [Fact]
    public void CustomLimitWarningSliderUpdatesOnlyItsProviderValueText()
    {
        var viewModel = new UsageViewModel();
        viewModel.Update(
            [],
            false,
            LimitDisplayMode.BothText,
            AppLanguage.Korean,
            limitWarningSettings:
            [
                new ProviderLimitWarningSetting("codex", 17, IsCustom: true),
                new ProviderLimitWarningSetting("claude", 83, IsCustom: true),
                new ProviderLimitWarningSetting("gemini-pro", 76, IsCustom: true)
            ]);
        var claude = viewModel.LimitWarningProviderSettings[1];

        claude.SetCustomThreshold(86);

        Assert.Equal(86, claude.ThresholdPercent);
        Assert.Equal("86% 사용 이상", claude.ValueText);
        Assert.Equal(17, viewModel.LimitWarningProviderSettings[0].ThresholdPercent);
        Assert.Equal(76, viewModel.LimitWarningProviderSettings[2].ThresholdPercent);
    }

    [Fact]
    public void UpdateShowsJapaneseAndChineseSettingsLabels()
    {
        var viewModel = new UsageViewModel();
        var snapshots = new[]
        {
            Snapshot("codex"),
            Snapshot("claude"),
            Snapshot("gemini-pro")
        };

        viewModel.Update(snapshots, false, LimitDisplayMode.BothText, AppLanguage.Japanese);

        Assert.Equal("3件のモデル", viewModel.HeaderTitle);
        Assert.Equal("設定", viewModel.SettingsButtonText);
        Assert.Equal("モデル設定", viewModel.ModelSettingsButtonText);
        Assert.Equal("言語", viewModel.LanguageSettingsTitleText);
        Assert.Equal("診断ログ", viewModel.DiagnosticLogTitleText);

        viewModel.Update(snapshots, false, LimitDisplayMode.BothText, AppLanguage.Chinese);

        Assert.Equal("3 个模型", viewModel.HeaderTitle);
        Assert.Equal("设置", viewModel.SettingsButtonText);
        Assert.Equal("模型设置", viewModel.ModelSettingsButtonText);
        Assert.Equal("语言", viewModel.LanguageSettingsTitleText);
        Assert.Equal("诊断日志", viewModel.DiagnosticLogTitleText);
    }

    [Fact]
    public void UpdateShowsSettingsApplyAndDiagnosticLogBugReportCopy()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update([], false, LimitDisplayMode.BothText, AppLanguage.Korean);

        Assert.Equal("적용", viewModel.SettingsApplyButtonText);
        Assert.Equal("적용을 누르면 변경 사항이 저장됩니다.", viewModel.SettingsImmediateApplyText);
        Assert.Equal("저장되지 않은 변경 사항", viewModel.SettingsUnsavedTitleText);
        Assert.Equal("변경 사항을 저장하지 않고 닫을까요?", viewModel.SettingsUnsavedDetailText);
        Assert.Equal("뒤로가기", viewModel.SettingsKeepEditingButtonText);
        Assert.Equal("버그를 발견하셨나요? 이 로그를 복사해서 개발자에게 보내주세요.", viewModel.DiagnosticLogDetailText);
    }

    [Fact]
    public void UpdateShowsAntigravityAutomaticCloudIdeSetupHint()
    {
        var viewModel = new UsageViewModel();
        var providers = AppSettings.Default.GetEffectiveProviders();

        viewModel.Update([], false, LimitDisplayMode.BothText, AppLanguage.English, providers);

        var antigravity = viewModel.ProviderSettings.Single(row => row.Id == "gemini-pro");
        Assert.True(antigravity.HasSetupHint);
        Assert.Contains("Automatic: Cloud first, then IDE fallback.", antigravity.SetupHintText);
        Assert.Contains("Cloud uses stored Antigravity OAuth tokens.", antigravity.SetupHintText);
        Assert.Contains("stored OAuth client values", antigravity.SetupHintText);
        Assert.Contains("ANTIGRAVITY_OAUTH_CLIENT_ID", antigravity.SetupHintText);
        Assert.Contains("ANTIGRAVITY_OAUTH_CLIENT_SECRET", antigravity.SetupHintText);
    }

    [Fact]
    public void UpdateLocalizesAntigravityOAuthSettingsText()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update([], false, LimitDisplayMode.Bars, AppLanguage.Korean);

        Assert.Contains("Google Cloud", viewModel.AntigravityOAuthDetailText);
        Assert.Contains("데스크톱 앱", viewModel.AntigravityOAuthDetailText);
        Assert.DoesNotContain("client ID and secret", viewModel.AntigravityOAuthDetailText);
        Assert.Equal("클라이언트 ID", viewModel.AntigravityOAuthClientIdLabelText);
        Assert.Equal("클라이언트 시크릿", viewModel.AntigravityOAuthClientSecretLabelText);
        Assert.Equal("저장", viewModel.AntigravityOAuthSaveButtonText);
        Assert.Equal("삭제", viewModel.AntigravityOAuthClearButtonText);
        Assert.Equal(
            "클라이언트 ID와 클라이언트 시크릿을 모두 입력하세요.",
            UsageViewModel.AntigravityOAuthStatusMessage(AppLanguage.Korean, AntigravityOAuthStatusKind.MissingInput));
    }

    [Theory]
    [InlineData(AppLanguage.English, "View setup guide")]
    [InlineData(AppLanguage.Korean, "발급 방법 보기")]
    [InlineData(AppLanguage.Japanese, "設定ガイドを見る")]
    [InlineData(AppLanguage.Chinese, "查看设置指南")]
    public void UpdateLocalizesAntigravityOAuthGuideButton(
        AppLanguage language,
        string expected)
    {
        var viewModel = new UsageViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.Update([], false, LimitDisplayMode.Bars, language);

        Assert.Equal(expected, viewModel.AntigravityOAuthGuideButtonText);
        Assert.Contains(nameof(UsageViewModel.AntigravityOAuthGuideButtonText), changedProperties);
    }

    [Fact]
    public void UpdateLocalizesAntigravityMissingOAuthClientSecretInKorean()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Google Antigravity token refresh failed because no OAuth client secret is available. Save your own OAuth client values in Settings > Antigravity OAuth.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Contains("클라이언트 ID", provider.LastErrorText);
        Assert.Contains("클라이언트 시크릿", provider.LastErrorText);
        Assert.Contains("설정 > Antigravity OAuth", provider.LastErrorText);
        Assert.DoesNotContain("client ID", provider.LastErrorText);
        Assert.DoesNotContain("client secret", provider.LastErrorText);
        Assert.DoesNotContain("secret", provider.LastErrorText);
    }

    [Fact]
    public void UpdatePrioritizesAntigravityMissingOAuthClientValuesOverTimeout()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity Cloud setup: Google Antigravity OAuth client values were not found. Details: Usage refresh timed out after 10 seconds.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("OAuth 설정 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("OAuth 설정 필요", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Contains("클라이언트 ID", provider.LastErrorText);
        Assert.Contains("클라이언트 시크릿", provider.LastErrorText);
        Assert.DoesNotContain("시간 초과", provider.LastErrorText);
    }

    [Fact]
    public void UpdateKeepsCodexModeInternalButDoesNotExposeModeOptions()
    {
        var viewModel = new UsageViewModel();
        var providers = AppSettings.Default.GetEffectiveProviders()
            .Select(provider => provider.Id == "codex" ? provider with { Mode = "pro" } : provider)
            .ToList();

        viewModel.Update([], false, LimitDisplayMode.BothText, AppLanguage.English, providers);

        var codex = viewModel.ProviderSettings.Single(row => row.Id == "codex");
        Assert.Equal("pro", codex.SelectedMode);
        Assert.All(viewModel.ProviderSettings, row => Assert.Empty(row.ModeOptions));
    }

    [Fact]
    public void UpdateShowsClaudeOAuthCredentialSetupHint()
    {
        var viewModel = new UsageViewModel();
        var providers = AppSettings.Default.GetEffectiveProviders();

        viewModel.Update([], false, LimitDisplayMode.BothText, AppLanguage.Korean, providers);

        var claude = viewModel.ProviderSettings.Single(row => row.Id == "claude");
        Assert.True(claude.HasSetupHint);
        Assert.Contains("Claude Code OAuth 인증 정보", claude.SetupHintText);
        Assert.Contains("계속 켜둘 필요는 없습니다", claude.SetupHintText);
    }

    [Fact]
    public void UpdateShowsLimitProfileForPlanSpecificCards()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "5-hour limit", 70, null, null, "high"),
                new UsageWindow("weekly", "Weekly limit", 60, null, null, "high"),
                new UsageWindow("codex-spark-primary", "GPT-5.3-Codex-Spark 5h limit", 88, null, null, "high"),
                new UsageWindow("codex-spark-secondary", "GPT-5.3-Codex-Spark weekly limit", 96, null, null, "high")
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [snapshot],
            false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            [new ProviderSetting("codex", "ChatGPT Codex", true, "pro")]);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Codex limits", provider.LimitProfileText);
        Assert.Contains("named extra buckets", provider.LimitProfileToolTipText);
        Assert.Collection(
            provider.Windows.Select(window => window.Label),
            label => Assert.Equal("Weekly limit", label),
            label => Assert.Equal("GPT-5.3-Codex-Spark 5h limit", label),
            label => Assert.Equal("GPT-5.3-Codex-Spark weekly limit", label));
    }

    [Fact]
    public void UnifiedClaudeCardShowsScreenshotRows()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "Current session", 70, null, null, "high"),
                new UsageWindow("weekly", "Current week (all models)", 60, null, null, "high"),
                new UsageWindow("weekly-sonnet", "Current week (Sonnet only)", 80, null, null, "high")
            ]);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Claude Code limits", provider.LimitProfileText);
        Assert.Equal("Current session", provider.PrimaryLabel);
        Assert.Collection(
            provider.Windows,
            window => Assert.Equal("Current week (all models)", window.Label),
            window => Assert.Equal("Current week (Sonnet only)", window.Label));
    }

    [Fact]
    public void UpdateLocalizesAntigravitySetupHintInKorean()
    {
        var viewModel = new UsageViewModel();
        var providers = AppSettings.Default.GetEffectiveProviders();

        viewModel.Update([], false, LimitDisplayMode.BothText, AppLanguage.Korean, providers);

        var antigravity = viewModel.ProviderSettings.Single(row => row.Id == "gemini-pro");
        Assert.Contains("클라우드를 먼저", antigravity.SetupHintText);
        Assert.DoesNotContain("Automatic:", antigravity.SetupHintText);
        Assert.DoesNotContain("Cloud first", antigravity.SetupHintText);
    }

    [Fact]
    public void UpdateLocalizesClaudeFailureGuidanceInKoreanCards()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Claude OAuth credentials were not found. Run Claude Code login first.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Contains("Claude Code가 설치되어 있지 않거나 로그인이 만료되었습니다.", provider.LastErrorText);
        Assert.DoesNotContain("Run Claude Code login first", provider.LastErrorText);
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Equal(
            "Claude Code가 설치되어 있지 않거나 로그인이 만료되었습니다. Claude Code를 설치하거나 다시 로그인한 뒤 새로고침하세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Theory]
    [InlineData("Claude Code was not found.")]
    [InlineData("claude command was not found.")]
    [InlineData("spawn claude ENOENT")]
    public void UpdateShowsClaudeNotFoundAsInstallLoginRequired(string error)
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            error);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal(
            "Claude Code가 설치되어 있지 않거나 로그인이 만료되었습니다. Claude Code를 설치하거나 다시 로그인한 뒤 새로고침하세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Fact]
    public void UpdateShowsCodexInstallLoginFailureTooltip()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Codex app-server failed: not installed or login expired.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Equal(
            "Codex가 설치되어 있지 않거나 로그인이 만료되었습니다. Codex를 설치하거나 다시 로그인한 뒤 새로고침하세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Theory]
    [InlineData("Codex OAuth token was not found in auth.json.")]
    [InlineData("codex command was not found.")]
    [InlineData("spawn codex ENOENT")]
    public void UpdateShowsCodexPreparationFailuresAsInstallLoginRequired(string error)
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            error);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal(
            "Codex가 설치되어 있지 않거나 로그인이 만료되었습니다. Codex를 설치하거나 다시 로그인한 뒤 새로고침하세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Fact]
    public void UpdateShowsAntigravityCloudSetupAsAuthRequired()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity Cloud setup failed.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("인증 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("인증 필요", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Equal(
            "인증이 없거나 만료되었습니다. 다시 로그인한 뒤 새로고침하세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Fact]
    public void UpdateShowsAntigravityMissingCredentialsAsAuthRequired()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Google Antigravity OAuth credentials were not found. Sign in to Antigravity with a Google account.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("인증 필요", ReadStringProperty(provider, "FailureBadgeText"));
    }

    [Theory]
    [InlineData("Antigravity quota was not available. Sign in to Antigravity with a Google account, or start Antigravity IDE as a fallback.", "한도 정보 없음")]
    [InlineData("IDE endpoint discovery returned no candidates.", "실행/로그인 필요")]
    public void UpdateShowsUnavailableAntigravityQuotaAsNoQuotaData(string error, string expectedBadge)
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            error);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal(expectedBadge, ReadStringProperty(provider, "FailureBadgeText"));
    }

    [Fact]
    public void UpdateExplainsWhenAntigravityIdeFallbackWasNotAvailable()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available. IDE fallback was not available because no endpoint was discovered.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("실행/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Contains("Antigravity를 실행", provider.LastErrorText);
    }

    [Fact]
    public void UpdateKeepsFallbackResultWhenAntigravityCloudAlsoTimedOut()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available. IDE fallback was not available because no endpoint was discovered. Last cloud error: Cloud request timed out.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("실행/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Contains("Antigravity를 실행", provider.LastErrorText);
        Assert.DoesNotContain("클라우드 조회가 지연되었습니다", provider.LastErrorText);
    }

    [Fact]
    public void UpdateExplainsWhenAntigravityIdeFallbackReturnedNoQuota()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available. IDE fallback was attempted but returned no quota buckets.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("한도 정보 없음", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Contains("IDE 폴백을 시도했지만", provider.LastErrorText);
    }

    [Fact]
    public void UpdateShowsNoValueBadgeWhenSnapshotHasNoWindowsOrError()
    {
        var snapshot = new UsageSnapshot(
            "other",
            "Other Provider",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            []);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("값 없음", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("값 없음", ReadStringProperty(provider, "StatusBadgeText"));
    }

    [Fact]
    public void UpdateShowsNoValueBadgeAlongsideFailureBadge()
    {
        var snapshot = new UsageSnapshot(
            "other",
            "Other Provider",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Usage refresh timed out after 25 seconds.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Timed out", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Equal("No value", ReadStringProperty(provider, "NoValueBadgeText"));
        Assert.True(ReadBoolProperty(provider, "ShowNoValueBadge"));
    }

    [Fact]
    public void UpdateShowsStartOrLoginForMissingAntigravityEndpoint()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available. IDE fallback was not available because no endpoint was discovered.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Start/login required", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.False(ReadBoolProperty(provider, "ShowNoValueBadge"));
    }

    [Theory]
    [InlineData(
        AppLanguage.English,
        "Open Antigravity and make sure you are signed in with your Google account, then refresh.")]
    [InlineData(
        AppLanguage.Korean,
        "Antigravity를 실행하고 Google 계정 로그인을 확인한 뒤 새로고침하세요.")]
    public void UpdateExplainsWhyAntigravityIdeFailoverCouldNotContinue(
        AppLanguage language,
        string expectedToolTip)
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available. IDE fallback was not available because no endpoint was discovered.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, language);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal(expectedToolTip, ReadStringProperty(provider, "StatusBadgeToolTipText"));
    }

    [Theory]
    [InlineData(
        AppLanguage.English,
        "Open Antigravity and make sure you are signed in with your Google account, then refresh.")]
    [InlineData(
        AppLanguage.Korean,
        "Antigravity를 실행하고 Google 계정 로그인을 확인한 뒤 새로고침하세요.")]
    public void UpdateDoesNotAssumeCloudFailureForStandaloneIdeDiscoveryError(
        AppLanguage language,
        string expectedToolTip)
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "IDE endpoint discovery returned no candidates.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, language);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal(expectedToolTip, ReadStringProperty(provider, "StatusBadgeToolTipText"));
    }

    [Theory]
    [InlineData(AppLanguage.Korean, "ide", "Antigravity IDE 자격증명")]
    [InlineData(AppLanguage.Korean, "user-saved", "본인이 저장한 값")]
    [InlineData(AppLanguage.Korean, "environment", "환경변수")]
    [InlineData(AppLanguage.Korean, "none", "(없음 — 만료 시 갱신 실패)")]
    [InlineData(AppLanguage.English, "ide", "Antigravity IDE credentials")]
    [InlineData(AppLanguage.Japanese, "ide", "Antigravity IDE の資格情報")]
    [InlineData(AppLanguage.Chinese, "ide", "Antigravity IDE 凭据")]
    public void ProviderItemAppendsOAuthClientOriginToSourceBadgeToolTip(
        AppLanguage language, string originToken, string expectedFragment)
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("antigravity-gemini-2-5-pro", "gemini-2.5-pro", 50, null, null, "medium")],
            SourceChannel: "cloud",
            OAuthClientOrigin: originToken);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, language);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Contains(expectedFragment, ReadStringProperty(provider, "SourceBadgeToolTipText"));
    }

    [Fact]
    public void UpdateExposesAntigravityOAuthActiveClientText()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [],
            isRefreshing: false,
            LimitDisplayMode.BothText,
            AppLanguage.Korean,
            antigravityActiveClientOrigin: AntigravityOAuthClientOrigin.UserSavedSettings);

        Assert.Equal(
            "현재 갱신에 사용 중: 본인이 저장한 값",
            viewModel.AntigravityOAuthActiveClientText);
    }

    [Fact]
    public void UpdateClarifiesAntigravityOAuthActiveClientWhenNone()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [],
            isRefreshing: false,
            LimitDisplayMode.BothText,
            AppLanguage.Korean,
            antigravityActiveClientOrigin: AntigravityOAuthClientOrigin.None);

        Assert.Equal(
            "토큰 갱신 클라이언트: 미설정 (IDE 실행 시 폴백 조회 · 없으면 만료 시 갱신 실패)",
            viewModel.AntigravityOAuthActiveClientText);
    }

    [Fact]
    public void ProviderItemShowsCloudSourceBadgeOnSuccess()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("antigravity-gemini-2-5-pro", "gemini-2.5-pro", 50, null, null, "medium")],
            SourceChannel: "cloud");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(ReadBoolProperty(provider, "ShowSourceBadge"));
        Assert.Equal("Cloud", ReadStringProperty(provider, "SourceBadgeText"));
        Assert.Contains("Antigravity Cloud", ReadStringProperty(provider, "SourceBadgeToolTipText"));
    }

    [Fact]
    public void ProviderItemShowsIdeFallbackBadgeOnSuccess()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("antigravity-gemini-2-5-pro", "gemini-2.5-pro", 50, null, null, "medium")],
            SourceChannel: "ide-fallback");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(ReadBoolProperty(provider, "ShowSourceBadge"));
        Assert.Equal("IDE 폴백", ReadStringProperty(provider, "SourceBadgeText"));
    }

    [Fact]
    public void ProviderItemOmitsNoneOAuthClientNoteWhenIdeFallbackIsLive()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("antigravity-gemini-2-5-pro", "gemini-2.5-pro", 50, null, null, "medium")],
            SourceChannel: "ide-fallback",
            OAuthClientOrigin: "none");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        var tooltip = ReadStringProperty(provider, "SourceBadgeToolTipText");
        // IDE fallback is live, so the "no OAuth client (refresh will fail)" note is suppressed.
        Assert.DoesNotContain("없음", tooltip);
        Assert.Contains("IDE 폴백", tooltip);
    }

    [Fact]
    public void ProviderItemLocalizesOAuthClientSecretMissingInLastErrorText()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available.",
            SourceChannel: null,
            CloudFailureSummary: "oauth client secret missing",
            IdeFailureSummary: "no endpoint discovered");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Contains("OAuth 클라이언트 시크릿이 없습니다", provider.LastErrorText);
        Assert.DoesNotContain("ANTIGRAVITY_OAUTH_CLIENT_ID", provider.LastErrorText);
    }

    [Fact]
    public void ProviderItemHidesSourceBadgeOnFailureAndShowsTwoLineSummary()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available.",
            SourceChannel: null,
            CloudFailureSummary: "HTTP 401",
            IdeFailureSummary: "no endpoint discovered");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.False(ReadBoolProperty(provider, "ShowSourceBadge"));
        var lines = provider.LastErrorText.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Cloud:", lines[0]);
        Assert.StartsWith("IDE 폴백:", lines[1]);
    }

    [Fact]
    public void ProviderItemUsesOAuthSetupBadgeWhenOAuthClientMissing()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Google Antigravity token refresh failed because no OAuth client secret is available.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("OAuth 설정 필요", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Contains("설정 > Antigravity OAuth", ReadStringProperty(provider, "StatusBadgeToolTipText"));
        Assert.Contains("폴백", ReadStringProperty(provider, "StatusBadgeToolTipText"));
    }

    [Fact]
    public void ProviderItemEmitsStartOrLoginBadgeWhenInstalledButIdeDown()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-11T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity quota was not available. Sign in to Antigravity with a Google account, or start Antigravity IDE as a fallback. IDE fallback was not available because no endpoint was discovered.",
            SourceChannel: null,
            CloudFailureSummary: "HTTP 401",
            IdeFailureSummary: "no endpoint discovered");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("실행/로그인 필요", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Contains("Antigravity를 실행", ReadStringProperty(provider, "StatusBadgeToolTipText"));
    }

    [Theory]
    [InlineData(
        AppLanguage.English,
        "No quota value could be displayed. Check the adjacent status badge for the reason.")]
    [InlineData(
        AppLanguage.Korean,
        "표시할 한도 값을 가져오지 못했습니다. 옆 상태 배지에서 원인을 확인하세요.")]
    public void UpdateExplainsNoValueBadge(
        AppLanguage language,
        string expectedToolTip)
    {
        var snapshot = new UsageSnapshot(
            "other",
            "Other Provider",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Usage refresh timed out after 25 seconds.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, language);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal(expectedToolTip, ReadStringProperty(provider, "NoValueBadgeToolTipText"));
    }

    [Fact]
    public void UpdatePrioritizesMissingIdeWhenCloudOAuthSetupAlsoFailed()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity Cloud setup: Google Antigravity OAuth client values were not found. Details: Antigravity quota was not available. IDE fallback was not available because no endpoint was discovered.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Start/login required", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.False(ReadBoolProperty(provider, "ShowNoValueBadge"));
    }

    [Fact]
    public void UpdateDoesNotShowNoValueBadgeForClaudeLoginFailure()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Claude OAuth credentials were not found. Run Claude Code login first.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.English);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Install/login required", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.False(ReadBoolProperty(provider, "ShowNoValueBadge"));
    }

    [Fact]
    public void UpdateShowsAntigravityMissingInstallationAsInstallLoginRequired()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Antigravity installation was not found. Install Antigravity, then sign in with your Google account.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal(
            "Antigravity가 설치되어 있지 않거나 로그인이 만료되었습니다. Antigravity를 설치하거나 다시 로그인한 뒤 새로고침하세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Fact]
    public void UpdateShowsUnknownFailureTooltipWithLogCopyGuidance()
    {
        var snapshot = new UsageSnapshot(
            "other",
            "Other Provider",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Unexpected provider error.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("확인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal(
            "사용량을 불러오지 못했습니다. 다시 새로고침해 보고, 계속 안 되면 진단 로그를 복사해서 보고해 주세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Theory]
    [InlineData("Usage refresh timed out after 10 seconds.", "시간 초과")]
    [InlineData("Socket connection failed.", "네트워크 문제")]
    [InlineData("JSON parse failed.", "응답 해석 실패")]
    [InlineData("Provider returned no quota buckets.", "한도 정보 없음")]
    public void UpdateShowsConservativeFailureCategories(string error, string expectedBadge)
    {
        var snapshot = new UsageSnapshot(
            "other",
            "Other Provider",
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            error);
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.BothText, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal(expectedBadge, ReadStringProperty(provider, "FailureBadgeText"));
    }

    [Fact]
    public void UpdateShowsAutomaticRetryTextForFailedProviderBackoff()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Now,
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Usage refresh timed out.");
        var retryStatus = new ProviderAutoRefreshStatus(
            "codex",
            DateTimeOffset.Now,
            2,
            DateTimeOffset.Now.AddMinutes(10));
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [snapshot],
            isRefreshing: false,
            LimitDisplayMode.BothText,
            AppLanguage.Korean,
            autoRefreshStatuses: new Dictionary<string, ProviderAutoRefreshStatus>
            {
                ["codex"] = retryStatus
            });

        var provider = Assert.Single(viewModel.Providers);
        Assert.True((bool)provider.GetType().GetProperty("ShowAutoRetryText")!.GetValue(provider)!);
        Assert.Contains("분 후 자동 재시도", ReadStringProperty(provider, "AutoRetryText"));
    }

    [Fact]
    public void UpdateShowsAntigravityTimeoutAsTimeout()
    {
        var snapshot = new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-06-01T21:11:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Usage refresh timed out after 10 seconds.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("시간 초과", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("시간 초과", ReadStringProperty(provider, "StatusBadgeText"));
        Assert.Contains("클라우드 조회가 지연되었습니다", provider.LastErrorText);
        Assert.Contains("IDE 폴백", provider.LastErrorText);
        Assert.DoesNotContain("Usage refresh timed out", provider.LastErrorText);
        Assert.Equal(
            "클라우드 조회가 지연되었습니다. IDE 폴백을 시도하려면 Antigravity IDE를 열고 다시 새로고침하세요.",
            ReadStringProperty(provider, "FailureToolTipText"));
    }

    [Fact]
    public void UpdateShowsCodexTimeoutAsInstallLoginRequired()
    {
        var snapshot = new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-06-01T21:11:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Usage refresh timed out after 10 seconds.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("설치/로그인 필요", ReadStringProperty(provider, "StatusBadgeText"));
    }

    [Fact]
    public void UpdateKeepsClaudeTimeoutAsTimeout()
    {
        var snapshot = new UsageSnapshot(
            "claude",
            "Claude Code",
            DateTimeOffset.Parse("2026-06-01T21:11:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Failed,
            [],
            "Usage refresh timed out after 10 seconds.");
        var viewModel = new UsageViewModel();

        viewModel.Update([snapshot], isRefreshing: false, LimitDisplayMode.Bars, AppLanguage.Korean);

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("시간 초과", ReadStringProperty(provider, "FailureBadgeText"));
        Assert.Equal("시간 초과", ReadStringProperty(provider, "StatusBadgeText"));
    }

    [Fact]
    public void CodexProviderExposesConfiguredProfilesAndSelection()
    {
        var snapshot = Snapshot("codex");
        var profiles = new[]
        {
            new CodexProfileSetting(CodexProfileSetting.DefaultId, "Default", @"C:\Codex\default\auth.json", true),
            new CodexProfileSetting("work", "Work", @"C:\Codex\work\auth.json")
        };
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [snapshot],
            isRefreshing: false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            codexProfiles: profiles,
            selectedCodexProfileId: "work");

        var provider = Assert.Single(viewModel.Providers);
        Assert.True(provider.ShowCodexProfileSelector);
        Assert.Equal("work", provider.SelectedCodexProfileId);
        Assert.Collection(
            provider.CodexProfiles,
            profile =>
            {
                Assert.Equal(CodexProfileSetting.DefaultId, profile.Id);
                Assert.False(profile.IsSelected);
            },
            profile =>
            {
                Assert.Equal("work", profile.Id);
                Assert.True(profile.IsSelected);
            });
    }

    [Fact]
    public void NonCodexProviderHidesProfileSelector()
    {
        var viewModel = new UsageViewModel();

        viewModel.Update(
            [Snapshot("claude")],
            isRefreshing: false,
            LimitDisplayMode.Bars,
            AppLanguage.English,
            codexProfiles:
            [
                new CodexProfileSetting(CodexProfileSetting.DefaultId, "Default", @"C:\Codex\auth.json", true)
            ],
            selectedCodexProfileId: CodexProfileSetting.DefaultId);

        var provider = Assert.Single(viewModel.Providers);
        Assert.False(provider.ShowCodexProfileSelector);
        Assert.Empty(provider.CodexProfiles);
    }

    private static UsageSnapshot AntigravitySnapshot()
    {
        return new UsageSnapshot(
            "gemini-pro",
            "Google Antigravity",
            DateTimeOffset.Parse("2026-05-30T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [
                new UsageWindow("antigravity-gemini-3-5-flash-medium", "Gemini 3.5 Flash (Medium)", 0, null, null, "medium", IsUsedPercent: true),
                new UsageWindow("antigravity-claude-sonnet-4-6-thinking", "Claude Sonnet 4.6 (Thinking)", 20, null, null, "medium", IsUsedPercent: true)
            ]);
    }

    private static UsageSnapshot SnapshotWithResetLabels()
    {
        return new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Parse("2026-05-18T02:00:00+09:00"),
            UsageSource.Mock,
            UsageStatus.Fresh,
            [
                new UsageWindow("five-hour", "5-hour limit", 63, null, "5시간 초기화", "high"),
                new UsageWindow("weekly", "Weekly limit", 41, null, "주간 초기화", "medium")
            ]);
    }

    private static UsageSnapshot Snapshot(string providerId)
    {
        return new UsageSnapshot(
            providerId,
            providerId,
            DateTimeOffset.Parse("2026-06-01T02:00:00+09:00"),
            UsageSource.Agent,
            UsageStatus.Fresh,
            [new UsageWindow("five-hour", "5-hour limit", 50, null, null, "high")]);
    }

    private static string ReadStringProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<string>(property!.GetValue(instance));
    }

    private static bool ReadBoolProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property!.GetValue(instance));
    }
}

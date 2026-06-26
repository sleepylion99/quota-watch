using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Xml.Linq;
using AiLimit.App.Services;
using AiLimit.App.ViewModels;
using AiLimit.App.Windows;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class DashboardMarkupTests
{
    [Fact]
    public void DashboardAndWidgetRenderDepletionPrediction()
    {
        var dashboard = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var widget = ReadSourceFile("src", "AiLimit.App", "Windows", "WidgetWindow.xaml");

        Assert.Contains("Points=\"{Binding SparklinePoints}\"", dashboard);
        Assert.Contains("Points=\"{Binding SparklineProjectionPoints}\"", dashboard);
        Assert.Contains("Text=\"{Binding PredictionText}\"", dashboard);
        Assert.Contains("Text=\"{Binding PredictionDetailText}\"", dashboard);
        Assert.Contains("ShowPrediction", dashboard);

        Assert.Contains("Text=\"{Binding PredictionText}\"", widget);
        Assert.Contains("ShowPrediction", widget);
    }

    [Fact]
    public void PinnedWindowsReapplyTopmostWhenActivated()
    {
        var dashboard = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");
        var widget = ReadSourceFile("src", "AiLimit.App", "Windows", "WidgetWindow.xaml.cs");
        var nativeTopmost = ReadSourceFile("src", "AiLimit.App", "Windows", "NativeTopmost.cs");

        Assert.Contains("SourceInitialized += OnSourceInitialized", dashboard);
        Assert.Contains("NativeTopmost.Apply(this, isAlwaysOnTop)", dashboard);
        Assert.Contains("SourceInitialized += OnSourceInitialized", widget);
        Assert.Contains("NativeTopmost.Apply(this, isAlwaysOnTop)", widget);
        Assert.Contains("SetWindowPos", nativeTopmost);
        Assert.Contains("HwndTopmost", nativeTopmost);
        Assert.Contains("HwndNotTopmost", nativeTopmost);
    }

    [Fact]
    public void DashboardTitleBarRestoresMaximizedWindowBeforeDragging()
    {
        var dashboard = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");

        Assert.Contains("e.ClickCount >= 2", dashboard);
        Assert.Contains("WindowState = WindowState.Normal", dashboard);
        Assert.Contains("RestoreMaximizedWindowForDrag(e)", dashboard);
        Assert.Contains("SystemParameters.WorkArea", dashboard);
    }

    [Fact]
    public void DashboardUsesBalancedPercentSizes()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        Assert.Matches("Text=\"\\{Binding PrimaryPercentText\\}\"[\\s\\S]*?FontSize=\"20\"", xaml);
        Assert.Matches("Text=\"\\{Binding PercentText\\}\"[\\s\\S]*?FontSize=\"14\"", xaml);
    }

    [Fact]
    public void WidgetWindowShowsLimitResetTextInDetailRows()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "WidgetWindow.xaml");

        var detailRowsStart = xaml.IndexOf("ItemsControl ItemsSource=\"{Binding VisibleWindows}\"", StringComparison.Ordinal);
        Assert.True(detailRowsStart >= 0);
        var detailRows = xaml[detailRowsStart..];

        Assert.Contains("Text=\"{Binding Label}\"", detailRows);
        Assert.Contains("Text=\"{Binding ResetText}\"", detailRows);
    }

    [Fact]
    public void WidgetHidesPrimarySummaryWhenProviderUsesOnlyDetailRows()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "WidgetWindow.xaml");

        var providerName = xaml.IndexOf("Text=\"{Binding ProviderName}\"", StringComparison.Ordinal);
        var primarySummary = xaml.IndexOf("x:Name=\"WidgetPrimarySummary\"", StringComparison.Ordinal);
        var detailRows = xaml.IndexOf("ItemsControl ItemsSource=\"{Binding VisibleWindows}\"", StringComparison.Ordinal);
        Assert.True(providerName >= 0 && primarySummary > providerName && detailRows > primarySummary);

        var primaryMarkup = xaml[primarySummary..detailRows];
        Assert.Contains("Binding=\"{Binding ShowPrimarySummary}\"", primaryMarkup);
        Assert.Contains("<Setter Property=\"Visibility\" Value=\"Collapsed\"", primaryMarkup);
    }

    [Fact]
    public void DashboardWindowOpensSettingsAsStandaloneWindow()
    {
        var dashXaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var dashCode = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");

        // Dashboard keeps only the trigger button
        Assert.Contains("x:Name=\"SettingsButton\"", dashXaml);
        Assert.Contains("SettingsButton_Click", dashXaml);
        Assert.Contains("Content=\"{Binding SettingsButtonText}\"", dashXaml);
        Assert.DoesNotContain("x:Name=\"SettingsPanel\"", dashXaml);
        Assert.DoesNotContain("Panel.ZIndex=\"100\"", dashXaml);
        Assert.DoesNotContain("<Popup", dashXaml);
        Assert.Contains("Width=\"132\"", dashXaml);

        // Dashboard code opens SettingsWindow, not a panel toggle
        Assert.Contains("new SettingsWindow(", dashCode);
        Assert.Contains(".Show()", dashCode);
        Assert.DoesNotContain("SetSettingsPanelOpen", dashCode);
        Assert.DoesNotContain("SettingsPanel.Visibility", dashCode);
        Assert.DoesNotContain("MessageBox.Show", dashCode);
        Assert.DoesNotContain("ProviderModeComboBox_SelectionChanged", dashCode);

        var settingsXaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");

        // SettingsWindow has full settings content
        Assert.Contains("ItemsSource=\"{Binding ProviderSettings}\"", settingsXaml);
        Assert.Contains("ProviderSettingCheckBox_Click", settingsXaml);
        Assert.DoesNotContain("<Popup", settingsXaml);
        Assert.DoesNotContain("ProviderModeComboBox_SelectionChanged", settingsXaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding ModeOptions}\"", settingsXaml);
        Assert.DoesNotContain("SelectedValue=\"{Binding SelectedMode", settingsXaml);
        Assert.Contains("Text=\"{Binding SetupHintText}\"", settingsXaml);
        Assert.Contains("HasSetupHint", settingsXaml);
        Assert.Contains("Text=\"{Binding SettingsPanelDetailText}\"", settingsXaml);
        Assert.Contains("Text=\"{Binding SettingsImmediateApplyText}\"", settingsXaml);
        Assert.Contains("x:Name=\"UnsavedChangesPanel\"", settingsXaml);
        Assert.Contains("Text=\"{Binding SettingsUnsavedTitleText}\"", settingsXaml);
        Assert.Contains("Text=\"{Binding SettingsUnsavedDetailText}\"", settingsXaml);
        Assert.Contains("Click=\"KeepEditingSettingsButton_Click\"", settingsXaml);
        Assert.Contains("Click=\"DiscardSettingsButton_Click\"", settingsXaml);
        Assert.Contains("Content=\"{Binding SettingsCloseButtonText}\"", settingsXaml);
        Assert.Contains("Content=\"{Binding SettingsApplyButtonText}\"", settingsXaml);
        Assert.Contains("Click=\"SettingsCloseButton_Click\"", settingsXaml);
        Assert.Contains("Click=\"SettingsApplyButton_Click\"", settingsXaml);
        Assert.Contains("Text=\"{Binding ModelSettingsDetailText}\"", settingsXaml);
        Assert.Contains("Text=\"{Binding SettingsAntigravityMovedToAccountsText}\"", settingsXaml);
        Assert.Contains("x:Name=\"AntigravityOAuthSettingsCard\"", settingsXaml);
        Assert.DoesNotContain("x:Name=\"AntigravityOAuthClientIdBox\"", settingsXaml);
        Assert.DoesNotContain("x:Name=\"AntigravityOAuthClientSecretBox\"", settingsXaml);
        Assert.DoesNotContain("Click=\"SaveAntigravityOAuthClientButton_Click\"", settingsXaml);
        Assert.DoesNotContain("Click=\"ClearAntigravityOAuthClientButton_Click\"", settingsXaml);
        Assert.Contains("Text=\"{Binding LanguageSettingsTitleText}\"", settingsXaml);
        Assert.Contains("Text=\"{Binding LanguageSettingsDetailText}\"", settingsXaml);
        Assert.Contains("ItemsSource=\"{Binding LanguageOptions}\"", settingsXaml);
        Assert.Contains("Click=\"LanguageOptionButton_Click\"", settingsXaml);
        Assert.Contains("ToolTip=\"{Binding Label}\"", settingsXaml);
        Assert.Contains("Tag=\"{Binding Language}\"", settingsXaml);
        Assert.Contains("Text=\"{Binding DiagnosticLogTitleText}\"", settingsXaml);
        Assert.Contains("Text=\"{Binding DiagnosticLogDetailText}\"", settingsXaml);
        Assert.Contains("Content=\"{Binding CopyDiagnosticLogButtonText}\"", settingsXaml);

        var settingsCode = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");
        Assert.Contains("SettingsCloseButton_Click", settingsCode);
        Assert.Contains("SettingsApplyButton_Click", settingsCode);
        Assert.Contains("_hasPendingSettingsChanges", settingsCode);
        Assert.Contains("UnsavedChangesPanel.Visibility = Visibility.Visible", settingsCode);
        Assert.Contains("KeepEditingSettingsButton_Click", settingsCode);
        Assert.Contains("DiscardSettingsButton_Click", settingsCode);
        Assert.Contains("SaveSettingsFromDashboard", settingsCode);
        Assert.Contains("_state.PreviewLanguage(language)", settingsCode);
        Assert.Contains("_state.ClearLanguagePreview()", settingsCode);
        Assert.Contains("protected override void OnClosed", settingsCode);
        Assert.DoesNotContain("AntigravityOAuthClientSecretBox.Clear()", settingsCode);
        Assert.DoesNotContain("SaveAntigravityOAuthClientButton_Click", settingsCode);
        Assert.DoesNotContain("MessageBox.Show", settingsCode);
        Assert.DoesNotContain("ProviderModeComboBox_SelectionChanged", settingsCode);

        var dashboardCode = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");
        var widgetCode = ReadSourceFile("src", "AiLimit.App", "Windows", "WidgetWindow.xaml.cs");
        var trayCode = ReadSourceFile("src", "AiLimit.App", "Tray", "TrayController.cs");
        Assert.Contains("_state.DisplayLanguage", dashboardCode);
        Assert.Contains("_state.DisplayLanguage", widgetCode);
        Assert.Contains("_state.DisplayLanguage", trayCode);
    }

    [Theory]
    [InlineData(AppLanguage.English, "en")]
    [InlineData(AppLanguage.Korean, "ko")]
    [InlineData(AppLanguage.Japanese, "ja")]
    [InlineData(AppLanguage.Chinese, "zh")]
    public void AntigravityOAuthGuideOpensPackagedFileInDefaultBrowser(
        AppLanguage language,
        string languageCode)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"ai-limit guide {Guid.NewGuid():N}");
        var guidePath = Path.Combine(baseDirectory, "Assets", "Help", "antigravity-oauth.html");
        Directory.CreateDirectory(Path.GetDirectoryName(guidePath)!);
        File.WriteAllText(guidePath, "<html></html>");
        ProcessStartInfo? capturedStartInfo = null;

        try
        {
            var guide = new AntigravityOAuthGuide(
                baseDirectory,
                startInfo => capturedStartInfo = startInfo);

            guide.Open(language);

            Assert.NotNull(capturedStartInfo);
            Assert.True(capturedStartInfo!.UseShellExecute);
            Assert.Equal(
                $"{new Uri(guidePath).AbsoluteUri}?lang={languageCode}",
                capturedStartInfo.FileName);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void AntigravityOAuthGuideResolvesSystemLanguageBeforeOpening()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"ai-limit-guide-{Guid.NewGuid():N}");
        var guidePath = Path.Combine(baseDirectory, "Assets", "Help", "antigravity-oauth.html");
        Directory.CreateDirectory(Path.GetDirectoryName(guidePath)!);
        File.WriteAllText(guidePath, "<html></html>");
        ProcessStartInfo? capturedStartInfo = null;

        try
        {
            var guide = new AntigravityOAuthGuide(
                baseDirectory,
                startInfo => capturedStartInfo = startInfo);

            guide.Open(AppLanguage.System);

            Assert.NotNull(capturedStartInfo);
            Assert.DoesNotContain("?lang=system", capturedStartInfo!.FileName);
            Assert.Matches(@"\?lang=(en|ko|ja|zh)$", capturedStartInfo.FileName);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void AntigravityOAuthGuideThrowsWhenPackagedFileIsMissing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"ai-limit-guide-{Guid.NewGuid():N}");
        var guide = new AntigravityOAuthGuide(baseDirectory, _ => { });

        Assert.Throws<FileNotFoundException>(() => guide.Open(AppLanguage.English));
    }

    [Fact]
    public void DashboardGuideHandlerReportsOpenFailureWithoutMessageBox()
    {
        var viewModel = new UsageViewModel();
        var guide = new AntigravityOAuthGuide(
            Path.Combine(Path.GetTempPath(), $"missing-guide-{Guid.NewGuid():N}"),
            _ => { });

        var opened = DashboardWindow.TryOpenAntigravityOAuthGuide(
            guide,
            AppLanguage.Korean,
            viewModel);

        Assert.False(opened);
        Assert.Equal("설정 가이드를 열지 못했습니다.", viewModel.AntigravityOAuthStatusText);
    }

    [Fact]
    public void SettingsOpensAsDraggableWindow()
    {
        var dashCode = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");
        var settingsXaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var settingsCode = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");

        // Dashboard creates and shows the settings window
        Assert.Contains("new SettingsWindow(", dashCode);
        Assert.Contains(".Show()", dashCode);

        // SettingsWindow is a proper draggable window, not embedded
        Assert.Contains("WindowStyle=\"None\"", settingsXaml);
        Assert.Contains("AllowsTransparency=\"True\"", settingsXaml);
        Assert.Contains("Header_MouseLeftButtonDown", settingsXaml);
        Assert.Contains("Background=\"{DynamicResource Brush.Surface.Card}\"", settingsXaml);
        Assert.DoesNotContain("Panel.ZIndex=\"100\"", settingsXaml);

        Assert.Contains("DragMove()", settingsCode);
    }

    [Fact]
    public void WindowsUseDedicatedTitleBarControlStyles()
    {
        // Styles now live in Controls.xaml (merged via App.xaml), not inline in App.xaml.
        var controlsXaml = ReadSourceFile("src", "AiLimit.App", "Theming", "Themes", "Controls.xaml");
        var dashboardXaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var settingsXaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var settingsCode = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");

        Assert.Contains("x:Key=\"WindowMinimizeButtonStyle\"", controlsXaml);
        Assert.Contains("x:Key=\"WindowCloseButtonStyle\"", controlsXaml);
        Assert.Contains("Style=\"{StaticResource WindowMinimizeButtonStyle}\"", dashboardXaml);
        Assert.Contains("Style=\"{StaticResource WindowCloseButtonStyle}\"", dashboardXaml);
        Assert.Contains("Style=\"{StaticResource WindowMinimizeButtonStyle}\"", settingsXaml);
        Assert.Contains("Style=\"{StaticResource WindowCloseButtonStyle}\"", settingsXaml);
        Assert.Contains("Click=\"SettingsMinimizeButton_Click\"", settingsXaml);
        Assert.Contains("WindowState = WindowState.Minimized", settingsCode);
        Assert.Contains("x:Name=\"DashboardTitleBarControls\"", dashboardXaml);
        Assert.Contains("x:Name=\"SettingsTitleBarControls\"", settingsXaml);
        Assert.Matches(
            "x:Name=\"DashboardTitleBarControls\"[\\s\\S]*?WindowMinimizeButtonStyle[\\s\\S]*?Margin=\"0,0,4,0\"",
            dashboardXaml);
        Assert.Matches(
            "x:Name=\"SettingsTitleBarControls\"[\\s\\S]*?WindowMinimizeButtonStyle[\\s\\S]*?Margin=\"0,0,4,0\"",
            settingsXaml);
    }

    [Fact]
    public void SettingsWindowUsesDashboardCardsAndFixedFooter()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");

        Assert.Contains("x:Name=\"SettingsTitleBar\"", xaml);
        Assert.Contains("Background=\"{DynamicResource Brush.Surface.TitleBar}\"", xaml);
        Assert.Contains("x:Name=\"ProvidersSettingsCard\"", xaml);
        Assert.Contains("x:Name=\"AntigravityOAuthSettingsCard\"", xaml);
        Assert.Contains("x:Name=\"LanguageSettingsCard\"", xaml);
        Assert.Contains("x:Name=\"LimitWarningSettingsCard\"", xaml);
        Assert.Contains("x:Name=\"UpdateSettingsCard\"", xaml);
        Assert.Contains("x:Name=\"DiagnosticSettingsCard\"", xaml);
        Assert.Contains("x:Name=\"SettingsActionFooter\"", xaml);

        var scrollEnd = xaml.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        var footerStart = xaml.IndexOf("x:Name=\"SettingsActionFooter\"", StringComparison.Ordinal);
        Assert.True(scrollEnd >= 0);
        Assert.True(footerStart > scrollEnd);
    }

    [Fact]
    public void DashboardWindowUsesGraphViewWithoutDisplayModeSelector()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");

        Assert.DoesNotContain("LimitModeComboBox", xaml);
        Assert.DoesNotContain("FiveHourModeItem", xaml);
        Assert.DoesNotContain("WeeklyModeItem", xaml);
        Assert.DoesNotContain("BothModeItem", xaml);
        Assert.DoesNotContain("BarsModeItem", xaml);
        Assert.DoesNotContain("LimitModeComboBox_SelectionChanged", code);
        Assert.DoesNotContain("SyncDisplayMode", code);
        Assert.Contains("LimitDisplayMode.Bars", code);
    }

    [Fact]
    public void SettingsWindowCanCopyDiagnosticLog()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");

        Assert.Contains("Content=\"{Binding CopyDiagnosticLogButtonText}\"", xaml);
        Assert.Contains("CopyDiagnosticLogButton_Click", xaml);
        Assert.Contains("System.Windows.Clipboard.SetText(AppLog.ReadDiagnosticLogForCopy())", code);
    }

    [Fact]
    public void TopActionsKeepOnlySettingsWidgetAndRefresh()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var topActionsStart = xaml.IndexOf("<Grid>", xaml.IndexOf("<RowDefinition Height=\"*\" />", StringComparison.Ordinal), StringComparison.Ordinal);
        var topActionsEnd = xaml.IndexOf("<Border Grid.Row=\"2\"", StringComparison.Ordinal);
        Assert.True(topActionsStart >= 0);
        Assert.True(topActionsEnd > topActionsStart);
        var topActions = xaml[topActionsStart..topActionsEnd];

        Assert.Contains("x:Name=\"SettingsButton\"", topActions);
        Assert.Contains("ToggleWidgetButton_Click", topActions);
        Assert.Contains("RefreshButton_Click", topActions);
        Assert.DoesNotContain("KoreanLanguageButton", topActions);
        Assert.DoesNotContain("EnglishLanguageButton", topActions);
        Assert.DoesNotContain("JapaneseLanguageButton", topActions);
        Assert.DoesNotContain("ChineseLanguageButton", topActions);
        Assert.DoesNotContain("CopyDiagnosticLogButton", topActions);
    }

    [Fact]
    public void ProviderCardsUseCompactDashboardTypography()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        Assert.Contains("CornerRadius=\"8\"", xaml);
        Assert.Matches("Text=\"\\{Binding ProviderName\\}\"[\\s\\S]*?FontSize=\"17\"", xaml);
        Assert.DoesNotContain("Text=\"{Binding ProviderId}\"", xaml);
        Assert.DoesNotContain("Text=\"{Binding LimitProfileText}\"", xaml);
        Assert.Contains("Foreground=\"{DynamicResource Brush.Text.Secondary}\"", xaml);
        Assert.Contains(
            "Background=\"{Binding BrandColorBrush, Converter={StaticResource BrushKeyConverter}}\"",
            xaml);
        Assert.DoesNotContain("CornerRadius=\"22\"", xaml);
    }

    [Fact]
    public void SettingsPanelCanCheckForUpdates()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");

        Assert.Contains("Text=\"{Binding UpdateCheckTitleText}\"", xaml);
        Assert.Contains("Content=\"{Binding CheckForUpdatesButtonText}\"", xaml);
        Assert.Contains("Text=\"{Binding UpdateCheckStatusText}\"", xaml);
        Assert.Contains("CheckForUpdatesButton_Click", xaml);
        Assert.Contains("OnLoaded", code);
        Assert.Contains("_hasStartedInitialUpdateCheck", code);
        Assert.Contains("CheckForUpdatesAsync", code);
    }

    [Fact]
    public void SettingsPanelShowsUpdateAvailableConfirmationForAutomaticAndManualChecks()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");

        Assert.Contains("x:Name=\"UpdateAvailableOverlay\"", xaml);
        Assert.Contains("Text=\"{Binding UpdateAvailableTitleText}\"", xaml);
        Assert.Contains("Text=\"{Binding UpdateAvailableMessageText}\"", xaml);
        Assert.Contains("Content=\"{Binding UpdateAvailableConfirmButtonText}\"", xaml);
        Assert.Contains("Content=\"{Binding UpdateAvailableCancelButtonText}\"", xaml);
        Assert.Contains("Click=\"ConfirmUpdateButton_Click\"", xaml);
        Assert.Contains("Click=\"CancelUpdateButton_Click\"", xaml);
        Assert.Contains("UpdateAvailableOverlay.Visibility != Visibility.Collapsed", code);
        Assert.Contains("result.IsUpdateAvailable", code);
        Assert.Contains("_pendingUpdateReleaseUrl = result.ReleaseUrl", code);
        Assert.Contains("new UpdateReleaseLauncher()", code);
        Assert.DoesNotContain("MessageBox.Show", code);
    }

    [Fact]
    public void SettingsPanelCanConfigureLimitWarnings()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");

        Assert.Contains("Text=\"{Binding LimitWarningSettingsTitleText}\"", xaml);
        Assert.Contains("IsChecked=\"{Binding IsLimitWarningEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding LimitWarningProviderSettings}\"", xaml);
        Assert.Contains("ItemsSource=\"{Binding Recommendations}\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding IsSliderEnabled}\"", xaml);
        Assert.Contains("Value=\"{Binding ThresholdPercent, Mode=OneWay}\"", xaml);
        Assert.Contains("LimitWarningEnabledCheckBox_Click", xaml);
        Assert.Contains("LimitWarningRecommendationButton_Click", xaml);
        Assert.Contains("LimitWarningCustomButton_Click", xaml);
        Assert.Contains("LimitWarningSlider_ValueChanged", xaml);
        Assert.Contains("_pendingLimitWarningEnabled", code);
        Assert.Contains("_pendingLimitWarningSettings", code);
    }

    [Fact]
    public void ProviderCardsDoNotStretchToFillEmptyScrollSpace()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        Assert.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Top\" />", xaml);
        Assert.Contains("VerticalAlignment=\"Top\"", xaml);
    }

    [Fact]
    public void ProviderCardsAreNotBoundToInitialViewportWidth()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        Assert.DoesNotContain("Path=ViewportWidth", xaml);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml);
    }

    [Fact]
    public void ProviderCardsShowRefreshFailureGuidance()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var cardStart = xaml.IndexOf("Text=\"{Binding ProviderName}\"", StringComparison.Ordinal);
        Assert.True(cardStart >= 0);
        var cardMarkup = xaml[cardStart..xaml.IndexOf("<Grid Margin=\"0,16,0,14\">", cardStart, StringComparison.Ordinal)];

        Assert.Contains("Text=\"{Binding LastErrorText}\"", cardMarkup);
        Assert.Contains("HasLastErrorText", cardMarkup);
        Assert.DoesNotContain("Text=\"{Binding StatusText}\"", cardMarkup);
        Assert.DoesNotContain("Text=\"{Binding FailureBadgeText}\"", xaml);
        Assert.DoesNotContain("ToolTip=\"{Binding FailureToolTipText}\"", xaml);
        Assert.Contains("Text=\"{Binding StatusBadgeText}\"", cardMarkup);
        Assert.Contains("ToolTip=\"{Binding StatusBadgeToolTipText}\"", cardMarkup);
        Assert.Contains("Text=\"{Binding NoValueBadgeText}\"", cardMarkup);
        Assert.Contains("ToolTip=\"{Binding NoValueBadgeToolTipText}\"", cardMarkup);
        Assert.Contains("ShowNoValueBadge", cardMarkup);
    }

    [Fact]
    public void SettingsPanelShowsAntigravityMovedPointer()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        Assert.Contains("Text=\"{Binding SettingsAntigravityMovedToAccountsText}\"", xaml);
        Assert.DoesNotContain("Text=\"{Binding AntigravityOAuthActiveClientText}\"", xaml);
    }

    [Fact]
    public void ProviderCardsBindSourceBadge()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        Assert.Contains("Text=\"{Binding SourceBadgeText}\"", xaml);
        Assert.Contains("ToolTip=\"{Binding SourceBadgeToolTipText}\"", xaml);
        Assert.Contains("ShowSourceBadge", xaml);
    }

    [Fact]
    public void PrimarySummaryAndBarUseSameVisibilityFlag()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        var primaryBarStart = xaml.IndexOf("Value=\"{Binding PrimaryPercent, Mode=OneWay}\"", StringComparison.Ordinal);
        Assert.True(primaryBarStart >= 0);
        var primaryBarMarkup = xaml[primaryBarStart..xaml.IndexOf("<ItemsControl ItemsSource=\"{Binding VisibleWindows}\">", primaryBarStart, StringComparison.Ordinal)];

        Assert.Contains("ShowPrimaryProgress", primaryBarMarkup);
        Assert.DoesNotContain("ShowPrimaryBar", primaryBarMarkup);
        Assert.DoesNotContain("ShowPrimarySummary", primaryBarMarkup);
    }

    [Fact]
    public void DetailRowsKeepHeaderSpacingWhenPrimarySummaryIsHidden()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        var detailRowsStart = xaml.IndexOf("<ItemsControl ItemsSource=\"{Binding VisibleWindows}\">", StringComparison.Ordinal);
        Assert.True(detailRowsStart >= 0);
        var detailRowsMarkup = xaml[detailRowsStart..xaml.IndexOf("</ItemsControl>", detailRowsStart, StringComparison.Ordinal)];

        Assert.Contains("<Setter Property=\"Margin\" Value=\"0,16,0,0\" />", detailRowsMarkup);
        Assert.Contains("ShowPrimarySummary", detailRowsMarkup);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\" />", detailRowsMarkup);
    }

    [Fact]
    public void AppDefinesDashboardToggleButtonStyle()
    {
        // Style is now in Controls.xaml, merged into App.xaml via MergedDictionaries.
        var xaml = ReadSourceFile("src", "AiLimit.App", "Theming", "Themes", "Controls.xaml");

        Assert.Contains("x:Key=\"DashboardToggleButtonStyle\"", xaml);
        Assert.Contains("CornerRadius=\"10\"", xaml);
        Assert.Contains("IsChecked", xaml);
    }

    [Fact]
    public void TrayMenuCanCopyDiagnosticLog()
    {
        var code = ReadSourceFile("src", "AiLimit.App", "Tray", "TrayController.cs");

        Assert.Contains("_copyDiagnosticLogItem", code);
        Assert.Contains("_settingsItem", code);
        Assert.Contains("_languageItem", code);
        Assert.Contains("AppLanguageCatalog.SupportedLanguages", code);
        Assert.Contains("Copy Diagnostic Log", code);
        Assert.Contains("Clipboard.SetText(AppLog.ReadDiagnosticLogForCopy())", code);
    }

    [Fact]
    public void AntigravityOAuthGuideSupportsFourLanguagesAndAutomaticSelection()
    {
        var html = ReadSourceFile("src", "AiLimit.App", "Assets", "Help", "antigravity-oauth.html");

        Assert.Contains("<select id=\"language-selector\"", html);
        Assert.Contains("<option value=\"en\">", html);
        Assert.Contains("<option value=\"ko\">", html);
        Assert.Contains("<option value=\"ja\">", html);
        Assert.Contains("<option value=\"zh\">", html);
        Assert.Contains("en: {", html);
        Assert.Contains("ko: {", html);
        Assert.Contains("ja: {", html);
        Assert.Contains("zh: {", html);
        Assert.Matches(
            @"function detectLanguage\(\)\s*\{[\s\S]*?new URLSearchParams\(window\.location\.search\)[\s\S]*?" +
            @"if \(params\.has\(""lang""\)\)\s*\{\s*return normalizeLanguage\(params\.get\(""lang""\)\) \|\| ""en"";\s*\}" +
            @"[\s\S]*?navigator\.languages && navigator\.languages\.length[\s\S]*?: \[navigator\.language\]" +
            @"[\s\S]*?for \(const browserLanguage of browserLanguages\)[\s\S]*?normalizeLanguage\(browserLanguage\)" +
            @"[\s\S]*?return ""en"";\s*\}",
            html);
        Assert.Matches(
            @"function applyLanguage\(language\)\s*\{[\s\S]*?" +
            @"const selectedLanguage = supportedLanguages\.includes\(language\) \? language : ""en"";" +
            @"[\s\S]*?document\.documentElement\.lang = selectedLanguage;" +
            @"[\s\S]*?officialHelpLink\.href = officialHelpUrls\[selectedLanguage\];[\s\S]*?\}",
            html);
        Assert.Matches(
            @"languageSelector\.addEventListener\(""change"", \(event\) =>\s*\{\s*" +
            @"applyLanguage\(event\.target\.value\);\s*\}\);",
            html);
        Assert.Matches(@"applyLanguage\(detectLanguage\(\)\);", html);
        Assert.Matches(
            @"const officialHelpUrls = \{[\s\S]*?" +
            @"en: ""https://support\.google\.com/cloud/answer/6158849\?hl=en""[\s\S]*?" +
            @"ko: ""https://support\.google\.com/cloud/answer/6158849\?hl=ko""[\s\S]*?" +
            @"ja: ""https://support\.google\.com/cloud/answer/6158849\?hl=ja""[\s\S]*?" +
            @"zh: ""https://support\.google\.com/cloud/answer/6158849\?hl=zh-CN""[\s\S]*?\};",
            html);

        foreach (var key in new[]
                 {
                     "pageTitle", "title", "intro", "optionalNotice", "overviewTitle",
                     "step1Title", "step2Title", "step3Title", "step4Title", "step5Title", "step6Title",
                     "secretWarning", "consoleLink", "officialHelpLink", "lastChecked", "footerGuidance"
                 })
        {
            Assert.Equal(4, Regex.Matches(html, $@"\b{key}:").Count);
        }
    }

    [Fact]
    public void AntigravityOAuthGuideContainsTranslatedStepsAndSemanticStructure()
    {
        var html = ReadSourceFile("src", "AiLimit.App", "Assets", "Help", "antigravity-oauth.html");

        Assert.Contains("<ol class=\"flow\"", html);
        Assert.Contains("<ol class=\"steps\"", html);
        Assert.True(Regex.Matches(html, "<li class=\"step\">").Count >= 6);
        Assert.Contains("aria-hidden=\"true\"", html);
        Assert.Contains("Desktop app", html);
        Assert.Contains("Client ID", html);
        Assert.Contains("Client Secret", html);
        Assert.Contains("https://console.cloud.google.com/auth/clients", html);
        Assert.Contains("id=\"official-help-link\"", html);
        Assert.Contains("https://support.google.com/cloud/answer/6158849?hl=en", html);
        Assert.Contains("2026-06-06", html);
        Assert.Contains("data-i18n=\"title\"", html);
        Assert.Contains("data-i18n=\"optionalNotice\"", html);
        Assert.Contains("data-i18n=\"secretWarning\"", html);
        Assert.Contains("data-i18n=\"officialHelpLink\"", html);
        Assert.Contains("data-i18n=\"footerGuidance\"", html);
    }

    [Fact]
    public void AntigravityOAuthGuideDoesNotLoadExternalResources()
    {
        var html = ReadSourceFile("src", "AiLimit.App", "Assets", "Help", "antigravity-oauth.html");

        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("url\\s*\\(", html);
        Assert.DoesNotMatch("<(?:img|source|iframe|object|embed|video|audio)\\b", html);
        Assert.DoesNotMatch("<script[^>]+src\\s*=", html);
        Assert.DoesNotMatch("<link[^>]+rel\\s*=\\s*[\"']?stylesheet", html);
        Assert.DoesNotMatch("<link[^>]+href\\s*=", html);
        Assert.DoesNotMatch("@font-face", html);

        var externalUrls = Regex.Matches(html, "(?:href|src)=\"(https?://[^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(
        [
            "https://console.cloud.google.com/auth/clients",
            "https://support.google.com/cloud/answer/6158849?hl=en"
        ], externalUrls);
    }

    [Fact]
    public void AppProjectConfiguresAntigravityOAuthGuideOutputCopy()
    {
        var project = ReadSourceFile("src", "AiLimit.App", "AiLimit.App.csproj");

        Assert.Contains(
            "<None Update=\"Assets\\Help\\antigravity-oauth.html\" CopyToOutputDirectory=\"PreserveNewest\" />",
            project);
        Assert.DoesNotContain("<Content Include=\"Assets\\Help\\antigravity-oauth.html\">", project);
    }

    // ── Task 5: Controls.xaml + App.xaml merged-dictionary assertions ────────

    [Fact]
    public void ControlsXamlHasNoInlineHexColorsInSetterValues()
    {
        var controlsXaml = ReadSourceFile("src", "AiLimit.App", "Theming", "Themes", "Controls.xaml");

        // Any Setter for a brush property must NOT have a raw hex Value="#..."
        var hexSetterPattern = new Regex(
            @"<Setter\s+Property=""(?:Foreground|Background|BorderBrush|Fill|Stroke)""\s+Value=""#[0-9A-Fa-f]{3,8}""",
            RegexOptions.IgnoreCase);

        var matches = hexSetterPattern.Matches(controlsXaml);
        Assert.True(matches.Count == 0,
            $"Controls.xaml contains {matches.Count} inline hex Setter(s):\n" +
            string.Join("\n", matches.Select(m => "  " + m.Value)));
    }

    [Fact]
    public void ControlsXamlBrushSettersUseDynamicResource()
    {
        var controlsPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Theming", "Themes", "Controls.xaml");
        Assert.True(File.Exists(controlsPath), $"Controls.xaml not found at {controlsPath}");

        var doc = XDocument.Load(controlsPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        var brushProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "Foreground", "Background", "BorderBrush", "Fill", "Stroke"
        };

        var badSetters = new List<string>();
        foreach (var setter in doc.Descendants(ns + "Setter"))
        {
            var property = (string?)setter.Attribute("Property");
            var value = (string?)setter.Attribute("Value");
            if (property is null || !brushProperties.Contains(property)) continue;
            if (value is null) continue; // uses Setter.Value child — OK
            if (value.StartsWith("Transparent", StringComparison.OrdinalIgnoreCase)) continue; // non-themed literal — OK
            if (value.StartsWith("{DynamicResource Brush.", StringComparison.Ordinal)) continue; // correct
            if (value.StartsWith("{TemplateBinding", StringComparison.Ordinal)) continue; // correct
            if (value.StartsWith("{StaticResource", StringComparison.Ordinal)) continue; // acceptable static ref
            badSetters.Add($"  Property=\"{property}\" Value=\"{value}\"");
        }

        Assert.True(badSetters.Count == 0,
            $"Controls.xaml has {badSetters.Count} Setter(s) with non-dynamic brush values:\n" +
            string.Join("\n", badSetters));
    }

    [Fact]
    public void ControlsXamlContainsRequiredStyles()
    {
        var controlsPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Theming", "Themes", "Controls.xaml");
        Assert.True(File.Exists(controlsPath), $"Controls.xaml not found at {controlsPath}");

        var doc = XDocument.Load(controlsPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var styles = doc.Descendants(ns + "Style").ToList();

        bool HasImplicit(string targetType) => styles.Any(s =>
            (string?)s.Attribute("TargetType") == targetType &&
            s.Attribute(x + "Key") is null);

        bool HasKeyed(string key) => styles.Any(s =>
            (string?)s.Attribute(x + "Key") == key);

        Assert.True(HasImplicit("Button"), "Controls.xaml: missing implicit Button style");
        Assert.True(HasKeyed("WindowMinimizeButtonStyle"), "Controls.xaml: missing WindowMinimizeButtonStyle");
        Assert.True(HasKeyed("WindowCloseButtonStyle"), "Controls.xaml: missing WindowCloseButtonStyle");
        Assert.True(HasKeyed("DashboardToggleButtonStyle"), "Controls.xaml: missing DashboardToggleButtonStyle");
        Assert.True(HasImplicit("ComboBox"), "Controls.xaml: missing implicit ComboBox style");
        Assert.True(HasImplicit("ComboBoxItem"), "Controls.xaml: missing implicit ComboBoxItem style");
        Assert.True(HasImplicit("ProgressBar"), "Controls.xaml: missing implicit ProgressBar style");
        Assert.True(HasImplicit("Thumb"), "Controls.xaml: missing implicit Thumb style");
        Assert.True(HasImplicit("ScrollBar"), "Controls.xaml: missing implicit ScrollBar style");
    }

    [Fact]
    public void AppXamlResourcesUseMergedDictionaryWithRequiredFiles()
    {
        var appXaml = ReadSourceFile("src", "AiLimit.App", "App.xaml");
        var doc = XDocument.Parse(appXaml);

        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        var sources = doc.Descendants(ns + "ResourceDictionary")
            .Select(rd => (string?)rd.Attribute("Source"))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        Assert.True(
            sources.Any(s => s.Contains("Brushes.xaml")),
            "App.xaml MergedDictionaries must reference Theming/Themes/Brushes.xaml");
        Assert.True(
            sources.Any(s => s.Contains("Colors.Dark.xaml") || s.Contains("Colors.Light.xaml")),
            "App.xaml MergedDictionaries must reference a Colors.*.xaml");
        Assert.True(
            sources.Any(s => s.Contains("Controls.xaml")),
            "App.xaml MergedDictionaries must reference Theming/Themes/Controls.xaml");
    }

    [Fact]
    public void AppXamlHasNoInlineStyles()
    {
        var appXaml = ReadSourceFile("src", "AiLimit.App", "App.xaml");
        var doc = XDocument.Parse(appXaml);

        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        // The Application.Resources element should contain no direct Style children
        var appResources = doc.Root?
            .Element(ns + "Application.Resources");

        Assert.NotNull(appResources);

        var inlineStyles = appResources!.Elements(ns + "Style").ToList();
        Assert.True(inlineStyles.Count == 0,
            $"App.xaml Application.Resources contains {inlineStyles.Count} inline <Style> element(s) — move them to Controls.xaml");
    }

    // ── Task 7: DashboardWindow.xaml semantic brush tokens ──────────────────

    [Fact]
    public void ControlsRegistersBrushKeyConverter()
    {
        var controlsPath = Path.Combine(
            RepoRoot(),
            "src",
            "AiLimit.App",
            "Theming",
            "Themes",
            "Controls.xaml");
        var doc = XDocument.Load(controlsPath);
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var converter = doc.Root?.Elements()
            .FirstOrDefault(element => (string?)element.Attribute(x + "Key") == "BrushKeyConverter");

        Assert.NotNull(converter);
        Assert.Equal("BrushKeyConverter", converter!.Name.LocalName);
    }

    [Theory]
    [InlineData("DashboardWindow.xaml")]
    [InlineData("WidgetWindow.xaml")]
    public void ViewModelBrushBindingsUseBrushKeyConverter(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", fileName);
        var doc = XDocument.Load(path);

        var brushBindings = doc.Descendants()
            .Attributes()
            .Select(attribute => attribute.Value)
            .Where(value => value.StartsWith("{Binding ", StringComparison.Ordinal)
                && value.Contains("Brush", StringComparison.Ordinal))
            .ToList();
        var missingConverter = brushBindings
            .Where(value => !value.Contains(
                "Converter={StaticResource BrushKeyConverter}",
                StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(brushBindings);
        Assert.True(
            missingConverter.Count == 0,
            $"{fileName} has brush bindings without BrushKeyConverter:\n" +
            string.Join("\n", missingConverter));
    }

    [Fact]
    public void DashboardWindowHasNoInlineHexColorLiterals()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");

        // Match any attribute value that is a raw hex color (#RGB, #RRGGBB, #AARRGGBB)
        var hexAttrPattern = new Regex(
            @"(?:Value|Background|Foreground|BorderBrush|Fill|Stroke)=""#[0-9A-Fa-f]{3,8}""",
            RegexOptions.IgnoreCase);

        var matches = hexAttrPattern.Matches(xaml);
        Assert.True(matches.Count == 0,
            $"DashboardWindow.xaml contains {matches.Count} inline hex color literal(s):\n" +
            string.Join("\n", matches.Select(m => "  " + m.Value)));
    }

    [Fact]
    public void DashboardWindowCoreSurfacesUseDynamicResourceBrushKeys()
    {
        var dashboardPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        Assert.True(File.Exists(dashboardPath), $"DashboardWindow.xaml not found at {dashboardPath}");

        var doc = XDocument.Load(dashboardPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        // Window Background — Transparent because AllowsTransparency=True;
        // semantic surface brush is applied to the outer rounded Border instead.
        var window = doc.Root;
        Assert.NotNull(window);
        var windowBg = (string?)window!.Attribute("Background");
        Assert.Equal("Transparent", windowBg);

        // Outer Border (direct child of Window) carries the Brush.Surface.Window brush.
        var outerBorder = window.Elements(ns + "Border").FirstOrDefault();
        Assert.NotNull(outerBorder);
        var outerBorderBg = (string?)outerBorder!.Attribute("Background");
        Assert.Equal("{DynamicResource Brush.Surface.Window}", outerBorderBg);

        // TitleBar Grid Background (first Grid child with MouseLeftButtonDown)
        var titleBarGrid = doc.Descendants(ns + "Grid")
            .FirstOrDefault(g => (string?)g.Attribute("MouseLeftButtonDown") == "TitleBar_MouseLeftButtonDown");
        Assert.NotNull(titleBarGrid);
        var titleBarBg = (string?)titleBarGrid!.Attribute("Background");
        Assert.Equal("{DynamicResource Brush.Surface.TitleBar}", titleBarBg);

        // At least one card Border uses Brush.Surface.Card and Brush.Border.Default
        var cardBorders = doc.Descendants(ns + "Border")
            .Where(b =>
                (string?)b.Attribute("Background") == "{DynamicResource Brush.Surface.Card}" &&
                (string?)b.Attribute("BorderBrush") == "{DynamicResource Brush.Border.Default}")
            .ToList();
        Assert.True(cardBorders.Count >= 1,
            $"No Border found with Background={{DynamicResource Brush.Surface.Card}} and BorderBrush={{DynamicResource Brush.Border.Default}}");

        // No TextBlock should have an inline hex Foreground
        var hexForegroundPattern = new Regex(@"^#[0-9A-Fa-f]{3,8}$");
        var textBlocksWithHexFg = doc.Descendants(ns + "TextBlock")
            .Where(tb => hexForegroundPattern.IsMatch((string?)tb.Attribute("Foreground") ?? ""))
            .ToList();
        Assert.True(textBlocksWithHexFg.Count == 0,
            $"{textBlocksWithHexFg.Count} TextBlock(s) still have inline hex Foreground:\n" +
            string.Join("\n", textBlocksWithHexFg.Select(tb => $"  Text=\"{(string?)tb.Attribute("Text")}\" Foreground=\"{(string?)tb.Attribute("Foreground")}\"")));

        // No Setter for a brush property should use an inline hex Value
        var hexSetterPattern = new Regex(@"^#[0-9A-Fa-f]{3,8}$");
        var brushProperties = new HashSet<string>(StringComparer.Ordinal)
            { "Foreground", "Background", "BorderBrush", "Fill", "Stroke" };
        var badSetters = doc.Descendants(ns + "Setter")
            .Where(s =>
                brushProperties.Contains((string?)s.Attribute("Property") ?? "") &&
                hexSetterPattern.IsMatch((string?)s.Attribute("Value") ?? ""))
            .ToList();
        Assert.True(badSetters.Count == 0,
            $"{badSetters.Count} Setter(s) still have inline hex brush values:\n" +
            string.Join("\n", badSetters.Select(s => $"  Property=\"{(string?)s.Attribute("Property")}\" Value=\"{(string?)s.Attribute("Value")}\"")));
    }

    // ── Task 8: WidgetWindow.xaml semantic brush tokens ─────────────────────

    [Fact]
    public void WidgetWindowHasNoInlineHexColorLiterals()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "WidgetWindow.xaml");

        var hexAttrPattern = new Regex(
            @"(?:Value|Background|Foreground|BorderBrush|Fill|Stroke)=""#[0-9A-Fa-f]{3,8}""",
            RegexOptions.IgnoreCase);

        var matches = hexAttrPattern.Matches(xaml);
        Assert.True(matches.Count == 0,
            $"WidgetWindow.xaml contains {matches.Count} inline hex color literal(s):\n" +
            string.Join("\n", matches.Select(m => "  " + m.Value)));
    }

    [Fact]
    public void WidgetWindowOuterContainerUsesOverlayBrush()
    {
        var widgetPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", "WidgetWindow.xaml");
        Assert.True(File.Exists(widgetPath), $"WidgetWindow.xaml not found at {widgetPath}");

        var doc = XDocument.Load(widgetPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        // The outermost Border (direct child of the Window) has Background = Brush.Surface.Overlay
        var outerBorder = doc.Root?.Elements(ns + "Border").FirstOrDefault();
        Assert.NotNull(outerBorder);
        var outerBg = (string?)outerBorder!.Attribute("Background");
        Assert.Equal("{DynamicResource Brush.Surface.Overlay}", outerBg);
    }

    [Fact]
    public void WidgetWindowInnerCardsUseCardInverseBrushAndWidgetCardBorder()
    {
        var widgetPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", "WidgetWindow.xaml");
        Assert.True(File.Exists(widgetPath), $"WidgetWindow.xaml not found at {widgetPath}");

        var doc = XDocument.Load(widgetPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        // At least one inner card Border uses Brush.Surface.CardInverse background
        var cardInverseBorders = doc.Descendants(ns + "Border")
            .Where(b => (string?)b.Attribute("Background") == "{DynamicResource Brush.Surface.CardInverse}")
            .ToList();
        Assert.True(cardInverseBorders.Count >= 1,
            "No Border found with Background={DynamicResource Brush.Surface.CardInverse}");

        // All CardInverse borders also use Brush.Border.WidgetCard for BorderBrush
        var badBorders = cardInverseBorders
            .Where(b => (string?)b.Attribute("BorderBrush") != "{DynamicResource Brush.Border.WidgetCard}")
            .ToList();
        Assert.True(badBorders.Count == 0,
            $"{badBorders.Count} CardInverse Border(s) do not use Brush.Border.WidgetCard for BorderBrush");
    }

    // ── Task 9: SettingsWindow.xaml semantic brush tokens ───────────────────

    [Fact]
    public void SettingsWindowHasNoInlineHexColorLiterals()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");

        var hexAttrPattern = new Regex(
            @"(?:Value|Background|Foreground|BorderBrush|Fill|Stroke)=""#[0-9A-Fa-f]{3,8}""",
            RegexOptions.IgnoreCase);

        var matches = hexAttrPattern.Matches(xaml);
        Assert.True(matches.Count == 0,
            $"SettingsWindow.xaml contains {matches.Count} inline hex color literal(s):\n" +
            string.Join("\n", matches.Select(m => "  " + m.Value)));
    }

    [Fact]
    public void SettingsWindowCardsUseSurfaceCardAndBorderDefault()
    {
        var settingsPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        Assert.True(File.Exists(settingsPath), $"SettingsWindow.xaml not found at {settingsPath}");

        var doc = XDocument.Load(settingsPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");

        // All named settings cards must use Brush.Surface.Card + Brush.Border.Default
        var cardNames = new[]
        {
            "ProvidersSettingsCard",
            "AntigravityOAuthSettingsCard",
            "LanguageSettingsCard",
            "LimitWarningSettingsCard",
            "UpdateSettingsCard",
            "DiagnosticSettingsCard",
        };

        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var name in cardNames)
        {
            var card = doc.Descendants(ns + "Border")
                .FirstOrDefault(b => (string?)b.Attribute(x + "Name") == name);
            Assert.True(card is not null, $"Card '{name}' not found in SettingsWindow.xaml");

            var bg = (string?)card!.Attribute("Background");
            Assert.True(
                bg == "{DynamicResource Brush.Surface.Card}",
                $"Card '{name}' Background should be Brush.Surface.Card but was '{bg}'");

            var border = (string?)card.Attribute("BorderBrush");
            Assert.True(
                border == "{DynamicResource Brush.Border.Default}",
                $"Card '{name}' BorderBrush should be Brush.Border.Default but was '{border}'");
        }
    }

    [Fact]
    public void DashboardThemeToggleUsesAnimatedThreePositionSlider()
    {
        var dashboardXaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var dashboardCode = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");
        var settingsXaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var settingsCode = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");

        Assert.Contains("x:Name=\"ThemeModeToggle\"", dashboardXaml);
        Assert.Contains("x:Name=\"ThemeToggleThumb\"", dashboardXaml);
        Assert.Contains("x:Name=\"ThemeToggleThumbTransform\"", dashboardXaml);
        Assert.Contains("Click=\"ThemeModeButton_Click\"", dashboardXaml);
        Assert.Contains("Tag=\"Dark\"", dashboardXaml);
        Assert.Contains("Tag=\"System\"", dashboardXaml);
        Assert.Contains("Tag=\"Light\"", dashboardXaml);
        Assert.Contains("DoubleAnimation", dashboardCode);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", dashboardCode);
        Assert.Contains("_state.SetThemeMode(mode)", dashboardCode);
        Assert.DoesNotContain("x:Name=\"ThemeSettingsCard\"", settingsXaml);
        Assert.DoesNotContain("ThemeOptionButton_Click", settingsCode);
    }

    [Fact]
    public void OpacitySlidersIgnoreProgrammaticSynchronization()
    {
        var dashboardCode = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");
        var widgetCode = ReadSourceFile("src", "AiLimit.App", "Windows", "WidgetWindow.xaml.cs");

        Assert.Contains("_isApplyingWindowOpacity", dashboardCode);
        Assert.Contains("DashboardOpacitySlider.SetCurrentValue(", dashboardCode);
        Assert.Contains("RangeBase.ValueProperty", dashboardCode);
        Assert.Contains("opacity * 100", dashboardCode);
        Assert.Contains("!IsLoaded || _isApplyingWindowOpacity", dashboardCode);

        Assert.Contains("_isApplyingWindowOpacity", widgetCode);
        Assert.Contains("WidgetOpacitySlider.SetCurrentValue(", widgetCode);
        Assert.Contains("RangeBase.ValueProperty", widgetCode);
        Assert.Contains("opacity * 100", widgetCode);
        Assert.Contains("!IsLoaded || _isApplyingWindowOpacity", widgetCode);
    }

    [Fact]
    public void SettingsWindowTitleBarUsesSurfaceTitleBarBrush()
    {
        var settingsPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        Assert.True(File.Exists(settingsPath), $"SettingsWindow.xaml not found at {settingsPath}");

        var doc = XDocument.Load(settingsPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var titleBar = doc.Descendants(ns + "Border")
            .FirstOrDefault(b => (string?)b.Attribute(x + "Name") == "SettingsTitleBar");
        Assert.NotNull(titleBar);
        var bg = (string?)titleBar!.Attribute("Background");
        Assert.Equal("{DynamicResource Brush.Surface.TitleBar}", bg);
    }

    [Fact]
    public void SettingsWindowFooterUsesSurfaceTitleBarBrush()
    {
        var settingsPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        Assert.True(File.Exists(settingsPath), $"SettingsWindow.xaml not found at {settingsPath}");

        var doc = XDocument.Load(settingsPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var footer = doc.Descendants(ns + "Border")
            .FirstOrDefault(b => (string?)b.Attribute(x + "Name") == "SettingsActionFooter");
        Assert.NotNull(footer);
        var bg = (string?)footer!.Attribute("Background");
        Assert.Equal("{DynamicResource Brush.Surface.TitleBar}", bg);
    }

    [Fact]
    public void SettingsStatusEllipseDefaultFillIsIndicatorClean()
    {
        var settingsPath = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        Assert.True(File.Exists(settingsPath), $"SettingsWindow.xaml not found at {settingsPath}");

        var doc = XDocument.Load(settingsPath);
        var ns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var ellipse = doc.Descendants(ns + "Ellipse")
            .FirstOrDefault(e => (string?)e.Attribute(x + "Name") == "SettingsStatusEllipse");
        Assert.NotNull(ellipse);
        var fill = (string?)ellipse!.Attribute("Fill");
        Assert.Equal("{DynamicResource Brush.Indicator.Clean}", fill);
    }

    [Fact]
    public void DashboardHasAccountsButtonWiredToCodexAccountProvider()
    {
        // After Task 5 the per-tile Codex profile ComboBox is replaced by the shared
        // Accounts window opened via an AccountsButton in the dashboard toolbar.
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "DashboardWindow.xaml.cs");

        Assert.Contains("x:Name=\"AccountsButton\"", xaml);
        Assert.Contains("Click=\"AccountsButton_Click\"", xaml);
        Assert.Contains("AppState.GetOrCreateCodexAccountProvider()", code);
        Assert.DoesNotContain("x:Name=\"CodexProfileSelector\"", xaml);
    }

    [Fact]
    public void LegacyCodexProfilesSurfaceIsRemoved()
    {
        // Regression: the old "Codex profiles" button + window were removed in Task 8.
        // SettingsWindow must no longer reference ManageCodexProfilesButton_Click or
        // CodexProfilesWindow, and settings must have no SelectedCodexProfileId plumbing.
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "SettingsWindow.xaml.cs");
        var settingsCs = ReadSourceFile("src", "AiLimit.Core", "Settings", "AppSettings.cs");
        var windowsDir = Path.Combine(RepoRoot(), "src", "AiLimit.App", "Windows");

        Assert.DoesNotContain("ManageCodexProfilesButton_Click", xaml);
        Assert.DoesNotContain("CodexProfilesWindow", code);
        Assert.DoesNotContain("SelectedCodexProfileId", settingsCs);
        Assert.False(File.Exists(Path.Combine(windowsDir, "CodexProfilesWindow.xaml")),
            "CodexProfilesWindow.xaml should have been deleted");
        Assert.False(File.Exists(Path.Combine(windowsDir, "CodexProfilesWindow.xaml.cs")),
            "CodexProfilesWindow.xaml.cs should have been deleted");

        // The Accounts button (new path) must still be present.
        Assert.Contains("OpenAccountsWindowButton_Click", code);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "quota-watch.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadSourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "quota-watch.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. segments]));
    }
}

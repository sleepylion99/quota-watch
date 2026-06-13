using AiLimit.App.Tray;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class LimitWarningTextTests
{
    [Fact]
    public void BuildBodyLocalizesKoreanRemainingLimitWarning()
    {
        var warning = Warning("five-hour", "5-hour limit", percent: 8, isUsedPercent: false);

        var text = LimitWarningText.BuildBody(warning, AppLanguage.Korean);

        Assert.Equal("ChatGPT Codex 5시간 한도: 8% 남았습니다.", text);
    }

    [Fact]
    public void BuildBodyLocalizesKoreanUsedLimitWarning()
    {
        var warning = Warning("antigravity-gemini-3-5-flash-medium", "Gemini 3.5 Flash (Medium)", percent: 91, isUsedPercent: true);

        var text = LimitWarningText.BuildBody(warning, AppLanguage.Korean);

        Assert.Contains("ChatGPT Codex Gemini 3.5 Flash (Medium): 91%", text);
    }

    [Theory]
    [InlineData(AppLanguage.English, "AI limit alert", "Weekly limit is low", "Don't show this weekly alert again", "Dismiss")]
    [InlineData(AppLanguage.Korean, "AI 한도 제한 알림", "주간 한도가 얼마 남지 않았습니다", "이 주간 한도 알림 다시 보지 않기", "닫기")]
    [InlineData(AppLanguage.Japanese, "AI 上限アラート", "週次上限が少なくなっています", "この週次アラートを今後表示しない", "閉じる")]
    [InlineData(AppLanguage.Chinese, "AI 限制提醒", "每周限制剩余不多", "不再显示此每周提醒", "关闭")]
    public void WeeklyWarningChromeMatchesSelectedLanguage(
        AppLanguage language,
        string title,
        string weeklyTitle,
        string suppressButton,
        string dismissButton)
    {
        Assert.Equal(title, LimitWarningText.Title(language));
        Assert.Equal(weeklyTitle, LimitWarningText.WeeklyTitle(language));
        Assert.Equal(suppressButton, LimitWarningText.SuppressWeeklyButton(language));
        Assert.Equal(dismissButton, LimitWarningText.DismissButton(language));
    }

    private static LimitWarning Warning(
        string windowId,
        string windowLabel,
        int percent,
        bool isUsedPercent)
    {
        return new LimitWarning(
            ProviderId: "codex",
            ProviderName: "ChatGPT Codex",
            WindowId: windowId,
            WindowLabel: windowLabel,
            Percent: percent,
            IsUsedPercent: isUsedPercent,
            ResetAt: null,
            AccountKey: null);
    }
}

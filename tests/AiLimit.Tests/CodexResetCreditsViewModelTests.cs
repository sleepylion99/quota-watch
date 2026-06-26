using System;
using AiLimit.App.Theming;
using AiLimit.App.ViewModels;
using AiLimit.Core.Domain;
using AiLimit.Core.Settings;
using Xunit;

namespace AiLimit.Tests;

public sealed class CodexResetCreditsViewModelTests
{
    private static UsageSnapshot CodexSnapshot(ResetCreditSummary? resetCredits)
    {
        return new UsageSnapshot(
            "codex",
            "ChatGPT Codex",
            DateTimeOffset.Now,
            UsageSource.Agent,
            UsageStatus.Fresh,
            new[] { new UsageWindow("five-hour", "5h limit", 80, DateTimeOffset.Now.AddHours(3), null, "high") },
            ResetCredits: resetCredits);
    }

    private static ProviderUsageItemViewModel Build(ResetCreditSummary? resetCredits, AppLanguage language)
    {
        var viewModel = new UsageViewModel();
        viewModel.Update([CodexSnapshot(resetCredits)], false, LimitDisplayMode.Bars, language);
        return Assert.Single(viewModel.Providers);
    }

    [Fact]
    public void ImminentExpiryShowsWarningTextAndBrush()
    {
        var provider = Build(new ResetCreditSummary(3, DateTimeOffset.Now.AddDays(3)), AppLanguage.Korean);

        Assert.True(provider.ShowResetCredits);
        Assert.Equal("초기화권 3개 있고 곧 만료되는 게 있어요", provider.ResetCreditText);
        Assert.Equal(BrushKey.StatusWarning, provider.ResetCreditBrush);
    }

    [Fact]
    public void DistantExpiryShowsNormalTextAndBrush()
    {
        var provider = Build(new ResetCreditSummary(2, DateTimeOffset.Now.AddDays(20)), AppLanguage.Korean);

        Assert.True(provider.ShowResetCredits);
        Assert.Equal("초기화권 2개 있음", provider.ResetCreditText);
        Assert.Equal(BrushKey.TextSecondary, provider.ResetCreditBrush);
    }

    [Fact]
    public void NoResetCreditsHidesLine()
    {
        var provider = Build(null, AppLanguage.Korean);

        Assert.False(provider.ShowResetCredits);
        Assert.Equal(string.Empty, provider.ResetCreditText);
    }

    [Fact]
    public void MissingExpiryIsNotImminent()
    {
        var provider = Build(new ResetCreditSummary(1, null), AppLanguage.English);

        Assert.True(provider.ShowResetCredits);
        Assert.Equal("1 reset credits available", provider.ResetCreditText);
        Assert.Equal(BrushKey.TextSecondary, provider.ResetCreditBrush);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(-1, true)]
    public void ImminenceBoundaryIsSevenDays(int daysFromNow, bool expected)
    {
        var now = DateTimeOffset.Parse("2026-06-25T00:00:00Z");

        Assert.Equal(
            expected,
            ProviderUsageItemViewModel.IsResetExpiryImminent(now.AddDays(daysFromNow), now));
    }

    [Fact]
    public void NullExpiryIsNotImminent()
    {
        var now = DateTimeOffset.Parse("2026-06-25T00:00:00Z");

        Assert.False(ProviderUsageItemViewModel.IsResetExpiryImminent(null, now));
    }
}

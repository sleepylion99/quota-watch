using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class InactiveAccountWarningSettingTests
{
    [Fact]
    public void DefaultsToDisabledAndSurvivesNormalize()
    {
        Assert.False(AppSettings.Default.IsInactiveAccountWarningEnabled);

        var enabled = (AppSettings.Default with { IsInactiveAccountWarningEnabled = true }).Normalize();

        Assert.True(enabled.IsInactiveAccountWarningEnabled);
    }
}

using AiLimit.App.Services;
using AiLimit.Core.Settings;

namespace AiLimit.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public void ApplyDark_SetsEffectiveThemeToDark()
    {
        var probe = new FakeSystemThemeProbe(appsUseLightTheme: true);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);

        service.Apply(AppThemeMode.Dark);

        Assert.Equal(ResolvedTheme.Dark, service.EffectiveTheme);
        Assert.Equal(ResolvedTheme.Dark, swapper.LastSwapped);
        Assert.Equal(1, swapper.SwapCallCount);
    }

    [Fact]
    public void ApplyLight_SetsEffectiveThemeToLight()
    {
        var probe = new FakeSystemThemeProbe(appsUseLightTheme: false);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);

        service.Apply(AppThemeMode.Light);

        Assert.Equal(ResolvedTheme.Light, service.EffectiveTheme);
    }

    [Fact]
    public void ApplySystem_ProbeReturnsTrue_SetsEffectiveThemeToLight()
    {
        var probe = new FakeSystemThemeProbe(appsUseLightTheme: true);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);

        service.Apply(AppThemeMode.System);

        Assert.Equal(ResolvedTheme.Light, service.EffectiveTheme);
    }

    [Fact]
    public void ApplySystem_ProbeReturnsFalse_SetsEffectiveThemeToDark()
    {
        var probe = new FakeSystemThemeProbe(appsUseLightTheme: false);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);

        service.Apply(AppThemeMode.System);

        Assert.Equal(ResolvedTheme.Dark, service.EffectiveTheme);
    }

    [Fact]
    public void Apply_FiresThemeChangedWithNewResolvedTheme()
    {
        var probe = new FakeSystemThemeProbe(appsUseLightTheme: false);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);
        ResolvedTheme? fired = null;
        service.ThemeChanged += (_, theme) => fired = theme;

        service.Apply(AppThemeMode.Dark);

        Assert.Equal(ResolvedTheme.Dark, fired);
    }

    [Fact]
    public void Apply_SameEffectiveMode_DoesNotFireThemeChanged()
    {
        var probe = new FakeSystemThemeProbe(appsUseLightTheme: false);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);
        service.Apply(AppThemeMode.Dark);

        int count = 0;
        service.ThemeChanged += (_, _) => count++;
        service.Apply(AppThemeMode.Dark);

        Assert.Equal(0, count);
    }

    [Fact]
    public void Apply_ProbeThrows_FallsBackToDark()
    {
        var probe = new ThrowingSystemThemeProbe();
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);

        service.Apply(AppThemeMode.System);

        Assert.Equal(ResolvedTheme.Dark, service.EffectiveTheme);
    }

    [Fact]
    public void Apply_SystemModeResolvesToSameEffectiveTheme_DoesNotFireThemeChanged()
    {
        // Probe returns false => system resolves to Dark
        var probe = new FakeSystemThemeProbe(appsUseLightTheme: false);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);

        // First apply: Dark explicitly
        service.Apply(AppThemeMode.Dark);
        int swapCountAfterFirstApply = swapper.SwapCallCount;

        // Subscribe AFTER the first apply so we only count subsequent events
        int eventCount = 0;
        service.ThemeChanged += (_, _) => eventCount++;

        // Second apply: System, which also resolves to Dark — guard should fire
        service.Apply(AppThemeMode.System);

        // Event must NOT have fired because the effective theme didn't change
        Assert.Equal(0, eventCount);
        // Mode DID update even though the event was suppressed
        Assert.Equal(AppThemeMode.System, service.CurrentMode);
        // Swap must NOT have been called again (guard short-circuits before Swap)
        Assert.Equal(swapCountAfterFirstApply, swapper.SwapCallCount);
    }

    [Fact]
    public void RaiseSystemThemeChanged_ReevaluatesSystemModeAndFiresThemeChanged()
    {
        var probe = new MutableSystemThemeProbe(appsUseLightTheme: false);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);
        service.Apply(AppThemeMode.System);
        ResolvedTheme? changedTheme = null;
        service.ThemeChanged += (_, theme) => changedTheme = theme;

        probe.AppsUseLightThemeValue = true;
        service.RaiseSystemThemeChanged();

        Assert.Equal(ResolvedTheme.Light, service.EffectiveTheme);
        Assert.Equal(ResolvedTheme.Light, changedTheme);
        Assert.Equal(2, swapper.SwapCallCount);
    }

    [Theory]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public void RaiseSystemThemeChanged_IgnoresManualModes(AppThemeMode mode)
    {
        var probe = new MutableSystemThemeProbe(appsUseLightTheme: false);
        var swapper = new FakeResourceDictionarySwapper();
        var service = new ThemeService(probe, swapper);
        service.Apply(mode);
        var initialTheme = service.EffectiveTheme;
        var initialSwapCount = swapper.SwapCallCount;

        probe.AppsUseLightThemeValue = true;
        service.RaiseSystemThemeChanged();

        Assert.Equal(initialTheme, service.EffectiveTheme);
        Assert.Equal(initialSwapCount, swapper.SwapCallCount);
    }

    private sealed class FakeSystemThemeProbe(bool appsUseLightTheme) : ISystemThemeProbe
    {
        public bool AppsUseLightTheme() => appsUseLightTheme;
    }

    private sealed class MutableSystemThemeProbe(bool appsUseLightTheme) : ISystemThemeProbe
    {
        public bool AppsUseLightThemeValue { get; set; } = appsUseLightTheme;

        public bool AppsUseLightTheme() => AppsUseLightThemeValue;
    }

    private sealed class ThrowingSystemThemeProbe : ISystemThemeProbe
    {
        public bool AppsUseLightTheme() => throw new InvalidOperationException("Registry unavailable");
    }

    private sealed class FakeResourceDictionarySwapper : IResourceDictionarySwapper
    {
        public ResolvedTheme? LastSwapped { get; private set; }
        public int SwapCallCount { get; private set; }

        public void Swap(ResolvedTheme theme)
        {
            LastSwapped = theme;
            SwapCallCount++;
        }
    }
}

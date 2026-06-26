using AiLimit.Core.Settings;
using Microsoft.Win32;

namespace AiLimit.App.Services;

public interface IResourceDictionarySwapper
{
    void Swap(ResolvedTheme theme);
}

// Useful for tests or fail-safe construction when resource dictionaries should not be mutated.
public sealed class NoOpResourceDictionarySwapper : IResourceDictionarySwapper
{
    public void Swap(ResolvedTheme theme) { }
}

public sealed class ThemeService : IDisposable
{
    private readonly ISystemThemeProbe _probe;
    private readonly IResourceDictionarySwapper _swapper;
    private readonly bool _watchSystemThemeChanges;
    private bool _hasApplied;
    private bool _isDisposed;

    public ThemeService(
        ISystemThemeProbe probe,
        IResourceDictionarySwapper swapper,
        bool watchSystemThemeChanges = false)
    {
        _probe = probe;
        _swapper = swapper;
        _watchSystemThemeChanges = watchSystemThemeChanges;
        if (_watchSystemThemeChanges)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    public AppThemeMode CurrentMode { get; private set; }
    public ResolvedTheme EffectiveTheme { get; private set; }
    public event EventHandler<ResolvedTheme>? ThemeChanged;

    public void Apply(AppThemeMode mode)
    {
        var resolved = Resolve(mode);

        if (_hasApplied && resolved == EffectiveTheme)
        {
            CurrentMode = mode;
            return;
        }

        CurrentMode = mode;
        EffectiveTheme = resolved;
        _hasApplied = true;
        _swapper.Swap(resolved);
        ThemeChanged?.Invoke(this, resolved);
    }

    public void ReevaluateSystemTheme()
    {
        if (!_hasApplied || CurrentMode != AppThemeMode.System)
        {
            return;
        }

        var resolved = ResolveFromSystem();
        if (resolved == EffectiveTheme)
        {
            return;
        }

        EffectiveTheme = resolved;
        _swapper.Swap(resolved);
        ThemeChanged?.Invoke(this, resolved);
    }

    internal void RaiseSystemThemeChanged()
    {
        ReevaluateSystemTheme();
    }

    private ResolvedTheme Resolve(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Light => ResolvedTheme.Light,
            AppThemeMode.Dark => ResolvedTheme.Dark,
            _ => ResolveFromSystem()
        };
    }

    private ResolvedTheme ResolveFromSystem()
    {
        try
        {
            return _probe.AppsUseLightTheme() ? ResolvedTheme.Light : ResolvedTheme.Dark;
        }
        catch
        {
            return ResolvedTheme.Dark;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ReevaluateSystemTheme();
            return;
        }

        _ = dispatcher.BeginInvoke(ReevaluateSystemTheme);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_watchSystemThemeChanges)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}

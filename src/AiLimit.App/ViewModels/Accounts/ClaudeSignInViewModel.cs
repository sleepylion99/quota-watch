using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiLimit.App.Localization;
using AiLimit.App.Services;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;

namespace AiLimit.App.ViewModels.Accounts;

public sealed class ClaudeSignInViewModel : INotifyPropertyChanged
{
    private readonly ClaudeLoginFlow _flow;
    private AppLanguage _language;

    public ClaudeSignInViewModel(ClaudeLoginFlow flow) => _flow = flow;

    internal void SetLanguage(AppLanguage language) => _language = language;

    public string? AuthUrl { get; private set; }
    public bool AwaitingCode { get; private set; }
    public string PastedCode { get; set; } = string.Empty;
    public string? StatusMessage { get; private set; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>The sign-in panel is shown once the flow has started or has something to report.</summary>
    public bool ShowPanel => AwaitingCode || HasStatus;

    public void Begin()
    {
        var begin = _flow.BeginSignIn();
        AuthUrl = begin.AuthUrl;
        AwaitingCode = true;
        StatusMessage = begin.BrowserOpened
            ? AppText.Get(_language, AppStringKeys.ClaudeSignInBrowserApproved)
            : AppText.Get(_language, AppStringKeys.ClaudeSignInBrowserFailed, begin.AuthUrl);
        AppLog.Write("Claude", $"Sign-in begin. browserOpened={begin.BrowserOpened}");
        RaiseAll();
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _flow.CompleteSignInAsync(PastedCode, cancellationToken).ConfigureAwait(true);
            StatusMessage = result.Outcome switch
            {
                ClaudeLoginOutcome.Added => AppText.Get(_language, AppStringKeys.ClaudeSignInAdded, result.ProfilePath ?? string.Empty),
                ClaudeLoginOutcome.Cancelled => AppText.Get(_language, AppStringKeys.ClaudeSignInCancelled),
                ClaudeLoginOutcome.ExchangeFailed => AppText.Get(_language, AppStringKeys.ClaudeSignInExchangeFailed, result.ErrorMessage ?? string.Empty),
                ClaudeLoginOutcome.WriteFailed => AppText.Get(_language, AppStringKeys.ClaudeSignInWriteFailed, result.ErrorMessage ?? string.Empty),
                _ => AppText.Get(_language, AppStringKeys.ClaudeSignInFailed, result.ErrorMessage ?? string.Empty),
            };
            AwaitingCode = result.Outcome != ClaudeLoginOutcome.Added;
            AppLog.Write(AppLogLevel.Info, "Claude", $"Sign-in complete. outcome={result.Outcome} error={result.ErrorMessage} path={result.ProfilePath}");
        }
        catch (Exception ex)
        {
            StatusMessage = AppText.Get(_language, AppStringKeys.ClaudeSignInError, ex.GetType().Name, ex.Message);
            AppLog.Write(AppLogLevel.Warning, "Claude", $"Sign-in threw. {ex.GetType().Name}: {ex.Message}");
        }
        RaiseAll();
    }

    private void RaiseAll()
    {
        Raise(nameof(AuthUrl));
        Raise(nameof(AwaitingCode));
        Raise(nameof(StatusMessage));
        Raise(nameof(HasStatus));
        Raise(nameof(ShowPanel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

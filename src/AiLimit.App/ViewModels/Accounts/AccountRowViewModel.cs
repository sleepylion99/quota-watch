using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiLimit.App.Localization;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;

namespace AiLimit.App.ViewModels.Accounts;

public sealed class AccountRowViewModel : INotifyPropertyChanged
{
    private const string PlanFallback = "—";

    private readonly AppLanguage _language;
    private readonly string _loadingText;
    private readonly string _activeText;
    private readonly string _idleText;
    private readonly bool _usesUsedPercent;
    private bool _isActive;
    private string _statusText;
    private string _planText = PlanFallback;
    private DateTimeOffset? _checkedAt;
    private string? _errorMessage;
    private string? _polledStatusText;
    private string _accountText;

    public AccountRowViewModel(
        AccountRecord record,
        AppLanguage language = AppLanguage.English,
        bool canTrash = false,
        bool usesUsedPercent = false)
    {
        ArgumentNullException.ThrowIfNull(record);
        _language = language;
        _usesUsedPercent = usesUsedPercent;
        _loadingText = AppText.Get(language, AppStringKeys.AccountsStatusLoading);
        _activeText = AppText.Get(language, AppStringKeys.AccountsActiveBadge);
        _idleText = AppText.Get(language, AppStringKeys.AccountsStatusIdle);
        _statusText = _loadingText;
        Id = record.Id;
        _accountText = !string.IsNullOrWhiteSpace(record.Email) ? record.Email! : record.DisplayName;
        CanTrash = canTrash;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string AccountText
    {
        get => _accountText;
        private set => SetField(ref _accountText, value);
    }

    public bool CanTrash { get; }

    /// <summary>Updates the displayed account name (e.g. after a poll resolves and caches the email).</summary>
    public void UpdateAccount(AccountRecord record)
        => AccountText = !string.IsNullOrWhiteSpace(record.Email) ? record.Email! : record.DisplayName;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string PlanText
    {
        get => _planText;
        private set => SetField(ref _planText, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetField(ref _isActive, value))
            {
                RecomputeStatusText();
            }
        }
    }

    public DateTimeOffset? CheckedAt
    {
        get => _checkedAt;
        private set => SetField(ref _checkedAt, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public void ApplySnapshot(AccountSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            _polledStatusText = null;
            ErrorMessage = null;
            CheckedAt = null;
            PlanText = PlanFallback;
            RecomputeStatusText();
            return;
        }

        CheckedAt = snapshot.CheckedAt;

        if (!snapshot.IsSuccess)
        {
            ErrorMessage = snapshot.ErrorMessage;
            _polledStatusText = snapshot.ErrorMessage ?? _idleText;
            PlanText = PlanFallback;
            RecomputeStatusText();
            return;
        }

        ErrorMessage = null;
        PlanText = FormatPlan(snapshot.Plan);

        if (snapshot.Buckets.Count == 0)
        {
            _polledStatusText = _idleText;
        }
        else
        {
            var lowest = snapshot.Buckets.Min(bucket => bucket.PercentRemaining);
            _polledStatusText = _usesUsedPercent
                ? AppText.Get(_language, AppStringKeys.PercentUsedValue, 100 - Math.Clamp(lowest, 0, 100))
                : AppText.Get(_language, AppStringKeys.PercentLeftValue, lowest);
        }

        RecomputeStatusText();
    }

    private void RecomputeStatusText()
    {
        if (_isActive)
        {
            StatusText = _activeText;
            return;
        }

        StatusText = _polledStatusText ?? _loadingText;
    }

    private string FormatPlan(AccountPlan plan)
    {
        return plan switch
        {
            AccountPlan.Free => AppText.Get(_language, AppStringKeys.AccountsPlanFree),
            AccountPlan.Pro => AppText.Get(_language, AppStringKeys.AccountsPlanPro),
            AccountPlan.Max => AppText.Get(_language, AppStringKeys.AccountsPlanMax),
            _ => PlanFallback
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

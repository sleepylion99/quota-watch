using AiLimit.App.Localization;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;

namespace AiLimit.App.ViewModels.Accounts;

public sealed class TrashAccountRowViewModel
{
    public TrashAccountRowViewModel(TrashedAccountRecord record, string providerText, AppLanguage language)
    {
        Id = record.Id;
        ProviderKey = record.ProviderKey;
        ProviderText = providerText;
        AccountText = !string.IsNullOrWhiteSpace(record.Email) ? record.Email! : record.DisplayName;
        DeletedAtText = AppText.Get(language, AppStringKeys.AccountsTrashDeletedAt, record.DeletedAt.LocalDateTime);
    }

    public Guid Id { get; }
    public string ProviderKey { get; }
    public string ProviderText { get; }
    public string AccountText { get; }
    public string DeletedAtText { get; }
}

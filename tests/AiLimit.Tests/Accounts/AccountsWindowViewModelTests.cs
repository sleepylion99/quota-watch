using AiLimit.App.ViewModels.Accounts;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AccountsWindowViewModelTests
{
    private static AccountRecord Record(string providerKey)
        => new(Guid.NewGuid(), providerKey, "alice", "alice@example.com", DateTimeOffset.UtcNow);

    private static FakeProvider Provider(string key, string name) => new(key, name);

    private static IReadOnlyList<IAccountProvider> ThreeProviders()
        => new IAccountProvider[]
        {
            Provider("codex", "ChatGPT Codex"),
            Provider("claude", "Claude Code"),
            Provider("gemini-pro", "Google Antigravity")
        };

    [Fact]
    public void ConstructorPopulatesThreeTabsInOrder()
    {
        var vm = new AccountsWindowViewModel(ThreeProviders());

        Assert.Equal(3, vm.Tabs.Count);
        Assert.Equal("codex", vm.Tabs[0].ProviderKey);
        Assert.Equal("claude", vm.Tabs[1].ProviderKey);
        Assert.Equal("gemini-pro", vm.Tabs[2].ProviderKey);
    }

    [Fact]
    public void SelectedTabIndexDefaultsToAntigravityWhenThreeTabsInOrder()
    {
        var vm = new AccountsWindowViewModel(ThreeProviders());
        Assert.Equal(2, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task ActiveAccountChangedFiresOncePerSwitch()
    {
        var providers = ThreeProviders();
        var antigravity = (FakeProvider)providers[2];
        var rec = Record("gemini-pro");
        antigravity.Records.Add(rec);
        var vm = new AccountsWindowViewModel(providers);
        await vm.ReloadCurrentTabAsync(CancellationToken.None);

        int fireCount = 0;
        vm.ActiveAccountChanged += (_, _) => fireCount++;

        await vm.SwitchInCurrentTabAsync(rec.Id, CancellationToken.None);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task MoveToTrashReloadsCurrentTabAndUpdatesTrashRows()
    {
        var providers = ThreeProviders();
        var antigravity = (FakeProvider)providers[2];
        var rec = Record("gemini-pro");
        antigravity.Records.Add(rec);
        var vm = new AccountsWindowViewModel(providers);
        await vm.ReloadCurrentTabAsync(CancellationToken.None);

        await vm.MoveToTrashInCurrentTabAsync(rec.Id, CancellationToken.None);
        await vm.OpenTrashAsync(CancellationToken.None);

        Assert.Empty(vm.SelectedTab!.Rows);
        Assert.Single(vm.TrashRows);
        Assert.Equal(1, vm.TrashCount);
        Assert.Equal("alice@example.com", vm.TrashRows[0].AccountText);
    }

    [Fact]
    public async Task RestoreTrashReturnsRecordToProviderTab()
    {
        var providers = ThreeProviders();
        var antigravity = (FakeProvider)providers[2];
        var rec = Record("gemini-pro");
        antigravity.Records.Add(rec);
        var vm = new AccountsWindowViewModel(providers);
        await vm.ReloadCurrentTabAsync(CancellationToken.None);
        await vm.MoveToTrashInCurrentTabAsync(rec.Id, CancellationToken.None);
        await vm.OpenTrashAsync(CancellationToken.None);

        await vm.RestoreTrashAsync(vm.TrashRows[0], CancellationToken.None);

        Assert.Empty(vm.TrashRows);
        Assert.Single(vm.SelectedTab!.Rows);
        Assert.Equal(rec.Id, vm.SelectedTab.Rows[0].Id);
    }

    [Fact]
    public async Task DeleteTrashPermanentlyRemovesProviderRecord()
    {
        var providers = ThreeProviders();
        var antigravity = (FakeProvider)providers[2];
        var rec = Record("gemini-pro");
        antigravity.Records.Add(rec);
        var vm = new AccountsWindowViewModel(providers);
        await vm.ReloadCurrentTabAsync(CancellationToken.None);
        await vm.MoveToTrashInCurrentTabAsync(rec.Id, CancellationToken.None);
        await vm.OpenTrashAsync(CancellationToken.None);

        await vm.DeleteTrashPermanentlyAsync(vm.TrashRows[0], CancellationToken.None);

        Assert.Empty(antigravity.Records);
        Assert.Empty(vm.TrashRows);
        Assert.Empty(vm.SelectedTab!.Rows);
    }

    [Fact]
    public void TabKeysMatchProviderCatalogIds()
    {
        var vm = new AccountsWindowViewModel(ThreeProviders());

        Assert.Equal("codex", vm.Tabs[0].ProviderKey);
        Assert.Equal("claude", vm.Tabs[1].ProviderKey);
        Assert.Equal("gemini-pro", vm.Tabs[2].ProviderKey);
    }

    private sealed class FakeProvider : IAccountProvider, ITrashableAccountProvider
    {
        public FakeProvider(string key, string name)
        {
            ProviderKey = key;
            DisplayName = name;
        }

        public List<AccountRecord> Records { get; } = new();
        public List<TrashedAccountRecord> Trash { get; } = new();
        public Guid? ActiveId { get; set; }

        public string ProviderKey { get; }
        public string DisplayName { get; }

        public IReadOnlyList<AccountRecord> LoadAccounts()
            => Records.Where(r => Trash.All(t => t.Id != r.Id)).ToList();
        public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken ct)
            => Task.FromResult(AccountSnapshot.Failure("no handler"));
        public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken ct)
            => Task.FromResult<AccountRecord?>(null);
        public void Remove(Guid id) => Records.RemoveAll(r => r.Id == id);
        public void MarkActive(Guid? id) => ActiveId = id;
        public Guid? GetActiveId() => ActiveId;

        public bool CanTrash(AccountRecord account) => Records.Any(r => r.Id == account.Id);
        public IReadOnlyList<TrashedAccountRecord> LoadTrash() => Trash.ToList();
        public Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken)
        {
            var record = Records.Single(r => r.Id == id);
            Trash.Add(new TrashedAccountRecord(record.Id, ProviderKey, record.DisplayName, record.Email, DateTimeOffset.UtcNow));
            if (ActiveId == id) { ActiveId = null; }
            return Task.CompletedTask;
        }

        public Task RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken)
        {
            Trash.RemoveAll(t => t.Id == id);
            return Task.CompletedTask;
        }

        public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken)
        {
            Records.RemoveAll(r => r.Id == id);
            Trash.RemoveAll(t => t.Id == id);
            if (ActiveId == id) { ActiveId = null; }
            return Task.CompletedTask;
        }
    }
}

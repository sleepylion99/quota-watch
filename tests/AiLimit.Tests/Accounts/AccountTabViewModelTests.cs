using AiLimit.App.ViewModels.Accounts;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Providers;
using AiLimit.Core.Settings;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AccountTabViewModelTests
{
    private static AccountRecord Record(string display = "alice", string? email = "alice@example.com")
        => new(Guid.NewGuid(), "gemini-pro", display, email, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ReloadPopulatesRowsFromProvider()
    {
        var provider = new RecordingAccountProvider();
        provider.Records.Add(Record("a"));
        provider.Records.Add(Record("b"));
        var vm = new AccountTabViewModel(provider);

        await vm.ReloadAsync(CancellationToken.None);

        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public async Task ReloadMarksActiveRowBasedOnGetActiveId()
    {
        var provider = new RecordingAccountProvider();
        var first = Record("a");
        var second = Record("b");
        provider.Records.Add(first);
        provider.Records.Add(second);
        provider.ActiveId = second.Id;
        var vm = new AccountTabViewModel(provider);

        await vm.ReloadAsync(CancellationToken.None);

        Assert.False(vm.Rows[0].IsActive);
        Assert.True(vm.Rows[1].IsActive);
    }

    [Fact]
    public async Task SyncFromLocalCatchesNotImplementedAndSetsError()
    {
        var provider = new RecordingAccountProvider
        {
            ImportHandler = (_) => throw new NotImplementedException("nope")
        };
        var vm = new AccountTabViewModel(provider);

        await vm.SyncFromLocalAsync(CancellationToken.None);

        Assert.Equal("Test Provider account import is coming in a later release.", vm.SyncErrorMessage);
    }

    [Fact]
    public async Task SyncFromLocalSetsNoSourceErrorWhenReturnsNull()
    {
        var provider = new RecordingAccountProvider
        {
            ImportHandler = (_) => Task.FromResult<AccountRecord?>(null)
        };
        var vm = new AccountTabViewModel(provider);

        await vm.SyncFromLocalAsync(CancellationToken.None);

        Assert.Equal("No local sign-in was detected to import.", vm.SyncErrorMessage);
    }

    [Fact]
    public async Task SyncFromLocalSuccessReloadsAndClearsError()
    {
        var provider = new RecordingAccountProvider();
        var imported = Record("freshly-synced");
        provider.ImportHandler = (_) =>
        {
            provider.Records.Add(imported);
            return Task.FromResult<AccountRecord?>(imported);
        };
        var vm = new AccountTabViewModel(provider);

        await vm.ReloadAsync(CancellationToken.None);
        Assert.Empty(vm.Rows);

        await vm.SyncFromLocalAsync(CancellationToken.None);

        Assert.Single(vm.Rows);
        Assert.Null(vm.SyncErrorMessage);
    }

    [Fact]
    public async Task SwitchToCallsMarkActive()
    {
        var provider = new RecordingAccountProvider();
        var rec = Record("x");
        provider.Records.Add(rec);
        var vm = new AccountTabViewModel(provider);
        await vm.ReloadAsync(CancellationToken.None);

        await vm.SwitchToAsync(rec.Id, CancellationToken.None);

        Assert.Contains(rec.Id, provider.MarkActiveCalls);
    }

    [Fact]
    public async Task SwitchToUpdatesIsActiveOnAllRows()
    {
        var provider = new RecordingAccountProvider();
        var r1 = Record("a");
        var r2 = Record("b");
        var r3 = Record("c");
        provider.Records.Add(r1);
        provider.Records.Add(r2);
        provider.Records.Add(r3);
        var vm = new AccountTabViewModel(provider);
        await vm.ReloadAsync(CancellationToken.None);

        await vm.SwitchToAsync(r2.Id, CancellationToken.None);

        Assert.False(vm.Rows[0].IsActive);
        Assert.True(vm.Rows[1].IsActive);
        Assert.False(vm.Rows[2].IsActive);
    }

    [Fact]
    public async Task RefreshAllPropagatesSnapshotsToCards()
    {
        var provider = new RecordingAccountProvider();
        var r1 = Record("a");
        var r2 = Record("b");
        provider.Records.Add(r1);
        provider.Records.Add(r2);
        provider.PollHandler = (record, _) =>
        {
            var pct = record.Id == r1.Id ? 70 : 25;
            return Task.FromResult(AccountSnapshot.Success(
                new[] { new QuotaBucket("m", pct, DateTimeOffset.UtcNow.AddHours(1)) },
                AccountPlan.Pro));
        };
        var vm = new AccountTabViewModel(provider);
        await vm.ReloadAsync(CancellationToken.None);

        await vm.RefreshAllAsync(CancellationToken.None);

        Assert.Equal("30% used", vm.Rows[0].StatusText);
        Assert.Equal("75% used", vm.Rows[1].StatusText);
    }

    [Fact]
    public async Task CreateProfileAsync_SurfacesNeedsElevationBanner()
    {
        var provider = new FakeCodexProvider(CreateProfileOutcome.NeedsElevation);
        var vm = new AccountTabViewModel(provider, AppLanguage.English);
        await vm.CreateProfileAsync(CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(vm.SyncErrorMessage));
        Assert.True(vm.SupportsProfileCreation);
    }

    [Fact]
    public async Task CreateProfileAsync_CreatedBannerMentionsCleanupWindow()
    {
        var provider = new FakeCodexProvider(CreateProfileOutcome.Created, "codex2");
        var vm = new AccountTabViewModel(provider, AppLanguage.English);

        await vm.CreateProfileAsync(CancellationToken.None);

        Assert.Contains("codex2", vm.SyncErrorMessage);
        Assert.Contains("60 seconds", vm.SyncErrorMessage);
        Assert.Contains("removed automatically", vm.SyncErrorMessage);
    }

    [Fact]
    public void SyncFromLocalReturnsToCapturedContextBeforeUpdatingRows()
    {
        var provider = new RecordingAccountProvider();
        var imported = Record("freshly-synced");
        provider.ImportHandler = async _ =>
        {
            await Task.Delay(10).ConfigureAwait(false);
            provider.Records.Add(imported);
            return imported;
        };
        var vm = new AccountTabViewModel(provider);
        var ownerThreadId = Environment.CurrentManagedThreadId;
        int? collectionChangedThreadId = null;
        vm.Rows.CollectionChanged += (_, _) =>
            collectionChangedThreadId = Environment.CurrentManagedThreadId;

        RunWithPumpedSynchronizationContext(() => vm.SyncFromLocalAsync(CancellationToken.None));

        Assert.Equal(ownerThreadId, collectionChangedThreadId);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public void RefreshAllReturnsToCapturedContextBeforeUpdatingRows()
    {
        var provider = new RecordingAccountProvider();
        var record = Record("alice");
        provider.Records.Add(record);
        provider.PollHandler = async (_, _) =>
        {
            await Task.Delay(10).ConfigureAwait(false);
            return AccountSnapshot.Success(
                new[] { new QuotaBucket("m", 42, DateTimeOffset.UtcNow.AddHours(1)) },
                AccountPlan.Pro);
        };
        var vm = new AccountTabViewModel(provider);
        RunWithPumpedSynchronizationContext(() => vm.ReloadAsync(CancellationToken.None));
        var ownerThreadId = Environment.CurrentManagedThreadId;
        int? propertyChangedThreadId = null;
        vm.Rows[0].PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AccountRowViewModel.StatusText))
            {
                propertyChangedThreadId = Environment.CurrentManagedThreadId;
            }
        };

        RunWithPumpedSynchronizationContext(() => vm.RefreshAllAsync(CancellationToken.None));

        Assert.Equal(ownerThreadId, propertyChangedThreadId);
        Assert.Equal("58% used", vm.Rows[0].StatusText);
    }

    [Fact]
    public async Task SignInWithGoogle_SuccessSetsBannerAndReloads()
    {
        var provider = new RecordingAccountProvider();
        var imported = Record("via-login");
        var vm = new AccountTabViewModel(provider, AppLanguage.English,
            signIn: ct => { provider.Records.Add(imported); return Task.FromResult(
                new AntigravityLoginResult(AntigravityLoginOutcome.Added, "via-login@example.com")); });

        await vm.SignInWithGoogleAsync(CancellationToken.None);

        Assert.Single(vm.Rows);
        Assert.False(string.IsNullOrEmpty(vm.SyncErrorMessage));
    }

    [Fact]
    public void SignIn_AbsentWhenNoDelegate()
    {
        var vm = new AccountTabViewModel(new RecordingAccountProvider());
        Assert.False(vm.SupportsGoogleSignIn);
    }

    [Fact]
    public async Task CompleteClaudeSignInAsync_ReloadsRowsAfterAccountAdded()
    {
        var provider = new RecordingAccountProvider("claude");
        var signIn = new ClaudeSignInViewModel(new ClaudeLoginFlow(
            openBrowser: _ => { },
            credentials: new ClaudeOAuthCredentialStore(new HttpClient(new CannedClaudeTokenHandler())),
            writeProfile: _ =>
            {
                provider.Records.Add(new AccountRecord(
                    Guid.NewGuid(),
                    "claude",
                    ".claude2",
                    "new-claude@example.com",
                    DateTimeOffset.UtcNow));
                return @"C:\Users\test\.claude2\.credentials.json";
            },
            stateFactory: () => "STATE"));
        var vm = new AccountTabViewModel(provider, AppLanguage.English, claudeSignIn: signIn);
        signIn.Begin();
        signIn.PastedCode = "code#STATE";

        await vm.CompleteClaudeSignInAsync(CancellationToken.None);

        Assert.False(signIn.AwaitingCode);
        Assert.Single(vm.Rows);
        Assert.Equal("new-claude@example.com", vm.Rows[0].AccountText);
    }

    [Fact]
    public async Task RefreshAll_ForClaudeProvider_ShowsUsedPercent()
    {
        var provider = new RecordingAccountProvider("claude");
        var record = Record("claude-user");
        provider.Records.Add(record);
        provider.PollHandler = (_, _) => Task.FromResult(AccountSnapshot.Success(
            new[] { new QuotaBucket("m", 2, DateTimeOffset.UtcNow.AddHours(1)) },
            AccountPlan.Pro));
        var vm = new AccountTabViewModel(provider);
        await vm.ReloadAsync(CancellationToken.None);

        await vm.RefreshAllAsync(CancellationToken.None);

        Assert.Equal("98% used", vm.Rows[0].StatusText);
    }

    [Fact]
    public async Task RefreshAll_ForCodexProvider_ShowsRemainingPercent()
    {
        var provider = new RecordingAccountProvider("codex");
        var record = Record("codex-user");
        provider.Records.Add(record);
        provider.PollHandler = (_, _) => Task.FromResult(AccountSnapshot.Success(
            new[] { new QuotaBucket("m", 0, DateTimeOffset.UtcNow.AddHours(1)) },
            AccountPlan.Pro));
        var vm = new AccountTabViewModel(provider);
        await vm.ReloadAsync(CancellationToken.None);

        await vm.RefreshAllAsync(CancellationToken.None);

        Assert.Equal("0% left", vm.Rows[0].StatusText);
    }

    [Theory]
    [InlineData(AppLanguage.English, "Sign in with Anthropic")]
    [InlineData(AppLanguage.Korean, "Anthropic으로 로그인")]
    public void SignInWithClaudeText_IsLocalized(AppLanguage language, string expected)
    {
        var vm = new AccountTabViewModel(new RecordingAccountProvider("claude"), language);
        Assert.Equal(expected, vm.SignInWithClaudeText);
    }

    [Theory]
    [InlineData(AppLanguage.English, "Submit code")]
    [InlineData(AppLanguage.Korean, "코드 제출")]
    public void ClaudeSubmitCodeText_IsLocalized(AppLanguage language, string expected)
    {
        var vm = new AccountTabViewModel(new RecordingAccountProvider("claude"), language);
        Assert.Equal(expected, vm.ClaudeSubmitCodeText);
    }

    private static void RunWithPumpedSynchronizationContext(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        using var context = new PumpSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            context.Run(action());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly AutoResetEvent _workAvailable = new(false);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_queue)
            {
                _queue.Enqueue((callback, state));
            }

            _workAvailable.Set();
        }

        public void Run(Task task)
        {
            while (!task.IsCompleted)
            {
                (SendOrPostCallback Callback, object? State)? work = null;
                lock (_queue)
                {
                    if (_queue.Count > 0)
                    {
                        work = _queue.Dequeue();
                    }
                }

                if (work is { } item)
                {
                    item.Callback(item.State);
                }
                else
                {
                    _workAvailable.WaitOne(TimeSpan.FromMilliseconds(100));
                }
            }

            task.GetAwaiter().GetResult();
        }

        public void Dispose() => _workAvailable.Dispose();
    }

    private sealed class FakeCodexProvider : IAccountProvider, ICodexProfileCreator
    {
        private readonly CreateProfileOutcome _outcome;
        private readonly string? _launchCommand;

        public FakeCodexProvider(CreateProfileOutcome outcome, string? launchCommand = null)
        {
            _outcome = outcome;
            _launchCommand = launchCommand;
        }

        public string ProviderKey => "codex";
        public string DisplayName => "Codex";
        public IReadOnlyList<AccountRecord> LoadAccounts() => Array.Empty<AccountRecord>();
        public Guid? GetActiveId() => null;
        public void MarkActive(Guid? id) { }
        public void Remove(Guid id) { }
        public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken ct)
            => Task.FromResult(AccountSnapshot.Failure("x"));
        public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken ct)
            => Task.FromResult<AccountRecord?>(null);
        public Task<CreateProfileResult> CreateParallelProfileAsync(CancellationToken ct)
            => Task.FromResult(new CreateProfileResult(_outcome, LaunchCommand: _launchCommand));
    }

    private sealed class LocalIdeRecordingProvider : IAccountProvider, ILocalIdeAccount
    {
        public List<AccountRecord> Records { get; } = new();
        public Guid? ActiveId { get; set; }
        public List<Guid?> MarkActiveCallsNullable { get; } = new();

        public string ProviderKey => "gemini-pro";
        public string DisplayName => "Test Provider";
        public IReadOnlyList<AccountRecord> LoadAccounts() => Records.ToList();
        public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken ct)
            => Task.FromResult(AccountSnapshot.Failure("no handler"));
        public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken ct)
            => Task.FromResult<AccountRecord?>(null);
        public void Remove(Guid id) => Records.RemoveAll(r => r.Id == id);
        public void MarkActive(Guid? id)
        {
            MarkActiveCallsNullable.Add(id);
            ActiveId = id;
        }
        public Guid? GetActiveId() => ActiveId;
    }

    [Fact]
    public async Task LocalIdeRow_IsPrependedAndActiveWhenNoAccountSelected()
    {
        var provider = new LocalIdeRecordingProvider();
        provider.Records.Add(Record("a"));
        var vm = new AccountTabViewModel(provider);

        await vm.ReloadAsync(CancellationToken.None);

        Assert.True(vm.SupportsLocalIde);
        Assert.Equal(2, vm.Rows.Count);          // local row + 1 account
        Assert.Equal(Guid.Empty, vm.Rows[0].Id); // local row first
        Assert.True(vm.Rows[0].IsActive);         // active because no account selected
    }

    [Fact]
    public async Task SwitchingToLocalIdeRow_ClearsActiveAccount()
    {
        var provider = new LocalIdeRecordingProvider();
        var acc = Record("a");
        provider.Records.Add(acc);
        var vm = new AccountTabViewModel(provider);
        await vm.ReloadAsync(CancellationToken.None);

        await vm.SwitchToAsync(acc.Id, CancellationToken.None);   // select cloud account
        Assert.Equal(acc.Id, provider.GetActiveId());

        await vm.SwitchToAsync(Guid.Empty, CancellationToken.None); // switch to IDE local
        Assert.Null(provider.GetActiveId());
    }

    [Fact]
    public async Task NoLocalIdeRow_ForProvidersWithoutLocalIde()
    {
        var provider = new RecordingAccountProvider(); // does NOT implement ILocalIdeAccount
        provider.Records.Add(Record("a"));
        var vm = new AccountTabViewModel(provider);
        await vm.ReloadAsync(CancellationToken.None);
        Assert.False(vm.SupportsLocalIde);
        Assert.Single(vm.Rows); // only the account, no local row
    }

    private sealed class RecordingAccountProvider : IAccountProvider
    {
        public RecordingAccountProvider(string providerKey = "gemini-pro") => ProviderKey = providerKey;

        public List<AccountRecord> Records { get; } = new();
        public Guid? ActiveId { get; set; }
        public Func<AccountRecord, CancellationToken, Task<AccountSnapshot>>? PollHandler { get; set; }
        public Func<CancellationToken, Task<AccountRecord?>>? ImportHandler { get; set; }
        public List<Guid> MarkActiveCalls { get; } = new();
        public List<Guid?> MarkActiveCallsNullable { get; } = new();

        public string ProviderKey { get; }
        public string DisplayName => "Test Provider";
        public IReadOnlyList<AccountRecord> LoadAccounts() => Records.ToList();
        public Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken ct) =>
            PollHandler?.Invoke(record, ct) ?? Task.FromResult(AccountSnapshot.Failure("no handler"));
        public Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken ct) =>
            ImportHandler?.Invoke(ct) ?? Task.FromResult<AccountRecord?>(null);
        public void Remove(Guid id) => Records.RemoveAll(r => r.Id == id);
        public void MarkActive(Guid? id)
        {
            MarkActiveCallsNullable.Add(id);
            if (id.HasValue) MarkActiveCalls.Add(id.Value);
            ActiveId = id;
        }
        public Guid? GetActiveId() => ActiveId;
    }

    private sealed class CannedClaudeTokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"at\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
            });
        }
    }
}

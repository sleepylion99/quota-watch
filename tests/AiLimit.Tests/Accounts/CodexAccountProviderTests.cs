using AiLimit.Core.Domain;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class CodexAccountProviderTests
{
    private static string NewRoot(out string codexDir)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"capr-{Guid.NewGuid():N}")).FullName;
        codexDir = Path.Combine(root, ".codex");
        Directory.CreateDirectory(codexDir);
        File.WriteAllText(Path.Combine(codexDir, "auth.json"),
            "{\"tokens\":{\"id_token\":\"x.x.\",\"access_token\":\"a\"}}");
        return root;
    }

    private sealed class ThrowingSymlinks : ISymlinkCreator
    {
        public void Create(string l, string t, bool d) => throw new InvalidOperationException();
    }

    private sealed class CreatedProfileLinker(string profilePath) : ICodexProfileCreator
    {
        public Task<CreateProfileResult> CreateParallelProfileAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(profilePath);
            return Task.FromResult(new CreateProfileResult(
                CreateProfileOutcome.Created,
                profilePath,
                Path.GetFileName(profilePath).TrimStart('.')));
        }
    }

    private static CodexAccountProvider Build(
        string root,
        Func<string, CancellationToken, Task<AccountSnapshot>>? poll = null,
        Func<string, CancellationToken, Task<UsageSnapshot>>? pollUsage = null)
    {
        var scanner = new CodexProfileScanner(root);
        var selection = new CodexActiveSelection(Path.Combine(root, "active.json"));
        var linker = new CodexProfileLinker(root, () => false, new ThrowingSymlinks(), Path.Combine(root, "bin"));
        var trash = new AccountTrashStore(Path.Combine(root, "trash.json"));
        poll ??= (_, _) => Task.FromResult(AccountSnapshot.Success(Array.Empty<QuotaBucket>(), AccountPlan.Unknown));
        return new CodexAccountProvider(scanner, selection, linker, poll, trash, pollUsage);
    }

    [Fact]
    public void LoadAccounts_ReturnsScannedProfiles()
    {
        var root = NewRoot(out _);
        Assert.Single(Build(root).LoadAccounts());
    }

    [Fact]
    public void MarkActive_RoundTrips()
    {
        var root = NewRoot(out _);
        var provider = Build(root);
        var id = provider.LoadAccounts()[0].Id;
        provider.MarkActive(id);
        Assert.Equal(id, provider.GetActiveId());
    }

    [Fact]
    public void ResolveActiveAuthPath_ReturnsActiveProfileAuth()
    {
        var root = NewRoot(out var codexDir);
        var provider = Build(root);
        provider.MarkActive(provider.LoadAccounts()[0].Id);
        Assert.Equal(Path.Combine(codexDir, "auth.json"), provider.ResolveActiveAuthPath());
    }

    [Fact]
    public void ResolveActiveAuthPath_FallsBackToDefault_WhenNoneSelected()
    {
        var root = NewRoot(out var codexDir);
        var provider = Build(root);
        Assert.Equal(Path.Combine(codexDir, "auth.json"), provider.ResolveActiveAuthPath());
    }

    [Fact]
    public async Task PollAsync_UsesInjectedDelegate()
    {
        var root = NewRoot(out _);
        var called = false;
        var provider = Build(root, (_, _) => { called = true; return Task.FromResult(AccountSnapshot.Success(Array.Empty<QuotaBucket>(), AccountPlan.Pro)); });
        var record = provider.LoadAccounts()[0];
        var snap = await provider.PollAsync(record, CancellationToken.None);
        Assert.True(called);
        Assert.Equal(AccountPlan.Pro, snap.Plan);
    }

    [Fact]
    public async Task PollAsync_FailsWhenProfileGone()
    {
        var root = NewRoot(out _);
        var provider = Build(root);
        var snap = await provider.PollAsync(new AccountRecord(Guid.NewGuid(), "codex", "x", null, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.False(snap.IsSuccess);
    }

    [Fact]
    public async Task CreateParallelProfileAsync_DelegatesToLinker()
    {
        var root = NewRoot(out _);
        var result = await Build(root).CreateParallelProfileAsync(CancellationToken.None);
        Assert.Equal(CreateProfileOutcome.NeedsElevation, result.Outcome); // linker built with isElevated:false
    }

    [Fact]
    public async Task CreateParallelProfileAsync_RemovesUnclaimedProfileAfterDelay()
    {
        var root = NewRoot(out _);
        var codex2 = Path.Combine(root, ".codex2");
        var cleanupRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new CodexAccountProvider(
            new CodexProfileScanner(root),
            new CodexActiveSelection(Path.Combine(root, "active.json")),
            new CreatedProfileLinker(codex2),
            (_, _) => Task.FromResult(AccountSnapshot.Success(Array.Empty<QuotaBucket>(), AccountPlan.Unknown)),
            new AccountTrashStore(Path.Combine(root, "trash.json")),
            unclaimedProfileCleanupDelay: TimeSpan.Zero,
            delayAsync: (_, _) => Task.CompletedTask,
            onUnclaimedProfileCleanupComplete: cleanupRan.SetResult);

        var result = await provider.CreateParallelProfileAsync(CancellationToken.None);
        await cleanupRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(CreateProfileOutcome.Created, result.Outcome);
        Assert.False(Directory.Exists(codex2));
    }

    [Fact]
    public async Task CreateParallelProfileAsync_KeepsProfileWhenLoginCompletesBeforeCleanup()
    {
        var root = NewRoot(out _);
        var codex2 = Path.Combine(root, ".codex2");
        var delayReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new CodexAccountProvider(
            new CodexProfileScanner(root),
            new CodexActiveSelection(Path.Combine(root, "active.json")),
            new CreatedProfileLinker(codex2),
            (_, _) => Task.FromResult(AccountSnapshot.Success(Array.Empty<QuotaBucket>(), AccountPlan.Unknown)),
            new AccountTrashStore(Path.Combine(root, "trash.json")),
            unclaimedProfileCleanupDelay: TimeSpan.FromSeconds(60),
            delayAsync: (_, _) => delayReleased.Task,
            onUnclaimedProfileCleanupComplete: cleanupRan.SetResult);

        var result = await provider.CreateParallelProfileAsync(CancellationToken.None);
        File.WriteAllText(Path.Combine(codex2, "auth.json"), "{\"tokens\":{\"id_token\":\"x.x.\"}}");
        delayReleased.SetResult();
        await cleanupRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(CreateProfileOutcome.Created, result.Outcome);
        Assert.True(Directory.Exists(codex2));
    }

    [Fact]
    public async Task MoveToTrash_HidesNumberedProfileAndRestoreReturnsIt()
    {
        var root = NewRoot(out _);
        var codex2 = Path.Combine(root, ".codex2");
        Directory.CreateDirectory(codex2);
        File.WriteAllText(Path.Combine(codex2, "auth.json"), "{\"tokens\":{\"id_token\":\"x.x.\"}}");
        var provider = Build(root);
        var record = provider.LoadAccounts().Single(r => r.DisplayName == ".codex2");

        Assert.True(provider.CanTrash(record));
        await provider.MoveToTrashAsync(record.Id, CancellationToken.None);

        Assert.DoesNotContain(provider.LoadAccounts(), r => r.Id == record.Id);
        Assert.Single(provider.LoadTrash());

        await provider.RestoreFromTrashAsync(record.Id, CancellationToken.None);
        Assert.Contains(provider.LoadAccounts(), r => r.Id == record.Id);
    }

    [Fact]
    public async Task DeletePermanently_RemovesOnlySafeNumberedProfile()
    {
        var root = NewRoot(out _);
        var codex2 = Path.Combine(root, ".codex2");
        Directory.CreateDirectory(codex2);
        File.WriteAllText(Path.Combine(codex2, "auth.json"), "{\"tokens\":{\"id_token\":\"x.x.\"}}");
        var provider = Build(root);
        var record = provider.LoadAccounts().Single(r => r.DisplayName == ".codex2");

        await provider.MoveToTrashAsync(record.Id, CancellationToken.None);
        await provider.DeletePermanentlyAsync(record.Id, CancellationToken.None);

        Assert.False(Directory.Exists(codex2));
        Assert.Empty(provider.LoadTrash());
    }

    [Fact]
    public async Task DeletePermanently_RemovesReadOnlyCacheLeftoversFromTrashedProfile()
    {
        var root = NewRoot(out _);
        var scanner = new CodexProfileScanner(root);
        var selection = new CodexActiveSelection(Path.Combine(root, "active.json"));
        var linker = new CodexProfileLinker(root, () => false, new ThrowingSymlinks(), Path.Combine(root, "bin"));
        var trash = new AccountTrashStore(Path.Combine(root, "trash.json"));
        var codex2 = Path.Combine(root, ".codex2");
        var packDir = Path.Combine(codex2, ".tmp", "plugins", ".git", "objects", "pack");
        Directory.CreateDirectory(packDir);
        var packFile = Path.Combine(packDir, "pack-test.pack");
        File.WriteAllText(packFile, "readonly cache");
        File.SetAttributes(packFile, FileAttributes.ReadOnly);
        var provider = new CodexAccountProvider(
            scanner,
            selection,
            linker,
            (_, _) => Task.FromResult(AccountSnapshot.Success(Array.Empty<QuotaBucket>(), AccountPlan.Unknown)),
            trash);
        var staleId = Guid.NewGuid();
        trash.Put(new TrashedAccountRecord(
            staleId,
            "codex",
            ".codex2",
            null,
            DateTimeOffset.UtcNow,
            codex2));

        await provider.DeletePermanentlyAsync(staleId, CancellationToken.None);

        Assert.False(Directory.Exists(codex2));
        Assert.Empty(provider.LoadTrash());
    }

    [Fact]
    public void CanTrash_ProtectsPrimaryCodexProfile()
    {
        var root = NewRoot(out _);
        var provider = Build(root);
        var primary = provider.LoadAccounts().Single();

        Assert.False(provider.CanTrash(primary));
    }

    [Fact]
    public async Task PollUsageAsyncStampsAccountLabelOntoDisplayName()
    {
        var root = NewRoot(out _);
        var provider = Build(root,
            pollUsage: (_, _) => Task.FromResult(new UsageSnapshot(
                "codex", "ChatGPT Codex", DateTimeOffset.UtcNow, UsageSource.Agent, UsageStatus.Fresh,
                [new UsageWindow("five-hour", "5-hour limit", 8, null, null, "high")],
                AccountKey: "codex-acct-1")));
        var record = provider.LoadAccounts().First();

        var snapshot = await provider.PollUsageAsync(record, CancellationToken.None);

        Assert.Equal(UsageStatus.Fresh, snapshot.Status);
        Assert.Equal("codex-acct-1", snapshot.AccountKey);
        Assert.Contains("—", snapshot.DisplayName);
    }
}

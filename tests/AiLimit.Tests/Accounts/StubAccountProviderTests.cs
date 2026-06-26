using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class StubAccountProviderTests
{
    private static StubAccountProvider New() => new("codex", "ChatGPT Codex");

    [Fact]
    public void LoadAccountsReturnsEmpty()
    {
        Assert.Empty(New().LoadAccounts());
    }

    [Fact]
    public async Task ImportFromLocalSourceThrowsNotImplemented()
    {
        var ex = await Assert.ThrowsAsync<NotImplementedException>(
            () => New().ImportFromLocalSourceAsync(CancellationToken.None));

        Assert.Contains("ChatGPT Codex", ex.Message);
        Assert.Contains("not yet implemented", ex.Message);
    }

    [Fact]
    public async Task PollAsyncThrowsNotSupported()
    {
        var record = new AccountRecord(Guid.NewGuid(), "codex", "x", null, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => New().PollAsync(record, CancellationToken.None));
    }

    [Fact]
    public void RemoveAndMarkActiveAreNoOps()
    {
        var stub = New();
        stub.Remove(Guid.NewGuid());
        stub.MarkActive(Guid.NewGuid());
        stub.MarkActive(null);
        Assert.Null(stub.GetActiveId());  // still null after MarkActive call
    }

    [Fact]
    public void ConstructorRejectsBlankArgs()
    {
        Assert.Throws<ArgumentException>(() => new StubAccountProvider("", "x"));
        Assert.Throws<ArgumentException>(() => new StubAccountProvider("x", ""));
    }

    [Fact]
    public async Task TrashContractIsSafeAndEmpty()
    {
        var stub = New();
        var record = new AccountRecord(Guid.NewGuid(), "codex", "x", null, DateTimeOffset.UtcNow);

        Assert.IsAssignableFrom<ITrashableAccountProvider>(stub);
        Assert.False(stub.CanTrash(record));
        Assert.Empty(stub.LoadTrash());

        await stub.MoveToTrashAsync(record.Id, CancellationToken.None);
        await stub.RestoreFromTrashAsync(record.Id, CancellationToken.None);
        await stub.DeletePermanentlyAsync(record.Id, CancellationToken.None);
    }
}

using AiLimit.App.ViewModels.Accounts;
using AiLimit.Core.Providers.Accounts;
using AiLimit.Core.Settings;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AccountRowViewModelTests
{
    private static AccountRecord SampleRecord(string? email = "alice@example.com") =>
        new(Guid.NewGuid(), "gemini-pro", "alice", email, DateTimeOffset.UtcNow);

    private static QuotaBucket Bucket(string model, int pct) =>
        new(model, pct, DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public void AccountTextPrefersEmail()
    {
        var vm = new AccountRowViewModel(SampleRecord("alice@example.com"));
        Assert.Equal("alice@example.com", vm.AccountText);
    }

    [Fact]
    public void AccountTextFallsBackToDisplayName()
    {
        var vm = new AccountRowViewModel(SampleRecord(null));
        Assert.Equal("alice", vm.AccountText);
    }

    [Fact]
    public void StatusTextDefaultsToLoading()
    {
        var vm = new AccountRowViewModel(SampleRecord());
        Assert.Equal("Loading…", vm.StatusText);
    }

    [Fact]
    public void ApplySnapshotSuccessSetsLowestBucketStatus()
    {
        var vm = new AccountRowViewModel(SampleRecord());
        var snapshot = AccountSnapshot.Success(
            new[] { Bucket("a", 80), Bucket("b", 45), Bucket("c", 92) },
            AccountPlan.Pro);

        vm.ApplySnapshot(snapshot);

        Assert.Equal("45% left", vm.StatusText);
        Assert.Equal("Pro", vm.PlanText);
    }

    [Fact]
    public void ApplySnapshotForUsedPercentProviderShowsHighestUsage()
    {
        var vm = new AccountRowViewModel(SampleRecord(), usesUsedPercent: true);
        var snapshot = AccountSnapshot.Success(
            new[] { Bucket("a", 80), Bucket("b", 45), Bucket("c", 92) },
            AccountPlan.Pro);

        vm.ApplySnapshot(snapshot);

        Assert.Equal("55% used", vm.StatusText);
    }

    [Fact]
    public void ApplySnapshotFailureSurfacesErrorAndClearsBuckets()
    {
        var vm = new AccountRowViewModel(SampleRecord());
        vm.ApplySnapshot(AccountSnapshot.Success(new[] { Bucket("a", 45) }, AccountPlan.Pro));
        Assert.Equal("45% left", vm.StatusText);

        vm.ApplySnapshot(AccountSnapshot.Failure("boom"));

        Assert.Equal("boom", vm.StatusText);
        Assert.Equal("boom", vm.ErrorMessage);
    }

    [Fact]
    public void IsActiveTrueOverridesStatusText()
    {
        var vm = new AccountRowViewModel(SampleRecord());
        vm.ApplySnapshot(AccountSnapshot.Success(new[] { Bucket("a", 45) }, AccountPlan.Pro));
        Assert.Equal("45% left", vm.StatusText);

        vm.IsActive = true;
        Assert.Equal("Active", vm.StatusText);

        vm.IsActive = false;
        Assert.Equal("45% left", vm.StatusText);
    }

    [Theory]
    [InlineData(AccountPlan.Free, "Free")]
    [InlineData(AccountPlan.Pro, "Pro")]
    [InlineData(AccountPlan.Max, "Max")]
    [InlineData(AccountPlan.Unknown, "—")]
    public void PlanFreeProMaxUnknown(AccountPlan plan, string expected)
    {
        var vm = new AccountRowViewModel(SampleRecord());
        vm.ApplySnapshot(AccountSnapshot.Success(new[] { Bucket("a", 50) }, plan));
        Assert.Equal(expected, vm.PlanText);
    }

    [Fact]
    public void KoreanRowUsesLocalizedAccountStatusText()
    {
        var vm = new AccountRowViewModel(SampleRecord(), AppLanguage.Korean);

        Assert.Equal("불러오는 중…", vm.StatusText);

        vm.ApplySnapshot(AccountSnapshot.Success([], AccountPlan.Pro));
        Assert.Equal("대기", vm.StatusText);
        Assert.Equal("Pro", vm.PlanText);

        vm.ApplySnapshot(AccountSnapshot.Success([Bucket("a", 42)], AccountPlan.Pro));
        Assert.Equal("42% 남음", vm.StatusText);

        vm.IsActive = true;
        Assert.Equal("사용 중", vm.StatusText);
    }
}

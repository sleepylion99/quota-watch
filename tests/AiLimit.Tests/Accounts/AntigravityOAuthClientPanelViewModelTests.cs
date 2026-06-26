using AiLimit.App.ViewModels.Accounts;
using AiLimit.Core.Providers;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class AntigravityOAuthClientPanelViewModelTests
{
    private sealed class PassthroughProtector : ISecretProtector
    {
        public string Protect(string v) => v;
        public string? Unprotect(string? v) => v;
    }

    private static AntigravityOAuthClientRegistry Registry(string dir)
        => new(Path.Combine(dir, "c.json"), Path.Combine(dir, "l.json"),
            new AntigravityOAuthClientConfig("b-id", "b-secret"), new PassthroughProtector());

    [Fact]
    public void AddClient_AppendsAndSelects()
    {
        using var tmp = new TempDir();
        var vm = new AntigravityOAuthClientPanelViewModel(Registry(tmp.Path));
        vm.NewLabel = "Work"; vm.NewClientId = "w-id"; vm.NewClientSecret = "w-secret";

        vm.AddClient();

        Assert.Contains(vm.Clients, c => c.Label == "Work");
        Assert.Equal("Work", vm.Clients.Single(c => c.IsActive).Label);
    }

    [Fact]
    public void RemoveDisabledForBuiltIn()
    {
        using var tmp = new TempDir();
        var vm = new AntigravityOAuthClientPanelViewModel(Registry(tmp.Path));
        var builtIn = vm.Clients.Single(c => c.IsBuiltIn);
        Assert.False(builtIn.CanRemove);
    }
}

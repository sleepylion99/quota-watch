using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AntigravityActiveSelectionTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"active-{Guid.NewGuid():N}.json");

    [Fact]
    public void GetReturnsNullWhenFileMissing()
    {
        Assert.Null(new AntigravityActiveSelection(TempPath()).Get());
    }

    [Fact]
    public void SetThenGetRoundTrips()
    {
        var path = TempPath();
        var selection = new AntigravityActiveSelection(path);
        var id = Guid.NewGuid();

        selection.Set(id);

        Assert.Equal(id, selection.Get());
        File.Delete(path);
    }

    [Fact]
    public void SetNullClearsTheActiveId()
    {
        var path = TempPath();
        var selection = new AntigravityActiveSelection(path);
        selection.Set(Guid.NewGuid());

        selection.Set(null);

        Assert.Null(selection.Get());
        File.Delete(path);
    }

    [Fact]
    public void GetReturnsNullWhenFileIsCorrupt()
    {
        var path = TempPath();
        File.WriteAllText(path, "not-json");
        Assert.Null(new AntigravityActiveSelection(path).Get());
        File.Delete(path);
    }
}

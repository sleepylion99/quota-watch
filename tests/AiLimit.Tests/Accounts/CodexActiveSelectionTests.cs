using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class CodexActiveSelectionTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"codex-active-{Guid.NewGuid():N}.json");

    [Fact]
    public void Get_ReturnsNull_WhenMissing() => Assert.Null(new CodexActiveSelection(TempPath()).Get());

    [Fact]
    public void SetThenGet_RoundTrips()
    {
        var path = TempPath();
        var sel = new CodexActiveSelection(path);
        var id = Guid.NewGuid();
        sel.Set(id);
        Assert.Equal(id, sel.Get());
        File.Delete(path);
    }

    [Fact]
    public void SetNull_Clears()
    {
        var path = TempPath();
        var sel = new CodexActiveSelection(path);
        sel.Set(Guid.NewGuid());
        sel.Set(null);
        Assert.Null(sel.Get());
        File.Delete(path);
    }

    [Fact]
    public void Get_ReturnsNull_WhenCorrupt()
    {
        var path = TempPath();
        File.WriteAllText(path, "not-json");
        Assert.Null(new CodexActiveSelection(path).Get());
        File.Delete(path);
    }
}

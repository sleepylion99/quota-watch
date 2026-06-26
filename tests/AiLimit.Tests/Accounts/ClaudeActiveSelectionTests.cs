using System.IO;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class ClaudeActiveSelectionTests
{
    [Fact]
    public void SetThenGet_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "claude-active-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var selection = new ClaudeActiveSelection(path);
            Assert.Null(selection.Get());

            var id = Guid.NewGuid();
            selection.Set(id);
            Assert.Equal(id, new ClaudeActiveSelection(path).Get());

            selection.Set(null);
            Assert.Null(new ClaudeActiveSelection(path).Get());
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }
}

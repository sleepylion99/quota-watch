using System.IO;
using System.Text.Json;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public class ClaudeProfileWriterTests
{
    [Fact]
    public void WriteNewProfile_CreatesClaude2WithCredentials()
    {
        var home = Path.Combine(Path.GetTempPath(), "claude-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            var writer = new ClaudeProfileWriter(home);

            var path = writer.WriteNewProfile(new ClaudeCredential("at", "rt", 1782071115660, Scopes: new[] { "user:inference" }));

            Assert.Equal(Path.Combine(home, ".claude2", ".credentials.json"), path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var oauth = doc.RootElement.GetProperty("claudeAiOauth");
            Assert.Equal("at", oauth.GetProperty("accessToken").GetString());
            Assert.Equal("rt", oauth.GetProperty("refreshToken").GetString());
            Assert.Equal(1782071115660, oauth.GetProperty("expiresAt").GetInt64());
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void WriteNewProfile_PicksNextFreeDirAndNeverTouchesPrimary()
    {
        var home = Path.Combine(Path.GetTempPath(), "claude-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".claude"));   // primary exists
        Directory.CreateDirectory(Path.Combine(home, ".claude2"));  // already taken
        try
        {
            var writer = new ClaudeProfileWriter(home);

            var path = writer.WriteNewProfile(new ClaudeCredential("at", "rt", null));

            Assert.Equal(Path.Combine(home, ".claude3", ".credentials.json"), path);
            Assert.False(File.Exists(Path.Combine(home, ".claude", ".credentials.json")));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}

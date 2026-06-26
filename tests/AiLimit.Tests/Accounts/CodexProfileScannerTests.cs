using System.Text;
using System.Text.Json;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class CodexProfileScannerTests
{
    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"codexroot-{Guid.NewGuid():N}")).FullName;

    private static void WriteAuth(string dir, string? email, string? planType)
    {
        Directory.CreateDirectory(dir);
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["https://api.openai.com/auth"] = new Dictionary<string, object?> { ["chatgpt_plan_type"] = planType }
        };
        string B64(object o) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = $"{B64(new { alg = "none" })}.{B64(payload)}.";
        var auth = new { tokens = new { id_token = jwt, access_token = "at" } };
        File.WriteAllText(Path.Combine(dir, "auth.json"), JsonSerializer.Serialize(auth));
    }

    [Fact]
    public void Scan_ReturnsEmpty_WhenNoCodexDir()
    {
        var scanner = new CodexProfileScanner(NewRoot());
        Assert.Empty(scanner.Scan());
    }

    [Fact]
    public void Scan_FindsDefaultAndNumberedProfiles_WithAuthJson()
    {
        var root = NewRoot();
        WriteAuth(Path.Combine(root, ".codex"), "a@x.com", "plus");
        WriteAuth(Path.Combine(root, ".codex2"), "b@x.com", "team");
        Directory.CreateDirectory(Path.Combine(root, ".codex3")); // no auth.json -> ignored

        var records = new CodexProfileScanner(root).Scan();

        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Email == "a@x.com");
        Assert.Contains(records, r => r.Email == "b@x.com");
        Assert.All(records, r => Assert.Equal("codex", r.ProviderKey));
    }

    [Fact]
    public void Scan_IdIsStableForSamePath()
    {
        var root = NewRoot();
        WriteAuth(Path.Combine(root, ".codex"), "a@x.com", "plus");
        var first = new CodexProfileScanner(root).Scan()[0].Id;
        var second = new CodexProfileScanner(root).Scan()[0].Id;
        Assert.Equal(first, second);
    }

    [Fact]
    public void Scan_FallsBackToDirName_WhenAuthUnparseable()
    {
        var root = NewRoot();
        var dir = Path.Combine(root, ".codex");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "auth.json"), "not-json");
        var record = new CodexProfileScanner(root).Scan()[0];
        Assert.Equal(".codex", record.DisplayName);
        Assert.Null(record.Email);
    }

    [Fact]
    public void ResolveAuthPath_ReturnsAuthJsonForId()
    {
        var root = NewRoot();
        WriteAuth(Path.Combine(root, ".codex"), "a@x.com", "plus");
        var scanner = new CodexProfileScanner(root);
        var record = scanner.Scan()[0];
        Assert.Equal(Path.Combine(root, ".codex", "auth.json"), scanner.ResolveAuthPath(record.Id));
    }
}

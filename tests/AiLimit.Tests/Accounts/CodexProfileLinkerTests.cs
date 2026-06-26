using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class CodexProfileLinkerTests
{
    private sealed class FakeSymlinks : ISymlinkCreator
    {
        public List<(string Link, string Target)> Links { get; } = new();
        public void Create(string linkPath, string targetPath, bool isDirectory) => Links.Add((linkPath, targetPath));
    }

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"linkroot-{Guid.NewGuid():N}")).FullName;

    private static void SeedCodex(string root)
    {
        var codex = Path.Combine(root, ".codex");
        Directory.CreateDirectory(Path.Combine(codex, "sessions"));
        Directory.CreateDirectory(Path.Combine(codex, ".sandbox-secrets"));
        Directory.CreateDirectory(Path.Combine(codex, "rules"));
        Directory.CreateDirectory(Path.Combine(codex, "skills"));
        Directory.CreateDirectory(Path.Combine(codex, "plugins"));
        File.WriteAllText(Path.Combine(codex, "config.toml"), "x");
        File.WriteAllText(Path.Combine(codex, "AGENTS.md"), "x");
        File.WriteAllText(Path.Combine(codex, "state_5.sqlite"), "x");
        File.WriteAllText(Path.Combine(codex, "auth.json"), "{}");
    }

    // A temp $PROFILE path so tests never touch the machine's real PowerShell profile.
    private static IReadOnlyList<string> ProfilePaths(string root) =>
        new[] { Path.Combine(root, "profile.ps1") };

    [Fact]
    public async Task Create_ReturnsNeedsElevation_WhenNotElevated()
    {
        var root = NewRoot();
        SeedCodex(root);
        var linker = new CodexProfileLinker(root, () => false, new FakeSymlinks(), binDir: Path.Combine(root, "bin"));
        var result = await linker.CreateParallelProfileAsync(CancellationToken.None);
        Assert.Equal(CreateProfileOutcome.NeedsElevation, result.Outcome);
    }

    [Fact]
    public async Task Create_LinksOnlySharedCustomizationAndKeepsRuntimeStateIsolated()
    {
        var root = NewRoot();
        SeedCodex(root);
        var symlinks = new FakeSymlinks();
        var profile = Path.Combine(root, "profile.ps1");
        var linker = new CodexProfileLinker(root, () => true, symlinks, binDir: Path.Combine(root, "bin"), ProfilePaths(root));

        var result = await linker.CreateParallelProfileAsync(CancellationToken.None);

        Assert.Equal(CreateProfileOutcome.Created, result.Outcome);
        Assert.Equal(Path.Combine(root, ".codex2"), result.ProfilePath);
        Assert.True(Directory.Exists(Path.Combine(root, ".codex2")));
        Assert.Contains(symlinks.Links, l => l.Link.EndsWith("config.toml", StringComparison.Ordinal));
        Assert.Contains(symlinks.Links, l => l.Link.EndsWith("AGENTS.md", StringComparison.Ordinal));
        Assert.Contains(symlinks.Links, l => l.Link.EndsWith("rules", StringComparison.Ordinal));
        Assert.Contains(symlinks.Links, l => l.Link.EndsWith("skills", StringComparison.Ordinal));
        Assert.Contains(symlinks.Links, l => l.Link.EndsWith("plugins", StringComparison.Ordinal));
        Assert.DoesNotContain(symlinks.Links, l => l.Link.EndsWith("auth.json", StringComparison.Ordinal));
        Assert.DoesNotContain(symlinks.Links, l => l.Link.EndsWith("sessions", StringComparison.Ordinal));
        Assert.DoesNotContain(symlinks.Links, l => l.Link.EndsWith("state_5.sqlite", StringComparison.Ordinal));
        Assert.DoesNotContain(symlinks.Links, l => l.Link.EndsWith(".sandbox-secrets", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_WritesLauncherThatRestoresCodexHome()
    {
        var root = NewRoot();
        SeedCodex(root);
        var profile = Path.Combine(root, "profile.ps1");
        var linker = new CodexProfileLinker(
            root,
            () => true,
            new FakeSymlinks(),
            binDir: Path.Combine(root, "bin"),
            ProfilePaths(root));

        var result = await linker.CreateParallelProfileAsync(CancellationToken.None);

        var cmdText = File.ReadAllText(Path.Combine(root, "bin", "codex2.cmd"));
        Assert.Contains("codex2", result.LaunchCommand);
        Assert.Contains("set CODEX_HOME=", cmdText);
        Assert.Contains("codex -C \"%CD%\" %*", cmdText);
        Assert.True(File.Exists(profile));
        var profileText = File.ReadAllText(profile);
        Assert.Contains("function codex2", profileText);
        Assert.Contains(Path.Combine(root, ".codex2"), profileText);
        Assert.Contains("$previousCodexHome", profileText);
        Assert.Contains("$previousLocation", profileText);
        Assert.Contains("-C $previousLocation", profileText);
        Assert.Contains("finally", profileText);
        Assert.Contains("Remove-Item Env:CODEX_HOME", profileText);
    }

    [Fact]
    public async Task Create_ReplacesLegacyProfileFunction()
    {
        var root = NewRoot();
        SeedCodex(root);
        var profile = Path.Combine(root, "profile.ps1");
        File.WriteAllText(profile,
            "# user content\r\n" +
            "# AiLimit Codex launcher: codex2\r\n" +
            "function codex2 { $env:CODEX_HOME = \"old\"; codex @args }\r\n");
        var linker = new CodexProfileLinker(root, () => true, new FakeSymlinks(), binDir: Path.Combine(root, "bin"), ProfilePaths(root));

        await linker.CreateParallelProfileAsync(CancellationToken.None);

        var profileText = File.ReadAllText(profile);
        var occurrences = profileText.Split("# AiLimit Codex launcher: codex2").Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Contains("# user content", profileText);
        Assert.DoesNotContain("$env:CODEX_HOME = \"old\"", profileText);
        Assert.Contains("$previousCodexHome", profileText);
    }

    [Fact]
    public async Task Create_PicksNextFreeNumber()
    {
        var root = NewRoot();
        SeedCodex(root);
        Directory.CreateDirectory(Path.Combine(root, ".codex2"));
        var linker = new CodexProfileLinker(root, () => true, new FakeSymlinks(), binDir: Path.Combine(root, "bin"), ProfilePaths(root));
        var result = await linker.CreateParallelProfileAsync(CancellationToken.None);
        Assert.Equal(Path.Combine(root, ".codex3"), result.ProfilePath);
    }
}

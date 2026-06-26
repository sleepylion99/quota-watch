using System.Runtime.Versioning;

namespace AiLimit.Core.Providers.Accounts;

public enum CreateProfileOutcome { Created, NeedsElevation, NoSourceProfile, Failed }

public sealed record CreateProfileResult(
    CreateProfileOutcome Outcome,
    string? ProfilePath = null,
    string? LaunchCommand = null,
    string? ErrorMessage = null);

/// <summary>Seam over OS symlink creation so the linker is testable.</summary>
public interface ISymlinkCreator
{
    void Create(string linkPath, string targetPath, bool isDirectory);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSymlinkCreator : ISymlinkCreator
{
    public void Create(string linkPath, string targetPath, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        else
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
    }
}

/// <summary>
/// Creates a parallel Codex profile (.codexN) that symlinks only static, account-agnostic
/// config (config.toml, skills, plugins, rules, prompts, AGENTS.md) from the primary .codex,
/// while keeping auth, sessions, SQLite state, sandbox secrets and caches isolated per profile
/// so accounts never clobber each other's OAuth/session state. Symlink creation needs admin on
/// Windows, so callers must surface <see cref="CreateProfileOutcome.NeedsElevation"/>.
/// </summary>
public sealed class CodexProfileLinker : ICodexProfileCreator
{
    private const string AuthFileName = "auth.json";

    /// <summary>
    /// Only these static, account-agnostic config entries are symlinked into a new profile.
    /// Everything else — auth.json, sessions, *.sqlite state, .sandbox-secrets, caches, logs —
    /// stays isolated per profile so two accounts never clobber each other's OAuth/session state.
    /// </summary>
    private static readonly HashSet<string> SharedConfigEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "config.toml", "AGENTS.md", "instructions.md", "rules", "skills", "plugins", "prompts",
    };

    private readonly string _homeRoot;
    private readonly Func<bool> _isElevated;
    private readonly ISymlinkCreator _symlinks;
    private readonly string _binDir;
    private readonly IReadOnlyList<string> _powerShellProfilePaths;

    public CodexProfileLinker(
        string homeRoot,
        Func<bool> isElevated,
        ISymlinkCreator symlinks,
        string binDir,
        IReadOnlyList<string>? powerShellProfilePaths = null)
    {
        _homeRoot = homeRoot;
        _isElevated = isElevated;
        _symlinks = symlinks;
        _binDir = binDir;
        _powerShellProfilePaths = powerShellProfilePaths ?? DefaultPowerShellProfilePaths();
    }

    /// <summary>
    /// The PowerShell profile files we append the <c>codexN</c> launcher function to.
    /// Windows PowerShell is always targeted (matches <c>powershell.exe</c>); PowerShell 7
    /// is added only when its profile directory already exists, so we don't litter the
    /// machine for users who don't have it.
    /// </summary>
    private static IReadOnlyList<string> DefaultPowerShellProfilePaths()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>
        {
            Path.Combine(docs, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1")
        };

        var ps7Dir = Path.Combine(docs, "PowerShell");
        if (Directory.Exists(ps7Dir))
        {
            paths.Add(Path.Combine(ps7Dir, "Microsoft.PowerShell_profile.ps1"));
        }

        return paths;
    }

    public Task<CreateProfileResult> CreateParallelProfileAsync(CancellationToken cancellationToken)
    {
        var source = Path.Combine(_homeRoot, ".codex");
        if (!Directory.Exists(source) || !File.Exists(Path.Combine(source, AuthFileName)))
        {
            return Task.FromResult(new CreateProfileResult(CreateProfileOutcome.NoSourceProfile));
        }

        if (!_isElevated())
        {
            return Task.FromResult(new CreateProfileResult(CreateProfileOutcome.NeedsElevation));
        }

        try
        {
            var (name, dest) = NextFreeProfile();
            Directory.CreateDirectory(dest);

            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = Path.GetFileName(entry);
                if (!SharedConfigEntries.Contains(entryName))
                {
                    // Auth, sessions, SQLite state, sandbox secrets, caches and logs are kept
                    // separate per profile; only shared config (above) is symlinked.
                    continue;
                }

                var isDir = Directory.Exists(entry);
                _symlinks.Create(Path.Combine(dest, entryName), entry, isDir);
            }

            var launchCommand = WriteLauncher(name, dest);
            return Task.FromResult(new CreateProfileResult(
                CreateProfileOutcome.Created, dest, launchCommand));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CreateProfileResult(CreateProfileOutcome.Failed, ErrorMessage: ex.Message));
        }
    }

    private (string Name, string Path) NextFreeProfile()
    {
        for (var i = 2; ; i++)
        {
            var name = $".codex{i}";
            var path = Path.Combine(_homeRoot, name);
            if (!Directory.Exists(path))
            {
                return (name.TrimStart('.'), path);
            }
        }
    }

    private string WriteLauncher(string commandName, string profilePath)
    {
        // .cmd backup in the app-owned bin dir (works from cmd.exe if the dir is on PATH).
        Directory.CreateDirectory(_binDir);
        var cmdPath = Path.Combine(_binDir, $"{commandName}.cmd");
        File.WriteAllText(cmdPath,
            "@echo off\r\n" +
            $"set CODEX_HOME={profilePath}\r\n" +
            "codex -C \"%CD%\" %*\r\n");

        // Register a PowerShell function in the user's $PROFILE so `commandName` is a
        // usable command in NEW shells (or after `. $PROFILE`). The function scopes CODEX_HOME
        // to the single codex invocation and restores it in a finally block, so a later
        // `codex login/logout` in the same shell never lands on this profile by accident.
        var marker = $"# AiLimit Codex launcher: {commandName}";
        var function =
            $"function {commandName} {{ " +
            "$previousCodexHome = $env:CODEX_HOME; " +
            "$previousLocation = (Get-Location).ProviderPath; " +
            $"$env:CODEX_HOME = \"{profilePath}\"; " +
            "try { codex -C $previousLocation @args } " +
            "finally { if ($null -eq $previousCodexHome) { Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue } " +
            "else { $env:CODEX_HOME = $previousCodexHome } } }";
        var block = $"{marker}\r\n{function}\r\n";

        foreach (var psProfile in _powerShellProfilePaths)
        {
            try
            {
                var dir = Path.GetDirectoryName(psProfile);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Replace any prior block for this command (rather than skip), so launchers
                // written by older versions are upgraded to the CODEX_HOME-restoring form.
                var existing = File.Exists(psProfile) ? File.ReadAllText(psProfile) : string.Empty;
                var stripped = StripLauncherBlock(existing, marker);
                var prefix = stripped.Length == 0 || stripped.EndsWith("\n", StringComparison.Ordinal)
                    ? string.Empty
                    : "\r\n";
                File.WriteAllText(psProfile, stripped + prefix + block);
            }
            catch
            {
                // Best-effort: a profile we can't write isn't fatal — the .cmd and the
                // manual `$env:CODEX_HOME=...; codex` path still work.
            }
        }

        return commandName;
    }

    /// <summary>Removes a prior launcher block (the marker line plus the function line that
    /// follows it) so re-registration replaces rather than duplicates or leaves a stale form.</summary>
    private static string StripLauncherBlock(string text, string marker)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(marker, StringComparison.Ordinal))
        {
            return text;
        }

        var pattern = System.Text.RegularExpressions.Regex.Escape(marker) + @"\r?\n[^\r\n]*\r?\n?";
        return System.Text.RegularExpressions.Regex.Replace(text, pattern, string.Empty);
    }
}

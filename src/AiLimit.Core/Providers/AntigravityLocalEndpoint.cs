using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiLimit.Core.Providers;

internal sealed record AntigravityLocalEndpoint(
    IReadOnlyList<string> BaseUrls,
    string? CsrfToken,
    IReadOnlySet<string>? CsrfProtectedBaseUrls = null)
{
    internal static IReadOnlyList<string> LogRoots { get; } =
    [
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antigravity",
            "logs"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antigravity IDE",
            "logs")
    ];

    public static readonly string[] RequestPaths =
    [
        "/exa.language_server_pb.LanguageServerService/GetUserStatus",
        "/exa.language_server_pb.LanguageServerService/GetCommandModelConfigs"
    ];

    private static readonly Regex CsrfTokenPattern = new("--csrf_token\\s+([^\\s]+)", RegexOptions.IgnoreCase);
    private static readonly Regex ExtensionPortPattern = new("--extension_server_port\\s+(\\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex StartedPortPattern = new("LS started on port\\s+(\\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex ListeningPortPattern = new("listening .* port at\\s+(\\d+)\\s+for\\s+(HTTPS|HTTP)", RegexOptions.IgnoreCase);

    public static AntigravityLocalEndpoint? TryDiscover()
    {
        var processEndpoint = TryDiscoverFromProcesses();
        var logEndpoint = TryDiscoverFromLogs();

        return processEndpoint is null ? null : Merge(logEndpoint, processEndpoint);
    }

    private static AntigravityLocalEndpoint? TryDiscoverFromLogs()
    {
        var candidates = LogRoots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.log", SearchOption.AllDirectories))
            .Where(path => path.EndsWith("ls-main.log", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("Antigravity.log", StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(24);

        return Merge(candidates.Select(file => TryDiscoverFromLog(file.FullName)).ToArray());
    }

    private static AntigravityLocalEndpoint? Merge(params AntigravityLocalEndpoint?[] endpoints)
    {
        var available = endpoints.Where(endpoint => endpoint is not null).Select(endpoint => endpoint!).ToList();
        if (available.Count == 0)
        {
            return null;
        }

        var tokenSource = available.FirstOrDefault(endpoint => !string.IsNullOrWhiteSpace(endpoint.CsrfToken));
        return new AntigravityLocalEndpoint(
            OrderBaseUrls(available.SelectMany(endpoint => endpoint.BaseUrls)),
            tokenSource?.CsrfToken,
            tokenSource is null
                ? null
                : ProtectedBaseUrlsFor(tokenSource.BaseUrls, tokenSource.CsrfToken, tokenSource.CsrfProtectedBaseUrls));
    }

    public bool ShouldSendCsrfToken(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(CsrfToken))
        {
            return false;
        }

        return CsrfProtectedBaseUrls is null
            || CsrfProtectedBaseUrls.Count == 0
            || CsrfProtectedBaseUrls.Contains(baseUrl);
    }

    private static AntigravityLocalEndpoint? TryDiscoverFromProcesses()
    {
        var processJson = RunPowerShell("""
            Get-CimInstance Win32_Process |
              Where-Object {
                $_.Name -eq 'language_server_windows_x64.exe' -and
                $_.CommandLine -and
                $_.CommandLine.ToLowerInvariant().Contains('antigravity') -and
                ($_.CommandLine.Contains('--csrf_token') -or $_.CommandLine.Contains('--extension_server_port'))
              } |
              Select-Object -First 1 ProcessId,CommandLine |
              ConvertTo-Json -Compress
            """);
        if (string.IsNullOrWhiteSpace(processJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(processJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("ProcessId", out var processIdElement)
                || !processIdElement.TryGetInt32(out var processId)
                || !root.TryGetProperty("CommandLine", out var commandLineElement))
            {
                return null;
            }

            var commandLine = commandLineElement.GetString() ?? string.Empty;
            var csrfToken = ExtractArgument(commandLine, "--csrf_token");
            var extensionPort = ExtractPort(commandLine, "--extension_server_port");
            var ports = DiscoverListeningPorts(processId);
            if (extensionPort is not null)
            {
                ports.Add(extensionPort.Value);
            }

            var baseUrls = new List<string>();
            if (extensionPort is not null)
            {
                baseUrls.Add($"http://127.0.0.1:{extensionPort.Value}");
            }

            foreach (var port in ports.Distinct().Where(port => port != extensionPort))
            {
                baseUrls.Add($"https://127.0.0.1:{port}");
                baseUrls.Add($"http://127.0.0.1:{port}");
            }

            var orderedBaseUrls = OrderBaseUrls(baseUrls);
            return orderedBaseUrls.Count == 0
                ? null
                : new AntigravityLocalEndpoint(
                    orderedBaseUrls,
                    csrfToken,
                    ProtectedBaseUrlsFor(orderedBaseUrls, csrfToken));
        }
        catch
        {
            return null;
        }
    }

    private static List<int> DiscoverListeningPorts(int processId)
    {
        var json = RunPowerShell($"""
            Get-NetTCPConnection -OwningProcess {processId} -State Listen -ErrorAction SilentlyContinue |
              Select-Object -ExpandProperty LocalPort |
              Sort-Object -Unique |
              ConvertTo-Json -Compress
            """);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement
                    .EnumerateArray()
                    .Where(element => element.TryGetInt32(out _))
                    .Select(element => element.GetInt32())
                    .ToList();
            }

            return document.RootElement.TryGetInt32(out var port)
                ? [port]
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static string? RunPowerShell(string command)
    {
        try
        {
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}"
            });
            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Best effort cleanup.
                }

                return null;
            }

            return process.ExitCode == 0
                ? process.StandardOutput.ReadToEnd()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractArgument(string commandLine, string name)
    {
        var escaped = Regex.Escape(name);
        var match = Regex.Match(commandLine, $"{escaped}(?:=|\\s+)([^\\s\"]+|\"[^\"]+\")", RegexOptions.IgnoreCase);
        return match.Success
            ? match.Groups[1].Value.Trim('"')
            : null;
    }

    private static int? ExtractPort(string commandLine, string name)
    {
        return int.TryParse(ExtractArgument(commandLine, name), out var port)
            ? port
            : null;
    }

    private static AntigravityLocalEndpoint? TryDiscoverFromLog(string path)
    {
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch
        {
            return null;
        }

        var ports = new List<(int Port, string Protocol)>();
        foreach (Match match in ListeningPortPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var port))
            {
                ports.Add((port, match.Groups[2].Value.Equals("HTTPS", StringComparison.OrdinalIgnoreCase) ? "https" : "http"));
            }
        }

        foreach (Match match in StartedPortPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var port))
            {
                ports.Add((port, "https"));
            }
        }

        foreach (Match match in ExtensionPortPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var port))
            {
                ports.Add((port, "http"));
            }
        }

        if (ports.Count == 0)
        {
            return null;
        }

        var csrfMatch = CsrfTokenPattern.Match(text);
        var csrfToken = csrfMatch.Success
            ? csrfMatch.Groups[1].Value
            : null;
        var baseUrls = ports
            .Distinct()
            .Select(port => $"{port.Protocol}://127.0.0.1:{port.Port}");
        var orderedBaseUrls = OrderBaseUrls(baseUrls);
        return new AntigravityLocalEndpoint(
            orderedBaseUrls,
            csrfToken,
            ProtectedBaseUrlsFor(orderedBaseUrls, csrfToken));
    }

    private static IReadOnlyList<string> OrderBaseUrls(IEnumerable<string> baseUrls)
    {
        return baseUrls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlySet<string>? ProtectedBaseUrlsFor(
        IReadOnlyList<string> baseUrls,
        string? csrfToken,
        IReadOnlySet<string>? existing = null)
    {
        if (string.IsNullOrWhiteSpace(csrfToken))
        {
            return null;
        }

        return existing ?? new HashSet<string>(baseUrls, StringComparer.OrdinalIgnoreCase);
    }
}

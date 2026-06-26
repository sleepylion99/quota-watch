using System.Diagnostics;

namespace AiLimit.App.Services;

internal sealed class UpdateReleaseLauncher
{
    internal const string LatestReleasePageUrl =
        "https://github.com/sleepylion99/quota-watch/releases/latest";

    private readonly Action<ProcessStartInfo> _startProcess;

    public UpdateReleaseLauncher()
        : this(startInfo => Process.Start(startInfo))
    {
    }

    internal UpdateReleaseLauncher(Action<ProcessStartInfo> startProcess)
    {
        _startProcess = startProcess;
    }

    public void Open(string? releaseUrl)
    {
        _startProcess(new ProcessStartInfo
        {
            FileName = ResolveReleaseUrl(releaseUrl),
            UseShellExecute = true
        });
    }

    internal static string ResolveReleaseUrl(string? releaseUrl)
    {
        return Uri.TryCreate(releaseUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(
                "/sleepylion99/quota-watch/releases/",
                StringComparison.OrdinalIgnoreCase)
                ? uri.ToString()
                : LatestReleasePageUrl;
    }
}

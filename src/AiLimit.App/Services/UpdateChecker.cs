using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AiLimit.App.Services;

public sealed class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/sleepylion99/quota-watch/releases/latest";
    private const string LatestReleasePageUrl = "https://github.com/sleepylion99/quota-watch/releases/latest";
    private readonly HttpClient _httpClient;
    private readonly Func<string> _currentVersionProvider;

    public UpdateChecker()
        : this(CreateHttpClient(), CurrentVersion)
    {
    }

    internal UpdateChecker(HttpClient httpClient, Func<string> currentVersionProvider)
    {
        _httpClient = httpClient;
        _currentVersionProvider = currentVersionProvider;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var currentVersion = _currentVersionProvider();
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
            {
                var fallbackResult = await TryCheckLatestReleasePageAsync(currentVersion, cancellationToken).ConfigureAwait(false);
                if (fallbackResult is not null)
                {
                    return fallbackResult;
                }

                throw new UpdateCheckException("GitHub 릴리즈 정보를 볼 수 없습니다. 저장소 또는 릴리즈 공개 상태를 확인하세요.");
            }

            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var latestTag = root.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? string.Empty
            : string.Empty;
        var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString()
            : null;

        var latestVersion = NormalizeVersion(latestTag);
        var updateAvailable = IsNewerVersion(latestVersion, NormalizeVersion(currentVersion));

        return new UpdateCheckResult(
            updateAvailable,
            currentVersion,
            string.IsNullOrWhiteSpace(latestVersion) ? latestTag : latestVersion,
            releaseUrl);
    }

    private async Task<UpdateCheckResult?> TryCheckLatestReleasePageAsync(
        string currentVersion,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(LatestReleasePageUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var releaseUrl = response.RequestMessage?.RequestUri?.ToString();
        var latestTag = ExtractReleaseTagFromUrl(releaseUrl);
        if (string.IsNullOrWhiteSpace(latestTag))
        {
            return null;
        }

        var latestVersion = NormalizeVersion(latestTag);
        return new UpdateCheckResult(
            IsNewerVersion(latestVersion, NormalizeVersion(currentVersion)),
            currentVersion,
            latestVersion,
            releaseUrl);
    }

    internal static bool IsNewerVersion(string candidate, string current)
    {
        if (!Version.TryParse(NormalizeVersion(candidate), out var candidateVersion))
        {
            return false;
        }

        return !Version.TryParse(NormalizeVersion(current), out var currentVersion)
            || candidateVersion > currentVersion;
    }

    internal static string NormalizeVersion(string version)
    {
        var normalized = version.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var metadataStart = normalized.IndexOfAny(['+', '-']);
        return metadataStart >= 0 ? normalized[..metadataStart] : normalized;
    }

    internal static string? ExtractReleaseTagFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        const string marker = "/releases/tag/";
        var path = uri.AbsolutePath;
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : Uri.UnescapeDataString(path[(index + marker.Length)..]);
    }

    private static string CurrentVersion()
    {
        var assembly = typeof(UpdateChecker).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return NormalizeVersion(informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Quota-Watch");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseUrl);

public sealed class UpdateCheckException(string userMessage) : Exception(userMessage)
{
    public string UserMessage { get; } = userMessage;
}

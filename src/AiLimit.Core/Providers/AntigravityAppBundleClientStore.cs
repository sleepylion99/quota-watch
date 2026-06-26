using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace AiLimit.Core.Providers;

internal sealed class AntigravityAppBundleClientStore
{
    private static readonly Regex ClientIdPattern = new(
        @"[0-9]+-[A-Za-z0-9_-]+\.apps\.googleusercontent\.com",
        RegexOptions.CultureInvariant);
    private static readonly Regex ClientSecretPattern = new(
        @"GOCSPX-[A-Za-z0-9_-]{28}",
        RegexOptions.CultureInvariant);
    private static readonly IReadOnlyList<string> DefaultRelativePaths =
    [
        "resources/app/out/main.js",
        "resources/app/extensions/antigravity/bin/language_server_win_x64.exe",
        "resources/app/extensions/antigravity/bin/language_server_win.exe"
    ];

    private readonly IReadOnlyList<string> _installRoots;
    private readonly Func<string, bool> _signatureVerifier;
    private readonly IReadOnlyList<string> _relativeCandidatePaths;

    // NOTE: this default ctor used to raise CA1416 (DefaultSignatureVerifier is
    // [SupportedOSPlatform("windows")], referenced here without a platform guard under Core's
    // cross-platform net10.0 TFM). Suppressed below.
#pragma warning disable CA1416
    public AntigravityAppBundleClientStore()
        : this(DefaultInstallRoots(), DefaultSignatureVerifier, DefaultRelativePaths)
    {
    }
#pragma warning restore CA1416

    internal AntigravityAppBundleClientStore(
        IReadOnlyList<string> installRoots,
        Func<string, bool> signatureVerifier,
        IReadOnlyList<string>? relativeCandidatePaths = null)
    {
        _installRoots = installRoots;
        _signatureVerifier = signatureVerifier;
        _relativeCandidatePaths = relativeCandidatePaths ?? DefaultRelativePaths;
    }

    public AntigravityOAuthClientConfig? Load()
    {
        foreach (var root in _installRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var relative in _relativeCandidatePaths)
            {
                var candidate = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (IsReparsePoint(candidate))
                {
                    continue;
                }

                if (!_signatureVerifier(candidate))
                {
                    continue;
                }

                var content = SafeReadAllText(candidate);
                if (content is null)
                {
                    continue;
                }

                var extracted = ExtractClient(content);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }

        return null;
    }

    internal static AntigravityOAuthClientConfig? ExtractClient(string content)
    {
        var idMatch = ClientIdPattern.Match(content);
        var secretMatch = ClientSecretPattern.Match(content);
        if (!idMatch.Success || !secretMatch.Success)
        {
            return null;
        }

        return new AntigravityOAuthClientConfig(idMatch.Value, secretMatch.Value);
    }

    private static IReadOnlyList<string> DefaultInstallRoots()
    {
        return AntigravityInstallation.InstallRootCandidates().ToList();
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // Treat unreadable files (permission denied, IO errors, etc.) as reparse
            // points so the scan skips them rather than crashing.
            return true;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool DefaultSignatureVerifier(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            // X509Certificate.CreateFromSignedFile returns the base type; wrap it in a
            // disposable X509Certificate2 and dispose both so no native handle leaks.
            // NOTE: CreateFromSignedFile used to raise SYSLIB0057 (API marked obsolete; no
            // non-obsolete equivalent for reading the cert out of a signed PE). Suppressed below.
#pragma warning disable SYSLIB0057
            using var rawCertificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var certificate = X509CertificateLoader.LoadCertificate(rawCertificate.GetRawCertData());
            if (!certificate.Subject.Contains("Google", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var chain = new X509Chain
            {
                ChainPolicy =
                {
                    RevocationMode = X509RevocationMode.Online,
                    RevocationFlag = X509RevocationFlag.ExcludeRoot,
                    VerificationFlags = X509VerificationFlags.NoFlag,
                    UrlRetrievalTimeout = TimeSpan.FromSeconds(5)
                }
            };
            return chain.Build(certificate);
        }
        catch
        {
            return false;
        }
    }

    private static string? SafeReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }
}

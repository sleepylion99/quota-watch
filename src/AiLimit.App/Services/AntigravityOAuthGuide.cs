using System.Diagnostics;
using System.IO;
using AiLimit.Core.Settings;

namespace AiLimit.App.Services;

public sealed class AntigravityOAuthGuide
{
    private readonly string _baseDirectory;
    private readonly Action<ProcessStartInfo> _startProcess;

    public AntigravityOAuthGuide()
        : this(AppContext.BaseDirectory, startInfo => Process.Start(startInfo))
    {
    }

    internal AntigravityOAuthGuide(
        string baseDirectory,
        Action<ProcessStartInfo> startProcess)
    {
        _baseDirectory = baseDirectory;
        _startProcess = startProcess;
    }

    public void Open(AppLanguage language)
    {
        var guidePath = Path.Combine(
            _baseDirectory,
            "Assets",
            "Help",
            "antigravity-oauth.html");
        if (!File.Exists(guidePath))
        {
            throw new FileNotFoundException(
                "The Antigravity OAuth setup guide was not found.",
                guidePath);
        }

        _startProcess(new ProcessStartInfo
        {
            FileName = GuideUri(guidePath, language),
            UseShellExecute = true
        });
    }

    internal static string GuideUri(string guidePath, AppLanguage language)
    {
        var languageCode = AppLanguageResolver.Resolve(language) switch
        {
            AppLanguage.Korean => "ko",
            AppLanguage.Japanese => "ja",
            AppLanguage.Chinese => "zh",
            _ => "en"
        };

        return $"{new Uri(Path.GetFullPath(guidePath)).AbsoluteUri}?lang={languageCode}";
    }
}

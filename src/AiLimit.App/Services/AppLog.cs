using System.IO;
using AiLimit.Core;

namespace AiLimit.App.Services;

public enum AppLogLevel
{
    Info,
    Warning,
    Error
}

public static class AppLog
{
    private static readonly object SyncRoot = new();

    public static string DashboardDebugFile => Path.Combine(AppPaths.AppDataDirectory, "dashboard-debug.log");

    public static bool IsEnabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("AILIMIT_DEBUG_LOG");
            return value is "1" || value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public static void Write(string area, string message)
    {
        Write(AppLogLevel.Info, area, message);
    }

    public static void Write(AppLogLevel level, string area, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            var entry = FormatEntry(DateTimeOffset.Now, level, area, message);
            lock (SyncRoot)
            {
                File.AppendAllText(DashboardDebugFile, entry);
            }
        }
        catch
        {
            // Diagnostic logging must never affect the app UI.
        }
    }

    public static string FormatEntry(DateTimeOffset timestamp, string area, string message)
    {
        return FormatEntry(timestamp, AppLogLevel.Info, area, message);
    }

    public static string FormatEntry(DateTimeOffset timestamp, AppLogLevel level, string area, string message)
    {
        return $"{timestamp:yyyy-MM-dd HH:mm:ss} [{level}] [{area}] {RedactSensitiveText(message)}{Environment.NewLine}";
    }

    public static string ReadDiagnosticLogForCopy()
    {
        try
        {
            return FormatCopyText(
                File.Exists(DashboardDebugFile)
                    ? File.ReadAllText(DashboardDebugFile)
                    : string.Empty);
        }
        catch (Exception ex)
        {
            return $"Quota Watch diagnostic log could not be read: {ex.Message}";
        }
    }

    public static string FormatCopyText(string content)
    {
        return string.IsNullOrWhiteSpace(content)
            ? "Quota Watch diagnostic log is empty."
            : RedactSensitiveText(content.TrimEnd());
    }

    internal static string RedactSensitiveText(string content) => DiagnosticSanitizer.Redact(content);
}

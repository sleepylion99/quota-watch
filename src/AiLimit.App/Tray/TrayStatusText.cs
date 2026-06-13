using AiLimit.App.Localization;
using AiLimit.Core.Domain;
using AiLimit.Core.Settings;

namespace AiLimit.App.Tray;

public static class TrayStatusText
{
    public static IReadOnlyList<TrayProviderItem> BuildProviderItems(
        IReadOnlyList<UsageSnapshot> snapshots,
        AppLanguage language)
    {
        language = AppLanguageResolver.Resolve(language);
        return snapshots
            .OrderByDescending(ProviderRiskScore)
            .Select(snapshot => new TrayProviderItem(
                snapshot.ProviderId,
                BuildProviderSummary(snapshot, language),
                BuildProviderDetails(snapshot, language)))
            .ToList();
    }

    private static int ProviderRiskScore(UsageSnapshot snapshot)
    {
        if (snapshot.Status == UsageStatus.Failed || snapshot.Windows.Count == 0)
        {
            return 101;
        }

        var window = IsUsedPercentSnapshot(snapshot)
            ? snapshot.Windows.Where(IsUsedPercentWindow).OrderByDescending(w => w.PercentRemaining).First()
            : snapshot.Windows.OrderBy(w => w.PercentRemaining).First();
        return IsUsedPercentWindow(window) ? window.PercentRemaining : 100 - window.PercentRemaining;
    }

    public static IReadOnlyList<TrayProviderLine> BuildVisibleProviderLines(
        IReadOnlyList<TrayProviderItem> providers,
        string? expandedProviderId)
    {
        var lines = new List<TrayProviderLine>();
        foreach (var provider in providers)
        {
            lines.Add(new TrayProviderLine(provider.ProviderId, provider.Summary, IsDetail: false));
            if (!string.Equals(provider.ProviderId, expandedProviderId, StringComparison.Ordinal))
            {
                continue;
            }

            lines.AddRange(provider.Details.Select(
                detail => new TrayProviderLine(provider.ProviderId, detail, IsDetail: true)));
        }

        return lines;
    }

    public static string BuildSummary(IReadOnlyList<UsageSnapshot> snapshots, AppLanguage language)
    {
        language = AppLanguageResolver.Resolve(language);
        if (snapshots.Count == 0)
        {
            return AppText.Get(language, AppStringKeys.TrayRefreshToLoad);
        }

        var failedCount = snapshots.Count(snapshot => snapshot.Status == UsageStatus.Failed);
        if (failedCount > 0)
        {
            return AppText.Get(language, AppStringKeys.TrayFailed, failedCount);
        }

        var critical = snapshots
            .SelectMany(snapshot => snapshot.Windows.Select(window => new
            {
                Snapshot = snapshot,
                Window = window,
                Risk = IsUsedPercentWindow(window)
                    ? window.PercentRemaining
                    : 100 - window.PercentRemaining
            }))
            .OrderByDescending(item => item.Risk)
            .FirstOrDefault();

        if (critical is null)
        {
            return AppText.Get(language, AppStringKeys.TrayNoWindows);
        }

        var label = critical.Snapshot.ProviderId == "gemini-pro"
            ? CompactAntigravityLabel(critical.Window.Label)
            : ShortWindowLabel(critical.Window, language);
        var usageText = IsUsedPercentWindow(critical.Window)
            ? AppText.Get(language, AppStringKeys.TrayLabeledPercentUsed, label, critical.Window.PercentRemaining)
            : $"{label} {critical.Window.PercentRemaining}%";

        return $"OK · {ShortProviderName(critical.Snapshot)} {usageText}";
    }

    public static IReadOnlyList<string> BuildProviderLines(IReadOnlyList<UsageSnapshot> snapshots, AppLanguage language)
    {
        language = AppLanguageResolver.Resolve(language);
        return snapshots
            .SelectMany(snapshot =>
            {
                if (snapshot.ProviderId == "gemini-pro" && IsUsedPercentSnapshot(snapshot))
                {
                    return BuildAntigravityProviderLines(snapshot, language);
                }

                var usage = BuildRemainingProviderLine(snapshot, language);
                return [$"{ShortProviderName(snapshot)} · {usage}"];
            })
            .ToList();
    }

    private static IReadOnlyList<string> BuildAntigravityProviderLines(UsageSnapshot snapshot, AppLanguage language)
    {
        var models = BuildUsedPercentParts(snapshot.Windows)
            .Select(item => $"  {item}")
            .ToList();

        return models.Count == 0
            ? [$"{ShortProviderName(snapshot)} · {StatusText(snapshot.Status, language)}"]
            : [$"{ShortProviderName(snapshot)}", ..models];
    }

    private static string BuildRemainingProviderLine(UsageSnapshot snapshot, AppLanguage language)
    {
        if (snapshot.ProviderId == "claude")
        {
            var claudeParts = snapshot.Windows
                .Select(window => $"{ShortWindowLabel(window, language)} {window.PercentRemaining}%")
                .ToList();
            return claudeParts.Count == 0 ? StatusText(snapshot.Status, language) : string.Join(" / ", claudeParts);
        }

        var primary = snapshot.Windows.FirstOrDefault();
        return primary is null
            ? StatusText(snapshot.Status, language)
            : $"{ShortWindowLabel(primary, language)} {primary.PercentRemaining}%";
    }

    private static string BuildProviderSummary(UsageSnapshot snapshot, AppLanguage language)
    {
        if (snapshot.Windows.Count == 0)
        {
            return $"{ShortProviderName(snapshot)} · {StatusText(snapshot.Status, language)}";
        }

        var window = IsUsedPercentSnapshot(snapshot)
            ? snapshot.Windows.Where(IsUsedPercentWindow).OrderByDescending(item => item.PercentRemaining).First()
            : snapshot.Windows.OrderBy(item => item.PercentRemaining).First();
        var percent = IsUsedPercentWindow(window)
            ? AppText.Get(language, AppStringKeys.TrayPercentUsed, window.PercentRemaining)
            : AppText.Get(language, AppStringKeys.TrayPercentRemaining, window.PercentRemaining);
        return $"{ShortProviderName(snapshot)} · {ShortWindowLabel(window, language)} {percent}";
    }

    private static IReadOnlyList<string> BuildProviderDetails(UsageSnapshot snapshot, AppLanguage language)
    {
        return snapshot.Windows
            .Select(window =>
            {
                var value = TryFormatCreditBalance(window.ResetLabel)
                    ?? $"{window.PercentRemaining}%";
                return $"{ShortWindowLabel(window, language)} · {value}";
            })
            .ToList();
    }

    private static string? TryFormatCreditBalance(string? resetLabel)
    {
        if (string.IsNullOrWhiteSpace(resetLabel))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            resetLabel,
            @"Remaining credits:\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static IReadOnlyList<string> BuildUsedPercentParts(IEnumerable<UsageWindow> windows)
    {
        return windows
            .Where(IsUsedPercentWindow)
            .GroupBy(window => CompactAntigravityLabel(window.Label))
            .Select(group => new
            {
                Label = group.Key,
                Percent = group.Max(window => window.PercentRemaining),
                SortOrder = group.Min(AntigravitySortOrder)
            })
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.Label} {item.Percent}%")
            .ToList();
    }

    private static string ShortProviderName(UsageSnapshot snapshot)
    {
        return snapshot.ProviderId switch
        {
            "gemini-pro" => "Antigravity",
            "codex" => "Codex",
            "claude" => "Claude",
            _ => snapshot.DisplayName
        };
    }

    private static string StatusText(UsageStatus status, AppLanguage language)
    {
        return status switch
        {
            UsageStatus.Fresh => AppText.Get(language, AppStringKeys.TrayStatusFresh),
            UsageStatus.Refreshing => AppText.Get(language, AppStringKeys.TrayStatusChecking),
            UsageStatus.Stale => AppText.Get(language, AppStringKeys.TrayStatusStale),
            UsageStatus.Failed => AppText.Get(language, AppStringKeys.TrayStatusFailed),
            _ => status.ToString()
        };
    }

    private static string ShortWindowLabel(UsageWindow window, AppLanguage language)
    {
        return window.Id switch
        {
            "five-hour" => AppText.Get(language, AppStringKeys.TrayWindowFiveHour),
            "weekly" => AppText.Get(language, AppStringKeys.TrayWindowWeekly),
            "weekly-sonnet" => "Sonnet",
            "weekly-opus" => "Opus",
            "weekly-routines" => "Routines",
            "weekly-cowork" => "Cowork",
            _ => window.Label
        };
    }

    private static int AntigravitySortOrder(UsageWindow window)
    {
        var label = window.Label.ToLowerInvariant();
        var family = 9;
        if (label.Contains("gemini") && label.Contains("flash"))
        {
            family = 0;
        }
        else if (label.Contains("gemini") && label.Contains("pro"))
        {
            family = 1;
        }
        else if (label.Contains("claude") && label.Contains("sonnet"))
        {
            family = 2;
        }
        else if (label.Contains("claude") && label.Contains("opus"))
        {
            family = 3;
        }
        else if (label.Contains("gpt") || label.Contains("oss"))
        {
            family = 4;
        }

        var tier = label switch
        {
            var value when value.Contains("medium") => 0,
            var value when value.Contains("high") => 1,
            var value when value.Contains("low") => 2,
            _ => 9
        };

        return family * 10 + tier;
    }

    private static string CompactAntigravityLabel(string label)
    {
        var normalized = label.ToLowerInvariant();
        var strength = AntigravityStrengthSuffix(label);
        if (normalized.Contains("gemini"))
        {
            if (normalized.Contains("flash"))
            {
                return $"Gemini Flash{strength}";
            }

            if (normalized.Contains("pro"))
            {
                return $"Gemini Pro{strength}";
            }

            return $"Gemini{strength}";
        }

        if (normalized.Contains("claude"))
        {
            if (normalized.Contains("opus"))
            {
                return $"Claude Opus{strength}";
            }

            if (normalized.Contains("sonnet"))
            {
                return $"Claude Sonnet{strength}";
            }

            return $"Claude{strength}";
        }

        if (normalized.Contains("gpt") || normalized.Contains("oss"))
        {
            return $"GPT-OSS{strength}";
        }

        return label;
    }

    private static string AntigravityStrengthSuffix(string label)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            label,
            @"\((Low|Medium|High|Thinking)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Groups[1].Value.ToLowerInvariant() switch
        {
            "low" => " L",
            "medium" => " M",
            "high" => " H",
            "thinking" => " T",
            _ => $" {match.Groups[1].Value}"
        };
    }

    private static bool IsUsedPercentSnapshot(UsageSnapshot snapshot)
    {
        return snapshot.Windows.Any(IsUsedPercentWindow);
    }

    private static bool IsUsedPercentWindow(UsageWindow window)
    {
        return window.IsUsedPercent
            || window.Id.StartsWith("gemini-", StringComparison.Ordinal)
            || window.Id.StartsWith("antigravity-", StringComparison.Ordinal);
    }

}

public sealed record TrayProviderItem(
    string ProviderId,
    string Summary,
    IReadOnlyList<string> Details);

public sealed record TrayProviderLine(
    string ProviderId,
    string Text,
    bool IsDetail);

namespace AiLimit.App.Theming;

public static class BrushKey
{
    // Surface — window and card backgrounds
    public const string SurfaceWindow = "Brush.Surface.Window";
    public const string SurfaceTitleBar = "Brush.Surface.TitleBar";
    public const string SurfaceCard = "Brush.Surface.Card";
    public const string SurfaceCardElevated = "Brush.Surface.CardElevated";
    public const string SurfaceCardInverse = "Brush.Surface.CardInverse";
    public const string SurfaceOverlay = "Brush.Surface.Overlay";

    // Text — foreground hierarchy
    public const string TextPrimary = "Brush.Text.Primary";
    public const string TextSecondary = "Brush.Text.Secondary";
    public const string TextTertiary = "Brush.Text.Tertiary";
    public const string TextOnAccent = "Brush.Text.OnAccent";
    public const string TextMuted = "Brush.Text.Muted";

    // Border — outlines and dividers
    public const string BorderDefault = "Brush.Border.Default";
    public const string BorderSubtle = "Brush.Border.Subtle";
    public const string BorderStrong = "Brush.Border.Strong";
    public const string BorderWidgetCard = "Brush.Border.WidgetCard";

    // Accent — interactive controls
    public const string AccentPrimary = "Brush.Accent.Primary";
    public const string AccentPrimaryHover = "Brush.Accent.PrimaryHover";
    public const string AccentPrimaryPressed = "Brush.Accent.PrimaryPressed";
    public const string AccentLink = "Brush.Accent.Link";

    // Status — provider health and refresh state
    public const string StatusFresh = "Brush.Status.Fresh";
    public const string StatusFreshSoft = "Brush.Status.FreshSoft";
    public const string StatusWarning = "Brush.Status.Warning";
    public const string StatusRefreshing = "Brush.Status.Refreshing";
    public const string StatusStale = "Brush.Status.Stale";
    public const string StatusFailed = "Brush.Status.Failed";
    public const string StatusFailedSoft = "Brush.Status.FailedSoft";
    public const string StatusFailedFaint = "Brush.Status.FailedFaint";
    public const string StatusNeutral = "Brush.Status.Neutral";

    // Urgency — usage percentage bands
    public const string UrgencyCritical = "Brush.Urgency.Critical";
    public const string UrgencyHigh = "Brush.Urgency.High";
    public const string UrgencyMedium = "Brush.Urgency.Medium";
    public const string UrgencyLow = "Brush.Urgency.Low";

    // Brand — per-provider accent colors
    public const string BrandClaude = "Brush.Brand.Claude";
    public const string BrandCodex = "Brush.Brand.Codex";
    public const string BrandAntigravity = "Brush.Brand.Antigravity";
    public const string BrandUnknown = "Brush.Brand.Unknown";

    // Badge — source and status badge surfaces
    public const string BadgeCloudFg = "Brush.Badge.Cloud.Fg";
    public const string BadgeCloudBg = "Brush.Badge.Cloud.Bg";
    public const string BadgeCloudBorder = "Brush.Badge.Cloud.Border";
    public const string BadgeIdeFg = "Brush.Badge.Ide.Fg";
    public const string BadgeIdeBg = "Brush.Badge.Ide.Bg";
    public const string BadgeIdeBorder = "Brush.Badge.Ide.Border";
    public const string BadgeFailureFg = "Brush.Badge.FailureFg";
    public const string BadgeFailureBg = "Brush.Badge.FailureBg";
    public const string BadgeFailureBorder = "Brush.Badge.FailureBorder";
    public const string BadgeOkFg = "Brush.Badge.OkFg";
    public const string BadgeOkBg = "Brush.Badge.OkBg";
    public const string BadgeOkBorder = "Brush.Badge.OkBorder";

    // Indicator — settings change state dots
    public const string IndicatorClean = "Brush.Indicator.Clean";
    public const string IndicatorDirty = "Brush.Indicator.Dirty";
}

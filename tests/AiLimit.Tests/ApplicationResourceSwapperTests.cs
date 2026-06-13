using System.Runtime.ExceptionServices;
using System.Windows.Media;
using AiLimit.App.Services;

namespace AiLimit.Tests;

public sealed class ApplicationResourceSwapperTests
{
    [Fact]
    public void ThemeResourcesUseQuotaWatchAssemblyName()
    {
        var source = File.ReadAllText(SourceFile(
            "src",
            "AiLimit.App",
            "Services",
            "ApplicationResourceSwapper.cs"));

        Assert.Contains("/QuotaWatch;component/Theming/Themes/", source);
        Assert.DoesNotContain("/AiLimit.App;component/Theming/Themes/", source);
    }

    [Fact]
    public void Swap_UpdatesApplicationBrushResourcesInBothDirections()
    {
        RunOnStaThread(() =>
        {
            var app = new AiLimit.App.App();
            app.InitializeComponent();

            var before = Assert.IsType<SolidColorBrush>(app.FindResource("Brush.Surface.Window"));
            Assert.Equal(Color.FromRgb(0x0D, 0x11, 0x17), before.Color);

            new ApplicationResourceSwapper().Swap(ResolvedTheme.Light);

            var after = Assert.IsType<SolidColorBrush>(app.FindResource("Brush.Surface.Window"));
            Assert.Equal(Color.FromRgb(0xF6, 0xF8, 0xFA), after.Color);

            var lightOverlay = Assert.IsType<SolidColorBrush>(app.FindResource("Brush.Surface.Overlay"));
            Assert.Equal(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), lightOverlay.Color);

            new ApplicationResourceSwapper().Swap(ResolvedTheme.Dark);

            var restored = Assert.IsType<SolidColorBrush>(app.FindResource("Brush.Surface.Window"));
            Assert.Equal(Color.FromRgb(0x0D, 0x11, 0x17), restored.Color);

            app.Shutdown();
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static string SourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "quota-watch.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}

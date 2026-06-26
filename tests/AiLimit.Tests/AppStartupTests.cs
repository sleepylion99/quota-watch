namespace AiLimit.Tests;

public sealed class AppStartupTests
{
    [Fact]
    public void AppStartupPreventsDuplicateInstances()
    {
        var code = ReadSourceFile("src", "AiLimit.App", "App.xaml.cs");

        Assert.Contains("SingleInstanceMutexName", code);
        Assert.Contains("new Mutex(initiallyOwned: true", code);
        Assert.Contains("if (!isFirstInstance)", code);
        Assert.Contains("Shutdown();", code);
        Assert.Contains("_ownsSingleInstanceMutex", code);
        Assert.Contains("ReleaseMutex()", code);
    }

    [Fact]
    public void ReleasePackagingInjectsAppVersion()
    {
        var script = ReadSourceFile("packaging", "build-release.ps1");

        Assert.Contains("-p:Version=$Version", script);
        Assert.Contains("-p:AssemblyVersion=$Version", script);
        Assert.Contains("-p:FileVersion=$Version", script);
        Assert.Contains("-p:InformationalVersion=$Version", script);
    }

    [Fact]
    public void AppAssemblyUsesQuotaWatchProductName()
    {
        var project = ReadSourceFile("src", "AiLimit.App", "AiLimit.App.csproj");

        Assert.Contains("<AssemblyName>QuotaWatch</AssemblyName>", project);
        Assert.Contains("<Product>Quota Watch</Product>", project);
        Assert.Contains("<Version>0.0.3</Version>", project);
    }

    [Fact]
    public void ReleasePackagingDefaultsToPublicBetaVersion()
    {
        var script = ReadSourceFile("packaging", "build-release.ps1");
        var installer = ReadSourceFile("packaging", "inno", "Quota-Watch.iss");

        Assert.Contains("[string]$Version = \"0.0.3\"", script);
        Assert.Contains("#define AppVersion \"0.0.3\"", installer);
    }

    [Fact]
    public void AppStartupOpensDashboardByDefault()
    {
        var app = ReadSourceFile("src", "AiLimit.App", "App.xaml.cs");
        var installer = ReadSourceFile("packaging", "inno", "Quota-Watch.iss");

        Assert.Contains("_state.ShowDashboard();", app);
        Assert.DoesNotContain("--dashboard", app);
        Assert.DoesNotContain("Parameters: \"--dashboard\"", installer);
        Assert.Contains("Name: \"{userdesktop}\\Quota Watch\"; Filename: \"{app}\\QuotaWatch.exe\"; Tasks: desktopicon", installer);
        Assert.Contains("Filename: \"{app}\\QuotaWatch.exe\"; Description: \"Launch Quota Watch\"", installer);
    }

    private static string ReadSourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "quota-watch.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. segments]));
    }
}

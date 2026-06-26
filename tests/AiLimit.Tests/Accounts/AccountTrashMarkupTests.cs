using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AccountTrashMarkupTests
{
    [Fact]
    public void AccountsWindowHostsTrashMode()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Windows", "AccountsWindow.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Windows", "AccountsWindow.xaml.cs");
        var tableXaml = ReadSourceFile("src", "AiLimit.App", "Controls", "AccountsTable.xaml");

        Assert.Contains("x:Name=\"AccountsTabsView\"", xaml);
        Assert.Contains("controls:AccountTrashView", xaml);
        Assert.Contains("RefreshModeVisibility", code);

        // The trash (recycle bin) button now lives in the per-tab action bar alongside the
        // other actions, not in a dedicated window-level row.
        Assert.Contains("x:Name=\"OpenTrashButton\"", tableXaml);
        Assert.Contains("Click=\"OpenTrashButton_Click\"", tableXaml);
        Assert.Contains("TrashButtonLabel", tableXaml);
    }

    [Fact]
    public void AccountsTableExposesMoveToTrashActionPerRow()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Controls", "AccountsTable.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Controls", "AccountsTable.xaml.cs");

        Assert.Contains("Content=\"{Binding DataContext.TrashButtonText", xaml);
        Assert.Contains("Binding=\"{Binding CanTrash}\"", xaml);
        Assert.Contains("Click=\"TrashButton_Click\"", xaml);
        Assert.Contains("MoveToTrashInCurrentTabAsync", code);
    }

    [Fact]
    public void AccountsTableOmitsOAuthClientSection()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Controls", "AccountsTable.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Controls", "AccountsTable.xaml.cs");

        // The account rows are the primary content of the tab.
        Assert.Contains("<Grid Grid.Row=\"2\">", xaml);
        // The OAuth client management section was removed — most users sign in with Google
        // (which uses the bundled client), and env vars still override it for power users.
        Assert.DoesNotContain("OAuthClientSection", xaml);
        Assert.DoesNotContain("OAuthClientSelectButton_Click", code);
    }

    [Fact]
    public void TrashViewExposesRestoreAndPermanentDeleteActions()
    {
        var xaml = ReadSourceFile("src", "AiLimit.App", "Controls", "AccountTrashView.xaml");
        var code = ReadSourceFile("src", "AiLimit.App", "Controls", "AccountTrashView.xaml.cs");

        Assert.Contains("ItemsSource=\"{Binding TrashRows}\"", xaml);
        Assert.Contains("Content=\"{Binding BackToAccountsText}\"", xaml);
        Assert.Contains("Content=\"{Binding DataContext.RestoreText", xaml);
        Assert.Contains("Content=\"{Binding DataContext.DeletePermanentlyText", xaml);
        Assert.Contains("x:Name=\"PermanentDeleteOverlay\"", xaml);
        Assert.Contains("Background=\"{DynamicResource Brush.Surface.ModalScrim}\"", xaml);
        Assert.Contains("Text=\"{Binding PendingDeleteTitleText, RelativeSource={RelativeSource AncestorType=UserControl}}\"", xaml);
        Assert.Contains("Text=\"{Binding PendingDeleteMessageText, RelativeSource={RelativeSource AncestorType=UserControl}}\"", xaml);
        Assert.DoesNotContain("ElementName=Root", xaml);
        Assert.Contains("x:Key=\"DangerButtonStyle\"", xaml);
        Assert.Contains("Brush.Status.FailedSoft", xaml);
        Assert.Contains("ConfirmPermanentDeleteButton_Click", xaml);
        Assert.Contains("CancelPermanentDeleteButton_Click", xaml);
        Assert.DoesNotContain("MessageBox", code);
        Assert.Contains("DeleteTrashPermanentlyAsync", code);
    }

    private static string ReadSourceFile(params string[] relativeParts)
    {
        var root = RepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "quota-watch.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

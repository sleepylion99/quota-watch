using System.IO;
using System.Windows;
using AiLimit.App.Services;
using AiLimit.App.ViewModels;
using AiLimit.Core.Settings;

namespace AiLimit.App.Windows;

public partial class CodexProfilesWindow : Window
{
    private readonly AppState _state;
    private readonly UsageViewModel _viewModel = new();

    public CodexProfilesWindow(AppState state)
    {
        InitializeComponent();
        _state = state;
        DataContext = _viewModel;
        UpdateViewModel();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ProfilesMinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BrowseCodexProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var language = AppLanguageResolver.Resolve(_state.DisplayLanguage);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = language switch
            {
                AppLanguage.Korean => "Codex auth.json 선택",
                AppLanguage.Japanese => "Codex auth.json を選択",
                AppLanguage.Chinese => "选择 Codex auth.json",
                _ => "Select Codex auth.json"
            },
            Filter = language switch
            {
                AppLanguage.Korean => "Codex auth.json (auth.json)|auth.json|JSON 파일 (*.json)|*.json",
                AppLanguage.Japanese => "Codex auth.json (auth.json)|auth.json|JSON ファイル (*.json)|*.json",
                AppLanguage.Chinese => "Codex auth.json (auth.json)|auth.json|JSON 文件 (*.json)|*.json",
                _ => "Codex auth.json (auth.json)|auth.json|JSON files (*.json)|*.json"
            },
            InitialDirectory = GetCodexProfileBrowseDirectory(),
            FileName = "auth.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        CodexProfilePathBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(CodexProfileNameBox.Text))
        {
            CodexProfileNameBox.Text = Path.GetFileName(Path.GetDirectoryName(dialog.FileName));
        }

        CodexProfileStatusText.Text = string.Empty;
    }

    private string GetCodexProfileBrowseDirectory()
    {
        var selectedProfile = _state.CurrentSettings
            .GetEffectiveCodexProfiles()
            .FirstOrDefault(profile => profile.Id == _state.CurrentSettings.SelectedCodexProfileId);
        var candidatePaths = new[]
        {
            CodexProfilePathBox.Text,
            selectedProfile?.AuthPath,
            CodexProfilePaths.DefaultAuthPath()
        };

        foreach (var candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            try
            {
                var fullPath = Path.GetFullPath(candidatePath.Trim());
                var directory = Directory.Exists(fullPath)
                    ? fullPath
                    : Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    return directory;
                }
            }
            catch
            {
                // Ignore malformed paths and try the next Codex location.
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void AddCodexProfileButton_Click(object sender, RoutedEventArgs e)
    {
        string path;
        try
        {
            path = Path.GetFullPath(CodexProfilePathBox.Text.Trim());
        }
        catch
        {
            SetStatus(CodexProfileStatus.InvalidPath);
            return;
        }

        if (!File.Exists(path))
        {
            SetStatus(CodexProfileStatus.FileMissing);
            return;
        }

        var profiles = _state.CurrentSettings.GetEffectiveCodexProfiles().ToList();
        if (profiles.Any(profile => string.Equals(profile.AuthPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus(CodexProfileStatus.AlreadyRegistered);
            return;
        }

        var displayName = CodexProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Path.GetFileName(Path.GetDirectoryName(path));
        }

        profiles.Add(new CodexProfileSetting(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(displayName) ? FallbackProfileName() : displayName,
            path));
        SaveProfiles(profiles);
        CodexProfileNameBox.Clear();
        CodexProfilePathBox.Clear();
        SetStatus(CodexProfileStatus.Added);
    }

    private void RemoveCodexProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string profileId })
        {
            return;
        }

        var profiles = _state.CurrentSettings.GetEffectiveCodexProfiles().ToList();
        var profile = profiles.FirstOrDefault(item => item.Id == profileId);
        if (profile is null || profile.IsDefault)
        {
            return;
        }

        profiles.Remove(profile);
        SaveProfiles(profiles);
        SetStatus(CodexProfileStatus.Removed);
    }

    private void SaveProfiles(IReadOnlyList<CodexProfileSetting> profiles)
    {
        var selectedId = profiles.Any(profile => profile.Id == _state.CurrentSettings.SelectedCodexProfileId)
            ? _state.CurrentSettings.SelectedCodexProfileId
            : CodexProfileSetting.DefaultId;
        _state.SaveSettingsFromDashboard(_state.CurrentSettings with
        {
            CodexProfiles = profiles,
            SelectedCodexProfileId = selectedId
        });
        UpdateViewModel();
    }

    private void UpdateViewModel()
    {
        _viewModel.Update(
            [],
            isRefreshing: false,
            LimitDisplayMode.Bars,
            AppLanguageResolver.Resolve(_state.DisplayLanguage),
            codexProfiles: _state.CurrentSettings.GetEffectiveCodexProfiles(),
            selectedCodexProfileId: _state.CurrentSettings.SelectedCodexProfileId);
    }

    private void SetStatus(CodexProfileStatus status)
    {
        CodexProfileStatusText.Text = LocalizeStatus(status, AppLanguageResolver.Resolve(_state.DisplayLanguage));
    }

    private string FallbackProfileName()
    {
        return AppLanguageResolver.Resolve(_state.DisplayLanguage) switch
        {
            AppLanguage.Korean => "Codex 프로필",
            AppLanguage.Japanese => "Codex プロファイル",
            AppLanguage.Chinese => "Codex 配置文件",
            _ => "Codex profile"
        };
    }

    private static string LocalizeStatus(CodexProfileStatus status, AppLanguage language) =>
        (status, language) switch
        {
            (CodexProfileStatus.InvalidPath, AppLanguage.Korean) => "올바른 auth.json 경로를 입력하세요.",
            (CodexProfileStatus.InvalidPath, AppLanguage.Japanese) => "正しい auth.json のパスを入力してください。",
            (CodexProfileStatus.InvalidPath, AppLanguage.Chinese) => "请输入有效的 auth.json 路径。",
            (CodexProfileStatus.InvalidPath, _) => "Enter a valid auth.json path.",
            (CodexProfileStatus.FileMissing, AppLanguage.Korean) => "선택한 파일을 찾을 수 없습니다.",
            (CodexProfileStatus.FileMissing, AppLanguage.Japanese) => "選択したファイルが見つかりません。",
            (CodexProfileStatus.FileMissing, AppLanguage.Chinese) => "未找到所选文件。",
            (CodexProfileStatus.FileMissing, _) => "The selected file does not exist.",
            (CodexProfileStatus.AlreadyRegistered, AppLanguage.Korean) => "이미 등록된 프로필입니다.",
            (CodexProfileStatus.AlreadyRegistered, AppLanguage.Japanese) => "すでに登録されているプロファイルです。",
            (CodexProfileStatus.AlreadyRegistered, AppLanguage.Chinese) => "该配置文件已注册。",
            (CodexProfileStatus.AlreadyRegistered, _) => "That profile is already registered.",
            (CodexProfileStatus.Added, AppLanguage.Korean) => "프로필을 추가했습니다.",
            (CodexProfileStatus.Added, AppLanguage.Japanese) => "プロファイルを追加しました。",
            (CodexProfileStatus.Added, AppLanguage.Chinese) => "已添加配置文件。",
            (CodexProfileStatus.Added, _) => "Profile added.",
            (CodexProfileStatus.Removed, AppLanguage.Korean) => "프로필을 삭제했습니다.",
            (CodexProfileStatus.Removed, AppLanguage.Japanese) => "プロファイルを削除しました。",
            (CodexProfileStatus.Removed, AppLanguage.Chinese) => "已删除配置文件。",
            (CodexProfileStatus.Removed, _) => "Profile removed.",
            _ => string.Empty
        };

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private enum CodexProfileStatus
    {
        InvalidPath,
        FileMissing,
        AlreadyRegistered,
        Added,
        Removed
    }
}

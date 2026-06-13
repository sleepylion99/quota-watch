#define AppName "Quota Watch"
#define AppPublisher "sleepylion99"
#ifndef AppVersion
#define AppVersion "0.0.1"
#endif
#ifndef SourceDir
#define SourceDir "..\..\artifacts\publish\win-x64-framework-dependent"
#endif
#ifndef OutputDir
#define OutputDir "..\..\artifacts\release"
#endif
#define DotNetUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{A4D216A2-2D4F-4EA7-90AA-FCC7EBC809D4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Quota Watch
DefaultGroupName=Quota Watch
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Quota-Watch-WebSetup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
SetupIconFile=..\..\src\AiLimit.App\Assets\Icons\app.ico
UninstallDisplayIcon={app}\QuotaWatch.exe
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Windows 시작 시 자동 실행 / Start automatically with Windows"; GroupDescription: "Windows Startup"; Flags: unchecked
Name: "launchafterinstall"; Description: "Launch Quota Watch after installation"; GroupDescription: "Post-install actions"; Flags: checkedonce

[InstallDelete]
; Wipe stale runtime files from previous installs (e.g. orphaned hostfxr.dll from a self-contained build)
; so the framework-dependent apphost resolves the system .NET cleanly. User data lives in %APPDATA%, not {app}.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Quota Watch"; Filename: "{app}\QuotaWatch.exe"
Name: "{userdesktop}\Quota Watch"; Filename: "{app}\QuotaWatch.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\QuotaWatch.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\QuotaWatch.exe"; Description: "Launch Quota Watch"; Flags: nowait postinstall skipifsilent; Tasks: launchafterinstall

[CustomMessages]
korean.RuntimeMissingTitle=.NET 10 Desktop Runtime 필요
korean.RuntimeMissingPrompt=Quota Watch 실행에 필요한 .NET 10 Desktop Runtime이 설치되어 있지 않습니다.%n지금 다운로드해 설치할까요? (인터넷 연결 필요, 관리자 권한이 요청됩니다)
korean.RuntimeDownloading=.NET 10 Desktop Runtime을 다운로드하는 중입니다...
korean.RuntimeInstalling=.NET 10 Desktop Runtime을 설치하는 중입니다...
korean.RuntimeDownloadFailed=.NET 10 Desktop Runtime 다운로드에 실패했습니다.%n%n%1
korean.RuntimeInstallFailed=.NET 10 Desktop Runtime 설치에 실패했습니다 (종료 코드: %1).
korean.RuntimeLaunchFailed=.NET 10 Desktop Runtime 설치 프로그램을 실행하지 못했습니다.
korean.RuntimeUserCancelled=.NET 10 Desktop Runtime 설치가 취소되었습니다. 설치를 계속할 수 없습니다.
english.RuntimeMissingTitle=.NET 10 Desktop Runtime required
english.RuntimeMissingPrompt=Quota Watch requires the .NET 10 Desktop Runtime, which is not installed.%nDownload and install it now? (Internet connection required, administrator privileges will be requested)
english.RuntimeDownloading=Downloading .NET 10 Desktop Runtime...
english.RuntimeInstalling=Installing .NET 10 Desktop Runtime...
english.RuntimeDownloadFailed=Failed to download the .NET 10 Desktop Runtime.%n%n%1
english.RuntimeInstallFailed=The .NET 10 Desktop Runtime installer failed (exit code: %1).
english.RuntimeLaunchFailed=Failed to launch the .NET 10 Desktop Runtime installer.
english.RuntimeUserCancelled=.NET 10 Desktop Runtime installation was cancelled. Setup cannot continue.

[Code]
var
  DownloadPage: TDownloadWizardPage;

function IsDotNet10DesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
  RuntimeDir: String;
  Found: Boolean;
begin
  Result := False;
  RuntimeDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(RuntimeDir) then
    Exit;

  Found := FindFirst(RuntimeDir + '\10.*', FindRec);
  if not Found then
    Exit;
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0)
         and (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(
    ExpandConstant('{cm:RuntimeDownloading}'),
    ExpandConstant('{cm:RuntimeMissingTitle}'),
    nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  InstallerPath: String;
begin
  Result := '';
  NeedsRestart := False;

  if IsDotNet10DesktopRuntimeInstalled() then
    Exit;

  if MsgBox(ExpandConstant('{cm:RuntimeMissingPrompt}'), mbConfirmation, MB_YESNO) <> IDYES then
  begin
    Result := ExpandConstant('{cm:RuntimeUserCancelled}');
    Exit;
  end;

  InstallerPath := ExpandConstant('{tmp}\windowsdesktop-runtime-10.0-win-x64.exe');

  DownloadPage.Clear;
  DownloadPage.Add('{#DotNetUrl}', 'windowsdesktop-runtime-10.0-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Result := FmtMessage(ExpandConstant('{cm:RuntimeDownloadFailed}'), [GetExceptionMessage]);
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  WizardForm.StatusLabel.Caption := ExpandConstant('{cm:RuntimeInstalling}');
  if not ShellExec('runas', InstallerPath, '/install /quiet /norestart', '',
                   SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := ExpandConstant('{cm:RuntimeLaunchFailed}');
    Exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    Result := FmtMessage(ExpandConstant('{cm:RuntimeInstallFailed}'), [IntToStr(ResultCode)]);
    Exit;
  end;

  if ResultCode = 3010 then
    NeedsRestart := True;

  if not IsDotNet10DesktopRuntimeInstalled() then
  begin
    Result := FmtMessage(ExpandConstant('{cm:RuntimeInstallFailed}'), [IntToStr(ResultCode)]);
    Exit;
  end;
end;

#define AppName "Quota Watch"
#define AppPublisher "sleepylion99"
#ifndef AppVersion
#define AppVersion "0.0.2"
#endif
#ifndef SourceDir
#define SourceDir "..\..\artifacts\publish\win-x64-self-contained"
#endif
#ifndef OutputDir
#define OutputDir "..\..\artifacts\release"
#endif

[Setup]
AppId={{A4D216A2-2D4F-4EA7-90AA-FCC7EBC809D4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Quota Watch
DefaultGroupName=Quota Watch
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Quota-Watch-Setup-{#AppVersion}-win-x64
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
; so apphost picks up the bundled or system .NET cleanly. User data lives in %APPDATA%, not {app}.
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

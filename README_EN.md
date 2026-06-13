# Quota Watch

<img width="1611" height="778" alt="Quota Watch" src="https://github.com/user-attachments/assets/19241298-ef79-497c-9558-9cf802e5256b" />

---

<p align="center">
  <a href="README.md">한국어</a> |
  <a href="README_EN.md">English</a>
</p>

<p align="center">Codex · Claude Code · Google Antigravity usage limits, all on one screen.</p>

---

**Quota Watch** is a Windows desktop app that shows the usage limits and quota status of Codex, Claude Code, and Google Antigravity on a single screen. Through the tray icon and dashboard you can quickly check your current usage, reset times, and per-provider login status.

> [!IMPORTANT]
> The current release channel is `v0.0.1 Public Beta`. The app relies on each provider's local login data and on unofficial/internal quota APIs, so lookups may temporarily fail when a provider changes its API. Auth tokens are read locally only and are never sent to a Quota Watch server.

## System requirements

- Windows 10 version 1809 (build 17763) or later, 64-bit (x64)
- Display languages: English, Korean, Japanese, Simplified Chinese — applied automatically based on your Windows display language

## Download

Get the latest release from GitHub Releases. It comes in three forms.

- **Full installer (recommended)**: `Quota-Watch-Setup-<version>-win-x64.exe` — a self-contained installer with the .NET runtime bundled in. Runs right away with no separate runtime install.
- **Web installer (smaller download)**: `Quota-Watch-WebSetup-<version>-win-x64.exe` — downloads the .NET 10 Desktop Runtime during setup if it is missing. Requires an internet connection and requests administrator privileges for the runtime install step.
- **Run without installing**: `Quota-Watch-<version>-win-x64-portable.zip` — a self-contained build you just extract and run.

The installers install per-user without administrator privileges (except the web installer's runtime download step).

## Installation

### Using an installer

1. Download the full installer `Quota-Watch-Setup-<version>-win-x64.exe` or the web installer `Quota-Watch-WebSetup-<version>-win-x64.exe` from Releases.
2. Run the installer. The web installer only prompts to download the .NET 10 Desktop Runtime when it is missing.
3. During setup you can opt into a desktop icon and starting automatically with Windows (both off by default).
4. After installation, launch it from the Start menu or the desktop shortcut, then open the dashboard from the Windows tray icon.

### Using the portable zip

1. Download `Quota-Watch-<version>-win-x64-portable.zip` from Releases.
2. Extract it to a folder of your choice.
3. Run `QuotaWatch.exe` from the extracted folder.

## Features

- Track Codex, Claude Code, and Google Antigravity usage
- Display 5-hour / weekly or per-model quota windows depending on the provider
- Quota-exhaustion notifications
- Per-provider login and configuration guidance
- Tray icon and profile switching
- Automatic provider refresh with retry backoff on failure
- Diagnostic logging that redacts sensitive data such as tokens, secrets, and passwords when written or copied

## Provider setup

### Codex

Codex must be installed and logged in. Quota Watch first queries cloud limits using the OAuth credentials in `%USERPROFILE%\.codex\auth.json` or `CODEX_HOME\auth.json`, so as long as those credentials are valid you do not need to keep Codex running. If credentials are missing or expired, the app shows install/login guidance.

### Claude Code

Claude Code OAuth credentials are required. Quota Watch checks the following sources in order:

- `CLAUDE_CODE_OAUTH_TOKEN`
- `CLAUDE_CONFIG_DIR`
- `%USERPROFILE%\.claude\.credentials.json`

If credentials are present, you do not need to keep the Claude Code app open.

### Google Antigravity

You must be signed in to Antigravity with a Google account. Quota Watch uses the following:

- `%USERPROFILE%\.antigravity\oauth_creds.json`
- `ANTIGRAVITY_OAUTH_ACCESS_TOKEN`
- The access and refresh tokens from the local Antigravity storage. To refresh an expired token without the IDE, the local storage must contain OAuth client values, or you must set `ANTIGRAVITY_OAUTH_CLIENT_ID` / `ANTIGRAVITY_OAUTH_CLIENT_SECRET` yourself.
- `%APPDATA%\Antigravity IDE\User\globalStorage\state.vscdb`
- `%APPDATA%\Antigravity\User\globalStorage\state.vscdb`

To refresh an expired token even when the IDE is closed, save your own OAuth client ID and secret under Settings > Antigravity OAuth. You get these values when you create an OAuth desktop app in the Google Cloud Console. They are not for connecting to the IDE; Quota Watch uses them to refresh — without the IDE — the refresh token that Antigravity created via Google sign-in. The saved values are not stored in plain text; they are encrypted to your current Windows user account with Windows DPAPI. The `ANTIGRAVITY_OAUTH_CLIENT_ID` / `ANTIGRAVITY_OAUTH_CLIENT_SECRET` environment variables also keep working.

If the stored credentials are valid, you do not need to keep Antigravity or the Antigravity IDE running. If cloud limits cannot be read directly, Quota Watch falls back to the local endpoint of a running Antigravity IDE.

## Troubleshooting

Check the guidance shown on the provider cards in the dashboard first.

- `Install/login required`: the provider app needs to be installed or you need to log in.
- `Launch/login required`: launch the provider app or IDE and confirm you are logged in.
- `Authentication required`: credentials are missing or expired.
- `Timed out`: the provider response is too slow. Try again shortly.
- `Network problem`: check your network or the provider's service status, then retry.
- `Failed to parse response`: the provider's response format differs from what was expected.
- `No quota info`: there was a response, but no quota information to display.

If you need a diagnostic log, run the app from PowerShell as shown below.

### When installed via an installer

```powershell
$env:AILIMIT_DEBUG_LOG="1"
& "$env:LOCALAPPDATA\Programs\Quota Watch\QuotaWatch.exe"
```

If the executable is not found at the path above, right-click the Quota Watch shortcut in the Start menu and open its file location to find the actual install path.

### When running the portable zip

Open PowerShell in the extracted folder and run:

```powershell
$env:AILIMIT_DEBUG_LOG="1"
.\QuotaWatch.exe
```

Log location:

```text
%APPDATA%\AiLimit\dashboard-debug.log
```

The diagnostic logging feature redacts sensitive data — access tokens, refresh tokens, client secrets, API keys, passwords, bearer tokens, Antigravity CSRF arguments, and the like — before writing it to a file or copying it to the clipboard.

## Developer commands

Run these from the repository root in PowerShell.

```powershell
dotnet restore .\quota-watch.slnx
dotnet build .\quota-watch.slnx
dotnet test .\quota-watch.slnx
dotnet run --project .\src\AiLimit.App\AiLimit.App.csproj
```

Build release packages:

```powershell
powershell -ExecutionPolicy Bypass -File .\packaging\build-release.ps1 -Version <version>
```

Output:

- `artifacts/release/Quota-Watch-<version>-win-x64-portable.zip`
- `artifacts/release/Quota-Watch-Setup-<version>-win-x64.exe`
- `artifacts/release/Quota-Watch-WebSetup-<version>-win-x64.exe`

If you hit temp-folder permission issues in the test environment, run:

```powershell
$env:TEMP="$PWD\.tmp"; $env:TMP="$PWD\.tmp"; dotnet test .\quota-watch.slnx --no-restore -c Release --verbosity minimal
```

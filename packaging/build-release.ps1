param(
    [string]$Version = "0.0.3",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [ValidateSet("offline", "online", "both")]
    [string]$Variant = "both",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/AiLimit.App/AiLimit.App.csproj"
$artifacts = Join-Path $repoRoot "artifacts"
$releaseDir = Join-Path $artifacts "release"

$selfContainedDir = Join-Path $artifacts "publish\$Runtime-self-contained"
$frameworkDependentDir = Join-Path $artifacts "publish\$Runtime-framework-dependent"

$portableZip = Join-Path $releaseDir "Quota-Watch-$Version-$Runtime-portable.zip"

$offlineInstallerScript = Join-Path $PSScriptRoot "inno\Quota-Watch.iss"
$onlineInstallerScript = Join-Path $PSScriptRoot "inno\Quota-Watch-Web.iss"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Resolve-Iscc {
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($null -eq $iscc) {
        $candidates = @(
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
        )
        foreach ($candidate in $candidates) {
            if (Test-Path $candidate) {
                $iscc = Get-Item $candidate
                break
            }
        }
    }

    if ($null -eq $iscc) {
        throw "Inno Setup compiler was not found. Install Inno Setup 6, then rerun this script."
    }

    if ($iscc.Source) { return $iscc.Source } else { return $iscc.FullName }
}

function Invoke-Publish {
    param(
        [Parameter(Mandatory = $true)][string]$OutDir,
        [Parameter(Mandatory = $true)][bool]$SelfContained
    )

    if (Test-Path $OutDir) {
        Remove-Item -Recurse -Force $OutDir
    }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    $selfContainedFlag = if ($SelfContained) { "true" } else { "false" }

    Invoke-Checked {
        dotnet publish $project `
            --no-restore `
            -c $Configuration `
            -r $Runtime `
            --self-contained $selfContainedFlag `
            -p:Version=$Version `
            -p:AssemblyVersion=$Version `
            -p:FileVersion=$Version `
            -p:InformationalVersion=$Version `
            -p:PublishSingleFile=false `
            -p:PublishReadyToRun=false `
            -p:DebugSymbols=false `
            -p:DebugType=None `
            -o $OutDir
    } "dotnet publish (self-contained=$selfContainedFlag)"
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Invoke-Checked { dotnet restore $project -r $Runtime } "dotnet restore"

$buildOffline = ($Variant -eq "offline") -or ($Variant -eq "both")
$buildOnline = ($Variant -eq "online") -or ($Variant -eq "both")

if ($buildOffline) {
    Invoke-Publish -OutDir $selfContainedDir -SelfContained $true

    if (Test-Path $portableZip) {
        Remove-Item -LiteralPath $portableZip -Force
    }
    Compress-Archive -Path (Join-Path $selfContainedDir "*") -DestinationPath $portableZip -Force
    Write-Host "Portable package: $portableZip"
}

if ($buildOnline) {
    Invoke-Publish -OutDir $frameworkDependentDir -SelfContained $false
    Write-Host "Framework-dependent publish: $frameworkDependentDir"
}

if ($SkipInstaller) {
    return
}

$isccPath = Resolve-Iscc

if ($buildOffline) {
    Invoke-Checked {
        & $isccPath `
        "/DAppVersion=$Version" `
        "/DSourceDir=$selfContainedDir" `
        "/DOutputDir=$releaseDir" `
        $offlineInstallerScript
    } "Inno Setup compiler (offline)"
    Write-Host "Offline installer: $releaseDir\Quota-Watch-Setup-$Version-win-x64.exe"
}

if ($buildOnline) {
    Invoke-Checked {
        & $isccPath `
        "/DAppVersion=$Version" `
        "/DSourceDir=$frameworkDependentDir" `
        "/DOutputDir=$releaseDir" `
        $onlineInstallerScript
    } "Inno Setup compiler (online)"
    Write-Host "Online (web) installer: $releaseDir\Quota-Watch-WebSetup-$Version-win-x64.exe"
}

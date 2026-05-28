param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$artifacts = Join-Path $repoRoot "artifacts"
$stageRoot = Join-Path $artifacts "stage"
$installerRoot = Join-Path $artifacts "installer"
$runtime = "win-x64"

function Assert-Command {
    param(
        [string]$Name,
        [string]$InstallHint
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. $InstallHint"
    }
}

function Publish-App {
    param(
        [string]$Project,
        [string]$Output
    )

    $args = @(
        "publish", $Project,
        "-c", $Configuration,
        "-o", $Output,
        "-r", $runtime
    )

    if ($FrameworkDependent) {
        $args += "--self-contained"
        $args += "false"
    } else {
        $args += "--self-contained"
        $args += "true"
        $args += "/p:PublishSingleFile=false"
    }

    dotnet @args
}

Assert-Command "dotnet" "Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"

$sdkList = dotnet --list-sdks
if (-not $sdkList) {
    throw "No .NET SDKs were found. Install the .NET 8 SDK before building the installer."
}

Remove-Item -Path $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stageRoot, $installerRoot | Out-Null

Publish-App -Project (Join-Path $repoRoot "src\OTM.Service\OTM.Service.csproj") -Output (Join-Path $stageRoot "service")
Publish-App -Project (Join-Path $repoRoot "src\OTM.ControlPanel\OTM.ControlPanel.csproj") -Output (Join-Path $stageRoot "control-panel")
Publish-App -Project (Join-Path $repoRoot "src\OTM.RecoveryTool\OTM.RecoveryTool.csproj") -Output (Join-Path $stageRoot "recovery")

$isccCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
$isccPath = if ($isccCommand) { $isccCommand.Source } else { $null }
if (-not $isccPath) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $isccPath = $candidate
            break
        }
    }
}

if (-not $isccPath) {
    throw "ISCC.exe was not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php, then rerun this script."
}

$env:OTM_KIOSK_VERSION = $Version
& $isccPath (Join-Path $repoRoot "installer\OTMKiosk.iss")

Write-Host "Installer created in $installerRoot"

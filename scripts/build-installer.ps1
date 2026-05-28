param(
    [string]$Configuration = "Release",
    [string]$Version = "4.0.0",
    [switch]$FrameworkDependent,
    [switch]$Sign,
    [string]$CertificateThumbprint = $env:OTM_SIGN_CERT_THUMBPRINT,
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStore = $(if ($env:OTM_SIGN_CERT_STORE) { $env:OTM_SIGN_CERT_STORE } else { "CurrentUser" }),
    [string]$PfxPath = $env:OTM_SIGN_PFX_PATH,
    [string]$PfxPassword = $env:OTM_SIGN_PFX_PASSWORD,
    [string]$TimestampUrl = $(if ($env:OTM_SIGN_TIMESTAMP_URL) { $env:OTM_SIGN_TIMESTAMP_URL } else { "http://timestamp.digicert.com" })
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$artifacts = Join-Path $repoRoot "artifacts"
$stageRoot = Join-Path $artifacts "stage"
$installerRoot = Join-Path $artifacts "installer"
$dependencyRoot = Join-Path $stageRoot "dependencies"
$runtime = "win-x64"
$webView2Bootstrapper = Join-Path $dependencyRoot "MicrosoftEdgeWebView2Setup.exe"
$webView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

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
New-Item -ItemType Directory -Force -Path $stageRoot, $installerRoot, $dependencyRoot | Out-Null

Write-Host "Downloading Microsoft Edge WebView2 Evergreen bootstrapper..."
try {
    Invoke-WebRequest -Uri $webView2BootstrapperUrl -OutFile $webView2Bootstrapper -UseBasicParsing
} catch {
    throw "Could not download WebView2 Evergreen bootstrapper from Microsoft. Embedded exam/web mode requires WebView2. Error: $($_.Exception.Message)"
}

Publish-App -Project (Join-Path $repoRoot "src\OTM.Service\OTM.Service.csproj") -Output (Join-Path $stageRoot "service")
Publish-App -Project (Join-Path $repoRoot "src\OTM.ControlPanel\OTM.ControlPanel.csproj") -Output (Join-Path $stageRoot "control-panel")
Publish-App -Project (Join-Path $repoRoot "src\OTM.KioskShell\OTM.KioskShell.csproj") -Output (Join-Path $stageRoot "kiosk-shell")
Publish-App -Project (Join-Path $repoRoot "src\OTM.RecoveryTool\OTM.RecoveryTool.csproj") -Output (Join-Path $stageRoot "recovery")

if ($Sign) {
    if (-not $PfxPath -and -not $CertificateThumbprint) {
        throw "Signing was requested, but no Authenticode code-signing certificate was provided. Use -PfxPath or -CertificateThumbprint. This is not an SSL certificate."
    }

    $signScript = Join-Path $repoRoot "scripts\sign-artifacts.ps1"
    $signArgs = @(
        "-Path", $stageRoot,
        "-Recurse",
        "-TimestampUrl", $TimestampUrl
    )

    if ($PfxPath) {
        $signArgs += "-PfxPath"
        $signArgs += $PfxPath
        if ($PfxPassword) {
            $signArgs += "-PfxPassword"
            $signArgs += $PfxPassword
        }
    } elseif ($CertificateThumbprint) {
        $signArgs += "-CertificateThumbprint"
        $signArgs += $CertificateThumbprint
        $signArgs += "-CertificateStore"
        $signArgs += $CertificateStore
    }

    & $signScript @signArgs
}

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

if ($Sign) {
    $installerFiles = Get-ChildItem -Path $installerRoot -Filter "OTM-Kiosk-Setup-$Version*.exe" -File
    if (-not $installerFiles) {
        throw "Installer was built, but no setup EXE matching version $Version was found in $installerRoot."
    }

    $signScript = Join-Path $repoRoot "scripts\sign-artifacts.ps1"
    foreach ($installer in $installerFiles) {
        $signArgs = @(
            "-Path", $installer.FullName,
            "-TimestampUrl", $TimestampUrl
        )

        if ($PfxPath) {
            $signArgs += "-PfxPath"
            $signArgs += $PfxPath
            if ($PfxPassword) {
                $signArgs += "-PfxPassword"
                $signArgs += $PfxPassword
            }
        } elseif ($CertificateThumbprint) {
            $signArgs += "-CertificateThumbprint"
            $signArgs += $CertificateThumbprint
            $signArgs += "-CertificateStore"
            $signArgs += $CertificateStore
        }

        & $signScript @signArgs
    }
}

Write-Host "Installer created in $installerRoot"

param(
    [string]$Version = "7.0.0",
    [switch]$Sign,
    [string]$PfxPath = $env:OTM_SIGN_PFX_PATH,
    [string]$PfxPassword = $env:OTM_SIGN_PFX_PASSWORD
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\.."
$stageRoot = Join-Path $repoRoot "artifacts\manager-stage"
$installerRoot = Join-Path $repoRoot "artifacts\manager-installer"
$managerOut = Join-Path $stageRoot "manager"

Remove-Item -Recurse -Force $stageRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $managerOut, $installerRoot | Out-Null
Remove-Item -Path (Join-Path $installerRoot "SimpleKioskOS-Remote-Manager-Setup*.exe") -Force -ErrorAction SilentlyContinue

dotnet publish (Join-Path $repoRoot "src\OTM.Manager\OTM.Manager.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -p:FileVersion=$Version `
    -p:AssemblyVersion=$Version `
    -p:EnableCompressionInSingleFile=true `
    -o $managerOut

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for OTM.Manager with exit code $LASTEXITCODE."
}

if ($Sign) {
    $signScript = Join-Path $repoRoot "scripts\sign-artifacts.ps1"
    & $signScript -Path $managerOut -Recurse -PfxPath $PfxPath -PfxPassword $PfxPassword
}

$env:OTM_KIOSK_VERSION = $Version
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = "iscc"
}

& $iscc (Join-Path $repoRoot "installer\SimpleKioskOSManager.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if ($Sign) {
    & (Join-Path $repoRoot "scripts\sign-artifacts.ps1") -Path $installerRoot -Recurse -PfxPath $PfxPath -PfxPassword $PfxPassword
}

Write-Host "Remote Manager installer created in $installerRoot"

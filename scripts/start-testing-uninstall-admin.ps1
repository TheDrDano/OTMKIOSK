param(
    [switch]$KeepData
)

$ErrorActionPreference = "Stop"

$uninstallScript = Join-Path $PSScriptRoot "uninstall-testing.ps1"
if (-not (Test-Path -LiteralPath $uninstallScript)) {
    throw "Testing uninstall script not found: $uninstallScript"
}

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "`"$uninstallScript`""
)

if (-not $KeepData) {
    $arguments += "-RemoveData"
}

$powershell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
Start-Process -FilePath $powershell -ArgumentList $arguments -Verb RunAs

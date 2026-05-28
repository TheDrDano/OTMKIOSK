param()

$ErrorActionPreference = "Stop"

$cleanupScript = Join-Path $PSScriptRoot "clear-browser-policies.ps1"
if (-not (Test-Path -LiteralPath $cleanupScript)) {
    throw "Browser policy cleanup script not found: $cleanupScript"
}

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    "`"$cleanupScript`""
)

$powershell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
Start-Process -FilePath $powershell -ArgumentList $arguments -Verb RunAs -Wait

param(
    [string]$InstallRoot = "$env:ProgramFiles\SimpleKioskOS",
    [string]$DataRoot = "$env:ProgramData\OTM Kiosk",
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

function Stop-OtmService {
    if (Get-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue) {
        sc.exe failure "OTMKioskService" reset= 0 actions= "" | Out-Null
        Stop-Service -Name "OTMKioskService" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Stop-Process -Name "OTM.Service" -Force -ErrorAction SilentlyContinue
        sc.exe delete "OTMKioskService" | Out-Null
        Start-Sleep -Seconds 2
        Write-Host "Removed OTMKioskService."
    } else {
        Write-Host "OTMKioskService is not installed."
    }
}

function Stop-OtmProcesses {
    $names = @("OTM.KioskShell", "OTM.ControlPanel", "OTM.Service", "OTM.RecoveryTool")
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Remove-DirectorySafe {
    param(
        [string]$Path,
        [string[]]$AllowedRoots
    )

    if (-not (Test-Path $Path)) {
        return
    }

    $resolved = (Resolve-Path $Path).Path
    $isAllowed = $false
    foreach ($root in $AllowedRoots) {
        $resolvedRoot = (Resolve-Path $root).Path
        $rootWithSeparator = $resolvedRoot.TrimEnd("\") + "\"
        if ($resolved.Equals($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            $resolved.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            $isAllowed = $true
            break
        }
    }

    if (-not $isAllowed) {
        throw "Refusing to remove '$resolved' because it is outside the expected install/data roots."
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Host "Removed $resolved"
}

function Remove-Shortcuts {
    $paths = @(
        "$env:Public\Desktop\OTM Kiosk.lnk",
        "$env:Public\Desktop\SimpleKioskOS.lnk",
        "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\OTM Kiosk Shell.lnk",
        "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\SimpleKioskOS.lnk",
        "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\SimpleKioskOS",
        "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\OTM Kiosk"
    )

    foreach ($path in $paths) {
        if (Test-Path $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
            Write-Host "Removed $path"
        }
    }
}

Stop-OtmService
Stop-OtmProcesses
Remove-Shortcuts

$programFiles = [Environment]::GetFolderPath("ProgramFiles")
$programData = [Environment]::GetFolderPath("CommonApplicationData")
Remove-DirectorySafe -Path $InstallRoot -AllowedRoots @($programFiles)

if ($RemoveData) {
    Remove-DirectorySafe -Path $DataRoot -AllowedRoots @($programData)
} else {
    Write-Host "Kept local data at $DataRoot. Rerun with -RemoveData to wipe SQLite policy/logs."
}

Write-Host "OTM Kiosk testing uninstall complete."

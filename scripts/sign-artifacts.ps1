param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [string]$CertificateThumbprint = $env:OTM_SIGN_CERT_THUMBPRINT,
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStore = $(if ($env:OTM_SIGN_CERT_STORE) { $env:OTM_SIGN_CERT_STORE } else { "CurrentUser" }),
    [string]$PfxPath = $env:OTM_SIGN_PFX_PATH,
    [string]$PfxPassword = $env:OTM_SIGN_PFX_PASSWORD,
    [string]$TimestampUrl = $(if ($env:OTM_SIGN_TIMESTAMP_URL) { $env:OTM_SIGN_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }),
    [switch]$Recurse
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $sdkRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    )

    foreach ($root in $sdkRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $candidate = Get-ChildItem -Path $root -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\x64\signtool.exe" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "signtool.exe was not found. Install the Windows SDK, or run from a Developer PowerShell."
}

function Resolve-SignableFiles {
    param([string[]]$InputPaths, [switch]$Recursive)

    $files = New-Object System.Collections.Generic.List[string]
    foreach ($inputPath in $InputPaths) {
        if (-not (Test-Path $inputPath)) {
            throw "Path not found: $inputPath"
        }

        $item = Get-Item -LiteralPath $inputPath
        if ($item.PSIsContainer) {
            $search = if ($Recursive) { Get-ChildItem -LiteralPath $item.FullName -Recurse -File } else { Get-ChildItem -LiteralPath $item.FullName -File }
            foreach ($file in $search) {
                if ($file.Extension -in @(".exe", ".dll", ".msi")) {
                    $files.Add($file.FullName)
                }
            }
        } elseif ($item.Extension -in @(".exe", ".dll", ".msi")) {
            $files.Add($item.FullName)
        }
    }

    return $files | Sort-Object -Unique
}

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint) -and [string]::IsNullOrWhiteSpace($PfxPath)) {
    throw "Provide -CertificateThumbprint or -PfxPath, or set OTM_SIGN_CERT_THUMBPRINT / OTM_SIGN_PFX_PATH."
}

if (-not [string]::IsNullOrWhiteSpace($PfxPath) -and -not (Test-Path $PfxPath)) {
    throw "PFX file not found: $PfxPath"
}

$signTool = Find-SignTool
$files = Resolve-SignableFiles -InputPaths $Path -Recursive:$Recurse
if (-not $files) {
    Write-Host "No signable files found."
    exit 0
}

foreach ($file in $files) {
    $args = @("sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $TimestampUrl)

    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
        $args += "/f"
        $args += $PfxPath
        if (-not [string]::IsNullOrWhiteSpace($PfxPassword)) {
            $args += "/p"
            $args += $PfxPassword
        }
    } else {
        $args += "/sha1"
        $args += $CertificateThumbprint
        if ($CertificateStore -eq "LocalMachine") {
            $args += "/sm"
        }
    }

    $args += $file
    & $signTool @args
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $file"
    }

    Write-Host "Signed $file"
}

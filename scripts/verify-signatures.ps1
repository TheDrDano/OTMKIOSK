param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [switch]$Recurse
)

$ErrorActionPreference = "Stop"

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

$hasInvalid = $false
$files = Resolve-SignableFiles -InputPaths $Path -Recursive:$Recurse
foreach ($file in $files) {
    $signature = Get-AuthenticodeSignature -FilePath $file
    [PSCustomObject]@{
        Path = $file
        Status = $signature.Status
        Subject = $signature.SignerCertificate.Subject
        Thumbprint = $signature.SignerCertificate.Thumbprint
    }

    if ($signature.Status -ne "Valid") {
        $hasInvalid = $true
    }
}

if ($hasInvalid) {
    exit 1
}

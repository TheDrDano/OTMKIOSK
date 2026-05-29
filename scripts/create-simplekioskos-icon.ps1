param(
    [string]$SourcePng = "$PSScriptRoot\..\branding\simplekioskos_app_icon.png",
    [string]$BrandingIcon = "$PSScriptRoot\..\branding\simplekioskos.ico",
    [string]$ShellIcon = "$PSScriptRoot\..\src\OTM.KioskShell\Assets\simplekioskos.ico",
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256),
    [double]$PaddingRatio = 0.04
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SourcePng)) {
    $fallback = "$PSScriptRoot\..\branding\simplekioskos.png"
    if (Test-Path $fallback) {
        $SourcePng = $fallback
    } else {
        throw "Source PNG not found: $SourcePng"
    }
}

Add-Type -AssemblyName System.Drawing

function New-IconBitmap {
    param(
        [System.Drawing.Image]$Source,
        [int]$Size,
        [double]$PaddingRatio
    )

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $padding = [Math]::Max(1, [int][Math]::Round($Size * $PaddingRatio))
        $maxWidth = $Size - ($padding * 2)
        $maxHeight = $Size - ($padding * 2)
        $scale = [Math]::Min($maxWidth / $Source.Width, $maxHeight / $Source.Height)
        $width = [Math]::Max(1, [int][Math]::Round($Source.Width * $scale))
        $height = [Math]::Max(1, [int][Math]::Round($Source.Height * $scale))
        $x = [int][Math]::Round(($Size - $width) / 2)
        $y = [int][Math]::Round(($Size - $height) / 2)

        $graphics.DrawImage($Source, $x, $y, $width, $height)
        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

function ConvertTo-IconPng {
    param(
        [System.Drawing.Bitmap]$Bitmap
    )

    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,([byte[]]$stream.ToArray())
    }
    finally {
        $stream.Dispose()
    }
}

function Save-MultiSizeIcon {
    param(
        [array]$Frames,
        [string]$Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = New-Object System.IO.BinaryWriter $stream
    try {
        $writer.Write([UInt16]0)                # reserved
        $writer.Write([UInt16]1)                # icon
        $writer.Write([UInt16]$Frames.Count)

        $offset = 6 + ($Frames.Count * 16)
        foreach ($frame in $Frames) {
            $frameBytes = [byte[]]$frame.Bytes
            $sizeByte = if ($frame.Size -eq 256) { 0 } else { [byte]$frame.Size }
            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]0)              # colors
            $writer.Write([byte]0)              # reserved
            $writer.Write([UInt16]1)            # planes
            $writer.Write([UInt16]32)           # bpp
            $writer.Write([UInt32]$frameBytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frameBytes.Length
        }

        foreach ($frame in $Frames) {
            $frameBytes = [byte[]]$frame.Bytes
            $writer.Write($frameBytes, 0, $frameBytes.Length)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$resolvedSource = Resolve-Path $SourcePng
$source = [System.Drawing.Image]::FromFile($resolvedSource)
$bitmaps = @()
try {
    $frames = foreach ($size in ($Sizes | Sort-Object -Unique)) {
        if ($size -lt 16 -or $size -gt 256) {
            throw "Icon size must be between 16 and 256: $size"
        }

        $bitmap = New-IconBitmap -Source $source -Size $size -PaddingRatio $PaddingRatio
        $bitmaps += $bitmap
        [pscustomobject]@{
            Size = $size
            Bytes = [byte[]](ConvertTo-IconPng -Bitmap $bitmap)
        }
    }

    Save-MultiSizeIcon -Frames $frames -Path $BrandingIcon
    Copy-Item -LiteralPath $BrandingIcon -Destination $ShellIcon -Force
}
finally {
    foreach ($bitmap in $bitmaps) {
        $bitmap.Dispose()
    }

    $source.Dispose()
}

Write-Host "Created multi-size icon: $BrandingIcon"
Write-Host "Copied icon:            $ShellIcon"
Write-Host "Frames:                 $($Sizes -join ', ')"

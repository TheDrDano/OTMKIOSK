param(
    [string]$SourcePng = "$PSScriptRoot\..\branding\simplekioskos_bottom.png",
    [string]$BrandingIcon = "$PSScriptRoot\..\branding\simplekioskos.ico",
    [string]$ShellIcon = "$PSScriptRoot\..\src\OTM.KioskShell\Assets\simplekioskos.ico"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SourcePng)) {
    throw "Source PNG not found: $SourcePng"
}

Add-Type -AssemblyName System.Drawing

function Convert-ToIconPngFrame {
    param(
        [System.Drawing.Image]$Source,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $padding = [Math]::Max(2, [int]($Size * 0.08))
        $maxWidth = $Size - ($padding * 2)
        $maxHeight = $Size - ($padding * 2)
        $scale = [Math]::Min($maxWidth / $Source.Width, $maxHeight / $Source.Height)
        $width = [int]($Source.Width * $scale)
        $height = [int]($Source.Height * $scale)
        $x = [int](($Size - $width) / 2)
        $y = [int](($Size - $height) / 2)
        $graphics.DrawImage($Source, $x, $y, $width, $height)

        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-IconFile {
    param(
        [string]$Path,
        [byte[][]]$Frames,
        [int[]]$Sizes
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $file = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = New-Object System.IO.BinaryWriter $file
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$Frames.Count)

        $offset = 6 + (16 * $Frames.Count)
        for ($i = 0; $i -lt $Frames.Count; $i++) {
            $sizeByte = if ($Sizes[$i] -eq 256) { 0 } else { $Sizes[$i] }
            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$Frames[$i].Length)
            $writer.Write([UInt32]$offset)
            $offset += $Frames[$i].Length
        }

        foreach ($frame in $Frames) {
            $writer.Write($frame)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng))
try {
    $sizes = @(256, 128, 64, 48, 32, 16)
    $frames = foreach ($size in $sizes) {
        Convert-ToIconPngFrame -Source $source -Size $size
    }

    Write-IconFile -Path $BrandingIcon -Frames $frames -Sizes $sizes
    Copy-Item -LiteralPath $BrandingIcon -Destination $ShellIcon -Force
}
finally {
    $source.Dispose()
}

Write-Host "Created icon: $BrandingIcon"
Write-Host "Copied icon:  $ShellIcon"

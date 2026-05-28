param(
    [string]$SourcePng = "$PSScriptRoot\..\branding\simplekioskos_app_icon.png",
    [string]$BrandingIcon = "$PSScriptRoot\..\branding\simplekioskos.ico",
    [string]$ShellIcon = "$PSScriptRoot\..\src\OTM.KioskShell\Assets\simplekioskos.ico"
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
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeIconMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

function New-IconBitmap {
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

        $padding = [Math]::Max(3, [int]($Size * 0.10))
        $maxWidth = $Size - ($padding * 2)
        $maxHeight = $Size - ($padding * 2)
        $scale = [Math]::Min($maxWidth / $Source.Width, $maxHeight / $Source.Height)
        $width = [int]($Source.Width * $scale)
        $height = [int]($Source.Height * $scale)
        $x = [int](($Size - $width) / 2)
        $y = [int](($Size - $height) / 2)

        $graphics.DrawImage($Source, $x, $y, $width, $height)
        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

function Save-CompilerSafeIcon {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $handle = $Bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($handle)
        $clone = [System.Drawing.Icon]$icon.Clone()
        try {
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
            try {
                $clone.Save($stream)
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $clone.Dispose()
            $icon.Dispose()
        }
    }
    finally {
        [NativeIconMethods]::DestroyIcon($handle) | Out-Null
    }
}

$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng))
try {
    # MSBuild's Win32 resource compiler is stricter than Windows Explorer.
    # A classic single-frame icon produced by System.Drawing.Icon.Save is accepted by csc.
    $bitmap = New-IconBitmap -Source $source -Size 64
    try {
        Save-CompilerSafeIcon -Bitmap $bitmap -Path $BrandingIcon
        Copy-Item -LiteralPath $BrandingIcon -Destination $ShellIcon -Force
    }
    finally {
        $bitmap.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "Created icon: $BrandingIcon"
Write-Host "Copied icon:  $ShellIcon"

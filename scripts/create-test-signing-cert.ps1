param(
    [string]$Subject = "CN=SimpleKioskOS Test Publisher",
    [string]$OutputPath = "$PSScriptRoot\..\artifacts\signing\simplekioskos-test.pfx",
    [Parameter(Mandatory = $true)]
    [string]$Password
)

$ErrorActionPreference = "Stop"

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears(3)

$directory = Split-Path $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $securePassword | Out-Null

Write-Host "Created test code-signing certificate."
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "PFX: $OutputPath"
Write-Host "Install this certificate into Trusted Root and Trusted Publishers on test machines only."

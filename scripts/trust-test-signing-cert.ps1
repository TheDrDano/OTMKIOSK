param(
    [Parameter(Mandatory = $true)]
    [string]$PfxPath,
    [Parameter(Mandatory = $true)]
    [string]$Password
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PfxPath)) {
    throw "PFX file not found: $PfxPath"
}

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
$cert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "Cert:\CurrentUser\TrustedPublisher" -Password $securePassword
Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "Cert:\CurrentUser\Root" -Password $securePassword | Out-Null

Write-Host "Trusted test certificate for current user."
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "Use this only on test machines you control."

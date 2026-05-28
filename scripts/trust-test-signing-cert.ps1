param(
    [Parameter(Mandatory = $true)]
    [string]$PfxPath,
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [switch]$LocalMachine
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PfxPath)) {
    throw "PFX file not found: $PfxPath"
}

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
$storeRoot = if ($LocalMachine) { "Cert:\LocalMachine" } else { "Cert:\CurrentUser" }
$cert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "$storeRoot\TrustedPublisher" -Password $securePassword
Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "$storeRoot\Root" -Password $securePassword | Out-Null

Write-Host "Trusted test certificate in $storeRoot\TrustedPublisher and $storeRoot\Root."
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "Use this only on test machines you control."

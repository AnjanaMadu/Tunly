#Requires -RunAsAdministrator
<#
    Installs the signed MSIX package.
    The release is signed with a per-release self-signed certificate, so the
    certificate is trusted in LocalMachine\TrustedPeople before sideloading.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$package = Get-ChildItem $root -Filter 'Tunly-*-windows-x64.msix' -File | Select-Object -First 1
if (-not $package) {
    $package = Get-ChildItem $root -Filter 'Tunly_1.0.0.0_x64.msix' -File | Select-Object -First 1
}
if (-not $package) { throw 'Tunly MSIX package was not found next to this script.' }

$certificate = Get-ChildItem $root -Filter 'Tunly-*-certificate.cer' -File | Select-Object -First 1
if (-not $certificate) {
    $certificate = Get-ChildItem $root -Filter 'Tunly.cer' -File | Select-Object -First 1
}
if (-not $certificate) { throw 'Tunly certificate (.cer) was not found next to this script.' }

Import-Certificate -FilePath $certificate.FullName -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
Add-AppxPackage -Path $package.FullName

Write-Host ''
Write-Host 'Tunly was installed as an MSIX package.' -ForegroundColor Green
Write-Host 'Note: MSIX apps start unelevated, so TUN mode reports that administrator'
Write-Host 'rights are required. Use the portable installer for full TUN support.'

#Requires -RunAsAdministrator
<#
    Removes the portable install and the MSIX package, if present.
    Profiles and settings in %LOCALAPPDATA%\Tunly are kept.
#>

$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\Tunly'

if (Test-Path $destination) {
    Remove-Item $destination -Recurse -Force
    Write-Host "Removed $destination"
}

$shortcuts = @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Tunly.lnk'),
    (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Tunly.lnk')
)

foreach ($shortcut in $shortcuts) {
    if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
}

$package = Get-AppxPackage -Name '94d72da9-ad55-40fa-95df-9a42ca75589d' -ErrorAction SilentlyContinue
if ($package) {
    $package | Remove-AppxPackage
    Write-Host 'Removed the Tunly MSIX package.'
}

Write-Host "Profiles and settings in $env:LOCALAPPDATA\Tunly were left untouched."

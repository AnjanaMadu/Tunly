#Requires -RunAsAdministrator
<#
    Installs the portable Release build of Tunly.
    The executable requests administrator rights (needed for the TUN adapter),
    so Windows shows a UAC prompt when it is launched.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$zip = Get-ChildItem $root -Filter 'Tunly-*-windows-x64-portable.zip' -File | Select-Object -First 1
if (-not $zip) {
    $zip = Get-ChildItem $root -Filter 'Tunly-portable-x64.zip' -File | Select-Object -First 1
}
if (-not $zip) {
    throw 'Portable ZIP not found. Put Install-Portable.ps1 next to Tunly-*-windows-x64-portable.zip.'
}

$destination = Join-Path $env:LOCALAPPDATA 'Programs\Tunly'

if (Test-Path $destination) {
    Remove-Item $destination -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $destination | Out-Null
Expand-Archive -Path $zip.FullName -DestinationPath $destination -Force

$exe = Join-Path $destination 'Tunly.exe'
if (-not (Test-Path $exe)) {
    throw 'Tunly.exe was not found after extraction.'
}

$shell = New-Object -ComObject WScript.Shell
$locations = @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'),
    ([Environment]::GetFolderPath('CommonDesktopDirectory'))
)

foreach ($location in $locations) {
    if (-not (Test-Path $location)) { continue }

    $link = $shell.CreateShortcut((Join-Path $location 'Tunly.lnk'))
    $link.TargetPath = $exe
    $link.WorkingDirectory = $destination
    $link.IconLocation = "$exe,0"
    $link.Description = 'Tunly - sing-box client'
    $link.Save()
}

Write-Host ''
Write-Host "Tunly installed to: $destination" -ForegroundColor Green
Write-Host 'Start Menu and desktop shortcuts were created.'
Write-Host 'Launching Tunly prompts for administrator rights (required for TUN mode).'

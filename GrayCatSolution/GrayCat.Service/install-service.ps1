<#
.SYNOPSIS
    Publishes GrayCat.Service and registers it as a Windows service that starts automatically.
.DESCRIPTION
    Run from an elevated (Administrator) PowerShell:
        .\install-service.ps1                 # install to C:\Program Files\GrayCat\Service
        .\install-service.ps1 -Uninstall      # stop and remove the service
    The editor works without the service too; it only speeds up generation and lets other tools use the API.
#>
param(
    [string]$InstallDir = "$env:ProgramFiles\GrayCat\Service",
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$serviceName = 'GrayCatGenerationService'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell started as Administrator.'
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping and removing the existing $serviceName..."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

if ($Uninstall) {
    Write-Host 'GrayCat service removed.'
    return
}

$packaged = Join-Path $PSScriptRoot 'GrayCat.Service.exe'
if (Test-Path $packaged) {
    # Running from a release package: the service is already built.
    Write-Host "Copying GrayCat.Service to $InstallDir..."
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot '*') $InstallDir -Recurse -Force
} else {
    # Running from the source tree.
    Write-Host "Publishing GrayCat.Service to $InstallDir..."
    dotnet publish "$PSScriptRoot\GrayCat.Service.csproj" -c Release -o $InstallDir --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

$exe = Join-Path $InstallDir 'GrayCat.Service.exe'
New-Service -Name $serviceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName 'GrayCat Generation Service' `
    -Description 'Generates web and mobile projects for GrayCat Studio (http://localhost:5123).' `
    -StartupType Automatic | Out-Null

# Run with minimal rights: it only needs to write into the folders users choose for their projects.
# (Running as LocalSystem would let any local program write files anywhere with SYSTEM rights.)
sc.exe config $serviceName obj= 'NT AUTHORITY\LocalService' password= '' | Out-Null

# Restart automatically if it crashes.
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

Start-Service -Name $serviceName
Start-Sleep -Seconds 2
try {
    $health = Invoke-RestMethod 'http://localhost:5123/api/generation/health' -TimeoutSec 5
    Write-Host "GrayCat service is running (version $($health.version))."
} catch {
    Write-Warning "The service was installed but did not answer yet. Logs: C:\Windows\ServiceProfiles\LocalService\AppData\Roaming\CatSuite\GrayCat\Logs"
}

<#
.SYNOPSIS
    Builds a release package of GrayCat Studio: artifacts\GrayCatStudio-<version>-<runtime>.zip
.DESCRIPTION
    The package is self-contained (no .NET install needed on the target PC):
      Studio\GrayCat.UI.exe        - the editor
      Service\GrayCat.Service.exe  - optional background service (+ install-service.ps1)
      README.md
    Node.js is still needed on the target PC to run React / Next.js / Node / Expo projects.
#>
param(
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'GrayCat.UI\GrayCat.UI.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Set <Version> in GrayCat.UI.csproj.' }

$name = "GrayCatStudio-$version-$Runtime"
$out = Join-Path $root "artifacts\$name"
$zip = "$out.zip"
if (Test-Path $out) { throw "$out already exists. Delete it (or bump the version) and run again." }

if (-not $SkipTests) {
    Write-Host '== Running tests'
    dotnet test (Join-Path $root 'GrayCatSolution.sln') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

Write-Host "== Publishing editor ($Runtime)"
dotnet publish (Join-Path $root 'GrayCat.UI\GrayCat.UI.csproj') -c Release -r $Runtime --self-contained true -o (Join-Path $out 'Studio') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publishing the editor failed.' }

Write-Host "== Publishing service ($Runtime)"
dotnet publish (Join-Path $root 'GrayCat.Service\GrayCat.Service.csproj') -c Release -r $Runtime --self-contained true -o (Join-Path $out 'Service') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publishing the service failed.' }

Copy-Item (Join-Path $root 'GrayCat.Service\install-service.ps1') (Join-Path $out 'Service')
Copy-Item (Join-Path $root 'README.md') $out

if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "== Done: $zip ($size MB)"

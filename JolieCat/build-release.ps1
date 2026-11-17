# Builds a JolieCat release: runs the tests, publishes one self-contained JolieCat.exe
# (64-bit Windows 10/11, nothing to install first) and packs it with the guides into
# artifacts\JolieCat-<version>-win-x64.zip.
#
#   powershell -File build-release.ps1              # full release
#   powershell -File build-release.ps1 -SkipTests   # skip the tests (not for a real release)
#
# The version comes from Directory.Build.props.
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw 'No <Version> in Directory.Build.props' }
Write-Host "JolieCat $version" -ForegroundColor Yellow

if (-not $SkipTests) {
    Write-Host 'Running the tests...' -ForegroundColor Cyan
    dotnet test JolieCat.slnx -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed: no release built.' }
}

Write-Host 'Publishing...' -ForegroundColor Cyan
$publish = Join-Path $root 'artifacts\publish\win-x64'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish JolieCat.UI\JolieCat.UI.csproj -p:PublishProfile=win-x64 -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# The package: the exe (named JolieCat.exe), the README and the guides with their pictures.
$name = "JolieCat-$version-win-x64"
$stage = Join-Path $root "artifacts\$name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'docs') | Out-Null
Copy-Item (Join-Path $publish 'JolieCat.UI.exe') (Join-Path $stage 'JolieCat.exe')
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'CHANGELOG.md') $stage
if (Test-Path (Join-Path $root 'LICENSE')) { Copy-Item (Join-Path $root 'LICENSE') $stage }
Copy-Item (Join-Path $root 'docs\*') (Join-Path $stage 'docs') -Recurse

$zip = Join-Path $root "artifacts\$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Set-Content -Path "$zip.sha256" -Value "$hash  $name.zip" -Encoding ascii
$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Done: $zip ($sizeMb MB)" -ForegroundColor Green
Write-Host "SHA256: $hash"

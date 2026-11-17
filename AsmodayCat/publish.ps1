# Builds the release: self-contained app folder + MSI installer.
#   powershell -ExecutionPolicy Bypass -File publish.ps1            # app + MSI
#   powershell -ExecutionPolicy Bypass -File publish.ps1 -NoMsi     # app folder only
# Output:
#   publish\AsmodayCat\      — AsmodayCat.UI.exe + AsmodayCat.Service.exe (no .NET install needed)
#   publish\AsmodayCat.msi   — per-user installer (no admin rights), used by CatSuite.Installer
param(
    [string]$Version = "4.0.0",
    [switch]$NoMsi
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out  = Join-Path $root "publish\AsmodayCat"

Write-Host "== Tests" -ForegroundColor Cyan
dotnet test (Join-Path $root "AsmodayCat.Tests\AsmodayCat.Tests.csproj") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

# Both apps go into one folder: the UI finds the service next to itself
foreach ($project in "AsmodayCat.Service", "AsmodayCat.UI") {
    Write-Host "== Publish $project" -ForegroundColor Cyan
    dotnet publish (Join-Path $root "$project\$project.csproj") -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:PublishReadyToRun=false -o $out --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Publish of $project failed" }
}

# Development-only settings don't belong in the release
Remove-Item (Join-Path $out "appsettings.Development.json") -ErrorAction SilentlyContinue
Get-ChildItem $out -Filter *.pdb | Remove-Item

$size = (Get-ChildItem $out -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("App folder: {0} ({1:N0} MB)" -f $out, $size) -ForegroundColor Green

if (-not $NoMsi) {
    Write-Host "== MSI" -ForegroundColor Cyan
    dotnet build (Join-Path $root "AsmodayCat.Setup\AsmodayCat.Setup.wixproj") -c Release -p:ProductVersion=$Version --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

    $msi = Get-ChildItem (Join-Path $root "AsmodayCat.Setup\bin") -Recurse -Filter AsmodayCat.msi | Sort-Object LastWriteTime | Select-Object -Last 1
    $target = Join-Path $root "publish\AsmodayCat.msi"
    Copy-Item $msi.FullName $target -Force
    $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("MSI: {0} ({1:N0} MB)" -f $target, ((Get-Item $target).Length / 1MB)) -ForegroundColor Green
    Write-Host "SHA256: $hash"
    Set-Content (Join-Path $root "publish\AsmodayCat.msi.sha256") $hash -Encoding ascii
}

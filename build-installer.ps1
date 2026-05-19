#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes Sport Splitter and compiles the Inno Setup installer.

.PARAMETER Version
    Optional. Overrides the version in the .csproj and .iss (e.g. "1.2.0").
    Defaults to the <Version> in SportSplitter.csproj.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.1.0
#>
param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root      = $PSScriptRoot
$csproj    = Join-Path $root "SportSplitter.csproj"
$issScript = Join-Path $root "installer\SportSplitter.iss"
$publishDir = Join-Path $root "publish"
$outputDir  = Join-Path $root "installer\output"

# ── Resolve version ───────────────────────────────────────────────────────────
if (-not $Version) {
    [xml]$proj = Get-Content $csproj
    $Version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}
Write-Host "Building version $Version" -ForegroundColor Cyan

# ── Patch version in .iss ─────────────────────────────────────────────────────
$iss = Get-Content $issScript -Raw
$iss = $iss -replace '#define AppVersion\s+"[^"]*"', "#define AppVersion   `"$Version`""
Set-Content $issScript $iss -Encoding UTF8
Write-Host "Patched installer version to $Version"

# ── dotnet publish ────────────────────────────────────────────────────────────
Write-Host "`nPublishing..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    --output $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Published to $publishDir" -ForegroundColor Green

# ── Find Inno Setup compiler ──────────────────────────────────────────────────
$iscc = $null
$candidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
)
foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) { $iscc = $c; break }
}

if (-not $iscc) {
    Write-Host "`nInno Setup not found. Download from https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "Publish output is ready at: $publishDir"
    exit 0
}

# ── Compile installer ─────────────────────────────────────────────────────────
Write-Host "`nCompiling installer..." -ForegroundColor Cyan
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

& $iscc $issScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

$installer = Get-ChildItem $outputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "`nInstaller ready: $($installer.FullName)" -ForegroundColor Green

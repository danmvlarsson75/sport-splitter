#Requires -Version 5.1
<#
.SYNOPSIS
    Bumps the patch version, publishes Sport Splitter, compiles the installer,
    commits the version bump, tags the commit, and creates a GitHub Release.

.PARAMETER Version
    Optional. Explicit version to use (e.g. "2.0.0"). Skips auto-increment.

.PARAMETER CurrentVersion
    Build using the version already recorded in SportSplitter.csproj.

.PARAMETER BumpMinor
    Bump the minor version instead of patch (e.g. 1.0.x -> 1.1.0).

.PARAMETER BumpMajor
    Bump the major version instead of patch (e.g. 1.x.x -> 2.0.0).

.PARAMETER NoRelease
    Build the publish output and installer only. Skips git commit, tag, push,
    and GitHub release creation.

.EXAMPLE
    .\build-installer.ps1                  # auto-increment patch: 1.0.0 -> 1.0.1
    .\build-installer.ps1 -CurrentVersion  # build/release current csproj version
    .\build-installer.ps1 -CurrentVersion -NoRelease  # build installer only
    .\build-installer.ps1 -BumpMinor       # 1.0.1 -> 1.1.0
    .\build-installer.ps1 -BumpMajor       # 1.1.0 -> 2.0.0
    .\build-installer.ps1 -Version 3.0.0   # explicit version
#>
param(
    [string]$Version   = "",
    [switch]$CurrentVersion,
    [switch]$BumpMinor,
    [switch]$BumpMajor,
    [switch]$NoRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root       = $PSScriptRoot
$csproj     = Join-Path $root "SportSplitter.csproj"
$issScript  = Join-Path $root "installer\SportSplitter.iss"
$publishDir = Join-Path $root "publish"
$outputDir  = Join-Path $root "installer\output"

# ── Read current version from csproj ─────────────────────────────────────────
[xml]$proj = Get-Content $csproj
$current = $proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $current) { $current = "1.0.0" }

# ── Resolve new version ───────────────────────────────────────────────────────
if ($CurrentVersion -and ($Version -or $BumpMinor -or $BumpMajor)) {
    throw "-CurrentVersion cannot be combined with -Version, -BumpMinor, or -BumpMajor."
}
if ($BumpMinor -and $BumpMajor) {
    throw "Use only one version bump switch at a time."
}

if ($CurrentVersion) {
    $Version = $current
}
elseif (-not $Version) {
    $parts = $current -split '\.'
    $major = [int]$parts[0]
    $minor = if ($parts.Count -gt 1) { [int]$parts[1] } else { 0 }
    $patch = if ($parts.Count -gt 2) { [int]$parts[2] } else { 0 }

    if ($BumpMajor)      { $major++; $minor = 0; $patch = 0 }
    elseif ($BumpMinor)  { $minor++; $patch = 0 }
    else                 { $patch++ }

    $Version = "$major.$minor.$patch"
}

Write-Host "Version: $current -> $Version" -ForegroundColor Cyan

# ── dotnet clean + publish ────────────────────────────────────────────────────
# Kill any running SportSplitter so publish/ files aren't locked
Get-Process -Name "SportSplitter" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "`nCleaning..." -ForegroundColor Cyan
dotnet clean $csproj --configuration Release | Out-Null

Write-Host "Publishing..." -ForegroundColor Cyan
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
$isccCmd = Get-Command iscc -ErrorAction SilentlyContinue
$candidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    $(if ($isccCmd) { $isccCmd.Source } else { $null })
)
foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) { $iscc = $c; break }
}

if (-not $iscc) {
    Write-Host "`nInno Setup not found. Download from https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "Publish output is ready at: $publishDir"
    exit 0
}

# ── Patch .iss version ────────────────────────────────────────────────────────
$iss = Get-Content $issScript -Raw
$updatedIss = $iss -replace '#define AppVersion\s+"[^"]*"', "#define AppVersion   `"$Version`""
if ($updatedIss -ne $iss) {
    Set-Content $issScript $updatedIss -Encoding UTF8
}

# ── Compile installer ─────────────────────────────────────────────────────────
Write-Host "`nCompiling installer..." -ForegroundColor Cyan
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

& $iscc $issScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

$installer = Get-ChildItem $outputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "`nInstaller ready: $($installer.FullName)" -ForegroundColor Green

# ── Write version to csproj (only after successful build) ────────────────────
$csprojContent = Get-Content $csproj -Raw
$updatedCsprojContent = $csprojContent -replace '<Version>[^<]*</Version>',                   "<Version>$Version</Version>"
$updatedCsprojContent = $updatedCsprojContent -replace '<AssemblyVersion>[^<]*</AssemblyVersion>',   "<AssemblyVersion>$Version.0</AssemblyVersion>"
$updatedCsprojContent = $updatedCsprojContent -replace '<FileVersion>[^<]*</FileVersion>',            "<FileVersion>$Version.0</FileVersion>"
if ($updatedCsprojContent -ne $csprojContent) {
    Set-Content $csproj $updatedCsprojContent -Encoding UTF8
}

if ($NoRelease) {
    Write-Host "`n-NoRelease specified; skipping git commit, tag, push, and GitHub Release." -ForegroundColor Yellow
    exit 0
}

# ── Commit version bump, tag, and GitHub Release ──────────────────────────────
Write-Host "`nCommitting version bump..." -ForegroundColor Cyan

$tag = "v$Version"

git add "SportSplitter.csproj" "installer\SportSplitter.iss"
git diff --cached --quiet -- "SportSplitter.csproj" "installer\SportSplitter.iss"
$hasVersionChanges = ($LASTEXITCODE -ne 0)

if ($hasVersionChanges) {
    git commit -m "chore: bump version to $Version"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed" }

    $branch = (git branch --show-current).Trim()
    git push origin $branch
    if ($LASTEXITCODE -ne 0) { throw "git push failed" }
}
else {
    Write-Host "No version metadata changes to commit." -ForegroundColor Yellow
}

if (-not (git tag --list $tag)) {
    git tag $tag
    if ($LASTEXITCODE -ne 0) { throw "git tag failed" }
}
else {
    Write-Host "Tag $tag already exists locally." -ForegroundColor Yellow
}

git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push tag failed" }

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    gh release view $tag *> $null
    $releaseExists = ($LASTEXITCODE -eq 0)
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($releaseExists) {
    Write-Host "Updating GitHub Release $tag..." -ForegroundColor Cyan
    gh release upload $tag "$($installer.FullName)#SportSplitter-$Version-Setup.exe" --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    gh release edit $tag --latest
    if ($LASTEXITCODE -ne 0) { throw "gh release edit failed" }
}
else {
    Write-Host "Creating GitHub Release $tag..." -ForegroundColor Cyan
    gh release create $tag "$($installer.FullName)#SportSplitter-$Version-Setup.exe" `
        --title "Sport Splitter $tag" `
        --notes "Release $tag" `
        --latest
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
}

Write-Host "`nRelease $tag published." -ForegroundColor Green

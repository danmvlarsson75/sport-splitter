#Requires -Version 5.1
<#
.SYNOPSIS
    Bumps the patch version, publishes Sport Splitter, packages it with
    Velopack (auto-updating installer), commits the version bump, tags the
    commit, and uploads the release to GitHub.

    Installed copies check GitHub Releases on startup and update themselves,
    so publishing here is all that's needed to roll out a new version.

.PARAMETER Version
    Optional. Explicit version to use (e.g. "2.0.0"). Skips auto-increment.

.PARAMETER CurrentVersion
    Build using the version already recorded in SportSplitter.csproj.

.PARAMETER BumpMinor
    Bump the minor version instead of patch (e.g. 1.0.x -> 1.1.0).

.PARAMETER BumpMajor
    Bump the major version instead of patch (e.g. 1.x.x -> 2.0.0).

.PARAMETER NoRelease
    Build the publish output and Velopack package only. Skips git commit,
    tag, push, and the GitHub release upload.

.EXAMPLE
    .\build-installer.ps1                  # auto-increment patch: 1.0.0 -> 1.0.1
    .\build-installer.ps1 -CurrentVersion  # build/release current csproj version
    .\build-installer.ps1 -CurrentVersion -NoRelease  # build package only
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

$root        = $PSScriptRoot
$csproj      = Join-Path $root "SportSplitter.csproj"
$publishDir  = Join-Path $root "publish"
$releasesDir = Join-Path $root "Releases"
$repoUrl     = "https://github.com/danmvlarsson75/sport-splitter"

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

# Note: no PublishSingleFile — Velopack packages the loose files itself and
# single-file bundles defeat its delta updates.
dotnet publish $csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    --output $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Published to $publishDir" -ForegroundColor Green

# ── Velopack: fetch previous release (enables delta packages) ────────────────
if (Test-Path $releasesDir) { Remove-Item $releasesDir -Recurse -Force }

Write-Host "`nDownloading previous release for delta generation..." -ForegroundColor Cyan
vpk download github --repoUrl $repoUrl -o $releasesDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "No previous Velopack release found (first release or offline); full package only." -ForegroundColor Yellow
}

# ── Velopack: pack ────────────────────────────────────────────────────────────
Write-Host "`nPacking with Velopack..." -ForegroundColor Cyan
vpk pack `
    --packId SportSplitter `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe SportSplitter.exe `
    --packTitle "Sport Splitter" `
    --packAuthors "Dan Larsson" `
    --icon (Join-Path $root "icons\app.ico") `
    --runtime win-x64 `
    --framework net8-x64-desktop `
    -o $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

$setup = Get-ChildItem $releasesDir -Filter "*-Setup.exe" | Select-Object -First 1
Write-Host "`nInstaller ready: $($setup.FullName)" -ForegroundColor Green

# ── Write version to csproj (only after successful build) ────────────────────
$csprojContent = Get-Content $csproj -Raw
$updatedCsprojContent = $csprojContent -replace '<Version>[^<]*</Version>',                   "<Version>$Version</Version>"
$updatedCsprojContent = $updatedCsprojContent -replace '<AssemblyVersion>[^<]*</AssemblyVersion>',   "<AssemblyVersion>$Version.0</AssemblyVersion>"
$updatedCsprojContent = $updatedCsprojContent -replace '<FileVersion>[^<]*</FileVersion>',            "<FileVersion>$Version.0</FileVersion>"
if ($updatedCsprojContent -ne $csprojContent) {
    Set-Content $csproj $updatedCsprojContent -Encoding UTF8
}

if ($NoRelease) {
    Write-Host "`n-NoRelease specified; skipping git commit, tag, push, and GitHub release." -ForegroundColor Yellow
    exit 0
}

# ── Commit version bump, tag, and upload release ──────────────────────────────
Write-Host "`nCommitting version bump..." -ForegroundColor Cyan

$tag = "v$Version"

git add "SportSplitter.csproj"
git diff --cached --quiet -- "SportSplitter.csproj"
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

# vpk uploads the Setup exe, full/delta packages, and the update feed the
# installed apps poll. Must be published (not draft) for updates to be found.
Write-Host "`nUploading release $tag to GitHub..." -ForegroundColor Cyan
$token = (gh auth token).Trim()
vpk upload github `
    --repoUrl $repoUrl `
    --token $token `
    --publish true `
    --merge true `
    --releaseName "Sport Splitter $tag" `
    --tag $tag `
    -o $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }

Write-Host "`nRelease $tag published. Installed copies will auto-update." -ForegroundColor Green

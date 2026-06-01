param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$Version = "1.4.3",
    [switch]$SkipV142
)

$ErrorActionPreference = "Stop"

function Assert-PathExists([string]$Path, [string]$Description) {
    if (-not (Test-Path $Path)) {
        throw "$Description not found: $Path"
    }
}

function Assert-NoNetworkCommand([string]$Path) {
    $text = Get-Content -Path $Path -Raw
    $networkPatterns = @(
        "Invoke-RestMethod",
        "Invoke-WebRequest",
        "curl ",
        "gh release",
        "git push",
        "git ls-remote"
    )

    foreach ($pattern in $networkPatterns) {
        if ($text -match [regex]::Escape($pattern)) {
            throw "Smoke target should not require network but contains '$pattern': $Path"
        }
    }
}

function Assert-NoAuditSensitiveExample([string]$Path) {
    $text = Get-Content -Path $Path -Raw
    $blockedPatterns = @(
        (-join ([char[]](86,97,108,116,101,114,83,97,108,118,97,100,111,114))),
        (-join ([char[]](67,58,92,82,101,112,111,115,105,116,111,114,105,101,115))),
        (-join ([char[]](83,118,97,108,97)))
    )

    foreach ($pattern in $blockedPatterns) {
        if ($text -match [regex]::Escape($pattern)) {
            throw "Tracked release/update file contains audit-sensitive example '$pattern': $Path"
        }
    }
}

$scripts = @(
    "scripts/Build-Release.ps1",
    "scripts/Upload-ReleaseAssets.ps1",
    "scripts/airepo-update.cmd",
    "scripts/airepo-update.ps1",
    "scripts/airepo-update.sh",
    "tests/v1.4.2-smoke.ps1"
)

foreach ($script in $scripts) {
    Assert-PathExists (Join-Path $RepoRoot $script) $script
}

$unixUpdater = Join-Path $RepoRoot "scripts/airepo-update.sh"
$firstLine = Get-Content -Path $unixUpdater -TotalCount 1
if ($firstLine -notlike "#!*") {
    throw "scripts/airepo-update.sh does not start with a shebang."
}

Assert-NoNetworkCommand (Join-Path $RepoRoot "scripts/Build-Release.ps1")

$auditSensitiveFiles = @(
    "README.md",
    "scripts/airepo-update.ps1",
    "scripts/airepo-update.sh",
    "scripts/Upload-ReleaseAssets.ps1",
    "scripts/Validate-And-Tag.ps1"
)

foreach ($file in $auditSensitiveFiles) {
    Assert-NoAuditSensitiveExample (Join-Path $RepoRoot $file)
}

$buildReleaseText = Get-Content -Path (Join-Path $RepoRoot "scripts/Build-Release.ps1") -Raw
$packagedAssetNames = @(
    "AiRepoKit.Cli.",
    "airepo-win-x64.zip",
    "airepo-linux-x64.tar.gz",
    "airepo-linux-arm64.tar.gz",
    "airepo-updater-win.zip",
    "airepo-updater-unix.tar.gz",
    "release-manifest.json"
)

foreach ($assetName in $packagedAssetNames) {
    if ($buildReleaseText -notmatch [regex]::Escape($assetName)) {
        throw "Build-Release.ps1 does not reference expected release asset: $assetName"
    }
}

$buildRelease = Join-Path $RepoRoot "scripts/Build-Release.ps1"
& powershell -NoProfile -ExecutionPolicy Bypass -File $buildRelease -Version $Version -Configuration Debug -SkipAudit -SkipRestore -SkipWindows -SkipLinux
if ($LASTEXITCODE -ne 0) {
    throw "Build-Release.ps1 smoke packaging failed."
}

$releaseDir = Join-Path $RepoRoot "artifacts/release"
$expectedAssets = @(
    "AiRepoKit.Cli.$Version.nupkg",
    "airepo-updater-win.zip",
    "airepo-updater-unix.tar.gz",
    "release-manifest.json"
)

foreach ($asset in $expectedAssets) {
    Assert-PathExists (Join-Path $releaseDir $asset) "Release asset $asset"
}

if (-not $SkipV142) {
    & (Join-Path $RepoRoot "tests/v1.4.2-smoke.ps1") -RepoRoot $RepoRoot
    if ($LASTEXITCODE -ne 0) {
        throw "v1.4.2 smoke test failed."
    }
}

Write-Host "v1.4.3 smoke tests passed."

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Repo = "",

    [string]$ReleaseDir = ""
)

$ErrorActionPreference = "Stop"

function Fail([string]$message) {
    Write-Host ""
    Write-Host "[FAIL] $message" -ForegroundColor Red
    exit 1
}

function RunNative([string]$file, [string[]]$arguments) {
    Write-Host "> $file $($arguments -join ' ')" -ForegroundColor DarkGray
    & $file @arguments
    if ($LASTEXITCODE -ne 0) {
        Fail "Command failed: $file $($arguments -join ' ')"
    }
}

function Convert-GitRemoteToRepo([string]$remoteUrl) {
    if ([string]::IsNullOrWhiteSpace($remoteUrl)) {
        return $null
    }

    $remoteUrl = $remoteUrl.Trim()
    $patterns = @(
        "github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$",
        "^[^:/]+/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$",
        "^(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$"
    )

    foreach ($pattern in $patterns) {
        $match = [regex]::Match($remoteUrl, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            return "$($match.Groups["owner"].Value)/$($match.Groups["repo"].Value)"
        }
    }

    return $null
}

function Resolve-Repo {
    if (-not [string]::IsNullOrWhiteSpace($Repo)) {
        return $Repo.Trim()
    }

    $remote = & git config --get remote.origin.url 2>$null
    if ($LASTEXITCODE -eq 0) {
        $derived = Convert-GitRemoteToRepo ([string]$remote)
        if ($derived) {
            return $derived
        }
    }

    return $null
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($ReleaseDir)) {
    $ReleaseDir = Join-Path $root "artifacts/release"
}

$tag = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
$resolvedRepo = Resolve-Repo
if (-not $resolvedRepo) {
    Fail "Release repository was not provided and could not be derived from git remote origin. Pass -Repo <owner>/<repo>."
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    Fail "GitHub CLI 'gh' was not found on PATH. Install gh and authenticate before uploading release assets."
}

RunNative $gh.Source @("auth", "status")

if (-not (Test-Path $ReleaseDir)) {
    Fail "Release asset directory not found: $ReleaseDir. Run scripts/Build-Release.ps1 first."
}

$assets = @(Get-ChildItem -Path $ReleaseDir -File | Sort-Object Name)
if ($assets.Count -eq 0) {
    Fail "No release assets found in $ReleaseDir. Run scripts/Build-Release.ps1 first."
}

Write-Host ""
Write-Host "Uploading $($assets.Count) release assets to $resolvedRepo $tag"
foreach ($asset in $assets) {
    Write-Host " - $($asset.Name)"
}

$uploadArguments = @("release", "upload", $tag) + @($assets | ForEach-Object { $_.FullName }) + @("--repo", $resolvedRepo, "--clobber")
RunNative $gh.Source $uploadArguments

Write-Host ""
Write-Host "[OK] Uploaded release assets for $tag" -ForegroundColor Green

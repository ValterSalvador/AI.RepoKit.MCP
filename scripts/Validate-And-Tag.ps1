param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Configuration = "Release",

    [string]$CommitMessage = "",

    [switch]$SkipMcpBuild,

    [switch]$SkipSmokeTests,

    [switch]$SkipStrictMcp,

    [switch]$NoAutoStopMcpLocks,

    [switch]$NoCommit,

    [switch]$NoPush,

    [switch]$UploadAssets,

    [string]$ReleaseRepo = ""
)

$ErrorActionPreference = "Stop"

function Fail([string]$message) {
    Write-Host ""
    Write-Host "[FAIL] $message" -ForegroundColor Red
    exit 1
}

function Step([string]$message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function RunNative([string]$file, [string[]]$arguments) {
    Write-Host "> $file $($arguments -join ' ')" -ForegroundColor DarkGray
    & $file @arguments
    if ($LASTEXITCODE -ne 0) {
        Fail "Command failed: $file $($arguments -join ' ')"
    }
}

function GetGitOutput([string[]]$arguments) {
    $output = & git @arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return $output
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

function Resolve-ReleaseRepo {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseRepo)) {
        return $ReleaseRepo.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:AIREPO_RELEASE_REPO)) {
        return $env:AIREPO_RELEASE_REPO.Trim()
    }

    $remote = GetGitOutput @("config", "--get", "remote.origin.url")
    return Convert-GitRemoteToRepo ([string]$remote)
}

function Get-UploadAssetsCommand([string]$PlainVersion, [string]$ResolvedReleaseRepo) {
    $command = "powershell -ExecutionPolicy Bypass -File scripts/Upload-ReleaseAssets.ps1 -Version $PlainVersion"
    if (-not [string]::IsNullOrWhiteSpace($ResolvedReleaseRepo)) {
        $command += " -Repo $ResolvedReleaseRepo"
    }

    return $command
}

function StopRepoMcpProcesses {
    if ($NoAutoStopMcpLocks) {
        return
    }

    $repoRoot = (Get-Location).Path
    $repoRootSlash = $repoRoot.Replace("\", "/")

    $processes = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine.Contains("AiRepo.ContextMcp.dll") -and
            ($_.CommandLine.Contains($repoRoot) -or $_.CommandLine.Contains($repoRootSlash))
        }

    foreach ($process in $processes) {
        Step "Stopping locked MCP process $($process.ProcessId)"
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

$tag = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
$plainVersion = $tag.TrimStart("v")
$resolvedReleaseRepo = Resolve-ReleaseRepo

if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
    $CommitMessage = "chore: release $tag"
}

Step "Validating repository root"
if (-not (Test-Path ".git")) {
    Fail "Run this script from the repository root."
}

Step "Checking tag does not already exist"
$localTag = GetGitOutput @("tag", "--list", $tag)
if ($localTag) {
    Fail "Local tag already exists: $tag"
}

$remoteTag = GetGitOutput @("ls-remote", "origin", "refs/tags/$tag")
if ($remoteTag) {
    Fail "Remote tag already exists: $tag"
}

Step "Updating project version to $plainVersion"
$csproj = "src/AiRepoKit.Cli/AiRepoKit.Cli.csproj"
if (-not (Test-Path $csproj)) {
    Fail "Project file not found: $csproj"
}

$csprojContent = Get-Content $csproj -Raw
if ($csprojContent -notmatch "<Version>[^<]+</Version>") {
    Fail "$csproj does not contain a <Version>...</Version> element."
}

$newCsprojContent = [regex]::Replace(
    $csprojContent,
    "<Version>[^<]+</Version>",
    "<Version>$plainVersion</Version>",
    1
)

if ($newCsprojContent -ne $csprojContent) {
    Set-Content $csproj -Value $newCsprojContent -Encoding UTF8
    Write-Host "Updated $csproj to $plainVersion"
} else {
    Write-Host "$csproj already has version $plainVersion"
}

Step "Current working tree before validation"
$statusBeforeValidation = GetGitOutput @("status", "--porcelain")
if ($statusBeforeValidation) {
    Write-Host $statusBeforeValidation
} else {
    Write-Host "Working tree is clean."
}

if (-not $SkipMcpBuild) {
    StopRepoMcpProcesses
}

Step "Building solution"
RunNative "dotnet" @("build", "AI.RepoKit.MCP.sln", "-c", $Configuration)

if (-not $SkipMcpBuild) {
    Step "Building MCP project"
    StopRepoMcpProcesses
    RunNative "dotnet" @("build", "Tools\AiContextMcp\AiRepo.ContextMcp.csproj", "-c", $Configuration)
}

if (-not $SkipSmokeTests) {
    Step "Running smoke tests"
    $smokeTests = Get-ChildItem "tests" -Filter "v*-smoke.ps1" | Sort-Object Name
    foreach ($test in $smokeTests) {
        RunNative "powershell" @("-ExecutionPolicy", "Bypass", "-File", $test.FullName)
    }
}

if (-not $SkipStrictMcp) {
    Step "Running strict MCP diagnostics"
    StopRepoMcpProcesses
    RunNative "dotnet" @(
        "src\AiRepoKit.Cli\bin\$Configuration\net10.0\AiRepoKit.Cli.dll",
        "mcp-diagnose",
        "--repo",
        ".",
        "--strict",
        "--strict-stdio",
        "--summary",
        "--timings",
        "--no-progress"
    )
}

if (-not $NoCommit) {
    Step "Committing release changes if needed"
    $status = GetGitOutput @("status", "--porcelain")

    if ($status) {
        Write-Host $status
        RunNative "git" @("add", ".")
        RunNative "git" @("commit", "-m", $CommitMessage)
    } else {
        Write-Host "No changes to commit."
    }
} else {
    Step "Skipping commit by request"
}

if (-not $NoPush) {
    Step "Pushing current branch"
    $branch = GetGitOutput @("rev-parse", "--abbrev-ref", "HEAD")
    if (-not $branch) {
        Fail "Could not detect current branch."
    }

    RunNative "git" @("push", "origin", $branch)

    Step "Checking branch/upstream state"
    $upstream = GetGitOutput @("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}")
    if (-not $upstream) {
        Fail "Current branch has no upstream."
    }

    $head = GetGitOutput @("rev-parse", "HEAD")
    $upstreamHead = GetGitOutput @("rev-parse", $upstream)

    if ($head -ne $upstreamHead) {
        Fail "Local HEAD is not equal to upstream $upstream after push."
    }
} else {
    Step "Skipping branch push by request"
}

Step "Checking working tree is clean before tagging"
$finalStatus = GetGitOutput @("status", "--porcelain")
if ($finalStatus) {
    Write-Host $finalStatus
    Fail "Working tree is not clean before tagging."
}

if ($NoCommit -or $NoPush) {
    Step "Skipping tag because NoCommit or NoPush was requested"
    Write-Host "[OK] Validation completed without tag." -ForegroundColor Green
    Write-Host ""
    Write-Host "Build release assets:"
    Write-Host "powershell -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -Version $plainVersion"
    Write-Host "Upload release assets after creating ${tag}:"
    Write-Host (Get-UploadAssetsCommand $plainVersion $resolvedReleaseRepo)
    exit 0
}

Step "Creating tag $tag"
RunNative "git" @("tag", $tag)

Step "Pushing tag $tag"
RunNative "git" @("push", "origin", $tag)

Step "Confirming remote tag"
RunNative "git" @("ls-remote", "origin", "refs/tags/$tag")

Write-Host ""
Write-Host "[OK] Release tag created and pushed: $tag" -ForegroundColor Green

Write-Host ""
Write-Host "Build release assets:"
Write-Host "powershell -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -Version $plainVersion"
Write-Host "Upload release assets:"
Write-Host (Get-UploadAssetsCommand $plainVersion $resolvedReleaseRepo)

if ($UploadAssets) {
    if ([string]::IsNullOrWhiteSpace($resolvedReleaseRepo)) {
        Fail "UploadAssets requires a release repository. Pass -ReleaseRepo <owner>/<repo>, set AIREPO_RELEASE_REPO, or configure git remote origin."
    }

    Step "Uploading release assets"
    RunNative "powershell" @("-ExecutionPolicy", "Bypass", "-File", "scripts/Upload-ReleaseAssets.ps1", "-Version", $plainVersion, "-Repo", $resolvedReleaseRepo)
}


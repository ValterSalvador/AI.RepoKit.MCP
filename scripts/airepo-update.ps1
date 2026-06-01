param(
    [string]$Repo,
    [string]$Root,
    [string]$Version = "latest",
    [ValidateSet("auto", "local", "github")]
    [string]$Source = "auto",
    [string]$ReleaseRepo = "",
    [int]$MaxDepth = 3,
    [switch]$All,
    [switch]$Apply,
    [switch]$Setup,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

$packageId = "AiRepoKit.Cli"
$commandName = "airepo"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRepo = (Resolve-Path (Join-Path $scriptDir "..")).Path
$cacheRoot = Join-Path $env:TEMP "airepo-update"

function Show-Help {
    Write-Host ""
    Write-Host "AI RepoKit updater"
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  scripts\airepo-update.cmd"
    Write-Host "  scripts\airepo-update.cmd --repo <target-repo>"
    Write-Host "  scripts\airepo-update.cmd --all"
    Write-Host "  scripts\airepo-update.cmd --root <repositories-root> --all"
    Write-Host "  scripts\airepo-update.cmd --root <repositories-root> --all --apply"
    Write-Host "  scripts\airepo-update.cmd --root <repositories-root> --all --max-depth 3"
    Write-Host "  scripts\airepo-update.cmd --version 1.4.2"
    Write-Host "  scripts\airepo-update.cmd --source local"
    Write-Host "  scripts\airepo-update.cmd --source github --release-repo <owner>/<repo>"
    Write-Host ""
    Write-Host "Defaults:"
    Write-Host "  No arguments inside an AiRepoKit-enabled repo updates that repo."
    Write-Host "  No arguments outside an AiRepoKit-enabled repo runs scan mode in the current directory."
    Write-Host "  --all without --root scans the current directory."
    Write-Host ""
    Write-Host "Scan criteria:"
    Write-Host "  Only real repos with .git plus .ai, AiRepoKit.Cli local tool manifest, or airepo executable are included."
    Write-Host ""
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

function Get-GitRemoteRepo([string]$workingDirectory) {
    if (-not $workingDirectory -or -not (Test-Path $workingDirectory)) {
        return $null
    }

    Push-Location $workingDirectory
    try {
        $remote = & git config --get remote.origin.url 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $null
        }

        return Convert-GitRemoteToRepo ([string]$remote)
    }
    finally {
        Pop-Location
    }
}

function Resolve-ReleaseRepo {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseRepo)) {
        return $ReleaseRepo.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:AIREPO_RELEASE_REPO)) {
        return $env:AIREPO_RELEASE_REPO.Trim()
    }

    $derived = Get-GitRemoteRepo $sourceRepo
    if ($derived) {
        return $derived
    }

    $current = Get-Location
    return Get-GitRemoteRepo $current.Path
}

function Run($file, [string[]]$arguments, [string]$workingDirectory = $null) {
    $display = "$file $($arguments -join ' ')"
    Write-Host "> $display" -ForegroundColor DarkGray

    if ($workingDirectory) {
        Push-Location $workingDirectory
        try {
            & $file @arguments | ForEach-Object { Write-Host $_ }
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    else {
        & $file @arguments | ForEach-Object { Write-Host $_ }
        $exitCode = $LASTEXITCODE
    }

    if ($exitCode -ne 0) {
        throw "Command failed: $display"
    }
}

function Test-RealRepo([string]$path) {
    return [System.IO.Directory]::Exists((Join-Path $path ".git"))
}

function Test-AiRepoKitEnabledRepo([string]$path) {
    if (-not (Test-RealRepo $path)) {
        return $false
    }

    if (Test-Path (Join-Path $path ".ai")) {
        return $true
    }

    $exeCandidates = @(
        "airepo.exe",
        "AiRepoKit.Cli.exe",
        ".ai\tools\airepo.exe",
        ".ai\tools\AiRepoKit.Cli.exe"
    )

    foreach ($candidate in $exeCandidates) {
        if (Test-Path (Join-Path $path $candidate)) {
            return $true
        }
    }

    $manifestCandidates = @(
        ".config\dotnet-tools.json",
        "dotnet-tools.json"
    )

    foreach ($candidate in $manifestCandidates) {
        $manifestPath = Join-Path $path $candidate
        if (Test-Path $manifestPath) {
            $text = Get-Content $manifestPath -Raw -ErrorAction SilentlyContinue
            if ($text -match "AiRepoKit\.Cli|airepo") {
                return $true
            }
        }
    }

    return $false
}

function Resolve-Version {
    if ($Version -ne "latest") {
        return $Version.TrimStart("v")
    }

    $csproj = Join-Path $sourceRepo "src\AiRepoKit.Cli\AiRepoKit.Cli.csproj"
    if (($Source -eq "auto" -or $Source -eq "local") -and (Test-Path $csproj)) {
        [xml]$project = Get-Content $csproj
        $projectVersion = $project.Project.PropertyGroup.Version | Select-Object -First 1
        if ($projectVersion) {
            return [string]$projectVersion
        }
    }

    $resolvedReleaseRepo = Resolve-ReleaseRepo
    if (-not $resolvedReleaseRepo) {
        throw "GitHub source requires a release repository. Pass --release-repo <owner>/<repo>, set AIREPO_RELEASE_REPO, or run from a checkout with git remote origin."
    }

    $release = Invoke-RestMethod "https://api.github.com/repos/$resolvedReleaseRepo/releases/latest"
    return ([string]$release.tag_name).TrimStart("v")
}

function Prepare-Source([string]$resolvedVersion) {
    $csproj = Join-Path $sourceRepo "src\AiRepoKit.Cli\AiRepoKit.Cli.csproj"

    if ($Source -ne "github" -and (Test-Path $csproj)) {
        Write-Host ""
        Write-Host "==> Packing local AiRepoKit.Cli $resolvedVersion"

        $nupkgDir = Join-Path $sourceRepo "artifacts\nuget"
        Run "dotnet" @("pack", $csproj, "-c", "Release", "-o", $nupkgDir)

        $expectedPackage = Join-Path $nupkgDir "$packageId.$resolvedVersion.nupkg"
        if (-not (Test-Path $expectedPackage)) {
            throw "Local nupkg not found: $expectedPackage"
        }

        return $nupkgDir
    }

    if ($Source -eq "local") {
        throw "Local source requested but source repo was not found: $sourceRepo"
    }

    Write-Host ""
    Write-Host "==> Downloading AiRepoKit.Cli $resolvedVersion from GitHub Release"
    $resolvedReleaseRepo = Resolve-ReleaseRepo
    if (-not $resolvedReleaseRepo) {
        throw "GitHub source requires a release repository. Pass --release-repo <owner>/<repo>, set AIREPO_RELEASE_REPO, or run from a checkout with git remote origin."
    }

    $nupkgDir = Join-Path $cacheRoot $resolvedVersion
    New-Item -ItemType Directory -Force $nupkgDir | Out-Null

    $release = Invoke-RestMethod "https://api.github.com/repos/$resolvedReleaseRepo/releases/tags/v$resolvedVersion"
    $asset = $release.assets | Where-Object { $_.name -like "*AiRepoKit.Cli*.nupkg" } | Select-Object -First 1

    if (-not $asset) {
        throw "No AiRepoKit.Cli nupkg asset found in GitHub Release. Use --source local from AI.RepoKit.MCP repo or publish the nupkg as a release asset."
    }

    $target = Join-Path $nupkgDir $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $target

    return $nupkgDir
}

function Update-OneRepo([string]$repoPath) {
    $repoPath = (Resolve-Path $repoPath).Path

    if (-not (Test-RealRepo $repoPath)) {
        throw "Refusing to update non-repository path: $repoPath. Run from a real git repository or use --all to scan."
    }

    $resolvedVersion = Resolve-Version
    $nupkgDir = Prepare-Source $resolvedVersion

    Write-Host ""
    Write-Host "==> Updating repo: $repoPath"

    Push-Location $repoPath
    try {
        if (-not (Test-Path ".config")) {
            New-Item -ItemType Directory -Force ".config" | Out-Null
        }

        if ((Test-Path "dotnet-tools.json") -and -not (Test-Path ".config\dotnet-tools.json")) {
            Write-Host "Found legacy dotnet-tools.json; copying to .config\dotnet-tools.json..."
            Copy-Item "dotnet-tools.json" ".config\dotnet-tools.json" -Force
        }

        if (-not (Test-Path ".config\dotnet-tools.json")) {
            Write-Host "Creating local tool manifest..."
            Run "dotnet" @("new", "tool-manifest")
        }

        Write-Host "Updating $packageId to $resolvedVersion..."

        & dotnet tool update $packageId --version $resolvedVersion --add-source $nupkgDir
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Update failed; trying install..."
            Run "dotnet" @("tool", "install", $packageId, "--version", $resolvedVersion, "--add-source", $nupkgDir)
        }

        Run "dotnet" @("tool", "restore")
        Run "dotnet" @("tool", "run", $commandName, "--", "--version")

        if ($Setup) {
            Run "dotnet" @(
                "tool", "run", $commandName, "--",
                "setup",
                "--repo", ".",
                "--clients", "codex,vscode,vs",
                "--mcp",
                "--agents",
                "--profile", "auto",
                "--no-progress"
            )
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    Write-Host "[OK] Updated repo: $repoPath" -ForegroundColor Green
}

function Get-RelativeDepth([string]$rootPath, [string]$path) {
    $rootPath = $rootPath.TrimEnd('\', '/')
    $relative = $path.Substring($rootPath.Length).TrimStart('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative)) {
        return 0
    }

    return ($relative -split '[\\/]').Count
}

function Find-AiRepoKitRepos([string]$rootPath) {
    $rootPath = (Resolve-Path $rootPath).Path.TrimEnd('\', '/')
    $skipRegex = '\\(\.tmp|bin|obj|node_modules|artifacts|packages|\.vs)($|\\)'

    $repos = New-Object 'System.Collections.Generic.List[string]'

    $directories = Get-ChildItem -Path $rootPath -Directory -Recurse -Depth $MaxDepth -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch $skipRegex }

    foreach ($directory in $directories) {
        $path = $directory.FullName

        if ((Get-RelativeDepth $rootPath $path) -gt $MaxDepth) {
            continue
        }

        if (Test-AiRepoKitEnabledRepo $path) {
            $repos.Add($path)
        }
    }

    return $repos | Sort-Object -Unique
}

function Update-AllRepos([string]$rootPath) {
    if (-not $rootPath) {
        $rootPath = (Get-Location).Path
    }

    Write-Host ""
    Write-Host "==> Scanning AiRepoKit-enabled repos under: $rootPath"
    Write-Host "    Apply: $Apply"
    Write-Host "    MaxDepth: $MaxDepth"
    Write-Host ""

    $repos = Find-AiRepoKitRepos $rootPath

    foreach ($repoPath in $repos) {
        if ($Apply) {
            try {
                Update-OneRepo $repoPath
            }
            catch {
                Write-Host "[WARN] Failed: $repoPath"
                Write-Host "       $($_.Exception.Message)"
            }
        }
        else {
            Write-Host "[DRY-RUN] Would update: $repoPath"
        }
    }

    if (-not $Apply) {
        Write-Host ""
        Write-Host "Dry-run only. Add --apply to update all repos."
    }
}

if ($Help) {
    Show-Help
    exit 0
}

if ($Repo) {
    Update-OneRepo $Repo
    exit 0
}

if ($All) {
    if (-not $Root) {
        $Root = (Get-Location).Path
    }

    Update-AllRepos $Root
    exit 0
}

$currentPath = (Get-Location).Path

if (Test-AiRepoKitEnabledRepo $currentPath) {
    Update-OneRepo $currentPath
    exit 0
}

Write-Host ""
Write-Host "Current directory is not AiRepoKit-enabled. Running scan mode in current directory."
Update-AllRepos $currentPath

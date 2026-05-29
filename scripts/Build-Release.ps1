param(
    [switch]$SkipLinux,
    [switch]$SkipWindows,
    [string[]]$RuntimeIdentifiers = @(),
    [string]$Configuration = "Release",
    [string]$Version = "",
    [switch]$SkipAudit
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src/AiRepoKit.Cli/AiRepoKit.Cli.csproj"
$nugetDir = Join-Path $root "artifacts/nuget"
$publishRoot = Join-Path $root "artifacts/publish"
$manifestPath = Join-Path $root "artifacts/release-manifest.json"
$projectXml = [xml](Get-Content $project)

function Resolve-ReleaseVersion([string]$RequestedVersion, [xml]$ProjectXml) {
    $resolvedVersion = $RequestedVersion
    if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
        $resolvedVersion = [string]($ProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
    }

    if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
        throw "Unable to resolve release version. Provide -Version or set <Version> in $project."
    }

    $resolvedVersion = $resolvedVersion.Trim()
    if ($resolvedVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $resolvedVersion = $resolvedVersion.Substring(1)
    }

    if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
        throw "Unable to resolve release version. The resolved version was empty after normalization."
    }

    return $resolvedVersion
}

$version = Resolve-ReleaseVersion $Version $projectXml
Write-Output "Release version: $version"
$targetFramework = $projectXml.Project.PropertyGroup.TargetFramework

function Invoke-DotNet([string[]]$Arguments) {
    dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-RelativePath([string]$Root, [string]$Path) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd("\","/")
    $pathFull = [IO.Path]::GetFullPath($Path)
    if ($pathFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $pathFull.Substring($rootFull.Length).TrimStart("\","/").Replace("\", "/")
    }
    return $pathFull.Replace("\", "/")
}

New-Item -ItemType Directory -Force -Path $nugetDir | Out-Null
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

Invoke-DotNet @("restore", $root)
Invoke-DotNet @("build", $root, "-c", $Configuration)
if (-not $SkipAudit) {
    dotnet run --project $project -- audit --repo $root
    if ($LASTEXITCODE -ne 0) {
        throw "airepo audit failed with exit code $LASTEXITCODE"
    }
}
Invoke-DotNet @("pack", $project, "-c", $Configuration, "-o", $nugetDir, "-p:Version=$version")

$targets = @(
    @{ Rid = "win-x64"; Name = "airepo.exe"; Source = "AiRepoKit.Cli.exe" },
    @{ Rid = "linux-x64"; Name = "airepo"; Source = "AiRepoKit.Cli" },
    @{ Rid = "linux-arm64"; Name = "airepo"; Source = "AiRepoKit.Cli" }
)

if ($SkipWindows) {
    $targets = @($targets | Where-Object { -not $_.Rid.StartsWith("win-") })
}

if ($SkipLinux) {
    $targets = @($targets | Where-Object { -not $_.Rid.StartsWith("linux-") })
}

if ($RuntimeIdentifiers.Count -gt 0) {
    $requestedRids = [string[]]$RuntimeIdentifiers
    $targets = @($targets | Where-Object { $requestedRids -contains $_.Rid })
}

foreach ($target in $targets) {
    $output = Join-Path $publishRoot $target.Rid
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    Invoke-DotNet @("publish", $project, "-c", $Configuration, "-r", $target.Rid, "--self-contained", "true", "/p:Version=$version", "/p:PublishSingleFile=true", "/p:EnableCompressionInSingleFile=true", "/p:IncludeAllContentForSelfExtract=true", "-o", $output)
    $source = Join-Path $output $target.Source
    $destination = Join-Path $output $target.Name
    if ((Test-Path $source) -and ($source -ne $destination)) {
        Move-Item -Force -Path $source -Destination $destination
    }

    if ($target.Rid -eq "win-x64") {
        Copy-Item -Force -Path (Join-Path $root "scripts/install-ai-context.cmd") -Destination (Join-Path $output "install-ai-context.cmd")
        Copy-Item -Force -Path (Join-Path $root "scripts/install-ai-context.ps1") -Destination (Join-Path $output "install-ai-context.ps1")
    }
}

$artifactFiles = @()
$artifactFiles += Get-ChildItem -Path $nugetDir -Filter "AiRepoKit.Cli.$version.nupkg" -File
foreach ($target in $targets) {
    $artifactFiles += Get-Item (Join-Path $publishRoot "$($target.Rid)/$($target.Name)")
    if ($target.Rid -eq "win-x64") {
        $artifactFiles += Get-Item (Join-Path $publishRoot "win-x64/install-ai-context.cmd")
        $artifactFiles += Get-Item (Join-Path $publishRoot "win-x64/install-ai-context.ps1")
    }
}

$manifest = [ordered]@{
    Version = $version
    GeneratedAtLocal = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    TargetFramework = $targetFramework
    Artifacts = @($artifactFiles | ForEach-Object {
        [ordered]@{
            Path = Get-RelativePath $root $_.FullName
            Sha256 = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
            SizeBytes = $_.Length
        }
    })
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8
Write-Output "Release artifacts generated in artifacts/"

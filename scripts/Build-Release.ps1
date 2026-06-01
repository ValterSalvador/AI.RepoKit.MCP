param(
    [switch]$SkipLinux,
    [switch]$SkipWindows,
    [string[]]$RuntimeIdentifiers = @(),
    [string]$Configuration = "Release",
    [string]$Version = "",
    [switch]$SkipAudit,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src/AiRepoKit.Cli/AiRepoKit.Cli.csproj"
$nugetDir = Join-Path $root "artifacts/nuget"
$publishRoot = Join-Path $root "artifacts/publish"
$releaseDir = Join-Path $root "artifacts/release"
$manifestPath = Join-Path $releaseDir "release-manifest.json"
$legacyManifestPath = Join-Path $root "artifacts/release-manifest.json"
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

function Remove-ExistingFile([string]$Path) {
    if (Test-Path $Path) {
        Remove-Item -Force -Path $Path
    }
}

function Assert-FileExists([string]$Path, [string]$Description) {
    if (-not (Test-Path $Path)) {
        throw "$Description not found: $Path"
    }
}

function New-ZipArchive([string[]]$Paths, [string]$DestinationPath) {
    Remove-ExistingFile $DestinationPath
    Compress-Archive -Path $Paths -DestinationPath $DestinationPath -CompressionLevel Optimal
}

function New-TarGzArchive([string]$SourceDirectory, [string[]]$Entries, [string]$DestinationPath) {
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    if (-not $tar) {
        throw "tar was not found on PATH. It is required to create $([IO.Path]::GetFileName($DestinationPath))."
    }

    Remove-ExistingFile $DestinationPath
    $arguments = @("-czf", $DestinationPath, "-C", $SourceDirectory) + $Entries
    & $tar.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "tar $($arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Clear-KnownReleaseAssets([string]$Directory) {
    $patterns = @(
        "AiRepoKit.Cli.*.nupkg",
        "airepo-win-x64.zip",
        "airepo-linux-x64.tar.gz",
        "airepo-linux-arm64.tar.gz",
        "airepo-updater-win.zip",
        "airepo-updater-unix.tar.gz",
        "release-manifest.json"
    )

    foreach ($pattern in $patterns) {
        Get-ChildItem -Path $Directory -Filter $pattern -File -ErrorAction SilentlyContinue | Remove-Item -Force
    }
}

New-Item -ItemType Directory -Force -Path $nugetDir | Out-Null
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Clear-KnownReleaseAssets $releaseDir

if (-not $SkipRestore) {
    Invoke-DotNet @("restore", $root)
}

$buildArguments = @("build", $root, "-c", $Configuration)
if ($SkipRestore) {
    $buildArguments += "--no-restore"
}
Invoke-DotNet $buildArguments
if (-not $SkipAudit) {
    dotnet run --project $project -- audit --repo $root
    if ($LASTEXITCODE -ne 0) {
        throw "airepo audit failed with exit code $LASTEXITCODE"
    }
}
$packArguments = @("pack", $project, "-c", $Configuration, "-o", $nugetDir, "-p:Version=$version")
if ($SkipRestore) {
    $packArguments += "--no-restore"
}
Invoke-DotNet $packArguments
$nupkg = Join-Path $nugetDir "AiRepoKit.Cli.$version.nupkg"
Assert-FileExists $nupkg "NuGet package"
Copy-Item -Force -Path $nupkg -Destination (Join-Path $releaseDir "AiRepoKit.Cli.$version.nupkg")

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
    $publishArguments = @("publish", $project, "-c", $Configuration, "-r", $target.Rid, "--self-contained", "true", "/p:Version=$version", "/p:PublishSingleFile=true", "/p:EnableCompressionInSingleFile=true", "/p:IncludeAllContentForSelfExtract=true", "-o", $output)
    if ($SkipRestore) {
        $publishArguments += "--no-restore"
    }
    Invoke-DotNet $publishArguments
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

foreach ($target in $targets) {
    $output = Join-Path $publishRoot $target.Rid
    Assert-FileExists (Join-Path $output $target.Name) "$($target.Rid) standalone executable"

    if ($target.Rid -eq "win-x64") {
        New-ZipArchive -Paths @((Join-Path $output "*")) -DestinationPath (Join-Path $releaseDir "airepo-win-x64.zip")
    }
    elseif ($target.Rid -eq "linux-x64") {
        New-TarGzArchive -SourceDirectory $output -Entries @($target.Name) -DestinationPath (Join-Path $releaseDir "airepo-linux-x64.tar.gz")
    }
    elseif ($target.Rid -eq "linux-arm64") {
        New-TarGzArchive -SourceDirectory $output -Entries @($target.Name) -DestinationPath (Join-Path $releaseDir "airepo-linux-arm64.tar.gz")
    }
}

$windowsUpdaterScripts = @(
    (Join-Path $root "scripts/airepo-update.cmd"),
    (Join-Path $root "scripts/airepo-update.ps1")
)
foreach ($script in $windowsUpdaterScripts) {
    Assert-FileExists $script "Windows updater script"
}
New-ZipArchive -Paths $windowsUpdaterScripts -DestinationPath (Join-Path $releaseDir "airepo-updater-win.zip")

$unixUpdaterScript = Join-Path $root "scripts/airepo-update.sh"
Assert-FileExists $unixUpdaterScript "Unix updater script"
$unixUpdaterText = Get-Content -Path $unixUpdaterScript -TotalCount 1
if ($unixUpdaterText -notlike "#!*") {
    throw "Unix updater script does not start with a shebang: $unixUpdaterScript"
}
New-TarGzArchive -SourceDirectory (Join-Path $root "scripts") -Entries @("airepo-update.sh") -DestinationPath (Join-Path $releaseDir "airepo-updater-unix.tar.gz")

$artifactFiles = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Name -ne "release-manifest.json" } | Sort-Object Name

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
$artifactFiles = Get-ChildItem -Path $releaseDir -File | Sort-Object Name
$legacyManifestParent = Split-Path -Parent $legacyManifestPath
New-Item -ItemType Directory -Force -Path $legacyManifestParent | Out-Null
Copy-Item -Force -Path $manifestPath -Destination $legacyManifestPath

Write-Output ""
Write-Output "Release assets generated in artifacts/release:"
foreach ($file in $artifactFiles) {
    Write-Output " - $($file.Name)"
}

param(
    [string]$RepoRoot = "",
    [int]$MaxFiles = 2000,
    [int]$MaxItems = 5000,
    [switch]$IncludePrivateMembers
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$commandArgs = @("code-index", "--repo", $RepoRoot, "--apply", "--max-files", $MaxFiles.ToString(), "--max-items", $MaxItems.ToString())
if ($IncludePrivateMembers) {
    $commandArgs += "--include-private-members"
}

$localToolAvailable = $false
$previousErrorActionPreference = $ErrorActionPreference

try {
    $ErrorActionPreference = "Continue"
    & dotnet tool run airepo -- --version *> $null
    $localToolAvailable = ($LASTEXITCODE -eq 0)
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($localToolAvailable) {
    & dotnet tool run airepo -- @commandArgs
    exit $LASTEXITCODE
}

if ($null -ne (Get-Command airepo -ErrorAction SilentlyContinue)) {
    & airepo @commandArgs
    exit $LASTEXITCODE
}

Write-Error "airepo was not found. Restore the local dotnet tool or install AiRepoKit.Cli."
exit 1

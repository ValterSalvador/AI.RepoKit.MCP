param(
    [string]$RepoRoot = "",
    [int]$StartupTimeoutSeconds = 20,
    [int]$ToolTimeoutSeconds = 30,
    [switch]$JsonOnly,
    [switch]$FailOnBudget,
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$commandArgs = @("mcp-budget", "--repo", $RepoRoot, "--startup-timeout-seconds", $StartupTimeoutSeconds.ToString(), "--tool-timeout-seconds", $ToolTimeoutSeconds.ToString())
if ($JsonOnly) {
    $commandArgs += "--json"
}

if ($VerboseOutput) {
    $commandArgs += "--verbose"
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

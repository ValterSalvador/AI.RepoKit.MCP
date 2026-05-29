param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$reportsRoot = Join-Path $RepoRoot ".ai/generated/reports"
$solution = Get-ChildItem -LiteralPath $RepoRoot -Filter *.sln -File | Sort-Object Name | Select-Object -First 1
New-Item -ItemType Directory -Path $reportsRoot -Force | Out-Null

if (-not $solution) {
    $report = [ordered]@{
        generatedAtLocal = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss zzz")
        target = ""
        restoreExitCode = 0
        buildExitCode = 0
        status = "No root solution found."
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reportsRoot "build-diagnostics-report.json") -Encoding UTF8
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reportsRoot "latest-build-summary.json") -Encoding UTF8
    Write-Output "No root solution found."
    exit 0
}

Write-Output "Restore target: $($solution.Name)"
$restoreOutput = & dotnet restore $solution.FullName 2>&1
$restoreExitCode = $LASTEXITCODE
Write-Output "Build target: $($solution.Name)"
$buildOutput = & dotnet build $solution.FullName -c Debug --no-restore 2>&1
$buildExitCode = $LASTEXITCODE
$report = [ordered]@{
    generatedAtLocal = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss zzz")
    target = $solution.Name
    restoreExitCode = $restoreExitCode
    buildExitCode = $buildExitCode
    restoreOutputTail = @($restoreOutput | Select-Object -Last 80)
    buildOutputTail = @($buildOutput | Select-Object -Last 120)
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reportsRoot "build-diagnostics-report.json") -Encoding UTF8
([ordered]@{ generatedAtLocal = $report.generatedAtLocal; target = $report.target; restoreExitCode = $restoreExitCode; buildExitCode = $buildExitCode } | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $reportsRoot "latest-build-summary.json") -Encoding UTF8
if ($restoreExitCode -ne 0) { exit $restoreExitCode }
if ($buildExitCode -ne 0) { exit $buildExitCode }

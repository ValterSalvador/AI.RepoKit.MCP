param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $RepoRoot 'src/AiRepoKit.Cli/AiRepoKit.Cli.csproj'
$dll = Join-Path $RepoRoot 'src/AiRepoKit.Cli/bin/Debug/net10.0/AiRepoKit.Cli.dll'

dotnet build $project | Out-Host

function Invoke-AiRepo {
    param(
        [string[]]$Arguments,
        [int[]]$AllowedExitCodes = @(0)
    )

    $output = & dotnet $dll @Arguments 2>&1
    if ($AllowedExitCodes -notcontains $LASTEXITCODE) {
        $output | Out-String | Write-Error
    }

    return $output | Out-String
}

$selfCheckQuick = Invoke-AiRepo -Arguments @('self-check', '--repo', $RepoRoot, '--quick', '--summary', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
if ($selfCheckQuick -notmatch '- Mode: `quick`' -or $selfCheckQuick -notmatch '## Timings') {
    throw 'self-check --quick did not report quick mode with timings.'
}

$selfCheckFull = Invoke-AiRepo -Arguments @('self-check', '--repo', $RepoRoot, '--full', '--summary', '--no-progress') -AllowedExitCodes @(0, 2)
if ($selfCheckFull -notmatch '- Mode: `full`') {
    throw 'self-check --full did not report full mode.'
}

$selfCheckStrict = Invoke-AiRepo -Arguments @('self-check', '--repo', $RepoRoot, '--strict', '--summary', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
if ($selfCheckStrict -notmatch '- Mode: `strict`' -or $selfCheckStrict -notmatch '## Timings') {
    throw 'self-check --strict did not report strict mode with timings.'
}

$mcpQuick = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--quick', '--summary', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
if ($mcpQuick -notmatch '- Mode: `quick`' -or $mcpQuick -notmatch '## Timings') {
    throw 'mcp-diagnose --quick did not report quick mode with timings.'
}

$mcpStrict = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--strict', '--summary', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
if ($mcpStrict -notmatch '- Mode: `strict`' -or $mcpStrict -notmatch '## Timings') {
    throw 'mcp-diagnose --strict did not report strict mode with timings.'
}

$selfCheckQuickJson = Invoke-AiRepo -Arguments @('self-check', '--repo', $RepoRoot, '--quick', '--json', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
$selfCheckQuickJsonObject = $selfCheckQuickJson | ConvertFrom-Json
if ($selfCheckQuickJsonObject.Mode -ne 'quick' -or $null -eq $selfCheckQuickJsonObject.Timings) {
    throw 'self-check --quick --json --timings did not return mode and timing data.'
}

$mcpQuickJson = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--quick', '--json', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
$mcpQuickJsonObject = $mcpQuickJson | ConvertFrom-Json
if ($mcpQuickJsonObject.Mode -ne 'quick' -or $null -eq $mcpQuickJsonObject.Timings) {
    throw 'mcp-diagnose --quick --json --timings did not return mode and timing data.'
}

$mcpConfig = Get-Content (Join-Path $RepoRoot '.mcp.json') -Raw | ConvertFrom-Json
if ($null -eq $mcpConfig.servers -and $null -eq $mcpConfig.mcpServers) {
    throw '.mcp.json is not valid JSON or lacks servers/mcpServers.'
}

Write-Host 'v1.4.0 smoke tests passed.'

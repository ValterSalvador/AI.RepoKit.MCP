param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $RepoRoot 'src/AiRepoKit.Cli/AiRepoKit.Cli.csproj'
$dll = Join-Path $RepoRoot 'src/AiRepoKit.Cli/bin/Debug/net10.0/AiRepoKit.Cli.dll'

dotnet build $project | Out-Host

function Invoke-AiRepo {
    param([string[]]$Arguments)
    $output = & dotnet $dll @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Out-String | Write-Error
    }

    return $output | Out-String
}

$graph = Invoke-AiRepo @('graph', '--no-progress')
if ($graph -notmatch '# Graph Dry Run' -or $graph -notmatch 'project') {
    throw 'graph dry-run did not produce a project graph summary.'
}

$impact = Invoke-AiRepo @('impact', '--no-progress')
if ($impact -notmatch '# Impact Preview') {
    throw 'impact preview did not run.'
}

$changed = Invoke-AiRepo @('context-pack', '--task', 'changed-files', '--apply', '--budget', '12000', '--limit', '30', '--no-progress')
if ($changed -notmatch '# Context Pack Apply' -or $changed -notmatch 'changed-files') {
    throw 'changed-files context pack was not generated.'
}

$budgetJson = Invoke-AiRepo @('context-pack', '--task', 'changed-files', '--budget', '10', '--json', '--no-progress')
$budget = $budgetJson | ConvertFrom-Json
if ($null -eq $budget.pack.estimatedTokens -or $null -eq $budget.pack.truncated) {
    throw 'changed-files context pack JSON did not report budget metadata.'
}

$graphApply = Invoke-AiRepo @('graph', '--kind', 'project', '--apply', '--format', 'json', '--no-progress')
if ($graphApply -notmatch 'project-graph.json') {
    throw 'graph --apply did not report project-graph.json.'
}

$diagnose = Invoke-AiRepo @('mcp-diagnose', '--skip-build', '--skip-budget', '--no-progress')
if ($diagnose -notmatch '# MCP Diagnose') {
    throw 'mcp-diagnose did not run.'
}

Write-Host 'v1.2.0 smoke tests passed.'

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

$scan = Invoke-AiRepo @('org', 'scan', '--root', $RepoRoot, '--max-depth', '0', '--no-progress')
if ($scan -notmatch '# Org Scan' -or $scan -notmatch 'Repositories') {
    throw 'org scan did not produce a markdown scan report.'
}

$report = Invoke-AiRepo @('org', 'report', '--root', $RepoRoot, '--max-depth', '0', '--no-progress')
if ($report -notmatch '# Org Report' -or $report -notmatch 'Readiness') {
    throw 'org report dry-run did not produce a readiness report.'
}

$selfCheck = Invoke-AiRepo @('org', 'self-check', '--root', $RepoRoot, '--max-depth', '0', '--skip-audit', '--skip-build-mcp', '--skip-budget', '--no-progress')
if ($selfCheck -notmatch '# Org Self-Check') {
    throw 'org self-check safe mode did not run.'
}

$setup = Invoke-AiRepo @('org', 'setup', '--root', $RepoRoot, '--max-depth', '0', '--dry-run', '--no-progress')
if ($setup -notmatch '# Org Setup Dry Run' -or $setup -notmatch 'dry-run-only') {
    throw 'org setup dry-run did not run.'
}

$efficiency = Invoke-AiRepo @('org', 'efficiency', '--root', $RepoRoot, '--max-depth', '0', '--no-progress')
if ($efficiency -notmatch '# Org Efficiency') {
    throw 'org efficiency dry-run did not run.'
}

$json = Invoke-AiRepo @('org', 'scan', '--root', $RepoRoot, '--max-depth', '0', '--json', '--no-progress')
$jsonObject = $json | ConvertFrom-Json
if ($null -eq $jsonObject.Repositories) {
    throw 'org scan --json was not parseable or lacked repositories.'
}

$mcpConfig = Get-Content (Join-Path $RepoRoot '.mcp.json') -Raw | ConvertFrom-Json
if ($null -eq $mcpConfig.servers -and $null -eq $mcpConfig.mcpServers) {
    throw '.mcp.json is not valid JSON or lacks servers/mcpServers.'
}

$csv = Invoke-AiRepo @('org', 'report', '--root', $RepoRoot, '--max-depth', '0', '--format', 'csv', '--no-progress')
if ($csv -notmatch 'repoRoot,repoName,recommendedProfile') {
    throw 'org report --format csv did not produce CSV.'
}

$emptyRoot = Join-Path $env:TEMP ('airepo-empty-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $emptyRoot | Out-Null
try {
    $empty = Invoke-AiRepo @('org', 'scan', '--root', $emptyRoot, '--max-depth', '0', '--json', '--no-progress')
    $emptyJson = $empty | ConvertFrom-Json
    if ($emptyJson.Repositories.Count -ne 0) {
        throw 'org scan against an empty root should not find repositories.'
    }
}
finally {
    Remove-Item -Recurse -Force $emptyRoot
}

$statusBefore = git -C $RepoRoot status --short
Invoke-AiRepo @('org', 'setup', '--root', $RepoRoot, '--max-depth', '0', '--dry-run', '--no-progress') | Out-Null
$statusAfter = git -C $RepoRoot status --short
if (($statusBefore | Out-String) -ne ($statusAfter | Out-String)) {
    throw 'org setup dry-run modified the repository.'
}

Write-Host 'v1.3.0 smoke tests passed.'

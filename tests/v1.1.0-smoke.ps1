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

$detect = Invoke-AiRepo @('detect', '--json', '--no-progress')
$detectJson = $detect | ConvertFrom-Json
if (-not $detectJson.repoRoot -or -not $detectJson.recommendedProfile) {
    throw 'detect --json did not return repoRoot and recommendedProfile.'
}

$setup = Invoke-AiRepo @('setup', '--no-progress')
if ($setup -notmatch '# Setup Preview' -or $setup -notmatch 'profile=') {
    throw 'setup preview did not report inferred defaults.'
}

$missingTerm = 'missing-term-' + [Guid]::NewGuid().ToString('N')
$selfCheck = Invoke-AiRepo @('self-check', '--skip-build-mcp', '--skip-code-index', '--skip-budget', '--skip-audit', '--forbidden-term', $missingTerm, '--no-progress')
if ($selfCheck -notmatch 'Status: `Passed`') {
    throw 'self-check --forbidden-term smoke check did not pass.'
}

$sanitize = Invoke-AiRepo @('sanitize', '--term', $missingTerm, '--replacement', 'replacement', '--no-progress')
if ($sanitize -notmatch '# Sanitize Dry Run') {
    throw 'sanitize dry-run did not run.'
}

Write-Host 'v1.1.0 smoke tests passed.'

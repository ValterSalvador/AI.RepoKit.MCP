param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $RepoRoot 'src/AiRepoKit.Cli/AiRepoKit.Cli.csproj'
$dll = Join-Path $RepoRoot 'src/AiRepoKit.Cli/bin/Release/net10.0/AiRepoKit.Cli.dll'

if (-not (Test-Path -LiteralPath $dll)) {
    dotnet build $project -c Release | Out-Host
}

function Invoke-AiRepo {
    param(
        [string[]]$Arguments,
        [int[]]$AllowedExitCodes = @(0)
    )

    $output = & dotnet $dll @Arguments 2>&1
    if ($AllowedExitCodes -notcontains $LASTEXITCODE) {
        throw ($output | Out-String)
    }

    return $output | Out-String
}

$help = Invoke-AiRepo -Arguments @('--help')
if ($help -notmatch 'airepo update' -or $help -notmatch 'airepo hooks') {
    throw 'CLI help does not expose update and hooks.'
}

$updateJson = Invoke-AiRepo -Arguments @('update', '--repo', $RepoRoot, '--dry-run', '--quick', '--json', '--no-progress')
$update = $updateJson | ConvertFrom-Json
if ($update.command -ne 'update' -or $update.mode -ne 'dry-run' -or $update.preset -ne 'quick') {
    throw 'update --dry-run --quick returned unexpected metadata.'
}

$phaseNames = @($update.phases | ForEach-Object { $_.Name })
foreach ($required in @('detect', 'code-index', 'context-pack changed-files', 'impact changed-files', 'self-check')) {
    if ($phaseNames -notcontains $required) {
        throw "update quick preset is missing phase: $required"
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("airepo-v1.8.0-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    git -C $tempRoot init | Out-Null
    $preview = Invoke-AiRepo -Arguments @('hooks', '--repo', $tempRoot)
    if ($preview -notmatch 'Git Hooks Preview') {
        throw 'hooks preview did not report preview mode.'
    }

    Invoke-AiRepo -Arguments @('hooks', '--repo', $tempRoot, '--apply') | Out-Null
    $configuredPath = git -C $tempRoot config --local --get core.hooksPath
    if ($configuredPath -ne '.githooks') {
        throw "hooks apply configured an unexpected core.hooksPath: $configuredPath"
    }

    foreach ($hook in @('pre-commit', 'post-merge', 'post-rewrite')) {
        $hookPath = Join-Path $tempRoot ".githooks/$hook"
        if (-not (Test-Path -LiteralPath $hookPath)) {
            throw "hooks apply did not create $hook"
        }
    }

    Invoke-AiRepo -Arguments @('hooks', '--repo', $tempRoot, '--apply') | Out-Null
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Host 'v1.8.0 update workflow and Git hooks smoke tests passed.'

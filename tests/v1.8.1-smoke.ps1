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

function New-TestRepo {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("airepo-v1.8.1-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    git -C $path init | Out-Null
    return $path
}

function Remove-TestRepo {
    param([string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside temp: $resolved"
    }

    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

$help = Invoke-AiRepo -Arguments @('--help')
if ($help -notmatch '\[--repo <path>\]' -or $help -notmatch '--no-hooks') {
    throw 'CLI help does not document optional --repo and --no-hooks.'
}

if ((Invoke-AiRepo -Arguments @('--version')).Trim() -ne '1.8.1') {
    throw 'CLI version is not 1.8.1.'
}

$bootstrapArgs = @(
    'bootstrap',
    '--apply',
    '--skip-ai-context',
    '--skip-code-index',
    '--skip-security-scan',
    '--skip-budget',
    '--skip-scripts',
    '--no-progress'
)

$defaultRepo = New-TestRepo
$optOutRepo = New-TestRepo
$setupPreviewRepo = New-TestRepo

try {
    Push-Location $defaultRepo
    try {
        $defaultOutput = Invoke-AiRepo -Arguments $bootstrapArgs
    }
    finally {
        Pop-Location
    }

    if ($defaultOutput -notmatch 'Git Hooks Status' -or $defaultOutput -notmatch 'Installed') {
        throw 'bootstrap did not install Git hooks by default.'
    }

    if ((git -C $defaultRepo config --local --get core.hooksPath) -ne '.githooks') {
        throw 'bootstrap did not configure core.hooksPath by default.'
    }

    foreach ($hook in @('pre-commit', 'post-merge', 'post-rewrite')) {
        if (-not (Test-Path -LiteralPath (Join-Path $defaultRepo ".githooks/$hook"))) {
            throw "bootstrap did not create hook: $hook"
        }
    }

    Push-Location $optOutRepo
    try {
        $optOutOutput = Invoke-AiRepo -Arguments ($bootstrapArgs + '--no-hooks')
    }
    finally {
        Pop-Location
    }

    if ($optOutOutput -notmatch 'Skipped by --no-hooks') {
        throw 'bootstrap --no-hooks did not report the opt-out.'
    }

    if (Test-Path -LiteralPath (Join-Path $optOutRepo '.githooks')) {
        throw 'bootstrap --no-hooks created hook files.'
    }

    $configuredOptOut = git -C $optOutRepo config --local --get core.hooksPath
    if (-not [string]::IsNullOrWhiteSpace($configuredOptOut)) {
        throw 'bootstrap --no-hooks configured core.hooksPath.'
    }

    Push-Location $setupPreviewRepo
    try {
        $setupPreview = Invoke-AiRepo -Arguments @('setup', '--summary', '--no-progress')
    }
    finally {
        Pop-Location
    }

    if ($setupPreview -notmatch 'preview git hooks') {
        throw 'setup preview did not include the default Git hooks phase.'
    }

    if (Test-Path -LiteralPath (Join-Path $setupPreviewRepo '.githooks')) {
        throw 'setup preview wrote hook files.'
    }
}
finally {
    Remove-TestRepo $defaultRepo
    Remove-TestRepo $optOutRepo
    Remove-TestRepo $setupPreviewRepo
}

Write-Host 'v1.8.1 default bootstrap/setup Git hooks and optional repo smoke tests passed.'
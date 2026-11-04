param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)

$updaterPath = Join-Path $repoRoot 'scripts/airepo-update.ps1'
$gitExe = (Get-Command git.exe -CommandType Application -ErrorAction Stop).Source

# Read only the definitions under test. Do not execute the updater entrypoint.
$tokens = $null
$parseErrors = $null

$ast = [Management.Automation.Language.Parser]::ParseFile(
    $updaterPath,
    [ref]$tokens,
    [ref]$parseErrors
)

if ($parseErrors.Count -gt 0) {
    throw "Updater parse failure: $($parseErrors[0].Message)"
}

$definitions = @(
    $ast.FindAll({
        param($node)

        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -in @(
            'Test-RealRepo',
            'Test-AiRepoKitEnabledRepo'
        )
    }, $true)
)

if ($definitions.Count -ne 2) {
    throw 'Expected exactly two repository detection functions.'
}

$functionSource = (
    $definitions |
        ForEach-Object { $_.Extent.Text }
) -join [Environment]::NewLine

. ([scriptblock]::Create($functionSource))

function Invoke-FixtureGit {
    param(
        [string]$Directory,
        [string[]]$Arguments
    )

    $output = @(& $gitExe -C $Directory @Arguments 2>&1)
    $code = $LASTEXITCODE

    if ($code -ne 0) {
        throw "Fixture git exit=$code; $($Arguments -join ' '): $($output -join ' ')"
    }

    return ($output -join "`n").Trim()
}

function Assert-Condition {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

$fixtureParent = 'C:\Temp'
$fixtureRoot = Join-Path $fixtureParent (
    'REL4-P08-Regression-' + [Guid]::NewGuid().ToString('N')
)

$repositoryPath = Join-Path $fixtureRoot 'repository'
$linkedPath = Join-Path $fixtureRoot 'linked-worktree'
$emptyPath = Join-Path $fixtureRoot 'ordinary-directory'
$nestedPath = Join-Path $linkedPath 'nested'

$ownershipToken = 'REL4_P08_TEST_OWNED'
$ownerPath = Join-Path $fixtureRoot '.test-owner'

try {
    [IO.Directory]::CreateDirectory($fixtureParent) | Out-Null
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    [IO.File]::WriteAllText(
        $ownerPath,
        $ownershipToken
    )

    [IO.Directory]::CreateDirectory($repositoryPath) | Out-Null
    [IO.Directory]::CreateDirectory($emptyPath) | Out-Null

    # Create a disposable Git repository, not a project worktree.
    $null = Invoke-FixtureGit $repositoryPath @('init', '--quiet')

    $null = Invoke-FixtureGit $repositoryPath @(
        '-c', 'user.name=AI RepoKit Regression',
        '-c', 'user.email=regression@example.invalid',
        '-c', 'commit.gpgSign=false',
        '-c', 'core.hooksPath=.git/no-hooks',
        'commit', '--quiet', '--allow-empty', '-m', 'test fixture'
    )

    $null = Invoke-FixtureGit $repositoryPath @(
        'worktree', 'add', '--quiet', '--detach', $linkedPath, 'HEAD'
    )

    [IO.Directory]::CreateDirectory($nestedPath) | Out-Null

    $normalGit = Join-Path $repositoryPath '.git'
    $linkedGit = Join-Path $linkedPath '.git'

    Assert-Condition (
        [IO.Directory]::Exists($normalGit)
    ) 'Conventional Git repository has no .git directory.'

    Assert-Condition (
        [IO.File]::Exists($linkedGit)
    ) 'Linked Git worktree has no .git file.'

    $linkedGitRoot = Invoke-FixtureGit $linkedPath @(
        'rev-parse', '--show-toplevel'
    )

    Assert-Condition (
        [IO.Path]::GetFullPath($linkedGitRoot).TrimEnd('\', '/').Equals(
            [IO.Path]::GetFullPath($linkedPath).TrimEnd('\', '/'),
            [StringComparison]::OrdinalIgnoreCase
        )
    ) 'Linked worktree is not recognized by Git.'

    Assert-Condition (
        (Test-RealRepo $repositoryPath) -eq $true
    ) 'Conventional repository detection failed.'

    Assert-Condition (
        (Test-RealRepo $linkedPath) -eq $true
    ) 'Linked worktree detection failed.'

    Assert-Condition (
        (Test-RealRepo $nestedPath) -eq $false
    ) 'Nested directory was incorrectly recognized as repository.'

    Assert-Condition (
        (Test-RealRepo $emptyPath) -eq $false
    ) 'Ordinary directory was incorrectly recognized as repository.'

    # Test the updater's eligibility filter using both supported markers.
    [IO.Directory]::CreateDirectory(
        (Join-Path $repositoryPath '.ai')
    ) | Out-Null

    $manifestDir = Join-Path $linkedPath '.config'
    [IO.Directory]::CreateDirectory($manifestDir) | Out-Null

    [IO.File]::WriteAllText(
        (Join-Path $manifestDir 'dotnet-tools.json'),
        '{"version":1,"tools":{"airepokit.cli":{"version":"3.0.0"}}}'
    )

    Assert-Condition (
        (Test-AiRepoKitEnabledRepo $repositoryPath) -eq $true
    ) 'Conventional repository eligibility failed.'

    Assert-Condition (
        (Test-AiRepoKitEnabledRepo $linkedPath) -eq $true
    ) 'Linked worktree eligibility failed.'

    Assert-Condition (
        (Test-AiRepoKitEnabledRepo $emptyPath) -eq $false
    ) 'Ordinary directory incorrectly passed eligibility.'

    Write-Output 'REAL_GIT_LINKED_WORKTREE=PASS'
    Write-Output 'NORMAL_REPOSITORY=PASS'
    Write-Output 'LINKED_REPOSITORY=PASS'
    Write-Output 'NESTED_DIRECTORY_REJECTION=PASS'
    Write-Output 'UPDATER_ELIGIBILITY=PASS'
}
finally {
    # Cleanup is restricted to this uniquely owned temporary fixture.
    if ([IO.Directory]::Exists($fixtureRoot)) {
        if (
            -not [IO.File]::Exists($ownerPath) -or
            [IO.File]::ReadAllText($ownerPath) -cne $ownershipToken
        ) {
            throw 'Temporary fixture ownership check failed. Cleanup refused.'
        }

        $expectedPrefix = [IO.Path]::GetFullPath(
            $fixtureParent
        ).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar

        if (-not [IO.Path]::GetFullPath($fixtureRoot).StartsWith(
            $expectedPrefix,
            [StringComparison]::OrdinalIgnoreCase
        )) {
            throw 'Fixture path escaped the approved temporary directory.'
        }

        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Output 'REL4_P08_REGRESSION=PASS'

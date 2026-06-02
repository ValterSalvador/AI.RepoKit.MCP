param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $RepoRoot 'src/AiRepoKit.Cli/AiRepoKit.Cli.csproj'
$dll = Join-Path $RepoRoot 'src/AiRepoKit.Cli/bin/Release/net10.0/AiRepoKit.Cli.dll'

if (-not (Test-Path -LiteralPath $dll)) {
    dotnet build $project -c Release | Out-Host
}

function Test-PathBoundaryTokenMatch {
    param(
        [string]$CommandLine,
        [string]$Path
    )

    $normalizedCommandLine = $CommandLine.Replace('\', '/').ToLowerInvariant()
    $normalizedPath = ([System.IO.Path]::GetFullPath($Path)).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Replace('\', '/').ToLowerInvariant()
    $startIndex = 0
    while ($startIndex -lt $normalizedCommandLine.Length) {
        $matchIndex = $normalizedCommandLine.IndexOf($normalizedPath, $startIndex, [System.StringComparison]::OrdinalIgnoreCase)
        if ($matchIndex -lt 0) {
            return $false
        }

        $afterIndex = $matchIndex + $normalizedPath.Length
        $hasStartBoundary = $matchIndex -eq 0 -or [char]::IsWhiteSpace($normalizedCommandLine[$matchIndex - 1]) -or $normalizedCommandLine[$matchIndex - 1] -eq '"' -or $normalizedCommandLine[$matchIndex - 1] -eq "'" -or $normalizedCommandLine[$matchIndex - 1] -eq '='
        $hasEndBoundary = $afterIndex -ge $normalizedCommandLine.Length -or $normalizedCommandLine[$afterIndex] -eq '/' -or [char]::IsWhiteSpace($normalizedCommandLine[$afterIndex]) -or $normalizedCommandLine[$afterIndex] -eq '"' -or $normalizedCommandLine[$afterIndex] -eq "'" -or $normalizedCommandLine[$afterIndex] -eq ';'
        if ($hasStartBoundary -and $hasEndBoundary) {
            return $true
        }

        $startIndex = $matchIndex + 1
    }

    return $false
}

function Assert-PathBoundaryMatch {
    param(
        [string]$CommandLine,
        [string]$Path,
        [bool]$Expected,
        [string]$Message
    )

    $actual = Test-PathBoundaryTokenMatch -CommandLine $CommandLine -Path $Path
    if ($actual -ne $Expected) {
        throw $Message
    }
}

$mcpHostProcessServicePath = Join-Path $RepoRoot 'src/AiRepoKit.Cli/Services/McpHostProcessService.cs'
$mcpHostProcessServiceSource = Get-Content -LiteralPath $mcpHostProcessServicePath -Raw
if ($mcpHostProcessServiceSource -notmatch 'IsCommandLinePathTokenMatch\(commandLine, normalizedRepoRoot_') {
    throw 'McpHostProcessService does not use path-boundary matching for the repo root.'
}

if ($mcpHostProcessServiceSource -notmatch 'IsCommandLinePathTokenMatch\(commandLine, normalizedMcpRoot_') {
    throw 'McpHostProcessService does not use path-boundary matching for Tools/AiContextMcp.'
}

if ($mcpHostProcessServiceSource -match 'commandLine\.Contains\(normalizedRepoRoot_') {
    throw 'McpHostProcessService still uses raw substring matching for the repo root.'
}

$repoRootForward = $RepoRoot.Replace('\', '/')
$siblingRootForward = $repoRootForward + 'Bar'
$toolsRootForward = (Join-Path $RepoRoot 'Tools/AiContextMcp').Replace('\', '/')
Assert-PathBoundaryMatch -CommandLine "dotnet AiRepo.ContextMcp.dll --repo `"$repoRootForward`"" -Path $RepoRoot -Expected $true -Message 'Path-boundary matcher rejected quoted repo root token.'
Assert-PathBoundaryMatch -CommandLine "dotnet AiRepo.ContextMcp.dll --repo=$repoRootForward --stdio" -Path $RepoRoot -Expected $true -Message 'Path-boundary matcher rejected repo root after argument equals boundary.'
Assert-PathBoundaryMatch -CommandLine "dotnet `"$toolsRootForward/bin/Release/net10.0/AiRepo.ContextMcp.dll`" --repo `"$repoRootForward`"" -Path (Join-Path $RepoRoot 'Tools/AiContextMcp') -Expected $true -Message 'Path-boundary matcher rejected Tools/AiContextMcp descendant token.'
Assert-PathBoundaryMatch -CommandLine "dotnet AiRepo.ContextMcp.dll --repo `"$siblingRootForward`"" -Path $RepoRoot -Expected $false -Message 'Path-boundary matcher accepted sibling repo prefix with forward slashes.'
Assert-PathBoundaryMatch -CommandLine "dotnet AiRepo.ContextMcp.dll --repo `"$($siblingRootForward.Replace('/', '\'))`"" -Path $RepoRoot -Expected $false -Message 'Path-boundary matcher accepted sibling repo prefix with backslashes.'

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

function Get-CodeIndexMetric {
    param(
        [string]$Output,
        [string[]]$Labels
    )

    foreach ($label in $Labels) {
        $escaped = [regex]::Escape($label)
        $match = [regex]::Match($Output, "(?im)^\s*-\s+$escaped\s*:\s*``?([0-9]+)``?\s*$")
        if ($match.Success) {
            return [int]$match.Groups[1].Value
        }
    }

    throw "code-index output did not include metric: $($Labels -join ' / ')"
}

function Assert-Contains {
    param(
        [string]$Output,
        [string]$Pattern,
        [string]$Message
    )

    if ($Output -notmatch $Pattern) {
        throw $Message
    }
}

$first = Invoke-AiRepo -Arguments @('code-index', '--repo', $RepoRoot, '--apply', '--rebuild-cache', '--timings', '--no-progress')
$firstDiscovered = Get-CodeIndexMetric -Output $first -Labels @('Files discovered')
$firstParsed = Get-CodeIndexMetric -Output $first -Labels @('Parsed files', 'ParsedFiles')

if ($firstDiscovered -le 0) {
    throw 'code-index discovered 0 .cs files; v1.7.0 incremental smoke requires at least one C# source file.'
}

if ($firstParsed -le 0) {
    throw "code-index rebuild did not parse any files. Files discovered: $firstDiscovered."
}

$second = Invoke-AiRepo -Arguments @('code-index', '--repo', $RepoRoot, '--apply', '--timings', '--no-progress')
$fastPathReused = Get-CodeIndexMetric -Output $second -Labels @('Fast-path reused files', 'FastPathReusedFiles')
$parsed = Get-CodeIndexMetric -Output $second -Labels @('Parsed files', 'ParsedFiles')
$hashValidations = Get-CodeIndexMetric -Output $second -Labels @('Hash validations', 'HashValidations', 'Hash validated files', 'HashValidatedFiles')

if ($fastPathReused -le 0) {
    throw 'Second code-index run did not report fast-path reuse.'
}

if ($parsed -ne 0) {
    throw "Second code-index run parsed $parsed file(s); expected 0."
}

if ($hashValidations -ne 0) {
    throw "Second code-index run performed $hashValidations hash validation(s); expected 0."
}

$changedDefault = Invoke-AiRepo -Arguments @('context-pack', '--repo', $RepoRoot, '--task', 'changed-files', '--apply', '--budget', '12000', '--limit', '30', '--no-progress')
Assert-Contains -Output $changedDefault -Pattern 'Existing compatible code inventories reused before context-pack generation' -Message 'changed-files context pack did not report compatible inventory reuse.'
if ($changedDefault -match 'Code-index generated before context-pack generation') {
    throw 'changed-files context pack generated code-index despite compatible inventories.'
}

$changedRebuild = Invoke-AiRepo -Arguments @('context-pack', '--repo', $RepoRoot, '--task', 'changed-files', '--apply', '--rebuild-index', '--budget', '12000', '--limit', '30', '--no-progress')
Assert-Contains -Output $changedRebuild -Pattern 'Code-index rebuilt before context-pack generation' -Message 'changed-files context pack --rebuild-index did not report rebuild.'

$changedSkip = Invoke-AiRepo -Arguments @('context-pack', '--repo', $RepoRoot, '--task', 'changed-files', '--apply', '--skip-code-index', '--budget', '12000', '--limit', '30', '--no-progress')
Assert-Contains -Output $changedSkip -Pattern 'freshness verification' -Message 'changed-files context pack --skip-code-index did not warn about unverified freshness.'

$inventoryRoot = Join-Path $RepoRoot '.ai/generated/inventories'
$backupRoot = Join-Path $RepoRoot ('.ai/generated/v1.7.0-smoke-inventory-backup-' + [guid]::NewGuid().ToString('N'))
$symbolInventory = Join-Path $inventoryRoot 'symbol-inventory.json'
$endpointInventory = Join-Path $inventoryRoot 'endpoint-inventory.json'
$moved = @()
try {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    foreach ($path in @($symbolInventory, $endpointInventory)) {
        if (Test-Path -LiteralPath $path) {
            $destination = Join-Path $backupRoot (Split-Path -Leaf $path)
            Move-Item -LiteralPath $path -Destination $destination
            $moved += [pscustomobject]@{ Source = $path; Backup = $destination }
        }
    }

    $changedSkipMissing = Invoke-AiRepo -Arguments @('context-pack', '--repo', $RepoRoot, '--task', 'changed-files', '--apply', '--skip-code-index', '--budget', '12000', '--limit', '30', '--no-progress')
    Assert-Contains -Output $changedSkipMissing -Pattern 'inventories are missing or incompatible' -Message 'changed-files --skip-code-index with missing inventories did not warn about missing/incompatible inventories.'
    Assert-Contains -Output $changedSkipMissing -Pattern 'without affected symbols' -Message 'changed-files --skip-code-index with missing inventories did not warn about missing affected-symbol enrichment.'

    $changedPackPath = Join-Path $RepoRoot '.ai/generated/context-packs/changed-files.json'
    $changedPack = Get-Content -LiteralPath $changedPackPath -Raw | ConvertFrom-Json
    if ($null -ne $changedPack.AffectedSymbols -and $changedPack.AffectedSymbols.Count -ne 0) {
        throw 'changed-files --skip-code-index with missing inventories should not include affected-symbol enrichment.'
    }
}
finally {
    foreach ($entry in $moved) {
        if (Test-Path -LiteralPath $entry.Backup) {
            if (Test-Path -LiteralPath $entry.Source) {
                Remove-Item -LiteralPath $entry.Source -Force
            }

            Move-Item -LiteralPath $entry.Backup -Destination $entry.Source
        }
    }

    if (Test-Path -LiteralPath $backupRoot) {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force
    }
}

$auditJson = Invoke-AiRepo -Arguments @('audit', '--repo', $RepoRoot, '--json', '--no-progress') -AllowedExitCodes @(0, 2)
$audit = $auditJson | ConvertFrom-Json

if ([int]$audit.activeHighSeverityCount -ne 0) {
    throw "Audit reported $($audit.activeHighSeverityCount) active high-severity finding(s); expected 0."
}

if ([int]$audit.reviewRequiredCount -ne 0) {
    throw "Audit reported $($audit.reviewRequiredCount) review-required finding(s); expected 0."
}

$rawLocalPathPattern = [regex]::Escape($RepoRoot)
$jsonEscapedRawLocalPathPattern = [regex]::Escape($RepoRoot.Replace('\', '\\'))
$programFilesPattern = 'Program' + ' Files(?: \([^\\]+\))?'
$windowsLocalPathPattern = '(?i)\b[A-Z]:\\(?:Users|Repositories|Temp|Windows\\Temp|' + $programFilesPattern + ')\\[^\s"''<>|]+'
$jsonEscapedWindowsLocalPathPattern = '(?i)\b[A-Z]:\\\\(?:Users|Repositories|Temp|Windows\\\\Temp|' + $programFilesPattern + ')\\\\[^\s"''<>|]+'

$help = Invoke-AiRepo -Arguments @('mcp-diagnose', '--help')
Assert-Contains -Output $help -Pattern '--stop-stale-mcp-hosts' -Message 'mcp-diagnose --help did not expose --stop-stale-mcp-hosts.'

$quickJson = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--quick', '--json', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
$quick = $quickJson | ConvertFrom-Json
if ($quick.Mode -ne 'quick') {
    throw 'mcp-diagnose --quick did not report quick mode.'
}

$quickBuild = $quick.Checks | Where-Object { $_.Name -eq 'mcp-build' } | Select-Object -First 1
if ($null -eq $quickBuild -or $quickBuild.Status -ne 'Skipped' -or $quickBuild.Message -notmatch 'quick mode') {
    throw 'mcp-diagnose --quick did not skip mcp-build with a quick-mode message.'
}

$quickBudget = $quick.Checks | Where-Object { $_.Name -eq 'budget' } | Select-Object -First 1
if ($null -eq $quickBudget -or $quickBudget.Status -ne 'Skipped') {
    throw 'mcp-diagnose --quick did not skip budget.'
}

$quickSmoke = $quick.Checks | Where-Object { $_.Name -eq 'smoke-test' } | Select-Object -First 1
if ($null -eq $quickSmoke -or $quickSmoke.Message -notmatch 'Minimal MCP smoke test passed') {
    throw 'mcp-diagnose --quick did not run minimal smoke.'
}

if (($quickSmoke.Details -join "`n") -match 'Resources:|Prompts:') {
    throw 'mcp-diagnose --quick minimal smoke unexpectedly included expanded resources/prompts details.'
}

foreach ($check in $quick.Checks) {
    if ($null -eq $check.ElapsedMilliseconds -or [string]::IsNullOrWhiteSpace([string]$check.Cost)) {
        throw "mcp-diagnose --quick check '$($check.Name)' did not include timing/cost metadata."
    }
}

$fullJson = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--full', '--json', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
$full = $fullJson | ConvertFrom-Json
if ($full.Mode -ne 'full') {
    throw 'mcp-diagnose --full did not report full mode.'
}

$fullBuild = $full.Checks | Where-Object { $_.Name -eq 'mcp-build' } | Select-Object -First 1
if ($null -eq $fullBuild -or ($fullBuild.Message -notmatch 'Built|SkippedCurrent')) {
    throw 'mcp-diagnose --full did not report built or current build freshness.'
}

if ($fullBuild.Message -match 'SkippedCurrent' -and (($fullBuild.Details -join "`n") -notmatch 'Freshness decision')) {
    throw 'mcp-diagnose --full SkippedCurrent build did not include a freshness decision detail.'
}

$fullSmoke = $full.Checks | Where-Object { $_.Name -eq 'smoke-test' } | Select-Object -First 1
if ($null -eq $fullSmoke -or $fullSmoke.Message -notmatch 'Expanded MCP smoke test passed') {
    throw 'mcp-diagnose --full did not run expanded smoke.'
}

$strictJson = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--strict', '--strict-stdio', '--json', '--verbose', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
if ($strictJson -match $rawLocalPathPattern -or $strictJson -match $jsonEscapedRawLocalPathPattern -or $strictJson -match $windowsLocalPathPattern -or $strictJson -match $jsonEscapedWindowsLocalPathPattern) {
    throw 'mcp-diagnose strict output contained a raw local path.'
}

$strict = $strictJson | ConvertFrom-Json
$strictSmoke = $strict.Checks | Where-Object { $_.Name -eq 'smoke-test' } | Select-Object -First 1
if ($null -eq $strictSmoke -or $strictSmoke.Status -eq 'Failed') {
    throw "mcp-diagnose strict smoke failed: $($strictSmoke.Message)"
}

$strictSmokeDetails = $strictSmoke.Details -join "`n"
if ($strictSmokeDetails -notmatch 'Tools:' -or $strictSmokeDetails -notmatch 'Resources:' -or $strictSmokeDetails -notmatch 'Prompts:' -or $strictSmokeDetails -notmatch 'stderr byte count:') {
    throw 'mcp-diagnose strict did not report tools, resources, prompts, and strict stdio details.'
}

Write-Host 'v1.7.0 incremental code-index, context-pack freshness, and mcp-diagnose cost-control smoke tests passed.'

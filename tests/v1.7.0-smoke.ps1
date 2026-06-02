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

Write-Host 'v1.7.0 incremental code-index and context-pack freshness smoke tests passed.'

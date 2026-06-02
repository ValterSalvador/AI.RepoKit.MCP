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

$auditJson = Invoke-AiRepo -Arguments @('audit', '--repo', $RepoRoot, '--json', '--no-progress') -AllowedExitCodes @(0, 2)
$audit = $auditJson | ConvertFrom-Json

if ([int]$audit.activeHighSeverityCount -ne 0) {
    throw "Audit reported $($audit.activeHighSeverityCount) active high-severity finding(s); expected 0."
}

if ([int]$audit.reviewRequiredCount -ne 0) {
    throw "Audit reported $($audit.reviewRequiredCount) review-required finding(s); expected 0."
}

Write-Host 'v1.7.0 incremental code-index smoke tests passed.'

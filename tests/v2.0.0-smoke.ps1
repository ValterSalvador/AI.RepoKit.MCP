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
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("airepo-v2.0.0-" + [Guid]::NewGuid().ToString('N'))
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

$version = (Invoke-AiRepo -Arguments @('--version')).Trim()
if ($version -ne '2.0.0') {
    throw "CLI version is not 2.0.0. Found: $version"
}

$rootMcpConfigPath = Join-Path $RepoRoot '.mcp.json'
if (-not (Test-Path -LiteralPath $rootMcpConfigPath)) {
    throw '.mcp.json was not found at repository root.'
}

$rootMcpConfig = Get-Content -LiteralPath $rootMcpConfigPath -Raw | ConvertFrom-Json
$rootServer = $rootMcpConfig.servers.ai_repo_context
if (-not $rootServer) {
    $rootServer = $rootMcpConfig.mcpServers.ai_repo_context
}

if (-not $rootServer) {
    throw '.mcp.json does not define an ai_repo_context server entry.'
}

if ($rootServer.command -ne 'airepo') {
    throw ".mcp.json ai_repo_context command is '$($rootServer.command)', expected 'airepo'."
}

$rootArgs = @($rootServer.args)
$repoArgIndex = [Array]::IndexOf($rootArgs, '--repo')
if ($repoArgIndex -lt 2) {
    throw '.mcp.json ai_repo_context args do not include the portable mcp serve --repo launch contract.'
}

if ($rootArgs[$repoArgIndex - 2] -ne 'mcp' -or $rootArgs[$repoArgIndex - 1] -ne 'serve') {
    throw '.mcp.json ai_repo_context args are not equivalent to: mcp serve --repo <repo>.'
}

if ($repoArgIndex -ge $rootArgs.Count - 1 -or [string]::IsNullOrWhiteSpace([string]$rootArgs[$repoArgIndex + 1])) {
    throw '.mcp.json ai_repo_context --repo value is missing.'
}

$tempRepo = New-TestRepo
try {
    $setupPreview = Invoke-AiRepo -Arguments @('setup', '--repo', $tempRepo, '--summary', '--no-progress') -AllowedExitCodes @(0, 2)
    if ($setupPreview -notmatch 'preview' -and $setupPreview -notmatch 'dry-run') {
        throw 'setup preview did not report preview/dry-run behavior.'
    }

    $portableConfig = [ordered]@{
        servers = [ordered]@{
            ai_repo_context = [ordered]@{
                transport = 'stdio'
                command = 'airepo'
                args = @('mcp', 'serve', '--repo', $tempRepo)
                cwd = $tempRepo
            }
        }
    }

    $portableConfig | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $tempRepo '.mcp.json') -Encoding utf8

    $legacyDllPath = Join-Path $tempRepo 'Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll'
    if (Test-Path -LiteralPath $legacyDllPath) {
        throw 'Unexpected legacy MCP DLL exists in temp repository before diagnose.'
    }

    $diagnoseOutput = Invoke-AiRepo -Arguments @(
        'mcp-diagnose',
        '--repo', $tempRepo,
        '--quick',
        '--skip-build',
        '--strict-stdio',
        '--summary',
        '--no-progress'
    ) -AllowedExitCodes @(0, 2)

    if ($diagnoseOutput -notmatch 'portable' -and $diagnoseOutput -notmatch 'mcp serve --repo') {
        throw 'mcp-diagnose did not report the portable launch contract.'
    }

    if (Test-Path -LiteralPath $legacyDllPath) {
        throw 'Portable MCP diagnose produced a legacy MCP DLL dependency artifact.'
    }
}
finally {
    Remove-TestRepo $tempRepo
}

Write-Host 'v2.0.0 portable runtime and release smoke tests passed.'

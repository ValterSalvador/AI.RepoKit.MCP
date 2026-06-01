param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipV143
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $RepoRoot 'src/AiRepoKit.Cli/AiRepoKit.Cli.csproj'
$dll = Join-Path $RepoRoot 'src/AiRepoKit.Cli/bin/Debug/net10.0/AiRepoKit.Cli.dll'
$mcpProject = Join-Path $RepoRoot 'Tools/AiContextMcp/AiRepo.ContextMcp.csproj'
$mcpDll = Join-Path $RepoRoot 'Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll'

dotnet build $project | Out-Host
dotnet build $mcpProject -c Release | Out-Host

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

function Invoke-McpMethod {
    param(
        [string]$Method,
        [object]$Params,
        [int]$Id
    )

    $stdoutLines = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
    $stderrLines = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))

    function Send-JsonRpc {
        param($Message)
        $process.StandardInput.WriteLine(($Message | ConvertTo-Json -Depth 20 -Compress))
        $process.StandardInput.Flush()
    }

    function Wait-JsonRpcResponse {
        param([int]$ResponseId, [int]$TimeoutSeconds = 20)
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $index = 0

        while ([DateTime]::UtcNow -lt $deadline) {
            while ($index -lt $stdoutLines.Count) {
                $line = [string]$stdoutLines[$index]
                $index++
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                try {
                    $json = $line | ConvertFrom-Json
                }
                catch {
                    continue
                }

                if ($null -ne $json.id -and [int]$json.id -eq $ResponseId) {
                    return $json
                }
            }

            Start-Sleep -Milliseconds 50
        }

        throw "No response received for $Method."
    }

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'dotnet'
    $psi.Arguments = '"' + $mcpDll + '" --repo "' + $RepoRoot + '"'
    $psi.WorkingDirectory = $RepoRoot
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8

    $process = [System.Diagnostics.Process]::Start($psi)
    $outputEvent = $null
    $errorEvent = $null
    try {
        $outputAction = {
            if ($null -ne $EventArgs.Data) {
                [void]$Event.MessageData.Add($EventArgs.Data)
            }
        }
        $errorAction = {
            if ($null -ne $EventArgs.Data) {
                [void]$Event.MessageData.Add($EventArgs.Data)
            }
        }
        $outputEvent = Register-ObjectEvent -InputObject $process -EventName OutputDataReceived -Action $outputAction -MessageData $stdoutLines
        $errorEvent = Register-ObjectEvent -InputObject $process -EventName ErrorDataReceived -Action $errorAction -MessageData $stderrLines
        $process.BeginOutputReadLine()
        $process.BeginErrorReadLine()

        Send-JsonRpc @{
            jsonrpc = '2.0'
            id = 1
            method = 'initialize'
            params = @{
                protocolVersion = '2024-11-05'
                capabilities = @{}
                clientInfo = @{
                    name = 'v1.5.0-smoke'
                    version = '1.0.0'
                }
            }
        }
        $null = Wait-JsonRpcResponse -ResponseId 1

        Send-JsonRpc @{
            jsonrpc = '2.0'
            method = 'notifications/initialized'
            params = @{}
        }

        Send-JsonRpc @{
            jsonrpc = '2.0'
            id = $Id
            method = $Method
            params = $Params
        }

        $response = Wait-JsonRpcResponse -ResponseId $Id

        $process.StandardInput.Close()
        [void]$process.WaitForExit(5000)

        $stderr = @($stderrLines) -join [Environment]::NewLine
        if ($stderrLines.Count -gt 0) {
            throw "MCP stderr was not empty: $stderr"
        }

        if ($null -ne $response.error) {
            throw "$Method returned a JSON-RPC error."
        }

        return $response
    }
    finally {
        if (-not $process.HasExited) {
            try {
                $process.StandardInput.Close()
            }
            catch {
            }
            $process.Kill()
        }

        if ($null -ne $outputEvent) {
            Unregister-Event -SubscriptionId $outputEvent.Id -ErrorAction SilentlyContinue
        }

        if ($null -ne $errorEvent) {
            Unregister-Event -SubscriptionId $errorEvent.Id -ErrorAction SilentlyContinue
        }
    }
}

$resources = Invoke-McpMethod -Method 'resources/list' -Params @{} -Id 2
$resourceText = $resources | ConvertTo-Json -Depth 20
foreach ($uri in @('repo://brief', 'repo://health', 'repo://context/changed-files')) {
    if ($resourceText -notmatch [regex]::Escape($uri)) {
        throw "resources/list did not include $uri."
    }
}

$brief = Invoke-McpMethod -Method 'resources/read' -Params @{ uri = 'repo://brief' } -Id 3
$briefText = $brief | ConvertTo-Json -Depth 20
if ($briefText -notmatch 'RepoName' -and $briefText -notmatch 'repoName') {
    throw 'resources/read repo://brief did not return repository brief content.'
}

$prompts = Invoke-McpMethod -Method 'prompts/list' -Params @{} -Id 4
$promptText = $prompts | ConvertTo-Json -Depth 20
foreach ($prompt in @('ai-repo.help', 'ai-repo.review-risk')) {
    if ($promptText -notmatch [regex]::Escape($prompt)) {
        throw "prompts/list did not include $prompt."
    }
}

$helpPrompt = Invoke-McpMethod -Method 'prompts/get' -Params @{ name = 'ai-repo.help'; arguments = @{} } -Id 5
$helpText = $helpPrompt | ConvertTo-Json -Depth 20
if ($helpText -notmatch 'get_repo_brief' -or $helpText -notmatch 'repo://brief') {
    throw 'prompts/get ai-repo.help did not return expected help content.'
}

$reviewPrompt = Invoke-McpMethod -Method 'prompts/get' -Params @{ name = 'ai-repo.review-risk'; arguments = @{} } -Id 6
$reviewText = $reviewPrompt | ConvertTo-Json -Depth 20
if ($reviewText -notmatch 'changed-files' -or $reviewText -notmatch 'search_context') {
    throw 'prompts/get ai-repo.review-risk did not return expected review workflow.'
}

$strictJson = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--strict', '--strict-stdio', '--json', '--verbose', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
$strict = $strictJson | ConvertFrom-Json
$smoke = $strict.Checks | Where-Object { $_.Name -eq 'smoke-test' } | Select-Object -First 1
if ($null -eq $smoke) {
    throw 'mcp-diagnose strict did not include a smoke-test check.'
}

$smokeDetails = $smoke.Details -join "`n"
if ($smokeDetails -notmatch 'Resources:' -or $smokeDetails -notmatch 'Prompts:' -or $smokeDetails -notmatch 'stderr byte count:') {
    throw 'mcp-diagnose strict did not report resources, prompts, and strict stdio details.'
}

if ($smoke.Status -eq 'Failed') {
    throw "mcp-diagnose strict smoke failed: $($smoke.Message)"
}

if (-not $SkipV143) {
    & (Join-Path $RepoRoot 'tests/v1.4.3-smoke.ps1') -RepoRoot $RepoRoot -SkipV142
    if ($LASTEXITCODE -ne 0) {
        throw 'v1.4.3 smoke test failed.'
    }
}

Write-Host 'v1.5.0 smoke tests passed.'

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $RepoRoot 'src/AiRepoKit.Cli/AiRepoKit.Cli.csproj'
$dll = Join-Path $RepoRoot 'src/AiRepoKit.Cli/bin/Debug/net10.0/AiRepoKit.Cli.dll'
$mcpDll = Join-Path $RepoRoot 'Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll'

dotnet build $project | Out-Host

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

$strictJson = Invoke-AiRepo -Arguments @('mcp-diagnose', '--repo', $RepoRoot, '--strict-stdio', '--json', '--verbose', '--timings', '--no-progress') -AllowedExitCodes @(0, 2)
$strict = $strictJson | ConvertFrom-Json
$smoke = $strict.Checks | Where-Object { $_.Name -eq 'smoke-test' } | Select-Object -First 1
if ($null -eq $smoke) {
    throw 'mcp-diagnose --strict-stdio did not include a smoke-test check.'
}

if (($smoke.Details -join "`n") -notmatch 'stderr byte count:') {
    throw 'strict stdio smoke details did not report stderr byte count.'
}

if ($smoke.Status -eq 'Failed' -and $smoke.Message -notmatch 'strict stdio') {
    throw 'strict stdio smoke failed for a reason other than stderr enforcement.'
}

function Invoke-Mcp {
    param(
        [string]$ToolName,
        [object]$Arguments,
        [int]$Id
    )

    $stdoutLines = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
    $stderrLines = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))

    function Send-JsonRpc {
        param($Message)
        $process.StandardInput.WriteLine(($Message | ConvertTo-Json -Depth 10 -Compress))
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

        throw "No response received for $ToolName."
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
                    name = 'v1.4.2-smoke'
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
            method = 'tools/call'
            params = @{
                name = $ToolName
                arguments = $Arguments
            }
        }

        $response = Wait-JsonRpcResponse -ResponseId $Id

        $process.StandardInput.Close()
        [void]$process.WaitForExit(5000)

        $stderr = @($stderrLines) -join [Environment]::NewLine
        if ($stderrLines.Count -gt 0) {
            throw "MCP stderr was not empty: $stderr"
        }

        if ($null -eq $response) {
            throw "No response received for $ToolName."
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

$capabilities = Invoke-Mcp -ToolName 'get_health' -Arguments @{ area = 'capabilities' } -Id 2
$capabilityText = $capabilities | ConvertTo-Json -Depth 20
if ($capabilityText -notmatch 'supportedContextKinds' -or $capabilityText -notmatch 'readOnlyMode') {
    throw 'get_health area=capabilities did not return expected capability metadata.'
}

$policy = Invoke-Mcp -ToolName 'get_policy' -Arguments @{ topic = 'all' } -Id 3
$policyText = $policy | ConvertTo-Json -Depth 20
if ($policyText -notmatch 'serverMode' -or $policyText -notmatch 'read-only' -or $policyText -notmatch 'commandExecution') {
    throw 'get_policy did not return the expanded read-only policy.'
}

& (Join-Path $RepoRoot 'tests/v1.4.0-smoke.ps1') -RepoRoot $RepoRoot

Write-Host 'v1.4.2 smoke tests passed.'

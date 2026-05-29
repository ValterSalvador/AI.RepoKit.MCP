param(
    [string]$RepoRoot = "",
    [int]$StartupTimeoutSeconds = 20,
    [int]$ToolTimeoutSeconds = 30,
    [switch]$JsonOnly,
    [switch]$FailOnBudget,
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

function Get-Utf8ByteCount {
    param([string]$Value)
    if ($null -eq $Value) {
        return 0
    }

    return [Text.Encoding]::UTF8.GetByteCount($Value)
}

function ConvertTo-JsonText {
    param($Value, [int]$Depth = 40)
    return ($Value | ConvertTo-Json -Depth $Depth -Compress)
}

function Add-WarningValue {
    param([string]$Value)
    if (-not [string]::IsNullOrWhiteSpace($Value) -and -not $warnings.Contains($Value)) {
        [void]$warnings.Add($Value)
    }
}

function Add-FailureValue {
    param([string]$Value)
    if (-not [string]::IsNullOrWhiteSpace($Value) -and -not $failures.Contains($Value)) {
        [void]$failures.Add($Value)
    }
}

function Test-SecretExposure {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) {
        return $false
    }

    $patterns = @(
        '(?i)\bpassword\s*[=:]\s*[^;\s,"''{}\]]+',
        '(?i)\bsecret\s*=\s*[^;\s,"''{}\]]+',
        '(?i)\btoken\s*=\s*[^;\s,"''{}\]]+',
        '(?i)\bclientSecret\s*[=:]\s*[^;\s,"''{}\]]+',
        '(?i)\bprivateKey\s*[=:]\s*[^;\s,"''{}\]]+',
        '(?i)\bconnectionString\s*[=:]\s*[^;\n\r,"''{}\]]+',
        '(?i)\bUser\s+Id\s*=\s*[^;]+;\s*Password\s*=\s*[^;]+'
    )

    foreach ($pattern in $patterns) {
        if ($Text -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-RedactionMarker {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) {
        return $false
    }

    return $Text -match '(?i)<redacted>|redacted|\*\*\*'
}

function Find-PropertyValue {
    param($Value, [string]$Name)
    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            $found = Find-PropertyValue $item $Name
            if ($null -ne $found) {
                return $found
            }
        }

        return $null
    }

    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ([string]::Equals([string]$key, $Name, [StringComparison]::OrdinalIgnoreCase)) {
                return $Value[$key]
            }

            $found = Find-PropertyValue $Value[$key] $Name
            if ($null -ne $found) {
                return $found
            }
        }

        return $null
    }

    $properties = @($Value.PSObject.Properties)
    foreach ($property in $properties) {
        if ([string]::Equals($property.Name, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $property.Value
        }

        $found = Find-PropertyValue $property.Value $Name
        if ($null -ne $found) {
            return $found
        }
    }

    return $null
}

function Get-ToolEnvelopeSource {
    param($Response)

    if ($null -ne $Response.result) {
        if ($null -ne $Response.result.structuredContent) {
            return $Response.result.structuredContent
        }

        if ($null -ne $Response.result.content) {
            foreach ($item in @($Response.result.content)) {
                if ($null -ne $item.text -and -not [string]::IsNullOrWhiteSpace([string]$item.text)) {
                    try {
                        return ([string]$item.text | ConvertFrom-Json)
                    }
                    catch {
                        return $item
                    }
                }
            }
        }
    }

    $structured = Find-PropertyValue $Response "structuredContent"
    if ($null -ne $structured) {
        return $structured
    }

    $content = Find-PropertyValue $Response "content"
    if ($content -is [System.Array]) {
        foreach ($item in $content) {
            $text = Find-PropertyValue $item "text"
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                try {
                    return ($text | ConvertFrom-Json)
                }
                catch {
                    return $item
                }
            }
        }
    }

    return $Response
}

function New-JsonRpcRequest {
    param([int]$Id, [string]$Method, $Params)
    $request = [ordered]@{
        jsonrpc = "2.0"
        id = $Id
        method = $Method
    }

    if ($null -ne $Params) {
        $request.params = $Params
    }

    return (ConvertTo-JsonText $request)
}

function Send-JsonRpc {
    param([string]$Text)
    if ($VerboseOutput) {
        Write-Host "JSON-RPC request: $Text"
    }

    $process.StandardInput.WriteLine($Text)
    $process.StandardInput.Flush()
}

function Wait-JsonRpcResponse {
    param([int]$Id, [int]$StartIndex, [int]$TimeoutSeconds)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $index = $StartIndex

    while ([DateTime]::UtcNow -lt $deadline) {
        while ($index -lt $stdoutLines.Count) {
            $line = [string]$stdoutLines[$index]
            $index++
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            try {
                $message = $line | ConvertFrom-Json
            }
            catch {
                $script:stdoutHadRawLogs = $true
                continue
            }

            if ($null -ne $message.id -and [int]$message.id -eq $Id) {
                return [ordered]@{
                    Response = $message
                    Raw = $line
                    NextIndex = $index
                }
            }
        }

        Start-Sleep -Milliseconds 50
    }

    throw "Timed out waiting for JSON-RPC response id $Id."
}

function Invoke-JsonRpc {
    param([int]$Id, [string]$Method, $Params, [int]$TimeoutSeconds)
    $startIndex = $stdoutLines.Count
    Send-JsonRpc (New-JsonRpcRequest $Id $Method $Params)
    return (Wait-JsonRpcResponse $Id $startIndex $TimeoutSeconds)
}

function Invoke-ToolCall {
    param([int]$Id, [string]$Name, $Arguments, [int]$BudgetBytes)
    $params = [ordered]@{
        name = $Name
        arguments = $Arguments
    }
    $responseItem = Invoke-JsonRpc $Id "tools/call" $params $ToolTimeoutSeconds
    $response = $responseItem.Response
    $raw = $responseItem.Raw
    $source = Get-ToolEnvelopeSource $response
    $sourceJson = ConvertTo-JsonText $source
    $sizeBytes = Get-Utf8ByteCount $raw
    $hasError = $null -ne $response.error
    $isToolError = Find-PropertyValue $response "isError"
    $hasSecretValueExposure = Test-SecretExposure $sourceJson
    $hasRedactionMarker = Test-RedactionMarker $sourceJson
    $secretsExposed = Find-PropertyValue $source "secretsExposed"
    $secretValuesReturned = Find-PropertyValue $source "secretValuesReturned"
    $redactedOnly = Find-PropertyValue $source "redactedOnly"
    $estimatedSizeBytes = Find-PropertyValue $source "estimatedSizeBytes"
    $tokenCostHint = Find-PropertyValue $source "tokenCostHint"
    $notes = New-Object System.Collections.ArrayList

    if ($hasError -or ($null -ne $isToolError -and [bool]$isToolError)) {
        [void]$notes.Add("JSON-RPC error returned.")
    }

    if ($sizeBytes -gt $BudgetBytes) {
        [void]$notes.Add("Response exceeded budget.")
    }

    if ($hasSecretValueExposure) {
        [void]$notes.Add("Potential sensitive value pattern detected; value omitted.")
    }

    if ($script:stdoutHadRawLogs) {
        [void]$notes.Add("stdout contained non JSON-RPC lines.")
    }

    $success = -not $hasError -and -not ($null -ne $isToolError -and [bool]$isToolError)
    $passed = $success -and $sizeBytes -le $BudgetBytes -and -not $hasSecretValueExposure -and -not $script:stdoutHadRawLogs

    return [ordered]@{
        Name = $Name
        Success = $success
        SizeBytes = $sizeBytes
        BudgetBytes = $BudgetBytes
        TokenCostHint = if ($null -eq $tokenCostHint) { "" } else { [string]$tokenCostHint }
        EstimatedSizeBytes = if ($null -eq $estimatedSizeBytes) { 0 } else { [int]$estimatedSizeBytes }
        HasRawLogs = [bool]$script:stdoutHadRawLogs
        HasSecretValueExposure = [bool]$hasSecretValueExposure
        HasRedactionMarker = [bool]$hasRedactionMarker
        SecretsExposed = if ($null -eq $secretsExposed) { $false } else { [bool]$secretsExposed }
        SecretValuesReturned = if ($null -eq $secretValuesReturned) { $false } else { [bool]$secretValuesReturned }
        RedactedOnly = if ($null -eq $redactedOnly) { $false } else { [bool]$redactedOnly }
        Passed = [bool]$passed
        Notes = @($notes)
    }
}

function Stop-McpProcess {
    if ($null -eq $process) {
        return
    }

    try {
        if (-not $process.HasExited) {
            try {
                $process.StandardInput.Close()
            }
            catch {
            }

            if (-not $process.WaitForExit(2000)) {
                $process.Kill()
                [void]$process.WaitForExit(5000)
            }
        }
    }
    catch {
        Add-WarningValue "Failed to stop MCP process cleanly."
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$aiRoot = Join-Path $RepoRoot ".ai"
$reportsRoot = Join-Path $aiRoot "generated/reports"
New-Item -ItemType Directory -Path $reportsRoot -Force | Out-Null

$dll = Join-Path $RepoRoot "Tools/AiContextMcp/bin/Release/{{TargetFramework}}/{{McpAssemblyName}}.dll"
$manifest = Join-Path $RepoRoot ".ai/manifests/mcp-context-manifest.json"
$fallbackManifest = Join-Path $RepoRoot ".ai/mcp-context-manifest.json"
$manifestPath = if (Test-Path -LiteralPath $manifest) { $manifest } elseif (Test-Path -LiteralPath $fallbackManifest) { $fallbackManifest } else { $null }
$jsonReportPath = Join-Path $reportsRoot "mcp-budget-report.json"
$markdownReportPath = Join-Path $reportsRoot "mcp-budget-report.md"
$failures = New-Object System.Collections.ArrayList
$warnings = New-Object System.Collections.ArrayList
$results = New-Object System.Collections.ArrayList
$toolsListed = @()
$stdoutLines = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
$stderrLines = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
$script:stdoutHadRawLogs = $false
$process = $null
$exitCode = 0

try {
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "MCP DLL not found: $dll"
    }

    if (-not $manifestPath) {
        throw "MCP manifest not found."
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = "`"$dll`" --repo `"$RepoRoot`""
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [Text.Encoding]::UTF8

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()

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

    $initializeParams = [ordered]@{
        protocolVersion = "2024-11-05"
        capabilities = [ordered]@{}
        clientInfo = [ordered]@{
            name = "MeasureMcpResponseBudget"
            version = "1.0.0"
        }
    }
    $initializeResponse = Invoke-JsonRpc 1 "initialize" $initializeParams $StartupTimeoutSeconds
    if ($null -ne $initializeResponse.Response.error) {
        throw "MCP initialize failed."
    }

    Send-JsonRpc (ConvertTo-JsonText ([ordered]@{
        jsonrpc = "2.0"
        method = "notifications/initialized"
        params = [ordered]@{}
    }))

    $toolsResponse = Invoke-JsonRpc 2 "tools/list" ([ordered]@{}) $ToolTimeoutSeconds
    if ($null -ne $toolsResponse.Response.error) {
        throw "MCP tools/list failed."
    }

    $toolValues = Find-PropertyValue $toolsResponse.Response "tools"
    if ($null -ne $toolValues) {
        $toolsListed = @($toolValues | ForEach-Object {
            $toolName = Find-PropertyValue $_ "name"
            if ($null -ne $toolName) {
                [string]$toolName
            }
        })
    }

    $calls = @(
        [ordered]@{ Id = 3; Name = "get_repo_brief"; Arguments = [ordered]@{}; Budget = 4096; Label = "get_repo_brief" },
        [ordered]@{ Id = 4; Name = "get_repo_brief"; Arguments = [ordered]@{ taskHint = "change a Blazor page" }; Budget = 4096; Label = "get_repo_brief taskHint" },
        [ordered]@{ Id = 5; Name = "get_context"; Arguments = [ordered]@{ kind = "packages"; detail = "brief"; limit = 5 }; Budget = 4096; Label = "get_context packages brief" },
        [ordered]@{ Id = 6; Name = "get_context"; Arguments = [ordered]@{ kind = "security"; detail = "brief"; limit = 5 }; Budget = 8192; Label = "get_context security brief" },
        [ordered]@{ Id = 7; Name = "get_health"; Arguments = [ordered]@{ area = "all" }; Budget = 4096; Label = "get_health all" },
        [ordered]@{ Id = 8; Name = "search_context"; Arguments = [ordered]@{ query = "AutoMapper"; limit = 5 }; Budget = 4096; Label = "search_context AutoMapper" },
        [ordered]@{ Id = 9; Name = "get_context"; Arguments = [ordered]@{ kind = "symbols"; detail = "brief"; limit = 5 }; Budget = 8192; Label = "get_context symbols brief" },
        [ordered]@{ Id = 10; Name = "get_context"; Arguments = [ordered]@{ kind = "endpoints"; detail = "brief"; limit = 5 }; Budget = 8192; Label = "get_context endpoints brief" },
        [ordered]@{ Id = 11; Name = "get_policy"; Arguments = [ordered]@{ topic = "secrets" }; Budget = 4096; Label = "get_policy secrets" }
    )

    foreach ($call in $calls) {
        $result = Invoke-ToolCall $call.Id $call.Name $call.Arguments $call.Budget
        $result.Name = $call.Label
        [void]$results.Add($result)
        if (-not $result.Passed) {
            Add-FailureValue "$($call.Label) failed smoke validation."
        }
    }
}
catch {
    Add-FailureValue $_.Exception.Message
    $exitCode = 1
}
finally {
    Stop-McpProcess
    if ($null -ne $outputEvent) {
        Unregister-Event -SubscriptionId $outputEvent.Id -ErrorAction SilentlyContinue
    }

    if ($null -ne $errorEvent) {
        Unregister-Event -SubscriptionId $errorEvent.Id -ErrorAction SilentlyContinue
    }
}

if ($script:stdoutHadRawLogs) {
    Add-WarningValue "stdout contained non JSON-RPC lines; stdout must be reserved for JSON-RPC."
}

if ($stderrLines.Count -gt 0) {
    Add-WarningValue "stderr contained $($stderrLines.Count) log line(s)."
}

$passed = $failures.Count -eq 0
if ($exitCode -eq 0 -and (-not $passed -or ($FailOnBudget -and $failures.Count -gt 0))) {
    $exitCode = 2
}

$report = [ordered]@{
    GeneratedAtLocal = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    RepoRoot = $RepoRoot
    McpAssembly = $dll
    McpAssemblyExists = Test-Path -LiteralPath $dll
    Manifest = $manifestPath
    ToolsListed = @($toolsListed)
    Results = @($results)
    Passed = [bool]$passed
    Failures = @($failures)
    Warnings = @($warnings)
    StderrLineCount = $stderrLines.Count
    StdoutLineCount = $stdoutLines.Count
}

$reportJson = $report | ConvertTo-Json -Depth 50
$reportJson | Set-Content -LiteralPath $jsonReportPath -Encoding UTF8

$markdown = New-Object System.Collections.ArrayList
[void]$markdown.Add("# MCP Budget Report")
[void]$markdown.Add("")
[void]$markdown.Add("- RepoRoot: $RepoRoot")
[void]$markdown.Add("- MCP DLL: $dll")
[void]$markdown.Add("- Manifest: $manifestPath")
[void]$markdown.Add("- Tools listed: $(@($toolsListed) -join ', ')")
[void]$markdown.Add("")
[void]$markdown.Add("| Call | Bytes | Budget | TokenCostHint | Passed |")
[void]$markdown.Add("| --- | ---: | ---: | --- | --- |")
foreach ($result in $results) {
    [void]$markdown.Add("| $($result.Name) | $($result.SizeBytes) | $($result.BudgetBytes) | $($result.TokenCostHint) | $($result.Passed) |")
}

[void]$markdown.Add("")
[void]$markdown.Add("## Failures")
if ($failures.Count -eq 0) {
    [void]$markdown.Add("- None")
}
else {
    foreach ($failure in $failures) {
        [void]$markdown.Add("- $failure")
    }
}

[void]$markdown.Add("")
[void]$markdown.Add("## Warnings")
if ($warnings.Count -eq 0) {
    [void]$markdown.Add("- None")
}
else {
    foreach ($warning in $warnings) {
        [void]$markdown.Add("- $warning")
    }
}

[void]$markdown.Add("")
[void]$markdown.Add("stderr may contain logs; stdout must contain only JSON-RPC.")
[void]$markdown.Add("No sensitive value is displayed in this report.")
$markdown | Set-Content -LiteralPath $markdownReportPath -Encoding UTF8

if ($JsonOnly) {
    $reportJson
}
else {
    $markdown -join [Environment]::NewLine
    ""
    "JSON report: $jsonReportPath"
}

exit $exitCode

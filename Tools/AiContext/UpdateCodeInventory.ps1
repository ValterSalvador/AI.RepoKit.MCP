param(
    [string]$RepoRoot = "",
    [int]$MaxFiles = 2000,
    [int]$MaxItems = 5000,
    [switch]$IncludePrivateMembers
)

$ErrorActionPreference = "Stop"

function ConvertTo-RelativePath {
    param([string]$Root, [string]$Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $pathFull = [IO.Path]::GetFullPath($Path)
    if ($pathFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $pathFull.Substring($rootFull.Length).Replace("\", "/")
    }

    return $pathFull.Replace("\", "/")
}

function Test-IgnoredDirectory {
    param([string]$RelativePath)
    $path = $RelativePath.Replace("\", "/").Trim("/")
    foreach ($ignored in $ignoredDirectories) {
        $value = $ignored.Replace("\", "/").Trim("/")
        if ($path.Equals($value, [StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($value + "/", [StringComparison]::OrdinalIgnoreCase) -or $path.IndexOf("/" + $value + "/", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Test-IgnoredFile {
    param([string]$FileName)
    foreach ($pattern in $ignoredFiles) {
        if ($FileName -like $pattern) {
            return $true
        }
    }

    return $false
}

function Get-CSharpFiles {
    param([string]$Root)
    $pending = New-Object System.Collections.Stack
    $pending.Push($Root)
    $files = New-Object System.Collections.ArrayList
    while ($pending.Count -gt 0 -and $files.Count -lt $MaxFiles) {
        $current = [string]$pending.Pop()
        foreach ($directory in [IO.Directory]::EnumerateDirectories($current)) {
            $relativeDirectory = ConvertTo-RelativePath $Root $directory
            if (-not (Test-IgnoredDirectory $relativeDirectory)) {
                $attributes = [IO.File]::GetAttributes($directory)
                if (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                    $pending.Push($directory)
                }
            }
        }

        foreach ($file in [IO.Directory]::EnumerateFiles($current, "*.cs", [IO.SearchOption]::TopDirectoryOnly)) {
            if ($files.Count -ge $MaxFiles) {
                break
            }

            $relativeFile = ConvertTo-RelativePath $Root $file
            if (-not (Test-IgnoredDirectory ([IO.Path]::GetDirectoryName($relativeFile))) -and -not (Test-IgnoredFile ([IO.Path]::GetFileName($file)))) {
                [void]$files.Add($file)
            }
        }
    }

    return @($files)
}

function Get-LineNumber {
    param([string[]]$Lines, [string]$Needle)
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index].IndexOf($Needle, [StringComparison]::Ordinal) -ge 0) {
            return $index + 1
        }
    }

    return 1
}

function Get-AttributesBeforeLine {
    param([string[]]$Lines, [int]$LineIndex)
    $attributes = New-Object System.Collections.ArrayList
    for ($index = $LineIndex - 1; $index -ge 0 -and $index -ge $LineIndex - 8; $index--) {
        $line = $Lines[$index].Trim()
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($line.StartsWith("[")) {
            foreach ($name in $relevantAttributes) {
                if ($line -match "(^|\W)$name(Attribute)?(\W|$)") {
                    [void]$attributes.Add($name)
                }
            }
        }
        elseif (-not $line.StartsWith("//")) {
            break
        }
    }

    return @($attributes | Select-Object -Unique)
}

function Split-TypeList {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return @($Value.Split(",", [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-Classification {
    param([string]$Name, [string]$Kind, [string[]]$BaseTypes, [string[]]$Attributes)
    if ($Name.EndsWith("Controller", [StringComparison]::OrdinalIgnoreCase) -or $Attributes -contains "ApiController") {
        return "Controller"
    }

    if ($BaseTypes | Where-Object { $_ -match '(^|\.)DbContext$|DbContext<' }) {
        return "DbContext"
    }

    if ($BaseTypes | Where-Object { $_ -match 'IRequestHandler|INotificationHandler' }) {
        return "Handler"
    }

    if ($Name.EndsWith("Handler", [StringComparison]::OrdinalIgnoreCase)) {
        return "Handler"
    }

    if ($Name.EndsWith("Service", [StringComparison]::OrdinalIgnoreCase)) {
        return "Service"
    }

    if ($Name.EndsWith("Repository", [StringComparison]::OrdinalIgnoreCase)) {
        return "Repository"
    }

    if ($Name -match '(Dto|Request|Response|ViewModel|Model)$') {
        return "Dto"
    }

    return $Kind
}

function Add-Endpoint {
    param([string]$Method, [string]$Route, [string]$File, [int]$Line, [string]$Handler, [string]$SourceKind, [string]$Preview)
    if ($endpoints.Count -ge $MaxItems) {
        return
    }

    $cleanPreview = ($Preview -replace '\s+', ' ').Trim()
    if ($cleanPreview.Length -gt 180) {
        $cleanPreview = $cleanPreview.Substring(0, 180)
    }

    [void]$endpoints.Add([ordered]@{
        Method = $Method.ToUpperInvariant()
        Route = $Route
        File = $File
        Line = $Line
        HandlerOrController = $Handler
        SourceKind = $SourceKind
        Preview = $cleanPreview
    })
}

function Get-RouteFromAttribute {
    param([string]$Text)
    if ($Text -match '\("([^"]*)"\)') {
        return $Matches[1]
    }

    return ""
}

function Join-Routes {
    param([string]$Prefix, [string]$Route)
    $left = if ([string]::IsNullOrWhiteSpace($Prefix)) { "" } else { $Prefix.Trim("/") }
    $right = if ([string]::IsNullOrWhiteSpace($Route)) { "" } else { $Route.Trim("/") }
    if ([string]::IsNullOrWhiteSpace($left)) {
        return "/" + $right
    }

    if ([string]::IsNullOrWhiteSpace($right)) {
        return "/" + $left
    }

    return "/" + $left + "/" + $right
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$ignoredDirectories = @(".git", "bin", "obj", ".vs", ".vscode", ".idea", "node_modules", "dist", "build", "artifacts", ".tmp", "Logs", "AutoGenerated", "oracle-data", "wwwroot/uploads", "Tools/AISandbox")
$ignoredFiles = @("appsettings*.json", "docker-compose*.yml", "docker-compose*.yaml", "*.Designer.cs", "*.g.cs", "*.AssemblyInfo.cs", "key.json", "*.pem", "*.pfx", "*.key", "*.jks", "*.keystore")
$relevantAttributes = @("ApiController", "Route", "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "Endpoint", "Handler")
$symbols = New-Object System.Collections.ArrayList
$endpoints = New-Object System.Collections.ArrayList
$files = Get-CSharpFiles $RepoRoot
$truncated = $files.Count -ge $MaxFiles

foreach ($file in $files) {
    if ($symbols.Count -ge $MaxItems) {
        $truncated = $true
        break
    }

    $relativeFile = ConvertTo-RelativePath $RepoRoot $file
    $text = [IO.File]::ReadAllText($file)
    $lines = $text -split "\r?\n"
    $namespace = ""
    if ($text -match '(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]') {
        $namespace = $Matches[1]
    }

    $typeRegex = [regex]'(?m)^\s*(public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*\b(class|record|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*([^{;\r\n]+))?'
    $typeMatches = $typeRegex.Matches($text)
    foreach ($match in $typeMatches) {
        if ($symbols.Count -ge $MaxItems) {
            $truncated = $true
            break
        }

        $visibility = if ([string]::IsNullOrWhiteSpace($match.Groups[1].Value)) { "internal" } else { $match.Groups[1].Value }
        if (-not $IncludePrivateMembers -and $visibility -eq "private") {
            continue
        }

        $kind = $match.Groups[2].Value
        $name = $match.Groups[3].Value
        $baseTypes = Split-TypeList $match.Groups[4].Value
        $line = Get-LineNumber $lines $match.Value.Trim()
        $attributes = Get-AttributesBeforeLine $lines ($line - 1)
        $classification = Get-Classification $name $kind $baseTypes $attributes
        $methods = New-Object System.Collections.ArrayList
        $properties = New-Object System.Collections.ArrayList

        $methodRegex = [regex]("(?m)^\s*(public" + $(if ($IncludePrivateMembers) { "|private|protected|internal" } else { "" }) + ")\s+(?:static\s+|virtual\s+|override\s+|async\s+|sealed\s+)*([A-Za-z_][A-Za-z0-9_<>,\[\]\?\. ]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^;\{\)]*)\)")
        foreach ($method in $methodRegex.Matches($text)) {
            $methodName = $method.Groups[3].Value
            if ($methodName -in @("if", "for", "foreach", "while", "switch", "catch", "using", "lock")) {
                continue
            }

            [void]$methods.Add([ordered]@{
                Name = $methodName
                ReturnType = ($method.Groups[2].Value -replace '\s+', ' ').Trim()
                Visibility = $method.Groups[1].Value
                Line = Get-LineNumber $lines $method.Value.Trim()
            })
        }

        $constructorRegex = [regex]("(?m)^\s*(public" + $(if ($IncludePrivateMembers) { "|private|protected|internal" } else { "" }) + ")\s+" + [regex]::Escape($name) + "\s*\(([^;\{\)]*)\)")
        foreach ($constructor in $constructorRegex.Matches($text)) {
            [void]$methods.Add([ordered]@{
                Name = $name
                ReturnType = "constructor"
                Visibility = $constructor.Groups[1].Value
                Line = Get-LineNumber $lines $constructor.Value.Trim()
            })
        }

        $propertyRegex = [regex]("(?m)^\s*(public" + $(if ($IncludePrivateMembers) { "|private|protected|internal" } else { "" }) + ")\s+(?:static\s+|virtual\s+|override\s+|required\s+|init\s+)*([A-Za-z_][A-Za-z0-9_<>,\[\]\?\. ]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{\s*(get|set|init)")
        foreach ($property in $propertyRegex.Matches($text)) {
            [void]$properties.Add([ordered]@{
                Name = $property.Groups[3].Value
                Type = ($property.Groups[2].Value -replace '\s+', ' ').Trim()
                Visibility = $property.Groups[1].Value
                Line = Get-LineNumber $lines $property.Value.Trim()
            })
        }

        [void]$symbols.Add([ordered]@{
            Name = $name
            Kind = $kind
            Namespace = $namespace
            File = $relativeFile
            Line = $line
            Visibility = $visibility
            Parent = ""
            BaseTypes = @($baseTypes)
            Attributes = @($attributes)
            Methods = @($methods | Select-Object -First 40)
            Properties = @($properties | Select-Object -First 40)
            Classification = $classification
        })

        if ($classification -eq "Controller") {
            $controllerRoute = ""
            for ($index = [Math]::Max(0, $line - 8); $index -lt [Math]::Min($lines.Count, $line + 3); $index++) {
                if ($lines[$index] -match '\[Route') {
                    $controllerRoute = Get-RouteFromAttribute $lines[$index]
                }
            }

            for ($index = 0; $index -lt $lines.Count; $index++) {
                $lineText = $lines[$index]
                if ($lineText -match '\[(HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch)(?:\("([^"]*)"\))?') {
                    $methodName = $Matches[1].Substring(4).ToUpperInvariant()
                    $route = if ($Matches.Count -gt 2) { $Matches[2] } else { "" }
                    Add-Endpoint $methodName (Join-Routes $controllerRoute $route) $relativeFile ($index + 1) $name "Controller" $lineText
                }
            }
        }
    }

    $minimalRegex = [regex]'(?m)\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*"([^"]*)"'
    foreach ($minimal in $minimalRegex.Matches($text)) {
        Add-Endpoint $minimal.Groups[1].Value $minimal.Groups[2].Value $relativeFile (Get-LineNumber $lines $minimal.Value.Trim()) "MinimalApi" "MinimalApi" $minimal.Value
    }
}

$aiRoot = Join-Path $RepoRoot ".ai"
$inventoriesRoot = Join-Path $aiRoot "generated/inventories"
New-Item -ItemType Directory -Path $inventoriesRoot -Force | Out-Null

$symbolInventory = [ordered]@{
    GeneratedAtLocal = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    RepoRoot = $RepoRoot
    TotalFilesScanned = $files.Count
    TotalSymbols = $symbols.Count
    Truncated = [bool]$truncated
    IgnoredDirectories = @($ignoredDirectories)
    Symbols = @($symbols)
}

$endpointInventory = [ordered]@{
    GeneratedAtLocal = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    TotalEndpoints = $endpoints.Count
    Endpoints = @($endpoints)
}

$symbolJsonPath = Join-Path $inventoriesRoot "symbol-inventory.json"
$endpointJsonPath = Join-Path $inventoriesRoot "endpoint-inventory.json"
$symbolMarkdownPath = Join-Path $inventoriesRoot "symbol-inventory.md"
$endpointMarkdownPath = Join-Path $inventoriesRoot "endpoint-inventory.md"
$symbolInventory | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $symbolJsonPath -Encoding UTF8
$endpointInventory | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $endpointJsonPath -Encoding UTF8

$classificationCounts = @($symbols | Group-Object { $_["Classification"] } | Sort-Object Count -Descending)
$symbolMarkdown = New-Object System.Collections.ArrayList
[void]$symbolMarkdown.Add("# Symbol Inventory")
[void]$symbolMarkdown.Add("")
[void]$symbolMarkdown.Add("- GeneratedAtLocal: $($symbolInventory.GeneratedAtLocal)")
[void]$symbolMarkdown.Add("- TotalFilesScanned: $($symbolInventory.TotalFilesScanned)")
[void]$symbolMarkdown.Add("- TotalSymbols: $($symbolInventory.TotalSymbols)")
[void]$symbolMarkdown.Add("- Truncated: $($symbolInventory.Truncated)")
[void]$symbolMarkdown.Add("")
[void]$symbolMarkdown.Add("## Top Classifications")
foreach ($group in @($classificationCounts | Select-Object -First 12)) {
    [void]$symbolMarkdown.Add("- $($group.Name): $($group.Count)")
}

[void]$symbolMarkdown.Add("")
[void]$symbolMarkdown.Add("## Controllers And Endpoints")
foreach ($symbol in @($symbols | Where-Object { $_.Classification -eq "Controller" } | Select-Object -First 20)) {
    [void]$symbolMarkdown.Add("- " + $symbol.Name + " " + $symbol.File + ":" + $symbol.Line)
}

[void]$symbolMarkdown.Add("")
[void]$symbolMarkdown.Add("## Services Handlers DbContexts")
foreach ($symbol in @($symbols | Where-Object { $_.Classification -in @("Service", "Handler", "DbContext", "Repository") } | Select-Object -First 40)) {
    [void]$symbolMarkdown.Add("- " + $symbol.Classification + ": " + $symbol.Name + " " + $symbol.File + ":" + $symbol.Line)
}

[void]$symbolMarkdown.Add("")
[void]$symbolMarkdown.Add("This is a heuristic structural inventory, not semantic analysis.")
$symbolMarkdown | Set-Content -LiteralPath $symbolMarkdownPath -Encoding UTF8

$endpointMarkdown = New-Object System.Collections.ArrayList
[void]$endpointMarkdown.Add("# Endpoint Inventory")
[void]$endpointMarkdown.Add("")
[void]$endpointMarkdown.Add("- GeneratedAtLocal: $($endpointInventory.GeneratedAtLocal)")
[void]$endpointMarkdown.Add("- TotalEndpoints: $($endpointInventory.TotalEndpoints)")
[void]$endpointMarkdown.Add("")
[void]$endpointMarkdown.Add("## Main Endpoints")
foreach ($endpoint in @($endpoints | Select-Object -First 60)) {
    [void]$endpointMarkdown.Add("- " + $endpoint.Method + " " + $endpoint.Route + " -> " + $endpoint.HandlerOrController + " " + $endpoint.File + ":" + $endpoint.Line)
}

[void]$endpointMarkdown.Add("")
[void]$endpointMarkdown.Add("This is a heuristic endpoint inventory, not semantic route analysis.")
$endpointMarkdown | Set-Content -LiteralPath $endpointMarkdownPath -Encoding UTF8

"Code inventory updated."
"Symbols: $($symbols.Count)"
"Endpoints: $($endpoints.Count)"

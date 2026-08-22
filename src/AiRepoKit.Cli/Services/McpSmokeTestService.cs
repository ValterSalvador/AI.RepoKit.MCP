using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiRepoKit.Cli.Models.McpDiagnostics;
using AiRepoKit.Cli.Services.McpLaunch;

namespace AiRepoKit.Cli.Services;

public enum McpSmokeTestDepth
{
    Minimal,
    Expanded
}

public sealed class McpSmokeTestService
{
    private static readonly System.Text.RegularExpressions.Regex RawLocalPathRegex = new(
        @"(?i)\b[A-Z]:(?:\\\\|\\u005[Cc]|\\)[^\\\s""'<>|]+(?:(?:\\\\|\\u005[Cc]|\\)[^\\\s""'<>|]+)+|\\\\(?!u00[0-9a-f]{2})[^\\\s""'<>|]+\\[^\\\s""'<>|]+\\[^\s""'<>|]+|/(?:Users|home)/(?!user(?:/|$))[^/\s""'<>]+/[^\s""'<>]+|/(?:tmp|var/tmp)/[^\s""'<>]+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] ExpectedTools =
    [
        "get_repo_brief",
        "get_health",
        "get_policy",
        "get_context",
        "search_context"
    ];

    private static readonly string[] ExpectedResourceUris =
    [
        "repo://brief",
        "repo://health",
        "repo://policy",
        "repo://context/changed-files",
        "repo://context/review-risk",
        "repo://context/test-generation",
        "repo://graph/dependencies",
        "repo://impact/current",
        "repo://org/report"
    ];

    private static readonly string[] ExpectedPrompts =
    [
        "ai-repo.help",
        "ai-repo.tutorial-en",
        "ai-repo.tutorial-pt",
        "ai-repo.token-efficiency-check",
        "ai-repo.review-risk",
        "ai-repo.changed-files-review",
        "ai-repo.generate-tests",
        "ai-repo.before-commit",
        "ai-repo.implementation-plan",
        "ai-repo.release-check",
        "ai-repo.workflow.feature-implementation",
        "ai-repo.workflow.bug-fix",
        "ai-repo.workflow.before-commit",
        "ai-repo.workflow.release-preparation",
        "ai-repo.workflow.test-generation",
        "ai-repo.workflow.architecture-review",
        "ai-repo.workflow.migration-planning"
    ];

    public McpSmokeTestResult Run(string repoPath_, string dllPath_, bool verbose_, bool strictStdio_ = false, McpSmokeTestDepth depth_ = McpSmokeTestDepth.Expanded)
    {
        if (!File.Exists(dllPath_))
        {
            return new McpSmokeTestResult("Failed", "MCP Release DLL is missing.", [], []);
        }

        return Run(McpServerLaunchSpecResolver.ResolveLegacy(repoPath_, dllPath_), verbose_, strictStdio_, depth_);
    }

    internal McpSmokeTestResult Run(McpServerLaunchSpec launchSpec_, bool verbose_, bool strictStdio_ = false, McpSmokeTestDepth depth_ = McpSmokeTestDepth.Expanded)
    {
        List<string> stdoutLines = [];
        List<string> stderrLines = [];

        using Process process = new();
        process.StartInfo.FileName = launchSpec_.FileName;
        process.StartInfo.WorkingDirectory = launchSpec_.WorkingDirectory;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        foreach (string argument in launchSpec_.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.OutputDataReceived += (_, eventArgs_) =>
            {
                if (eventArgs_.Data is not null)
                {
                    lock (stdoutLines)
                    {
                        stdoutLines.Add(eventArgs_.Data);
                    }
                }
            };
            process.ErrorDataReceived += (_, eventArgs_) =>
            {
                if (eventArgs_.Data is not null)
                {
                    lock (stderrLines)
                    {
                        stderrLines.Add(eventArgs_.Data);
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "airepo-mcp-diagnose",
                        version = "1.0.0"
                    }
                }
            });

            using JsonDocument initialize = WaitForResponse(stdoutLines, 1, TimeSpan.FromSeconds(20));
            if (initialize.RootElement.TryGetProperty("error", out _))
            {
                return new McpSmokeTestResult("Failed", "MCP initialize returned a JSON-RPC error.", GetSmokeDetails(stdoutLines, stderrLines, verbose_), []);
            }

            WriteJson(process, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });

            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            });

            using JsonDocument tools = WaitForResponse(stdoutLines, 2, TimeSpan.FromSeconds(20));
            if (tools.RootElement.TryGetProperty("error", out _))
            {
                return new McpSmokeTestResult("Failed", "MCP tools/list returned a JSON-RPC error.", GetSmokeDetails(stdoutLines, stderrLines, verbose_), []);
            }

            IReadOnlyList<string> toolNames = GetToolNames(tools.RootElement);
            string[] missing = ExpectedTools.Where(tool_ => !toolNames.Contains(tool_, StringComparer.Ordinal)).ToArray();
            List<string> smokeWarnings = [];
            IReadOnlyList<string> resourceUris = [];
            IReadOnlyList<string> promptNames = [];
            if (missing.Length == 0 && depth_ == McpSmokeTestDepth.Expanded)
            {
                AddCoreToolCall(process, stdoutLines, smokeWarnings, 3, "get_repo_brief", new { detail = "brief" });
                AddCoreToolCall(process, stdoutLines, smokeWarnings, 4, "get_health", new { area = "capabilities" });
                AddCoreToolCall(process, stdoutLines, smokeWarnings, 5, "get_policy", new { topic = "all" });
                AddCoreToolCall(process, stdoutLines, smokeWarnings, 6, "get_context", new { kind = "changed-files", detail = "brief", limit = 5 });
                AddCoreToolCall(process, stdoutLines, smokeWarnings, 7, "search_context", new { query = "MCP", limit = 3 });

                resourceUris = AddResourceSmokeCalls(process, stdoutLines, smokeWarnings, 8);
                promptNames = AddPromptSmokeCalls(process, stdoutLines, smokeWarnings, 13);
            }

            process.StandardInput.Close();
            process.WaitForExit(2000);

            if (missing.Length > 0)
            {
                return new McpSmokeTestResult("Failed", "MCP smoke test did not list expected tools: " + string.Join(", ", missing) + ".", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames, depth_ == McpSmokeTestDepth.Expanded ? resourceUris : null, depth_ == McpSmokeTestDepth.Expanded ? promptNames : null), toolNames);
            }

            if (depth_ == McpSmokeTestDepth.Expanded)
            {
                string[] missingResources = ExpectedResourceUris.Where(uri_ => !resourceUris.Contains(uri_, StringComparer.Ordinal)).ToArray();
                if (missingResources.Length > 0)
                {
                    return new McpSmokeTestResult("Failed", "MCP smoke test did not list expected resources: " + string.Join(", ", missingResources) + ".", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames, resourceUris, promptNames), toolNames);
                }

                string[] missingPrompts = ExpectedPrompts.Where(prompt_ => !promptNames.Contains(prompt_, StringComparer.Ordinal)).ToArray();
                if (missingPrompts.Length > 0)
                {
                    return new McpSmokeTestResult("Failed", "MCP smoke test did not list expected prompts: " + string.Join(", ", missingPrompts) + ".", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames, resourceUris, promptNames), toolNames);
                }
            }

            string message = depth_ == McpSmokeTestDepth.Expanded
                ? "Expanded MCP smoke test passed: initialize, tools/list, resources/list, prompts/list, minimal core tool calls, resource reads, and prompt gets. Expected tools listed: " + string.Join(", ", ExpectedTools) + "."
                : "Minimal MCP smoke test passed: initialize and tools/list. Expected tools listed: " + string.Join(", ", ExpectedTools) + ".";
            IReadOnlyList<string>? detailResources = depth_ == McpSmokeTestDepth.Expanded ? resourceUris : null;
            IReadOnlyList<string>? detailPrompts = depth_ == McpSmokeTestDepth.Expanded ? promptNames : null;
            if (strictStdio_ && TryFindRawLocalPath(stdoutLines.Concat(stderrLines), out string rawLocalPathSource))
            {
                return new McpSmokeTestResult("Failed", message + " strict stdio failed because MCP output contained an unredacted local path near: " + rawLocalPathSource, GetSmokeDetails(stdoutLines, stderrLines, true, toolNames, detailResources, detailPrompts), toolNames);
            }

            if (smokeWarnings.Count > 0)
            {
                return new McpSmokeTestResult("Warning", message + " Smoke calls returned warnings: " + string.Join("; ", smokeWarnings) + ".", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames, detailResources, detailPrompts), toolNames);
            }

            if (strictStdio_ && stderrLines.Count > 0)
            {
                return new McpSmokeTestResult("Failed", message + $" strict stdio failed because stderr contained {stderrLines.Count} line(s) and {GetByteCount(stderrLines)} byte(s).", GetSmokeDetails(stdoutLines, stderrLines, true, toolNames, detailResources, detailPrompts), toolNames);
            }

            if (stderrLines.Count > 0)
            {
                return new McpSmokeTestResult("Warning", message + $" stderr contained {stderrLines.Count} log line(s), but stdout was valid JSON-RPC.", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames, detailResources, detailPrompts), toolNames);
            }

            return new McpSmokeTestResult("Passed", message, GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames, detailResources, detailPrompts), toolNames);
        }
        catch (Exception exception)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Close();
                    process.WaitForExit(2000);
                }
            }
            catch
            {
            }

            return new McpSmokeTestResult("Failed", ProcessRunner.Redact(exception.Message), GetSmokeDetails(stdoutLines, stderrLines, verbose_), []);
        }
    }

    private static void WriteJson(Process process_, object value_)
    {
        process_.StandardInput.WriteLine(JsonSerializer.Serialize(value_));
        process_.StandardInput.Flush();
    }

    private static void AddCoreToolCall(Process process_, List<string> stdoutLines_, List<string> warnings_, int id_, string name_, object arguments_)
    {
        try
        {
            WriteJson(process_, new
            {
                jsonrpc = "2.0",
                id = id_,
                method = "tools/call",
                @params = new
                {
                    name = name_,
                    arguments = arguments_
                }
            });
            using JsonDocument response = WaitForResponse(stdoutLines_, id_, TimeSpan.FromSeconds(20));
            if (response.RootElement.TryGetProperty("error", out _))
            {
                warnings_.Add($"{name_} returned a JSON-RPC error");
            }
        }
        catch (Exception exception)
        {
            warnings_.Add($"{name_}: {ProcessRunner.Redact(exception.Message)}");
        }
    }

    private static IReadOnlyList<string> AddResourceSmokeCalls(Process process_, List<string> stdoutLines_, List<string> warnings_, int startId_)
    {
        try
        {
            WriteJson(process_, new
            {
                jsonrpc = "2.0",
                id = startId_,
                method = "resources/list",
                @params = new { }
            });
            using JsonDocument response = WaitForResponse(stdoutLines_, startId_, TimeSpan.FromSeconds(20));
            if (response.RootElement.TryGetProperty("error", out _))
            {
                warnings_.Add("resources/list returned a JSON-RPC error");
                return [];
            }

            IReadOnlyList<string> resourceUris = GetResourceUris(response.RootElement);
            AddResourceRead(process_, stdoutLines_, warnings_, startId_ + 1, "repo://brief");
            AddResourceRead(process_, stdoutLines_, warnings_, startId_ + 2, "repo://health");
            AddResourceRead(process_, stdoutLines_, warnings_, startId_ + 3, "repo://policy");
            return resourceUris;
        }
        catch (Exception exception)
        {
            warnings_.Add("resources/list: " + ProcessRunner.Redact(exception.Message));
            return [];
        }
    }

    private static void AddResourceRead(Process process_, List<string> stdoutLines_, List<string> warnings_, int id_, string uri_)
    {
        try
        {
            WriteJson(process_, new
            {
                jsonrpc = "2.0",
                id = id_,
                method = "resources/read",
                @params = new
                {
                    uri = uri_
                }
            });
            using JsonDocument response = WaitForResponse(stdoutLines_, id_, TimeSpan.FromSeconds(20));
            if (response.RootElement.TryGetProperty("error", out _))
            {
                warnings_.Add($"resources/read {uri_} returned a JSON-RPC error");
            }
        }
        catch (Exception exception)
        {
            warnings_.Add($"resources/read {uri_}: {ProcessRunner.Redact(exception.Message)}");
        }
    }

    private static IReadOnlyList<string> AddPromptSmokeCalls(Process process_, List<string> stdoutLines_, List<string> warnings_, int startId_)
    {
        try
        {
            WriteJson(process_, new
            {
                jsonrpc = "2.0",
                id = startId_,
                method = "prompts/list",
                @params = new { }
            });
            using JsonDocument response = WaitForResponse(stdoutLines_, startId_, TimeSpan.FromSeconds(20));
            if (response.RootElement.TryGetProperty("error", out _))
            {
                warnings_.Add("prompts/list returned a JSON-RPC error");
                return [];
            }

            IReadOnlyList<string> promptNames = GetPromptNames(response.RootElement);
            AddPromptGet(process_, stdoutLines_, warnings_, startId_ + 1, "ai-repo.help");
            AddPromptGet(process_, stdoutLines_, warnings_, startId_ + 2, "ai-repo.review-risk");
            AddPromptGet(process_, stdoutLines_, warnings_, startId_ + 3, "ai-repo.workflow.before-commit");
            return promptNames;
        }
        catch (Exception exception)
        {
            warnings_.Add("prompts/list: " + ProcessRunner.Redact(exception.Message));
            return [];
        }
    }

    private static void AddPromptGet(Process process_, List<string> stdoutLines_, List<string> warnings_, int id_, string name_)
    {
        try
        {
            WriteJson(process_, new
            {
                jsonrpc = "2.0",
                id = id_,
                method = "prompts/get",
                @params = new
                {
                    name = name_,
                    arguments = new { }
                }
            });
            using JsonDocument response = WaitForResponse(stdoutLines_, id_, TimeSpan.FromSeconds(20));
            if (response.RootElement.TryGetProperty("error", out _))
            {
                warnings_.Add($"prompts/get {name_} returned a JSON-RPC error");
            }
        }
        catch (Exception exception)
        {
            warnings_.Add($"prompts/get {name_}: {ProcessRunner.Redact(exception.Message)}");
        }
    }

    private static JsonDocument WaitForResponse(List<string> stdoutLines_, int id_, TimeSpan timeout_)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout_);
        int index = 0;
        while (DateTime.UtcNow < deadline)
        {
            List<string> snapshot;
            lock (stdoutLines_)
            {
                snapshot = stdoutLines_.ToList();
            }

            while (index < snapshot.Count)
            {
                string line = snapshot[index++];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch
                {
                    continue;
                }

                if (document.RootElement.TryGetProperty("id", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id_)
                {
                    return document;
                }

                document.Dispose();
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for JSON-RPC response id {id_}.");
    }

    private static IReadOnlyList<string> GetToolNames(JsonElement root_)
    {
        JsonElement current = root_;
        if (current.TryGetProperty("result", out JsonElement result))
        {
            current = result;
        }

        if (!current.TryGetProperty("tools", out JsonElement tools) || tools.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> names = [];
        foreach (JsonElement tool in tools.EnumerateArray())
        {
            if (tool.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
            {
                names.Add(name.GetString() ?? string.Empty);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> GetResourceUris(JsonElement root_)
    {
        JsonElement current = root_;
        if (current.TryGetProperty("result", out JsonElement result))
        {
            current = result;
        }

        if (!current.TryGetProperty("resources", out JsonElement resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> uris = [];
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            if (resource.TryGetProperty("uri", out JsonElement uri) && uri.ValueKind == JsonValueKind.String)
            {
                uris.Add(uri.GetString() ?? string.Empty);
            }
        }

        return uris;
    }

    private static IReadOnlyList<string> GetPromptNames(JsonElement root_)
    {
        JsonElement current = root_;
        if (current.TryGetProperty("result", out JsonElement result))
        {
            current = result;
        }

        if (!current.TryGetProperty("prompts", out JsonElement prompts) || prompts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> names = [];
        foreach (JsonElement prompt in prompts.EnumerateArray())
        {
            if (prompt.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
            {
                names.Add(name.GetString() ?? string.Empty);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> GetSmokeDetails(List<string> stdoutLines_, List<string> stderrLines_, bool verbose_, IReadOnlyList<string>? tools_ = null, IReadOnlyList<string>? resources_ = null, IReadOnlyList<string>? prompts_ = null)
    {
        List<string> details = [];
        if (tools_ is not null)
        {
            details.Add("Tools: " + string.Join(", ", tools_));
        }

        if (resources_ is not null)
        {
            details.Add("Resources: " + string.Join(", ", resources_));
        }

        if (prompts_ is not null)
        {
            details.Add("Prompts: " + string.Join(", ", prompts_));
        }

        details.Add($"stdout JSON-RPC line count: {stdoutLines_.Count}");
        details.Add($"stderr line count: {stderrLines_.Count}");
        details.Add($"stderr byte count: {GetByteCount(stderrLines_)}");
        if (verbose_)
        {
            details.AddRange(stderrLines_.TakeLast(5).Select(line_ => "stderr: " + ProcessRunner.Redact(line_)));
        }

        return details;
    }

    private static bool TryFindRawLocalPath(IEnumerable<string> lines_, out string source_)
    {
        foreach (string line in lines_)
        {
            Match match = RawLocalPathRegex.Match(line);
            if (match.Success)
            {
                int start = Math.Max(0, match.Index - 80);
                int length = Math.Min(220, line.Length - start);
                source_ = ProcessRunner.Redact(line.Substring(start, length));
                return true;
            }
        }

        source_ = string.Empty;
        return false;
    }

    private static int GetByteCount(IReadOnlyList<string> lines_)
    {
        return Encoding.UTF8.GetByteCount(string.Join(Environment.NewLine, lines_));
    }
}

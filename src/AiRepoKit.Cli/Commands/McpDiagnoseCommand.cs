using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.McpDiagnostics;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class McpDiagnoseCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] ExpectedTools =
    [
        "get_repo_brief",
        "get_health",
        "get_policy",
        "get_context",
        "search_context"
    ];

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        try
        {
            string repoPath = Path.GetFullPath(options_.RepoPath);
            IReadOnlyList<ClientKind> clients = NormalizeClients(options_.Clients);
            List<McpDiagnosticItem> checks = [];
            List<string> hints = [];
            bool rebuilt = false;

            progress.StartPhase("Checking repository");
            AddRepoChecks(checks, repoPath);
            progress.CompletePhase("Repository check completed");
            progress.StartPhase("Checking client configs");
            AddClientChecks(checks, repoPath, clients);
            progress.CompletePhase("Client config checks completed");
            progress.StartPhase("Checking MCP project");
            AddDotnetCheck(checks);
            progress.CompletePhase("MCP project checks completed");

            if (options_.SkipBuildMcp)
            {
                checks.Add(Skipped("mcp-build", true, "Skipped by --skip-build."));
            }
            else
            {
                progress.StartPhase("Building MCP project");
                McpDiagnosticItem buildCheck = BuildMcp(repoPath, options_.McpProjectRelativePath);
                checks.Add(buildCheck);
                rebuilt = buildCheck.Status == "Passed";
                if (buildCheck.Status == "Passed")
                {
                    progress.CompletePhase("MCP project build completed");
                }
                else
                {
                    progress.FailPhase("MCP project build failed");
                }
            }

            string dllPath = GetMcpDllPath(repoPath);
            if (options_.SkipSmoke)
            {
                checks.Add(Skipped("smoke-test", true, "Skipped by --skip-smoke."));
            }
            else
            {
                progress.StartPhase("Running MCP smoke test");
                checks.Add(RunSmokeTest(repoPath, dllPath, options_.Verbose));
                McpDiagnosticItem smoke = checks[^1];
                if (smoke.Status is "Passed" or "Warning")
                {
                    progress.CompletePhase("MCP smoke test completed");
                }
                else
                {
                    progress.FailPhase("MCP smoke test failed");
                }
            }

            DowngradeLockedBuildWhenSmokePassed(checks, options_.Strict);

            if (options_.SkipBudget)
            {
                checks.Add(Skipped("budget", false, "Skipped by --skip-budget."));
            }
            else
            {
                progress.StartPhase("Running budget script");
                checks.Add(RunBudget(repoPath));
                McpDiagnosticItem budget = checks[^1];
                if (budget.Status is "Passed" or "Warning")
                {
                    progress.CompletePhase("Budget script completed");
                }
                else
                {
                    progress.FailPhase("Budget script failed");
                }
            }

            AddClientHints(hints, checks, clients, repoPath, rebuilt);
            int exitCode = checks.Any(check_ => check_.Required && check_.Status == "Failed") ? 2 : 0;
            string status = exitCode == 2 ? "Failed" : checks.Any(check_ => check_.Status == "Warning") ? "Warning" : "Passed";
            McpDiagnosticResult result = new(status, repoPath, exitCode, clients.Select(GetClientName).ToArray(), checks, hints);
            string output = options_.AuditJson ? JsonSerializer.Serialize(result, JsonOptions) : WriteMarkdown(result, options_.Verbose);
            if (exitCode == 0)
            {
                progress.CompletePhase("MCP diagnose completed");
            }
            else
            {
                progress.FailPhase("MCP diagnose completed with failures");
            }
            return new CommandResult(exitCode == 0, output, exitCode);
        }
        catch (Exception exception)
        {
            progress.FailPhase("MCP diagnose failed");
            string repoPath = Path.GetFullPath(options_.RepoPath);
            McpDiagnosticResult result = new(
                "Failed",
                repoPath,
                1,
                NormalizeClients(options_.Clients).Select(GetClientName).ToArray(),
                [Failed("fatal", true, ProcessRunner.Redact(exception.Message))],
                []);
            string output = options_.AuditJson ? JsonSerializer.Serialize(result, JsonOptions) : WriteMarkdown(result, options_.Verbose);
            return CommandResult.Failure(output, 1);
        }
    }

    private static IReadOnlyList<ClientKind> NormalizeClients(IReadOnlyList<ClientKind> clients_)
    {
        return clients_.Count == 0
            ? [ClientKind.Codex, ClientKind.Vscode, ClientKind.VisualStudio]
            : clients_.Where(client_ => client_ is ClientKind.Codex or ClientKind.Vscode or ClientKind.VisualStudio).Distinct().ToArray();
    }

    private static void AddRepoChecks(List<McpDiagnosticItem> checks_, string repoPath_)
    {
        checks_.Add(Check("repo-root", true, Directory.Exists(repoPath_), $"Repo path: {repoPath_}."));

        string mcpProjectRoot = Path.Combine(repoPath_, "Tools", "AiContextMcp");
        bool mcpRootExists = Directory.Exists(mcpProjectRoot);
        bool hasProject = mcpRootExists && Directory.EnumerateFiles(mcpProjectRoot, "*.csproj", SearchOption.TopDirectoryOnly).Any();
        checks_.Add(Check("mcp-project", true, mcpRootExists && hasProject, "Tools/AiContextMcp exists and contains a .csproj."));
        checks_.Add(Check("mcp-release-dll", true, File.Exists(GetMcpDllPath(repoPath_)), "Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll exists."));
    }

    private static void AddClientChecks(List<McpDiagnosticItem> checks_, string repoPath_, IReadOnlyList<ClientKind> clients_)
    {
        if (clients_.Contains(ClientKind.Vscode))
        {
            checks_.Add(CheckVscode(repoPath_));
        }

        if (clients_.Contains(ClientKind.Codex))
        {
            checks_.Add(CheckCodex(repoPath_));
        }

        if (clients_.Contains(ClientKind.VisualStudio))
        {
            string path = Path.Combine(repoPath_, ".ai", "client-configs", "visualstudio-mcp.snippet.json");
            checks_.Add(Check("vs-config", true, File.Exists(path), ".ai/client-configs/visualstudio-mcp.snippet.json exists."));
        }
    }

    private static McpDiagnosticItem CheckVscode(string repoPath_)
    {
        string path = Path.Combine(repoPath_, ".vscode", "mcp.json");
        if (!File.Exists(path))
        {
            return Failed("vscode-config", true, ".vscode/mcp.json is missing.", "Run bootstrap with --clients vscode --mcp --apply or restore the file.");
        }

        string content = File.ReadAllText(path);
        List<string> missing = [];
        if (!content.Contains("ai_repo_context", StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("ai_repo_context");
        }

        if (!content.Contains("Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("${workspaceFolder}", StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("MCP Release DLL path or ${workspaceFolder}");
        }

        if (!content.Contains("--repo", StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("--repo");
        }

        if (missing.Count > 0)
        {
            return Failed("vscode-config", true, ".vscode/mcp.json is present but missing: " + string.Join(", ", missing) + ".");
        }

        return Passed("vscode-config", true, ".vscode/mcp.json contains ai_repo_context, repo argument, and the MCP DLL path or ${workspaceFolder}.", null, UsesWorkspaceFolder(content) ? ["Uses ${workspaceFolder}."] : []);
    }

    private static McpDiagnosticItem CheckCodex(string repoPath_)
    {
        string localPath = Path.Combine(repoPath_, ".codex", "config.toml");
        string snippetPath = Path.Combine(repoPath_, ".ai", "client-configs", "codex.config.toml");
        if (File.Exists(localPath))
        {
            return Passed("codex-config", true, ".codex/config.toml exists.");
        }

        if (File.Exists(snippetPath))
        {
            return Warning("codex-config", true, ".codex/config.toml is not present. This file may be local or ignored; .ai/client-configs/codex.config.toml is the versionable snippet.");
        }

        return Failed("codex-config", true, ".codex/config.toml is missing and .ai/client-configs/codex.config.toml was not found.");
    }

    private static void AddDotnetCheck(List<McpDiagnosticItem> checks_)
    {
        ProcessResult result = new ProcessRunner().Run("dotnet", ["--version"], Directory.GetCurrentDirectory());
        checks_.Add(new McpDiagnosticItem("dotnet", result.Success ? "Passed" : "Failed", true, result.Success ? "dotnet is available." : GetProcessMessage(result), null, []));
    }

    private static McpDiagnosticItem BuildMcp(string repoPath_, string projectRelativePath_)
    {
        string projectPath = Path.Combine(repoPath_, projectRelativePath_.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(projectPath))
        {
            return Failed("mcp-build", true, $"Missing {projectRelativePath_}.");
        }

        ProcessResult build = new ProcessRunner().Run("dotnet", ["build", projectRelativePath_, "-c", "Release"], repoPath_);
        if (!build.Success && McpBuildFailureDiagnostics.IsLockedDllFailure(build))
        {
            return Failed("mcp-build", true, McpBuildFailureDiagnostics.LockedDllMessage, McpBuildFailureDiagnostics.LockedDllHint, GetProcessDetails(build));
        }

        return new McpDiagnosticItem("mcp-build", build.Success ? "Passed" : "Failed", true, build.Success ? "Release MCP build passed." : GetProcessMessage(build), null, GetProcessDetails(build));
    }

    private static McpDiagnosticItem RunBudget(string repoPath_)
    {
        string script = "Tools/AiContext/MeasureMcpResponseBudget.ps1";
        string scriptPath = Path.Combine(repoPath_, script.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(scriptPath))
        {
            return Warning("budget", false, "Tools/AiContext/MeasureMcpResponseBudget.ps1 is missing.");
        }

        ProcessResult result = new ProcessRunner().Run("powershell", ["-ExecutionPolicy", "Bypass", "-File", script, "-RepoRoot", repoPath_], repoPath_);
        return new McpDiagnosticItem("budget", result.Success ? "Passed" : "Failed", false, result.Success ? "MeasureMcpResponseBudget.ps1 passed." : GetProcessMessage(result), null, GetProcessDetails(result));
    }

    private static void DowngradeLockedBuildWhenSmokePassed(List<McpDiagnosticItem> checks_, bool strict_)
    {
        if (strict_)
        {
            return;
        }

        int buildIndex = checks_.FindIndex(check_ => check_.Name == "mcp-build"
            && check_.Status == "Failed"
            && check_.Message.Contains(McpBuildFailureDiagnostics.LockedDllMessage, StringComparison.OrdinalIgnoreCase));
        bool smokePassed = checks_.Any(check_ => check_.Name == "smoke-test" && check_.Status is "Passed" or "Warning");
        if (buildIndex < 0 || !smokePassed)
        {
            return;
        }

        McpDiagnosticItem build = checks_[buildIndex];
        checks_[buildIndex] = new McpDiagnosticItem(
            build.Name,
            "Warning",
            false,
            build.Message + " JSON-RPC smoke test passed, so this is non-blocking outside strict validation.",
            build.Hint,
            build.Details);
    }

    private static McpDiagnosticItem RunSmokeTest(string repoPath_, string dllPath_, bool verbose_)
    {
        if (!File.Exists(dllPath_))
        {
            return Failed("smoke-test", true, "MCP Release DLL is missing.");
        }

        List<string> stdoutLines = [];
        List<string> stderrLines = [];

        using Process process = new();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = repoPath_;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.ArgumentList.Add(dllPath_);
        process.StartInfo.ArgumentList.Add("--repo");
        process.StartInfo.ArgumentList.Add(repoPath_);

        try
        {
            process.OutputDataReceived += (_, eventArgs_) =>
            {
                if (eventArgs_.Data is not null)
                {
                    lock (stdoutLines)
                    {
                        stdoutLines.Add(ProcessRunner.Redact(eventArgs_.Data));
                    }
                }
            };
            process.ErrorDataReceived += (_, eventArgs_) =>
            {
                if (eventArgs_.Data is not null)
                {
                    lock (stderrLines)
                    {
                        stderrLines.Add(ProcessRunner.Redact(eventArgs_.Data));
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
                return Failed("smoke-test", true, "MCP initialize returned a JSON-RPC error.", null, GetSmokeDetails(stdoutLines, stderrLines, verbose_));
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
                return Failed("smoke-test", true, "MCP tools/list returned a JSON-RPC error.", null, GetSmokeDetails(stdoutLines, stderrLines, verbose_));
            }

            IReadOnlyList<string> toolNames = GetToolNames(tools.RootElement);
            string[] missing = ExpectedTools.Where(tool_ => !toolNames.Contains(tool_, StringComparer.Ordinal)).ToArray();
            List<string> optionalWarnings = [];
            if (missing.Length == 0)
            {
                AddOptionalContextCall(process, stdoutLines, stderrLines, optionalWarnings, 3, "context-packs");
                AddOptionalContextCall(process, stdoutLines, stderrLines, optionalWarnings, 4, "changed-files");
                AddOptionalContextCall(process, stdoutLines, stderrLines, optionalWarnings, 5, "graph");
            }

            process.StandardInput.Close();
            process.WaitForExit(2000);

            if (missing.Length > 0)
            {
                return Failed("smoke-test", true, "MCP smoke test did not list expected tools: " + string.Join(", ", missing) + ".", null, GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames));
            }

            string message = "MCP initialize and tools/list passed. Expected tools listed: " + string.Join(", ", ExpectedTools) + ".";
            if (optionalWarnings.Count > 0)
            {
                return Warning("smoke-test", true, message + " Optional context-kind checks returned warnings: " + string.Join("; ", optionalWarnings) + ".", null, GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames));
            }

            if (stderrLines.Count > 0)
            {
                return Warning("smoke-test", true, message + $" stderr contained {stderrLines.Count} log line(s), but stdout was valid JSON-RPC.", null, GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames));
            }

            return Passed("smoke-test", true, message, null, GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames));
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

            return Failed("smoke-test", true, ProcessRunner.Redact(exception.Message), null, GetSmokeDetails(stdoutLines, stderrLines, verbose_));
        }
    }

    private static void WriteJson(Process process_, object value_)
    {
        process_.StandardInput.WriteLine(JsonSerializer.Serialize(value_));
        process_.StandardInput.Flush();
    }

    private static void AddOptionalContextCall(Process process_, List<string> stdoutLines_, List<string> stderrLines_, List<string> warnings_, int id_, string kind_)
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
                    name = "get_context",
                    arguments = new
                    {
                        kind = kind_,
                        detail = "brief",
                        limit = 5
                    }
                }
            });
            using JsonDocument response = WaitForResponse(stdoutLines_, id_, TimeSpan.FromSeconds(20));
            if (response.RootElement.TryGetProperty("error", out _))
            {
                warnings_.Add($"get_context kind={kind_} returned a JSON-RPC error");
            }
        }
        catch (Exception exception)
        {
            warnings_.Add($"get_context kind={kind_}: {ProcessRunner.Redact(exception.Message)}");
            lock (stderrLines_)
            {
                stderrLines_.Add($"optional context smoke warning for {kind_}");
            }
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

    private static IReadOnlyList<string> GetSmokeDetails(List<string> stdoutLines_, List<string> stderrLines_, bool verbose_, IReadOnlyList<string>? tools_ = null)
    {
        List<string> details = [];
        if (tools_ is not null)
        {
            details.Add("Tools: " + string.Join(", ", tools_));
        }

        details.Add($"stdout JSON-RPC line count: {stdoutLines_.Count}");
        details.Add($"stderr line count: {stderrLines_.Count}");
        if (verbose_)
        {
            details.AddRange(stderrLines_.TakeLast(5).Select(line_ => "stderr: " + line_));
        }

        return details;
    }

    private static void AddClientHints(List<string> hints_, List<McpDiagnosticItem> checks_, IReadOnlyList<ClientKind> clients_, string repoPath_, bool rebuilt_)
    {
        bool configsPassed = checks_.Where(check_ => check_.Name.EndsWith("-config", StringComparison.Ordinal)).All(check_ => check_.Status is "Passed" or "Warning");
        bool smokePassed = checks_.Any(check_ => check_.Name == "smoke-test" && check_.Status is "Passed" or "Warning");
        if (configsPassed && smokePassed && clients_.Contains(ClientKind.Vscode))
        {
            hints_.Add("If ai_repo_context is still not visible in VS Code/Copilot Agent, close and reopen the VS Code workspace or run Developer: Reload Window.");
        }

        string vscodePath = Path.Combine(repoPath_, ".vscode", "mcp.json");
        if (clients_.Contains(ClientKind.Vscode) && File.Exists(vscodePath) && UsesWorkspaceFolder(File.ReadAllText(vscodePath)))
        {
            hints_.Add("This VS Code config uses ${workspaceFolder}; the workspace must be opened at the repository root.");
        }

        if (rebuilt_)
        {
            hints_.Add("The MCP DLL was rebuilt; MCP clients may need a restart or reload before they use the new server binary.");
        }
    }

    private static bool UsesWorkspaceFolder(string value_)
    {
        return value_.Contains("${workspaceFolder}", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMcpDllPath(string repoPath_)
    {
        return Path.Combine(repoPath_, "Tools", "AiContextMcp", "bin", "Release", "net10.0", "AiRepo.ContextMcp.dll");
    }

    private static string GetClientName(ClientKind client_)
    {
        return client_ switch
        {
            ClientKind.Codex => "codex",
            ClientKind.Vscode => "vscode",
            ClientKind.VisualStudio => "vs",
            _ => client_.ToString().ToLowerInvariant()
        };
    }

    private static McpDiagnosticItem Check(string name_, bool required_, bool passed_, string message_)
    {
        return passed_ ? Passed(name_, required_, message_) : Failed(name_, required_, message_);
    }

    private static McpDiagnosticItem Passed(string name_, bool required_, string message_, string? hint_ = null, IReadOnlyList<string>? details_ = null)
    {
        return new McpDiagnosticItem(name_, "Passed", required_, message_, hint_, details_ ?? []);
    }

    private static McpDiagnosticItem Warning(string name_, bool required_, string message_, string? hint_ = null, IReadOnlyList<string>? details_ = null)
    {
        return new McpDiagnosticItem(name_, "Warning", required_, message_, hint_, details_ ?? []);
    }

    private static McpDiagnosticItem Failed(string name_, bool required_, string message_, string? hint_ = null, IReadOnlyList<string>? details_ = null)
    {
        return new McpDiagnosticItem(name_, "Failed", required_, message_, hint_, details_ ?? []);
    }

    private static McpDiagnosticItem Skipped(string name_, bool required_, string message_)
    {
        return new McpDiagnosticItem(name_, "Skipped", required_, message_, null, []);
    }

    private static string GetProcessMessage(ProcessResult process_)
    {
        if (process_.Success)
        {
            return $"Exit code {process_.ExitCode}.";
        }

        string output = string.Join(" ", $"{process_.StandardOutput}{Environment.NewLine}{process_.StandardError}"
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(4)
            .Select(line_ => line_.Trim()));
        return string.IsNullOrWhiteSpace(output) ? $"Exit code {process_.ExitCode}." : $"Exit code {process_.ExitCode}. {output}";
    }

    private static IReadOnlyList<string> GetProcessDetails(ProcessResult process_)
    {
        return $"{process_.StandardOutput}{Environment.NewLine}{process_.StandardError}"
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(10)
            .Select(line_ => line_.Trim())
            .ToArray();
    }

    private static string WriteMarkdown(McpDiagnosticResult result_, bool verbose_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# MCP Diagnose");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{result_.RepoPath}`");
        builder.AppendLine($"- Clients: `{string.Join(",", result_.Clients)}`");
        builder.AppendLine($"- Status: `{result_.Status}`");
        builder.AppendLine($"- ExitCode: `{result_.ExitCode}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Passed: `{result_.Checks.Count(check_ => check_.Status == "Passed")}`");
        builder.AppendLine($"- Warnings: `{result_.Checks.Count(check_ => check_.Status == "Warning")}`");
        builder.AppendLine($"- Failed: `{result_.Checks.Count(check_ => check_.Status == "Failed")}`");
        builder.AppendLine($"- Skipped: `{result_.Checks.Count(check_ => check_.Status == "Skipped")}`");
        builder.AppendLine();
        builder.AppendLine("## Checks");
        builder.AppendLine();
        IEnumerable<McpDiagnosticItem> checks = verbose_ ? result_.Checks : result_.Checks.Where(check_ => check_.Status != "Passed");
        if (!checks.Any())
        {
            builder.AppendLine("- All checks passed.");
        }
        else
        {
            builder.AppendLine("| Status | Required | Check | Message |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (McpDiagnosticItem check in checks)
            {
                string message = EscapeTable(check.Message);
                if (!string.IsNullOrWhiteSpace(check.Hint))
                {
                    message = $"{message}<br>Hint: {EscapeTable(check.Hint)}";
                }

                if (verbose_ && check.Details is { Count: > 0 })
                {
                    message = $"{message}<br>Details: {EscapeTable(string.Join(" / ", check.Details))}";
                }

                builder.AppendLine($"| {check.Status} | `{check.Required}` | `{check.Name}` | {message} |");
            }
        }

        if (result_.ClientHints.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Client Hints");
            builder.AppendLine();
            foreach (string hint in result_.ClientHints)
            {
                builder.AppendLine($"- {hint}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string EscapeTable(string value_)
    {
        return value_.Replace("|", "\\|", StringComparison.Ordinal);
    }
}

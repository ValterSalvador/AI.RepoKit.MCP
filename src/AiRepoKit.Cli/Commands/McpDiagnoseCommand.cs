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

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        try
        {
            string repoPath = Path.GetFullPath(options_.RepoPath);
            string mode = GetMode(options_);
            IReadOnlyList<ClientKind> clients = NormalizeClients(options_.Clients);
            List<McpDiagnosticItem> checks = [];
            List<string> hints = [];
            bool rebuilt = false;

            progress.StartPhase("Checking repository");
            AddTimedCheckGroup(checks, "cheap", checks_ => AddRepoChecks(checks_, repoPath));
            progress.CompletePhase("Repository check completed");
            progress.StartPhase("Checking client configs");
            AddTimedCheckGroup(checks, "cheap", checks_ => AddClientChecks(checks_, repoPath, clients));
            progress.CompletePhase("Client config checks completed");
            progress.StartPhase("Checking MCP project");
            AddTimedCheckGroup(checks, "external-process", AddDotnetCheck);
            progress.CompletePhase("MCP project checks completed");

            if (options_.SkipBuildMcp || string.Equals(mode, "quick", StringComparison.OrdinalIgnoreCase))
            {
                string reason = options_.SkipBuildMcp
                    ? "Skipped by --skip-build."
                    : "Skipped in quick mode to avoid Release MCP build.";
                checks.Add(Skipped("mcp-build", !string.Equals(mode, "quick", StringComparison.OrdinalIgnoreCase), reason));
            }
            else
            {
                progress.StartPhase("Building MCP project");
                (McpBuildResult buildResult, McpHostProcessStopResult? stopResult) = Measure(out long elapsedMilliseconds, () => BuildMcpWithOptionalStaleHostRetry(options_, repoPath));
                McpDiagnosticItem buildCheck = CreateBuildCheck(buildResult, stopResult);
                checks.Add(WithTiming(buildCheck, elapsedMilliseconds, "external-process"));
                rebuilt = buildResult.State == "Built";
                if (buildCheck.Status is "Passed" or "Warning")
                {
                    progress.CompletePhase("MCP project build completed");
                }
                else
                {
                    progress.FailPhase("MCP project build failed");
                }
            }

            string dllPath = McpBuildService.GetDllPath(repoPath, options_);
            bool expandedSmoke = !string.Equals(mode, "quick", StringComparison.OrdinalIgnoreCase);
            if (options_.SkipSmoke)
            {
                checks.Add(Skipped("smoke-test", true, "Skipped by --skip-smoke."));
            }
            else
            {
                progress.StartPhase("Running MCP smoke test");
                McpSmokeTestDepth smokeDepth = expandedSmoke ? McpSmokeTestDepth.Expanded : McpSmokeTestDepth.Minimal;
                McpSmokeTestResult smokeResult = Measure(out long elapsedMilliseconds, () => new McpSmokeTestService().Run(repoPath, dllPath, options_.Verbose, options_.StrictStdio, smokeDepth));
                checks.Add(WithTiming(CreateSmokeCheck(smokeResult), elapsedMilliseconds, expandedSmoke ? "expanded-smoke" : "external-process"));
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

            DowngradeLockedBuildWhenSmokePassed(checks, options_.Strict || options_.StrictStdio);

            if (options_.SkipBudget || string.Equals(mode, "quick", StringComparison.OrdinalIgnoreCase))
            {
                string reason = options_.SkipBudget ? "Skipped by --skip-budget." : "Skipped in quick mode.";
                checks.Add(Skipped("budget", false, reason));
            }
            else
            {
                progress.StartPhase("Running budget script");
                McpDiagnosticItem budgetCheck = Measure(out long elapsedMilliseconds, () => RunBudget(repoPath));
                checks.Add(WithTiming(budgetCheck, elapsedMilliseconds, "budget"));
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
            CommandTimingReport? timingReport = options_.Timings ? progress.GetTimingReport() : null;
            McpDiagnosticResult result = new(status, mode, "<repo-root>", exitCode, clients.Select(GetClientName).ToArray(), checks, hints.Select(ProcessRunner.Redact).ToArray(), timingReport);
            string output = options_.AuditJson ? JsonSerializer.Serialize(result, JsonOptions) : WriteMarkdown(result, options_.Verbose, options_.Summary, options_.Timings);
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
                GetMode(options_),
                "<repo-root>",
                1,
                NormalizeClients(options_.Clients).Select(GetClientName).ToArray(),
                [Failed("fatal", true, ProcessRunner.Redact(exception.Message))],
                [],
                options_.Timings ? progress.GetTimingReport() : null);
            string output = options_.AuditJson ? JsonSerializer.Serialize(result, JsonOptions) : WriteMarkdown(result, options_.Verbose, options_.Summary, options_.Timings);
            return CommandResult.Failure(output, 1);
        }
    }

    private static string GetMode(BootstrapOptions options_)
    {
        if (options_.Strict)
        {
            return "strict";
        }

        return options_.Quick ? "quick" : "full";
    }

    private static IReadOnlyList<ClientKind> NormalizeClients(IReadOnlyList<ClientKind> clients_)
    {
        return clients_.Count == 0
            ? [ClientKind.Codex, ClientKind.Vscode, ClientKind.VisualStudio]
            : clients_.Where(client_ => client_ is ClientKind.Codex or ClientKind.Vscode or ClientKind.VisualStudio).Distinct().ToArray();
    }

    private static void AddRepoChecks(List<McpDiagnosticItem> checks_, string repoPath_)
    {
        checks_.Add(Check("repo-root", true, Directory.Exists(repoPath_), "Repo path: <repo-root>."));

        string mcpProjectRoot = Path.Combine(repoPath_, "Tools", "AiContextMcp");
        bool mcpRootExists = Directory.Exists(mcpProjectRoot);
        bool hasProject = mcpRootExists && Directory.EnumerateFiles(mcpProjectRoot, "*.csproj", SearchOption.TopDirectoryOnly).Any();
        checks_.Add(Check("mcp-project", true, mcpRootExists && hasProject, "Tools/AiContextMcp exists and contains a .csproj."));
        checks_.Add(Check("mcp-release-dll", true, File.Exists(GetMcpDllPath(repoPath_)), "Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll exists."));
    }

    private static void AddClientChecks(List<McpDiagnosticItem> checks_, string repoPath_, IReadOnlyList<ClientKind> clients_)
    {
        checks_.Add(BuildClientDiscoverySummary(repoPath_, clients_));

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
            checks_.Add(CheckVisualStudio(repoPath_));
        }
    }

    private static McpDiagnosticItem CheckVscode(string repoPath_)
    {
        return CheckWorkspaceConfig(
            Path.Combine(repoPath_, ".vscode", "mcp.json"),
            "vscode-config",
            ".vscode/mcp.json",
            "vscode");
    }

    private static McpDiagnosticItem CheckVisualStudio(string repoPath_)
    {
        (bool exists, bool valid, string message, bool usesWorkspaceFolder) rootConfig = CheckVisualStudioConfig(Path.Combine(repoPath_, ".mcp.json"), ".mcp.json");
        (bool exists, bool valid, string message, bool usesWorkspaceFolder) localConfig = CheckVisualStudioConfig(Path.Combine(repoPath_, ".vs", "mcp.json"), ".vs/mcp.json");

        if (!rootConfig.exists && !localConfig.exists)
        {
            return Failed("vs-config", true, "Neither .mcp.json nor .vs/mcp.json was found.", "Run bootstrap with --clients vs --mcp --apply or restore the Visual Studio MCP config.");
        }

        List<string> messages = [];
        List<string> details = [];
        bool hasFailure = false;

        AppendVisualStudioConfigResult(rootConfig, messages, details, ref hasFailure);
        AppendVisualStudioConfigResult(localConfig, messages, details, ref hasFailure);

        if (hasFailure)
        {
            return Failed("vs-config", true, string.Join(" ", messages), null, details);
        }

        return Passed("vs-config", true, string.Join(" ", messages), null, details);
    }

    private static McpDiagnosticItem CheckWorkspaceConfig(string path_, string checkName_, string displayPath_, string clientName_)
    {
        if (!File.Exists(path_))
        {
            return Failed(checkName_, true, $"{displayPath_} is missing.", $"Run bootstrap with --clients {clientName_} --mcp --apply or restore the file.");
        }

        string content = File.ReadAllText(path_);
        if (!IsReadableJson(content))
        {
            return Failed(checkName_, true, displayPath_ + " is not readable JSON.");
        }

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
            return Failed(checkName_, true, displayPath_ + " is present but missing: " + string.Join(", ", missing) + ".");
        }

        return Passed(checkName_, true, displayPath_ + " contains ai_repo_context, repo argument, and the MCP DLL path or ${workspaceFolder}.", null, UsesWorkspaceFolder(content) ? ["Uses ${workspaceFolder}."] : []);
    }

    private static (bool Exists, bool Valid, string Message, bool UsesWorkspaceFolder) CheckVisualStudioConfig(string path_, string displayPath_)
    {
        if (!File.Exists(path_))
        {
            return (false, true, displayPath_ + " was not found.", false);
        }

        string content = File.ReadAllText(path_);
        if (!IsReadableJson(content))
        {
            return (true, false, displayPath_ + " is not readable JSON.", false);
        }

        using JsonDocument document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("servers", out JsonElement servers)
            || servers.ValueKind != JsonValueKind.Object)
        {
            return (true, false, displayPath_ + " is missing the Visual Studio MCP `servers` object.", false);
        }

        if (!servers.TryGetProperty("ai_repo_context", out JsonElement server)
            || server.ValueKind != JsonValueKind.Object)
        {
            return (true, false, displayPath_ + " is missing the `ai_repo_context` server entry.", false);
        }

        List<string> missing = [];
        bool hasStdioTransport = server.TryGetProperty("transport", out JsonElement transport)
            && transport.ValueKind == JsonValueKind.String
            && string.Equals(transport.GetString(), "stdio", StringComparison.Ordinal);
        bool hasStdioType = server.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), "stdio", StringComparison.Ordinal);
        if (!hasStdioTransport && !hasStdioType)
        {
            missing.Add("type=stdio or transport=stdio");
        }

        if (!server.TryGetProperty("command", out JsonElement command)
            || command.ValueKind != JsonValueKind.String
            || !string.Equals(command.GetString(), "dotnet", StringComparison.Ordinal))
        {
            missing.Add("command=dotnet");
        }

        bool usesWorkspaceFolder = false;
        bool hasRepoArgument = false;
        bool hasDllArgument = false;
        if (!server.TryGetProperty("args", out JsonElement args)
            || args.ValueKind != JsonValueKind.Array)
        {
            missing.Add("args");
        }
        else
        {
            foreach (JsonElement arg in args.EnumerateArray())
            {
                if (arg.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? value = arg.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                usesWorkspaceFolder |= value.Contains("${workspaceFolder}", StringComparison.OrdinalIgnoreCase);
                hasRepoArgument |= string.Equals(value, "--repo", StringComparison.Ordinal);
                hasDllArgument |= value.EndsWith("AiRepo.ContextMcp.dll", StringComparison.OrdinalIgnoreCase);
            }

            if (!hasDllArgument)
            {
                missing.Add("AiRepo.ContextMcp.dll arg");
            }

            if (!hasRepoArgument)
            {
                missing.Add("--repo");
            }
        }

        if (missing.Count > 0)
        {
            return (true, false, displayPath_ + " is present but missing: " + string.Join(", ", missing) + ".", usesWorkspaceFolder);
        }

        return (true, true, displayPath_ + " uses the Visual Studio MCP schema with ai_repo_context, command, args, stdio transport/type, and --repo.", usesWorkspaceFolder);
    }

    private static void AppendVisualStudioConfigResult(
        (bool exists, bool valid, string message, bool usesWorkspaceFolder) result_,
        List<string> messages_,
        List<string> details_,
        ref bool hasFailure_)
    {
        if (!result_.exists)
        {
            details_.Add(result_.message);
            return;
        }

        messages_.Add(result_.message);
        if (result_.usesWorkspaceFolder)
        {
            details_.Add("Uses ${workspaceFolder}.");
        }

        hasFailure_ |= !result_.valid;
    }

    private static McpDiagnosticItem CheckCodex(string repoPath_)
    {
        string localPath = Path.Combine(repoPath_, ".codex", "config.toml");
        string snippetPath = Path.Combine(repoPath_, ".ai", "client-configs", "codex.config.toml");
        if (File.Exists(localPath))
        {
            string content = File.ReadAllText(localPath);
            bool valid = content.Contains("ai_repo_context", StringComparison.OrdinalIgnoreCase)
                && content.Contains("--repo", StringComparison.OrdinalIgnoreCase);
            return valid
                ? Passed("codex-config", true, ".codex/config.toml exists and contains ai_repo_context plus --repo.")
                : Failed("codex-config", true, ".codex/config.toml exists but is missing ai_repo_context or --repo.");
        }

        if (File.Exists(snippetPath))
        {
            string content = File.ReadAllText(snippetPath);
            bool valid = content.Contains("ai_repo_context", StringComparison.OrdinalIgnoreCase)
                && content.Contains("--repo", StringComparison.OrdinalIgnoreCase);
            return valid
                ? Warning("codex-config", true, ".codex/config.toml is not present. This file may be local or ignored; .ai/client-configs/codex.config.toml is the versionable snippet.")
                : Failed("codex-config", true, ".ai/client-configs/codex.config.toml exists but is missing ai_repo_context or --repo.");
        }

        return Failed("codex-config", true, ".codex/config.toml is missing and .ai/client-configs/codex.config.toml was not found.");
    }

    private static McpDiagnosticItem BuildClientDiscoverySummary(string repoPath_, IReadOnlyList<ClientKind> clients_)
    {
        List<string> states = [];
        foreach (ClientKind client in clients_)
        {
            string primaryPath = ConfigGenerator.GetClientConfigPath(client);
            bool primaryExists = File.Exists(Path.Combine(repoPath_, primaryPath.Replace('/', Path.DirectorySeparatorChar)));
            states.Add($"{GetClientName(client)}={(primaryExists ? primaryPath : "missing")}");

            foreach (string extraPath in new ConfigGenerator().GetAdditionalClientConfigPaths(client))
            {
                bool extraExists = File.Exists(Path.Combine(repoPath_, extraPath.Replace('/', Path.DirectorySeparatorChar)));
                if (extraExists)
                {
                    states.Add($"{GetClientName(client)}-extra={extraPath}");
                }
            }
        }

        return Passed("client-config-discovery", false, "Discovered client config paths: " + string.Join("; ", states) + ".");
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

    private static (McpBuildResult BuildResult, McpHostProcessStopResult? StopResult) BuildMcpWithOptionalStaleHostRetry(BootstrapOptions options_, string repoPath_)
    {
        McpBuildService buildService = new();
        McpBuildResult first = buildService.Execute(options_);
        if (!options_.StopStaleMcpHosts
            || first.State != "Failed"
            || first.Process is null
            || !McpBuildFailureDiagnostics.IsLockedDllFailure(first.Process))
        {
            return (first, null);
        }

        McpHostProcessStopResult stopResult = new McpHostProcessService().StopStaleHostsForRepo(repoPath_);
        if (!stopResult.Supported)
        {
            return (first with
            {
                Message = stopResult.Message,
                Hint = McpBuildFailureDiagnostics.LockedDllRetryHint
            }, stopResult);
        }

        McpBuildResult retry = buildService.Execute(options_);
        if (retry.State == "Failed" && retry.Process is not null && McpBuildFailureDiagnostics.IsLockedDllFailure(retry.Process))
        {
            retry = retry with
            {
                Message = "MCP build still failed after stopping stale MCP hosts.",
                Hint = McpBuildFailureDiagnostics.LockedDllRetryHint
            };
        }

        return (retry, stopResult);
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
            "SkippedLockedSmokePassed. Locked MCP DLL reuse was accepted because JSON-RPC smoke passed.",
            build.Hint,
            build.Details,
            build.ElapsedMilliseconds,
            build.Cost);
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

        if (configsPassed && smokePassed && clients_.Contains(ClientKind.VisualStudio))
        {
            hints_.Add("Visual Studio MCP requires Visual Studio 2022 17.14 or later. Reload the solution after generation and enable the MCP tools manually in Copilot Agent if they are still disabled.");
        }

        if (clients_.Contains(ClientKind.Codex))
        {
            hints_.Add("Codex-compatible clients may prefer the local .codex/config.toml while the versionable snippet remains under .ai/client-configs/codex.config.toml.");
        }

        if (rebuilt_)
        {
            hints_.Add("The MCP DLL was rebuilt; MCP clients may need a restart or reload before they use the new server binary.");
        }
    }

    private static bool IsReadableJson(string content_)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(content_);
            return true;
        }
        catch
        {
            return false;
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
        return new McpDiagnosticItem(name_, "Passed", required_, ProcessRunner.Redact(message_), hint_ is null ? null : ProcessRunner.Redact(hint_), details_?.Select(ProcessRunner.Redact).ToArray() ?? []);
    }

    private static McpDiagnosticItem Warning(string name_, bool required_, string message_, string? hint_ = null, IReadOnlyList<string>? details_ = null)
    {
        return new McpDiagnosticItem(name_, "Warning", required_, ProcessRunner.Redact(message_), hint_ is null ? null : ProcessRunner.Redact(hint_), details_?.Select(ProcessRunner.Redact).ToArray() ?? []);
    }

    private static McpDiagnosticItem Failed(string name_, bool required_, string message_, string? hint_ = null, IReadOnlyList<string>? details_ = null)
    {
        return new McpDiagnosticItem(name_, "Failed", required_, ProcessRunner.Redact(message_), hint_ is null ? null : ProcessRunner.Redact(hint_), details_?.Select(ProcessRunner.Redact).ToArray() ?? []);
    }

    private static McpDiagnosticItem Skipped(string name_, bool required_, string message_)
    {
        return new McpDiagnosticItem(name_, "Skipped", required_, ProcessRunner.Redact(message_), null, [], 0, "skipped");
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

    private static McpDiagnosticItem CreateBuildCheck(McpBuildResult build_, McpHostProcessStopResult? stopResult_ = null)
    {
        string message = build_.State switch
        {
            "Built" => "Built. Release MCP build passed.",
            "SkippedCurrent" => "SkippedCurrent. Release MCP build skipped because the output DLL is current.",
            "SkippedLockedSmokePassed" => "SkippedLockedSmokePassed. Locked MCP DLL reuse was accepted because JSON-RPC smoke passed.",
            _ => $"Failed. {build_.Message}"
        };
        IReadOnlyList<string> details = build_.State == "SkippedCurrent"
            ? ["Freshness decision: MCP output DLL is newer than project inputs; Release build was not run."]
            : build_.Process is null ? [] : GetProcessDetails(build_.Process);
        if (stopResult_ is not null)
        {
            details = details.Concat([stopResult_.Message]).ToArray();
        }

        return new McpDiagnosticItem("mcp-build", build_.State == "Failed" ? "Failed" : build_.State == "SkippedLockedSmokePassed" ? "Warning" : "Passed", build_.State == "Failed", ProcessRunner.Redact(message), build_.Hint is null ? null : ProcessRunner.Redact(build_.Hint), details.Select(ProcessRunner.Redact).ToArray());
    }

    private static McpDiagnosticItem CreateSmokeCheck(McpSmokeTestResult result_)
    {
        return new McpDiagnosticItem("smoke-test", result_.Status, true, ProcessRunner.Redact(result_.Message), null, result_.Details.Select(ProcessRunner.Redact).ToArray());
    }

    private static string WriteMarkdown(McpDiagnosticResult result_, bool verbose_, bool summary_, bool showTimings_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# MCP Diagnose");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{result_.RepoPath}`");
        builder.AppendLine($"- Clients: `{string.Join(",", result_.Clients)}`");
        builder.AppendLine($"- Mode: `{result_.Mode}`");
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
        AppendChecks(builder, result_.Checks, verbose_, summary_);

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

        if (showTimings_ && result_.Timings is not null)
        {
            AppendTimings(builder, result_.Timings);
            AppendCheckTimings(builder, result_.Checks);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendChecks(StringBuilder builder_, IReadOnlyList<McpDiagnosticItem> checks_, bool verbose_, bool summary_)
    {
        builder_.AppendLine("## Checks");
        builder_.AppendLine();
        IEnumerable<McpDiagnosticItem> checks = summary_ ? checks_.Where(check_ => check_.Status != "Passed") : verbose_ ? checks_ : checks_.Where(check_ => check_.Status != "Passed");
        if (!checks.Any())
        {
            builder_.AppendLine("- All checks passed.");
            return;
        }

        if (summary_)
        {
            foreach (McpDiagnosticItem check in checks)
            {
                builder_.AppendLine($"- [{check.Status}] `{check.Name}`: {check.Message}");
                if (!string.IsNullOrWhiteSpace(check.Hint))
                {
                    builder_.AppendLine($"  - Hint: {check.Hint}");
                }
            }

            return;
        }

        builder_.AppendLine("| Status | Required | Check | Message |");
        builder_.AppendLine("| --- | --- | --- | --- |");
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

            builder_.AppendLine($"| {check.Status} | `{check.Required}` | `{check.Name}` | {message} |");
        }
    }

    private static void AppendTimings(StringBuilder builder_, CommandTimingReport timings_)
    {
        builder_.AppendLine();
        builder_.AppendLine("## Timings");
        builder_.AppendLine();
        builder_.AppendLine($"- Total: `{timings_.TotalElapsedMilliseconds} ms`");
        foreach (CommandPhaseTiming phase in timings_.Phases)
        {
            builder_.AppendLine($"- {phase.Name}: `{phase.ElapsedMilliseconds} ms` ({phase.Status})");
        }
    }

    private static void AppendCheckTimings(StringBuilder builder_, IReadOnlyList<McpDiagnosticItem> checks_)
    {
        builder_.AppendLine();
        builder_.AppendLine("## Check Timings");
        builder_.AppendLine();
        builder_.AppendLine("| Check | Status | Cost | Elapsed |");
        builder_.AppendLine("| --- | --- | --- | --- |");
        foreach (McpDiagnosticItem check in checks_)
        {
            string elapsed = check.ElapsedMilliseconds.HasValue ? $"{check.ElapsedMilliseconds.Value} ms" : "n/a";
            builder_.AppendLine($"| `{check.Name}` | {check.Status} | `{check.Cost ?? "unknown"}` | `{elapsed}` |");
        }
    }

    private static void AddTimedCheckGroup(List<McpDiagnosticItem> checks_, string cost_, Action<List<McpDiagnosticItem>> addChecks_)
    {
        int start = checks_.Count;
        Stopwatch stopwatch = Stopwatch.StartNew();
        addChecks_(checks_);
        stopwatch.Stop();

        for (int index = start; index < checks_.Count; index++)
        {
            checks_[index] = WithTiming(checks_[index], stopwatch.ElapsedMilliseconds, cost_);
        }
    }

    private static T Measure<T>(out long elapsedMilliseconds_, Func<T> action_)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        T result = action_();
        stopwatch.Stop();
        elapsedMilliseconds_ = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private static McpDiagnosticItem WithTiming(McpDiagnosticItem item_, long elapsedMilliseconds_, string cost_)
    {
        return item_ with
        {
            ElapsedMilliseconds = elapsedMilliseconds_,
            Cost = cost_
        };
    }

    private static string EscapeTable(string value_)
    {
        return value_.Replace("|", "\\|", StringComparison.Ordinal);
    }
}

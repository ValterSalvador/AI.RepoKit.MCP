using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ManagedFiles;
using AiRepoKit.Cli.Models.SelfCheck;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.CodeIndex;
using AiRepoKit.Cli.Services.ManagedFiles;

namespace AiRepoKit.Cli.Commands;

public sealed class SelfCheckCommand
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
            List<SelfCheckItem> checks = [];
            progress.StartPhase("Checking repository");
            RepoAnalysis analysis = new RepoDetector().Analyze(repoPath);

            checks.Add(Check("repo-detection", true, IsRepoDetected(analysis), $"Repo exists: {analysis.Profile.Exists}; solutions: {analysis.SolutionFiles.Count + CountRootSlnx(repoPath)}; projects: {analysis.ProjectFiles.Count}."));
            AddRequiredFileChecks(checks, repoPath);
            AddClientConfigChecks(checks, repoPath, ConfigGenerator.GetSelectedClients(options_));
            progress.CompletePhase("Repository check completed");

            if (ShouldSkipAudit(options_, mode))
            {
                checks.Add(Skipped("audit", false, options_.SkipAudit ? "Skipped by --skip-audit." : $"Skipped in {mode} mode."));
            }
            else
            {
                progress.StartPhase("Running audit");
                CommandResult audit = new AuditCommand().Execute(options_);
                checks.Add(new SelfCheckItem("audit", audit.ExitCode != 0 ? "Failed" : "Passed", true, $"Exit code {audit.ExitCode}.", audit.ExitCode));
                if (audit.Success)
                {
                    progress.CompletePhase("Audit completed");
                }
                else
                {
                    progress.WarnPhase("Audit completed with findings");
                }
            }

            if (ShouldSkipCodeIndex(options_, mode))
            {
                checks.Add(Skipped("code-index", false, options_.SkipCodeInventory ? "Skipped by --skip-code-index." : $"Skipped in {mode} mode."));
                string cachePath = Path.Combine(repoPath, CodeIndexCacheService.CacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
                checks.Add(Check("code-index-cache", false, File.Exists(cachePath), File.Exists(cachePath)
                    ? CodeIndexCacheService.CacheRelativePath + " reused."
                    : CodeIndexCacheService.CacheRelativePath + " is missing."));
            }
            else
            {
                progress.StartPhase("Running code index");
                CommandResult codeIndex = new CodeIndexCommand().Execute(CreateValidationCodeIndexOptions(options_));
                checks.Add(new SelfCheckItem("code-index", codeIndex.Success ? "Passed" : "Failed", true, $"Exit code {codeIndex.ExitCode}.", codeIndex.ExitCode));
                string cachePath = Path.Combine(repoPath, CodeIndexCacheService.CacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
                checks.Add(Check("code-index-cache", true, File.Exists(cachePath), CodeIndexCacheService.CacheRelativePath));
                if (codeIndex.Success)
                {
                    progress.CompletePhase("Code index completed");
                }
                else
                {
                    progress.FailPhase("Code index failed");
                }
            }

            if (ShouldSkipMcpBuild(options_, mode))
            {
                checks.Add(Skipped("mcp-build", false, options_.SkipBuildMcp ? "Skipped by --skip-build-mcp." : $"Skipped in {mode} mode."));
            }
            else
            {
                progress.StartPhase("Checking MCP files");
                string mcpProjectPath = Path.Combine(repoPath, options_.McpProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(mcpProjectPath))
                {
                    progress.StartPhase("Building MCP project");
                    McpBuildResult build = new McpBuildService().Execute(options_);
                    if (build.State == "Failed" && build.Process is not null && McpBuildFailureDiagnostics.IsLockedDllFailure(build.Process) && !options_.Strict)
                    {
                        var smoke = new McpSmokeTestService().Run(repoPath, build.DllPath, options_.Verbose);
                        if (smoke.Success)
                        {
                            build = McpBuildService.CreateLockedSmokePassed(build);
                        }
                    }

                    checks.Add(GetMcpBuildCheck(build, options_));
                    if (build.State != "Failed")
                    {
                        progress.CompletePhase("MCP project build completed");
                    }
                    else
                    {
                        progress.FailPhase("MCP project build failed");
                    }
                }
                else
                {
                    checks.Add(Failed("mcp-build", true, $"Missing {options_.McpProjectRelativePath}."));
                    progress.FailPhase("MCP project file missing");
                }
            }

            if (ShouldSkipBudget(options_, mode))
            {
                checks.Add(Skipped("mcp-budget", false, options_.SkipBudget ? "Skipped by --skip-budget." : $"Skipped in {mode} mode."));
            }
            else
            {
                string budgetScript = "Tools/AiContext/MeasureMcpResponseBudget.ps1";
                string budgetPath = Path.Combine(repoPath, budgetScript.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(budgetPath))
                {
                    progress.StartPhase("Running budget script");
                    ProcessResult budget = new ProcessRunner().Run("powershell", ["-ExecutionPolicy", "Bypass", "-File", budgetScript], repoPath);
                    checks.Add(new SelfCheckItem("mcp-budget", budget.Success ? "Passed" : "Failed", true, GetProcessMessage(budget), budget.ExitCode));
                    if (budget.Success)
                    {
                        progress.CompletePhase("Budget script completed");
                    }
                    else
                    {
                        progress.FailPhase("Budget script failed");
                    }
                }
                else
                {
                    checks.Add(Failed("mcp-budget", true, $"Missing {budgetScript}."));
                }
            }

            progress.StartPhase("Checking generated outputs");
            AddGeneratedOutputChecks(checks, repoPath);
            AddContextPackChecks(checks, repoPath, options_.RequireContextPacks);
            AddChangedFilesContextPackCheck(checks, repoPath, options_.RequireContextPacks);
            AddGraphChecks(checks, repoPath, false);
            AddForbiddenTermChecks(checks, repoPath, options_.ForbiddenTerms);
            AddGitIgnoreCheck(checks, repoPath);
            AddAgentChecks(checks, repoPath, options_.IncludeAgents, options_.Profile);
            progress.CompletePhase("Generated output checks completed");

            int exitCode = checks.Any(check_ => check_.Required && string.Equals(check_.Status, "Failed", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;
            string status = exitCode == 2
                ? "Failed"
                : checks.Any(check_ => string.Equals(check_.Status, "Warning", StringComparison.OrdinalIgnoreCase))
                    ? "Warning"
                    : "Passed";
            CommandTimingReport? timingReport = options_.Timings ? progress.GetTimingReport() : null;
            SelfCheckResult result = new(status, mode, repoPath, exitCode, checks, timingReport);
            string output = options_.AuditJson ? JsonSerializer.Serialize(result, JsonOptions) : WriteMarkdown(result, options_.Verbose, options_.Summary, options_.Timings);
            if (exitCode == 0)
            {
                progress.CompletePhase("Self-check completed");
            }
            else
            {
                progress.FailPhase("Self-check completed with failures");
            }
            return new CommandResult(exitCode == 0, output, exitCode);
        }
        catch (Exception exception)
        {
            progress.FailPhase("Self-check failed");
            SelfCheckResult result = new("Failed", GetMode(options_), Path.GetFullPath(options_.RepoPath), 1, [Failed("fatal", true, ProcessRunner.Redact(exception.Message), 1)], options_.Timings ? progress.GetTimingReport() : null);
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

        if (options_.Full)
        {
            return "full";
        }

        return options_.Quick ? "quick" : "balanced";
    }

    private static bool ShouldSkipAudit(BootstrapOptions options_, string mode_)
    {
        return options_.SkipAudit || mode_ is "balanced" or "quick";
    }

    private static bool ShouldSkipCodeIndex(BootstrapOptions options_, string mode_)
    {
        return options_.SkipCodeInventory || mode_ is "balanced" or "quick";
    }

    private static bool ShouldSkipMcpBuild(BootstrapOptions options_, string mode_)
    {
        return options_.SkipBuildMcp || mode_ is "balanced" or "quick";
    }

    private static bool ShouldSkipBudget(BootstrapOptions options_, string mode_)
    {
        return options_.SkipBudget || mode_ is "balanced" or "quick";
    }

    private static void AddRequiredFileChecks(List<SelfCheckItem> checks_, string repoPath_)
    {
        foreach (string path in GetRequiredFiles())
        {
            checks_.Add(Check($"required-file:{path}", true, File.Exists(Path.Combine(repoPath_, path.Replace('/', Path.DirectorySeparatorChar))), path));
        }
    }

    private static IReadOnlyList<string> GetRequiredFiles()
    {
        return
        [
            ".ai/README.md",
            ".ai/manifests/mcp-context-manifest.json",
            "Tools/AiContext/UpdateAiContext.ps1",
            "Tools/AiContext/CheckSdkAlignment.ps1",
            "Tools/AiContext/UpdateCodeInventory.ps1",
            "Tools/AiContext/InvokeBuildDiagnostics.ps1",
            "Tools/AiContext/CheckSecrets.ps1",
            "Tools/AiContext/MeasureMcpResponseBudget.ps1",
            "Tools/AiContextMcp/Program.cs",
            "Tools/AiContextMcp/README.md"
        ];
    }

    private static void AddClientConfigChecks(List<SelfCheckItem> checks_, string repoPath_, IReadOnlyList<ClientKind> clients_)
    {
        if (!clients_.Contains(ClientKind.VisualStudio))
        {
            return;
        }

        (bool exists, bool valid, string message) rootConfig = CheckVisualStudioConfig(Path.Combine(repoPath_, ".mcp.json"), ".mcp.json");
        (bool exists, bool valid, string message) localConfig = CheckVisualStudioConfig(Path.Combine(repoPath_, ".vs", "mcp.json"), ".vs/mcp.json");
        if (!rootConfig.exists && !localConfig.exists)
        {
            checks_.Add(Failed("client-config:vs", true, "Missing .mcp.json and .vs/mcp.json for Visual Studio MCP discovery."));
            return;
        }

        List<string> messages = [];
        bool passed = true;
        AppendVisualStudioConfigResult(rootConfig, messages, ref passed);
        AppendVisualStudioConfigResult(localConfig, messages, ref passed);
        checks_.Add(Check("client-config:vs", true, passed, string.Join(" ", messages)));
    }

    private static (bool Exists, bool Valid, string Message) CheckVisualStudioConfig(string path_, string displayPath_)
    {
        if (!File.Exists(path_))
        {
            return (false, true, displayPath_ + " was not found.");
        }

        if (!IsReadableJson(path_))
        {
            return (true, false, displayPath_ + " is not readable JSON.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path_));
        if (!document.RootElement.TryGetProperty("servers", out JsonElement servers)
            || servers.ValueKind != JsonValueKind.Object)
        {
            return (true, false, displayPath_ + " is missing the Visual Studio MCP `servers` object.");
        }

        if (!servers.TryGetProperty("ai_repo_context", out JsonElement server)
            || server.ValueKind != JsonValueKind.Object)
        {
            return (true, false, displayPath_ + " is missing the `ai_repo_context` server entry.");
        }

        List<string> missing = [];
        if (!server.TryGetProperty("transport", out JsonElement transport)
            || transport.ValueKind != JsonValueKind.String
            || !string.Equals(transport.GetString(), "stdio", StringComparison.Ordinal))
        {
            missing.Add("transport=stdio");
        }

        if (!server.TryGetProperty("command", out JsonElement command)
            || command.ValueKind != JsonValueKind.String
            || !string.Equals(command.GetString(), "dotnet", StringComparison.Ordinal))
        {
            missing.Add("command=dotnet");
        }

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
            return (true, false, displayPath_ + " is present but missing: " + string.Join(", ", missing) + ".");
        }

        return (true, true, displayPath_ + " uses the Visual Studio MCP schema with ai_repo_context, command, args, transport=stdio, and --repo.");
    }

    private static void AppendVisualStudioConfigResult((bool exists, bool valid, string message) result_, List<string> messages_, ref bool passed_)
    {
        if (!result_.exists)
        {
            return;
        }

        messages_.Add(result_.message);
        passed_ &= result_.valid;
    }

    private static void AddGeneratedOutputChecks(List<SelfCheckItem> checks_, string repoPath_)
    {
        string generatedPath = Path.Combine(repoPath_, ".ai", "generated");
        checks_.Add(Check("generated-root", true, Directory.Exists(generatedPath), ".ai/generated exists."));

        bool bootstrapApplied = Directory.Exists(Path.Combine(repoPath_, "Tools", "AiContextMcp"))
            || Directory.Exists(Path.Combine(repoPath_, "Tools", "AiContext"))
            || File.Exists(Path.Combine(repoPath_, ".mcp.json"))
            || File.Exists(Path.Combine(repoPath_, ".vscode", "mcp.json"))
            || File.Exists(Path.Combine(repoPath_, ".codex", "config.toml"));
        string manifestPath = Path.Combine(repoPath_, ManagedFilesService.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (bootstrapApplied)
        {
            checks_.Add(Check("managed-files-manifest", true, File.Exists(manifestPath), ManagedFilesService.ManifestRelativePath));
        }
        else
        {
            checks_.Add(Skipped("managed-files-manifest", false, "Bootstrap does not appear to be applied."));
        }
    }

    private static void AddContextPackChecks(List<SelfCheckItem> checks_, string repoPath_, bool required_)
    {
        string path = Path.Combine(repoPath_, ".ai", "generated", "context-packs");
        if (!Directory.Exists(path))
        {
            checks_.Add(required_ ? Failed("context-packs", true, "No context packs found.") : Skipped("context-packs", false, "No context packs found."));
            return;
        }

        string[] files = Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            checks_.Add(required_ ? Failed("context-packs", true, "No context pack JSON files found.") : Skipped("context-packs", false, "No context pack JSON files found."));
            return;
        }

        int readable = 0;
        foreach (string file in files)
        {
            try
            {
                using FileStream stream = File.OpenRead(file);
                JsonDocument.Parse(stream);
                readable++;
            }
            catch
            {
                checks_.Add(Failed($"context-pack-json:{Path.GetFileName(file)}", true, "Context pack JSON is not readable."));
            }
        }

        checks_.Add(Check("context-packs", required_, readable == files.Length && readable > 0, $"Readable context pack JSON files: {readable}/{files.Length}."));
    }

    private static void AddChangedFilesContextPackCheck(List<SelfCheckItem> checks_, string repoPath_, bool required_)
    {
        string path = Path.Combine(repoPath_, ".ai", "generated", "context-packs", "changed-files.json");
        if (!File.Exists(path))
        {
            checks_.Add(required_ ? Failed("context-pack:changed-files", true, "Missing .ai/generated/context-packs/changed-files.json.") : Skipped("context-pack:changed-files", false, "changed-files context pack is not present."));
            return;
        }

        checks_.Add(Check("context-pack:changed-files", required_, IsReadableJson(path), ".ai/generated/context-packs/changed-files.json is readable."));
    }

    private static void AddGraphChecks(List<SelfCheckItem> checks_, string repoPath_, bool required_)
    {
        string path = Path.Combine(repoPath_, ".ai", "generated", "graphs");
        if (!Directory.Exists(path))
        {
            checks_.Add(Skipped("graphs", required_, "No graph artifacts found."));
            return;
        }

        string[] files = Directory.GetFiles(path, "*-graph.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            checks_.Add(Skipped("graphs", required_, "No graph JSON artifacts found."));
            return;
        }

        int readable = files.Count(IsReadableJson);
        checks_.Add(Check("graphs", required_, readable == files.Length, $"Readable graph JSON files: {readable}/{files.Length}."));
    }

    private static void AddGitIgnoreCheck(List<SelfCheckItem> checks_, string repoPath_)
    {
        string path = Path.Combine(repoPath_, ".gitignore");
        bool exists = File.Exists(path);
        bool hasSection = exists && new GitIgnoreService().HasSection(File.ReadAllText(path));
        checks_.Add(Check("gitignore-airepokit-section", true, hasSection, ".gitignore contains AiRepoKit section."));
    }

    private static bool IsReadableJson(string path_)
    {
        try
        {
            using FileStream stream = File.OpenRead(path_);
            JsonDocument.Parse(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddForbiddenTermChecks(List<SelfCheckItem> checks_, string repoPath_, IReadOnlyList<string> terms_)
    {
        if (terms_.Count == 0)
        {
            return;
        }

        IReadOnlyList<ForbiddenTermFinding> findings = new ForbiddenTermScanner().Scan(repoPath_, terms_);
        if (findings.Count == 0)
        {
            checks_.Add(Check("forbidden-term", true, true, $"Forbidden terms not found: {string.Join(", ", terms_)}."));
            return;
        }

        int generated = findings.Count(finding_ => finding_.GeneratedArtifact);
        int managed = findings.Count(finding_ => finding_.ManagedFile);
        checks_.Add(Failed("forbidden-term", true, $"Forbidden term findings: {findings.Count}; managed: {managed}; generated/cache: {generated}. Regenerate generated artifacts or run sanitize for managed files."));
    }

    private static void AddAgentChecks(List<SelfCheckItem> checks_, string repoPath_, bool required_, string profileName_)
    {
        ConfigGenerator configGenerator = new();
        IReadOnlyList<string> paths = configGenerator.GetAgentTemplateDestinationPaths(profileName_);
        IReadOnlyList<string> present = paths.Where(path_ => File.Exists(Path.Combine(repoPath_, path_.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
        checks_.Add(Check("profile", false, true, $"Selected profile: {profileName_}; present profile files: {present.Count}/{paths.Count}."));
        if (!required_ && present.Count == 0)
        {
            checks_.Add(Skipped("agents", false, "Optional agent templates are not present."));
            return;
        }

        if (!required_ && present.Count < paths.Count)
        {
            checks_.Add(new SelfCheckItem("agents", "Warning", false, $"Optional agent templates are partially present: {present.Count}/{paths.Count}.", 0));
            return;
        }

        foreach (string path in paths)
        {
            checks_.Add(Check($"agent-file:{path}", required_, File.Exists(Path.Combine(repoPath_, path.Replace('/', Path.DirectorySeparatorChar))), path));
        }
    }

    private static BootstrapOptions CreateValidationCodeIndexOptions(BootstrapOptions options_)
    {
        return options_.With(command_: "code-index", apply_: true, dryRun_: false, validationOnly_: true);
    }

    private static bool IsRepoDetected(RepoAnalysis analysis_)
    {
        return analysis_.Profile.Exists
            && (analysis_.SolutionFiles.Count > 0
                || CountRootSlnx(analysis_.Profile.RootPath) > 0
                || analysis_.ProjectFiles.Count > 0
                || Directory.Exists(Path.Combine(analysis_.Profile.RootPath, ".git"))
                || analysis_.HasAiDirectory);
    }

    private static int CountRootSlnx(string repoPath_)
    {
        return Directory.Exists(repoPath_) ? Directory.EnumerateFiles(repoPath_, "*.slnx", SearchOption.TopDirectoryOnly).Count() : 0;
    }

    private static SelfCheckItem Check(string name_, bool required_, bool passed_, string message_)
    {
        return passed_ ? new SelfCheckItem(name_, "Passed", required_, message_, 0) : Failed(name_, required_, message_);
    }

    private static SelfCheckItem Failed(string name_, bool required_, string message_, int exitCode_ = 2)
    {
        return new SelfCheckItem(name_, "Failed", required_, message_, exitCode_);
    }

    private static SelfCheckItem Skipped(string name_, bool required_, string message_)
    {
        return new SelfCheckItem(name_, "Skipped", required_, message_, 0);
    }

    private static SelfCheckItem GetMcpBuildCheck(McpBuildResult build_, BootstrapOptions options_)
    {
        if (build_.State == "SkippedLockedSmokePassed")
        {
            return new SelfCheckItem("mcp-build", "Warning", false, "SkippedLockedSmokePassed. Locked MCP DLL reuse was accepted because JSON-RPC smoke passed.", 0, build_.Hint);
        }

        return build_.State switch
        {
            "Built" => new SelfCheckItem("mcp-build", "Passed", true, "Built. Release MCP build passed.", 0),
            "SkippedCurrent" => new SelfCheckItem("mcp-build", "Passed", true, "SkippedCurrent. Release MCP build skipped because the output DLL is current.", 0),
            _ => new SelfCheckItem("mcp-build", "Failed", true, build_.Message, build_.Process?.ExitCode ?? 2, build_.Hint)
        };
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

    private static string WriteMarkdown(SelfCheckResult result_, bool verbose_, bool summary_, bool showTimings_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Self Check");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{result_.RepoPath}`");
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
        builder.AppendLine("## Checks");
        builder.AppendLine();
        IEnumerable<SelfCheckItem> checks = summary_ ? result_.Checks.Where(check_ => check_.Status != "Passed") : verbose_ ? result_.Checks : result_.Checks.Where(check_ => check_.Status != "Passed");
        if (!checks.Any())
        {
            builder.AppendLine("- All checks passed.");
        }
        else if (summary_)
        {
            foreach (SelfCheckItem check in checks)
            {
                builder.AppendLine($"- [{check.Status}] `{check.Name}`: {check.Message}");
                if (!string.IsNullOrWhiteSpace(check.Hint))
                {
                    builder.AppendLine($"  - Hint: {check.Hint}");
                }
            }
        }
        else
        {
            builder.AppendLine("| Status | Required | Check | Message |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (SelfCheckItem check in checks)
            {
                string message = check.Message.Replace("|", "\\|", StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(check.Hint))
                {
                    message = $"{message}<br>Hint: {check.Hint.Replace("|", "\\|", StringComparison.Ordinal)}";
                }

                builder.AppendLine($"| {check.Status} | `{check.Required}` | `{check.Name}` | {message} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## MCP Diagnostics");
        builder.AppendLine();
        builder.AppendLine("- Run `airepo mcp-diagnose` for MCP/client diagnostics.");

        if (showTimings_ && result_.Timings is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Timings");
            builder.AppendLine();
            builder.AppendLine($"- Total: `{result_.Timings.TotalElapsedMilliseconds} ms`");
            foreach (CommandPhaseTiming phase in result_.Timings.Phases)
            {
                builder.AppendLine($"- {phase.Name}: `{phase.ElapsedMilliseconds} ms` ({phase.Status})");
            }
        }

        return builder.ToString().TrimEnd();
    }
}

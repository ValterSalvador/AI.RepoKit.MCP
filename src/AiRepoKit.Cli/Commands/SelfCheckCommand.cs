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
            List<SelfCheckItem> checks = [];
            progress.StartPhase("Checking repository");
            RepoAnalysis analysis = new RepoDetector().Analyze(repoPath);

            checks.Add(Check("repo-detection", true, IsRepoDetected(analysis), $"Repo exists: {analysis.Profile.Exists}; solutions: {analysis.SolutionFiles.Count + CountRootSlnx(repoPath)}; projects: {analysis.ProjectFiles.Count}."));
            AddRequiredFileChecks(checks, repoPath);
            progress.CompletePhase("Repository check completed");

            if (options_.SkipAudit)
            {
                checks.Add(Skipped("audit", false, "Skipped by --skip-audit."));
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

            if (options_.SkipCodeInventory)
            {
                checks.Add(Skipped("code-index", false, "Skipped by --skip-code-index."));
                checks.Add(Skipped("code-index-cache", false, "Skipped because code-index was skipped."));
            }
            else
            {
                progress.StartPhase("Running code index");
                CommandResult codeIndex = new CodeIndexCommand().Execute(CreateApplyOptions(options_));
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

            if (options_.SkipBuildMcp)
            {
                checks.Add(Skipped("mcp-build", false, "Skipped by --skip-build-mcp."));
            }
            else
            {
                progress.StartPhase("Checking MCP files");
                string mcpProjectPath = Path.Combine(repoPath, options_.McpProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(mcpProjectPath))
                {
                    progress.StartPhase("Building MCP project");
                    ProcessResult build = new ProcessRunner().Run("dotnet", ["build", options_.McpProjectRelativePath, "-c", "Release"], repoPath);
                    checks.Add(GetMcpBuildCheck(build, options_));
                    if (build.Success)
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

            if (options_.SkipBudget)
            {
                checks.Add(Skipped("mcp-budget", false, "Skipped by --skip-budget."));
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
            SelfCheckResult result = new(status, repoPath, exitCode, checks);
            string output = options_.AuditJson ? JsonSerializer.Serialize(result, JsonOptions) : WriteMarkdown(result, options_.Verbose);
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
            SelfCheckResult result = new("Failed", Path.GetFullPath(options_.RepoPath), 1, [Failed("fatal", true, ProcessRunner.Redact(exception.Message), 1)]);
            string output = options_.AuditJson ? JsonSerializer.Serialize(result, JsonOptions) : WriteMarkdown(result, options_.Verbose);
            return CommandResult.Failure(output, 1);
        }
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

    private static void AddGeneratedOutputChecks(List<SelfCheckItem> checks_, string repoPath_)
    {
        string generatedPath = Path.Combine(repoPath_, ".ai", "generated");
        checks_.Add(Check("generated-root", true, Directory.Exists(generatedPath), ".ai/generated exists."));

        bool bootstrapApplied = Directory.Exists(Path.Combine(repoPath_, "Tools", "AiContextMcp"))
            || Directory.Exists(Path.Combine(repoPath_, "Tools", "AiContext"))
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

    private static void AddGitIgnoreCheck(List<SelfCheckItem> checks_, string repoPath_)
    {
        string path = Path.Combine(repoPath_, ".gitignore");
        bool exists = File.Exists(path);
        bool hasSection = exists && new GitIgnoreService().HasSection(File.ReadAllText(path));
        checks_.Add(Check("gitignore-airepokit-section", true, hasSection, ".gitignore contains AiRepoKit section."));
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

    private static BootstrapOptions CreateApplyOptions(BootstrapOptions options_)
    {
        return new BootstrapOptions(
            options_.Command,
            options_.RepoPath,
            options_.Clients,
            options_.IncludeMcp,
            true,
            false,
            options_.Backup,
            options_.Force,
            options_.ForceManaged,
            options_.Profile,
            options_.TargetFramework,
            options_.McpServerName,
            options_.ToolCommandName,
            options_.McpProjectName,
            options_.McpNamespace,
            options_.McpAssemblyName,
            options_.McpProjectRelativePath,
            options_.SkipBuildMcp,
            options_.SkipAiContext,
            options_.SkipCodeInventory,
            options_.SkipSecurityScan,
            options_.SkipBudget,
            options_.SkipSmoke,
            options_.SkipScripts,
            options_.MaxFiles,
            options_.MaxItems,
            options_.IncludePrivateMembers,
            false,
            false,
            options_.Output,
            options_.Format,
            options_.Verbose,
            options_.AuditJson,
            options_.IncludeSource,
            false,
            false,
            false,
            false,
            options_.SkipAudit,
            options_.IncludeAgents,
            options_.Task,
            options_.Target,
            options_.Limit,
            options_.RequireContextPacks,
            options_.UnknownOptions,
            options_.NoProgress);
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

    private static SelfCheckItem GetMcpBuildCheck(ProcessResult build_, BootstrapOptions options_)
    {
        if (!build_.Success && McpBuildFailureDiagnostics.IsLockedDllFailure(build_))
        {
            if (!options_.Strict)
            {
                CommandResult diagnose = new McpDiagnoseCommand().Execute(options_.With(command_: "mcp-diagnose", skipBuildMcp_: true));
                if (diagnose.Success)
                {
                    return new SelfCheckItem("mcp-build", "Warning", false, McpBuildFailureDiagnostics.LockedDllMessage + " JSON-RPC diagnostics passed, so this is non-blocking outside strict validation.", build_.ExitCode, McpBuildFailureDiagnostics.LockedDllHint);
                }
            }

            return new SelfCheckItem("mcp-build", "Failed", true, McpBuildFailureDiagnostics.LockedDllMessage, build_.ExitCode, McpBuildFailureDiagnostics.LockedDllHint);
        }

        return new SelfCheckItem("mcp-build", build_.Success ? "Passed" : "Failed", true, GetProcessMessage(build_), build_.ExitCode);
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

    private static string WriteMarkdown(SelfCheckResult result_, bool verbose_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Self Check");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{result_.RepoPath}`");
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
        IEnumerable<SelfCheckItem> checks = verbose_ ? result_.Checks : result_.Checks.Where(check_ => check_.Status != "Passed");
        if (!checks.Any())
        {
            builder.AppendLine("- All checks passed.");
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

        return builder.ToString().TrimEnd();
    }
}

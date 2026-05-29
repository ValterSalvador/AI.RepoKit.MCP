using System.Globalization;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.Org;
using AiRepoKit.Cli.Models.SelfCheck;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.Org;

namespace AiRepoKit.Cli.Commands;

public sealed class OrgCommand
{
    private static readonly JsonSerializerOptions SelfCheckJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> EfficiencyIgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "artifacts",
        ".tmp",
        ".dotnet-home"
    };

    public CommandResult Execute(BootstrapOptions options_)
    {
        try
        {
            string subcommand = string.IsNullOrWhiteSpace(options_.OrgSubcommand) ? "scan" : options_.OrgSubcommand.ToLowerInvariant();
            return subcommand switch
            {
                "scan" => this.Scan(options_),
                "report" => this.Report(options_),
                "self-check" => this.SelfCheck(options_),
                "setup" => this.Setup(options_),
                "efficiency" => this.Efficiency(options_),
                _ => CommandResult.Failure("# Org" + Environment.NewLine + Environment.NewLine + $"Unknown org subcommand `{options_.OrgSubcommand}`.", 1)
            };
        }
        catch (Exception exception)
        {
            string message = ProcessRunner.Redact(exception.Message);
            return CommandResult.Failure(options_.AuditJson ? $$"""{"error":"{{JsonEscape(message)}}"}""" : "# Org" + Environment.NewLine + Environment.NewLine + "Failed: " + message, 1);
        }
    }

    private CommandResult Scan(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        progress.StartPhase("Scanning organization root");
        OrgScanReport scan = new OrgRepositoryScanner().Scan(GetRoot(options_), options_.MaxDepth);
        progress.CompletePhase("Organization scan completed");

        OrgReportWriter writer = new();
        if (options_.Apply)
        {
            progress.StartPhase("Writing org scan reports");
            writer.WriteScan(scan.Root, scan);
            progress.CompletePhase("Org scan reports written");
        }

        string format = options_.AuditJson ? "json" : options_.Format;
        string output = writer.FormatScan(scan, format);
        if (!string.IsNullOrWhiteSpace(options_.Output))
        {
            progress.StartPhase("Writing org scan output");
            writer.WriteOutput(options_.Output, output);
            progress.CompletePhase("Org scan output written");
        }

        return CommandResult.Ok(output);
    }

    private CommandResult Report(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        progress.StartPhase("Scanning repositories");
        OrgScanReport scan = new OrgRepositoryScanner().Scan(GetRoot(options_), options_.MaxDepth);
        OrgReport report = new(
            scan.Root,
            Now(),
            scan.Repositories,
            OrgScoringService.Summarize(scan.Repositories.Select(repo_ => repo_.Readiness)),
            OrgScoringService.Summarize(scan.Repositories.Select(repo_ => repo_.Compliance)),
            scan.Warnings);
        progress.CompletePhase("Org report calculated");

        OrgReportWriter writer = new();
        if (options_.Apply)
        {
            progress.StartPhase("Writing org report");
            writer.WriteReport(report.Root, report);
            progress.CompletePhase("Org report written");
        }

        string format = options_.AuditJson ? "json" : options_.Format;
        return CommandResult.Ok(writer.FormatReport(report, format));
    }

    private CommandResult SelfCheck(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        progress.StartPhase("Scanning repositories");
        OrgScanReport scan = new OrgRepositoryScanner().Scan(GetRoot(options_), options_.MaxDepth);
        progress.CompletePhase("Repositories scanned");

        List<OrgSelfCheckRepository> repositories = [];
        foreach (OrgRepositorySummary repo in scan.Repositories)
        {
            progress.StartPhase($"Self-check {repo.RepoName}");
            try
            {
                BootstrapOptions repoOptions = ForRepo(options_, repo.RepoRoot, "self-check", auditJson_: true, skipAudit_: true, skipBuildMcp_: true, skipCodeInventory_: true, skipBudget_: true, noProgress_: true);
                CommandResult result = new SelfCheckCommand().Execute(repoOptions);
                repositories.Add(BuildSelfCheckRepository(repo.RepoRoot, repo.RepoName, result));
                progress.CompletePhase($"Self-check completed for {repo.RepoName}");
            }
            catch (Exception exception)
            {
                string message = ProcessRunner.Redact(exception.Message);
                repositories.Add(new OrgSelfCheckRepository(
                    repo.RepoRoot,
                    repo.RepoName,
                    "failed",
                    1,
                    ["fatal"],
                    [TrimForSummary(message, 160)],
                    [new OrgSelfCheckCheck("fatal", "Failed", true, message, 1, null)]));
                progress.WarnPhase($"Self-check failed for {repo.RepoName}");
            }
        }

        int failed = repositories.Count(repo_ => repo_.Status.Equals("failed", StringComparison.OrdinalIgnoreCase));
        OrgSelfCheckReport report = new(
            scan.Root,
            Now(),
            repositories,
            repositories.Count(repo_ => repo_.Status.Equals("passed", StringComparison.OrdinalIgnoreCase)),
            repositories.Count(repo_ => repo_.Status.Equals("warning", StringComparison.OrdinalIgnoreCase)),
            failed);
        string format = options_.AuditJson ? "json" : options_.Format;
        string output = new OrgReportWriter().FormatSelfCheck(report, format);
        return new CommandResult(failed == 0, output, failed == 0 ? 0 : 2);
    }

    private CommandResult Setup(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        if (options_.Apply)
        {
            return CommandResult.Failure(options_.AuditJson ? """{"error":"org setup --apply is not implemented in v1.3.0"}""" : "# Org Setup" + Environment.NewLine + Environment.NewLine + "`org setup --apply` is not implemented in v1.3.0. This command is dry-run only.", 1);
        }

        progress.StartPhase("Scanning repositories");
        OrgScanReport scan = new OrgRepositoryScanner().Scan(GetRoot(options_), options_.MaxDepth);
        progress.CompletePhase("Repositories scanned");

        IReadOnlyList<OrgSetupPreviewRepository> repositories = scan.Repositories.Select(repo_ => new OrgSetupPreviewRepository(
            repo_.RepoRoot,
            repo_.RepoName,
            repo_.RecommendedProfile,
            repo_.Confidence,
            "dry-run",
            [$"airepo setup --repo \"{repo_.RepoRoot}\" --dry-run --profile {repo_.RecommendedProfile}"],
            PlannedSetupChanges(repo_),
            repo_.Warnings)).ToArray();

        OrgSetupPreviewReport report = new(scan.Root, Now(), repositories, scan.Warnings);
        string format = options_.AuditJson ? "json" : options_.Format;
        return CommandResult.Ok(new OrgReportWriter().FormatSetup(report, format));
    }

    private CommandResult Efficiency(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        progress.StartPhase("Scanning repositories");
        OrgScanReport scan = new OrgRepositoryScanner().Scan(GetRoot(options_), options_.MaxDepth);
        progress.CompletePhase("Repositories scanned");

        List<OrgEfficiencyRepository> repositories = [];
        foreach (OrgRepositorySummary repo in scan.Repositories)
        {
            progress.StartPhase($"Measuring efficiency for {repo.RepoName}");
            repositories.Add(MeasureEfficiency(repo));
            progress.CompletePhase($"Efficiency measured for {repo.RepoName}");
        }

        OrgEfficiencyReport report = new(scan.Root, Now(), repositories, scan.Warnings);
        OrgReportWriter writer = new();
        if (options_.Apply)
        {
            progress.StartPhase("Writing org efficiency reports");
            writer.WriteEfficiency(report.Root, report);
            progress.CompletePhase("Org efficiency reports written");
        }

        string format = options_.AuditJson ? "json" : options_.Format;
        return CommandResult.Ok(writer.FormatEfficiency(report, format));
    }

    private static OrgEfficiencyRepository MeasureEfficiency(OrgRepositorySummary repo_)
    {
        int filesAnalyzed = 0;
        int filesExcluded = 0;
        long rawBytes = 0;
        List<string> warnings = [];
        EfficiencyFileScan scan = EnumerateEfficiencyFiles(repo_.RepoRoot, warnings);
        filesExcluded = scan.FilesExcluded;
        foreach (string file in scan.Files)
        {
            filesAnalyzed++;
            rawBytes += new FileInfo(file).Length;
        }

        long generatedBytes = SumGeneratedBytes(Path.Combine(repo_.RepoRoot, ".ai", "generated", "context-packs"))
            + SumGeneratedBytes(Path.Combine(repo_.RepoRoot, ".ai", "generated", "graphs"))
            + SumFile(Path.Combine(repo_.RepoRoot, ".ai", "generated", "reports", "mcp-budget-report.json"));
        long rawTokens = EstimateTokens(rawBytes);
        long contextTokens = EstimateTokens(generatedBytes);
        double? reduction = rawTokens > 0 && contextTokens > 0 ? Math.Clamp(100.0 * (1.0 - contextTokens / (double)rawTokens), 0.0, 100.0) : null;
        List<string> opportunities = [];
        List<string> recommendations = [];
        if (!repo_.Footprint.HasContextPacks)
        {
            opportunities.Add("Context packs are missing.");
            recommendations.Add("Run `airepo context-pack --apply` in this repo when ready.");
        }

        if (!repo_.Footprint.HasGraphs)
        {
            opportunities.Add("Graph reports are missing.");
            recommendations.Add("Run `airepo graph --apply` in this repo when ready.");
        }

        if (generatedBytes == 0)
        {
            opportunities.Add("No generated context was found.");
        }

        return new OrgEfficiencyRepository(repo_.RepoRoot, repo_.RepoName, filesAnalyzed, filesExcluded, rawBytes, generatedBytes, rawTokens, contextTokens, reduction, repo_.Footprint.HasContextPacks, repo_.Footprint.HasGraphs, opportunities, recommendations, warnings);
    }

    private static EfficiencyFileScan EnumerateEfficiencyFiles(string repoRoot_, List<string> warnings_)
    {
        Stack<string> pending = new();
        List<string> filesFound = [];
        int filesExcluded = 0;
        pending.Push(repoRoot_);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception)
            {
                warnings_.Add($"{current}: {ProcessRunner.Redact(exception.Message)}");
                continue;
            }

            foreach (string file in files)
            {
                string extension = Path.GetExtension(file);
                if (extension is ".cs" or ".csproj" or ".sln" or ".slnx" or ".props" or ".targets" or ".json" or ".toml" or ".md" or ".ps1" or ".yml" or ".yaml")
                {
                    filesFound.Add(file);
                }
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception)
            {
                warnings_.Add($"{current}: {ProcessRunner.Redact(exception.Message)}");
                continue;
            }

            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory);
                string normalized = directory.Replace('\\', '/');
                if (EfficiencyIgnoredDirectories.Contains(name) || normalized.Contains("/.ai/generated/", StringComparison.OrdinalIgnoreCase))
                {
                    filesExcluded++;
                    continue;
                }

                pending.Push(directory);
            }
        }

        return new EfficiencyFileScan(filesFound, filesExcluded);
    }

    private static long SumGeneratedBytes(string directory_)
    {
        if (!Directory.Exists(directory_))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory_, "*.json", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(directory_, "*.md", SearchOption.TopDirectoryOnly))
            .Sum(path_ => new FileInfo(path_).Length);
    }

    private static long SumFile(string path_) => File.Exists(path_) ? new FileInfo(path_).Length : 0;

    private static long EstimateTokens(long bytes_) => (long)Math.Ceiling(bytes_ / 4.0);

    private static IReadOnlyList<string> PlannedSetupChanges(OrgRepositorySummary repo_)
    {
        List<string> changes = [];
        if (!repo_.Footprint.HasAiDirectory)
        {
            changes.Add("create .ai guidance and generated report folders");
        }

        if (!repo_.Footprint.HasMcpConfig)
        {
            changes.Add("preview MCP config for Codex/VS Code/Visual Studio");
        }

        if (!repo_.Footprint.HasAgentsMd || !repo_.Footprint.HasCopilotInstructions)
        {
            changes.Add("preview agent and instruction files");
        }

        return changes.Count == 0 ? ["no immediate managed-file gaps detected"] : changes;
    }

    private static BootstrapOptions ForRepo(
        BootstrapOptions options_,
        string repoPath_,
        string command_,
        bool auditJson_,
        bool skipAudit_,
        bool skipBuildMcp_,
        bool skipCodeInventory_,
        bool skipBudget_,
        bool noProgress_)
    {
        return new BootstrapOptions(command_, repoPath_, options_.Clients, options_.IncludeMcp, false, true, false, options_.Force, options_.ForceManaged, options_.Profile, options_.TargetFramework, options_.McpServerName, options_.ToolCommandName, options_.McpProjectName, options_.McpNamespace, options_.McpAssemblyName, options_.McpProjectRelativePath, skipBuildMcp_, options_.SkipAiContext, skipCodeInventory_, options_.SkipSecurityScan, skipBudget_, true, options_.SkipScripts, options_.MaxFiles, options_.MaxItems, options_.IncludePrivateMembers, options_.NoCache, options_.RebuildCache, options_.Output, options_.Format, options_.Verbose, options_.Summary, auditJson_, options_.Timings, options_.IncludeSource, options_.CreateAuditBaseline, options_.UpdateAuditBaseline, options_.ShowAuditBaseline, options_.FailOnAccepted, skipAudit_, options_.IncludeAgents, options_.Task, options_.Target, options_.Limit, options_.RequireContextPacks, [], noProgress_, options_.Refresh, options_.NoRefresh, options_.SampleQuery, options_.ProfileExplicit, options_.ForbiddenTerms, options_.SanitizeTerm, options_.SanitizeReplacement, options_.Strict, options_.Quick, options_.Full, options_.DefaultsSummary, options_.Budget, options_.Kind, options_.Since, options_.ChangedFiles, options_.RootPath, options_.OrgSubcommand, options_.MaxDepth);
    }

    private static string GetRoot(BootstrapOptions options_) => string.IsNullOrWhiteSpace(options_.RootPath) ? Path.GetFullPath(options_.RepoPath) : Path.GetFullPath(options_.RootPath);

    private static OrgSelfCheckRepository BuildSelfCheckRepository(string repoRoot_, string repoName_, CommandResult result_)
    {
        SelfCheckResult? selfCheck = ParseSelfCheckResult(result_.Markdown);
        if (selfCheck is null)
        {
            string status = result_.ExitCode == 0 ? "passed" : "failed";
            IReadOnlyList<string> fallbackFailedChecks = result_.ExitCode == 0 ? [] : ["self-check"];
            IReadOnlyList<string> fallbackWarnings = result_.ExitCode == 0 ? [] : [TrimForSummary(result_.Markdown, 160)];
            return new OrgSelfCheckRepository(repoRoot_, repoName_, status, result_.ExitCode, fallbackFailedChecks, fallbackWarnings, []);
        }

        OrgSelfCheckCheck[] checks = selfCheck.Checks
            .Select(check_ => new OrgSelfCheckCheck(check_.Name, check_.Status, check_.Required, check_.Message, check_.ExitCode, check_.Hint))
            .ToArray();
        string[] failedChecks = selfCheck.Checks
            .Where(IsFailedCheck)
            .Select(check_ => check_.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] warnings = selfCheck.Checks
            .Where(ShouldIncludeSummaryMessage)
            .Select(FormatSummaryMessage)
            .Where(message_ => !string.IsNullOrWhiteSpace(message_))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OrgSelfCheckRepository(
            repoRoot_,
            repoName_,
            NormalizeSelfCheckStatus(selfCheck.Status, selfCheck.ExitCode),
            selfCheck.ExitCode,
            failedChecks,
            warnings,
            checks);
    }

    private static SelfCheckResult? ParseSelfCheckResult(string value_)
    {
        try
        {
            return JsonSerializer.Deserialize<SelfCheckResult>(value_, SelfCheckJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsFailedCheck(SelfCheckItem check_) =>
        string.Equals(check_.Status, "Failed", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIncludeSummaryMessage(SelfCheckItem check_) =>
        (string.Equals(check_.Status, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(check_.Status, "Warning", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(check_.Message);

    private static string FormatSummaryMessage(SelfCheckItem check_)
    {
        string message = TrimForSummary(check_.Message, 120);
        return string.IsNullOrWhiteSpace(message) ? check_.Name : $"{check_.Name}: {message}";
    }

    private static string NormalizeSelfCheckStatus(string status_, int exitCode_) =>
        string.Equals(status_, "Warning", StringComparison.OrdinalIgnoreCase)
            ? "warning"
            : exitCode_ == 0 ? "passed" : "failed";

    private static string TrimForSummary(string value_, int maxLength_ = 240)
    {
        string text = value_.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        return text.Length <= maxLength_ ? text : text[..maxLength_];
    }

    private static string JsonEscape(string value_) => value_.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private sealed record EfficiencyFileScan(IReadOnlyList<string> Files, int FilesExcluded);
}

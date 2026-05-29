using System.Globalization;
using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models.Org;

namespace AiRepoKit.Cli.Services.Org;

public sealed class OrgReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string FormatScan(OrgScanReport report_, string format_) => NormalizeFormat(format_) switch
    {
        "json" => JsonSerializer.Serialize(report_, JsonOptions),
        "csv" => ToCsv(report_.Repositories),
        _ => ScanMarkdown(report_)
    };

    public string FormatReport(OrgReport report_, string format_) => NormalizeFormat(format_) switch
    {
        "json" => JsonSerializer.Serialize(report_, JsonOptions),
        "csv" => ToCsv(report_.Repositories),
        _ => ReportMarkdown(report_)
    };

    public string FormatSelfCheck(OrgSelfCheckReport report_, string format_) => NormalizeFormat(format_) switch
    {
        "json" => JsonSerializer.Serialize(report_, JsonOptions),
        "csv" => ToCsv(report_.Repositories),
        _ => SelfCheckMarkdown(report_)
    };

    public string FormatSetup(OrgSetupPreviewReport report_, string format_) => NormalizeFormat(format_) switch
    {
        "json" => JsonSerializer.Serialize(report_, JsonOptions),
        "csv" => ToCsv(report_.Repositories),
        _ => SetupMarkdown(report_)
    };

    public string FormatEfficiency(OrgEfficiencyReport report_, string format_) => NormalizeFormat(format_) switch
    {
        "json" => JsonSerializer.Serialize(report_, JsonOptions),
        "csv" => ToCsv(report_.Repositories),
        _ => EfficiencyMarkdown(report_)
    };

    public IReadOnlyList<string> WriteScan(string root_, OrgScanReport report_, string prefix_ = "org-scan")
    {
        return this.WriteAll(root_, prefix_, FormatScan(report_, "json"), FormatScan(report_, "markdown"), FormatScan(report_, "csv"));
    }

    public IReadOnlyList<string> WriteReport(string root_, OrgReport report_, string prefix_ = "org-report")
    {
        return this.WriteAll(root_, prefix_, FormatReport(report_, "json"), FormatReport(report_, "markdown"), FormatReport(report_, "csv"));
    }

    public IReadOnlyList<string> WriteEfficiency(string root_, OrgEfficiencyReport report_, string prefix_ = "org-efficiency")
    {
        return this.WriteAll(root_, prefix_, FormatEfficiency(report_, "json"), FormatEfficiency(report_, "markdown"), FormatEfficiency(report_, "csv"));
    }

    public void WriteOutput(string outputPath_, string content_)
    {
        string fullPath = Path.GetFullPath(outputPath_);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(fullPath, content_);
    }

    private IReadOnlyList<string> WriteAll(string root_, string prefix_, string json_, string markdown_, string csv_)
    {
        string directory = Path.Combine(Path.GetFullPath(root_), ".ai", "generated", "reports");
        Directory.CreateDirectory(directory);
        string jsonPath = Path.Combine(directory, prefix_ + ".json");
        string mdPath = Path.Combine(directory, prefix_ + ".md");
        string csvPath = Path.Combine(directory, prefix_ + ".csv");
        File.WriteAllText(jsonPath, json_);
        File.WriteAllText(mdPath, markdown_);
        File.WriteAllText(csvPath, csv_);
        return [jsonPath, mdPath, csvPath];
    }

    private static string ScanMarkdown(OrgScanReport report_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Org Scan");
        builder.AppendLine();
        builder.AppendLine($"- Root: `{report_.Root}`");
        builder.AppendLine($"- GeneratedAtLocal: `{report_.GeneratedAtLocal}`");
        builder.AppendLine($"- MaxDepth: `{report_.MaxDepth}`");
        builder.AppendLine($"- Repositories: `{report_.Repositories.Count}`");
        AppendRepoTable(builder, report_.Repositories);
        AppendMessages(builder, "Warnings", report_.Warnings);
        return builder.ToString().TrimEnd();
    }

    private static string ReportMarkdown(OrgReport report_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Org Report");
        builder.AppendLine();
        builder.AppendLine($"- Root: `{report_.Root}`");
        builder.AppendLine($"- GeneratedAtLocal: `{report_.GeneratedAtLocal}`");
        builder.AppendLine($"- Repositories: `{report_.Repositories.Count}`");
        builder.AppendLine($"- Readiness average: `{report_.Readiness.Average.ToString("0.0", CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Compliance average: `{report_.Compliance.Average.ToString("0.0", CultureInfo.InvariantCulture)}`");
        AppendRepoTable(builder, report_.Repositories);
        builder.AppendLine();
        builder.AppendLine("## Recommendations");
        foreach (OrgRepositorySummary repo in report_.Repositories)
        {
            builder.AppendLine($"- `{repo.RepoName}`: {FirstOrNone(repo.Recommendations)}");
        }

        AppendMessages(builder, "Warnings", report_.Warnings);
        return builder.ToString().TrimEnd();
    }

    private static string SelfCheckMarkdown(OrgSelfCheckReport report_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Org Self-Check");
        builder.AppendLine();
        builder.AppendLine($"- Root: `{report_.Root}`");
        builder.AppendLine($"- GeneratedAtLocal: `{report_.GeneratedAtLocal}`");
        builder.AppendLine($"- Passed: `{report_.Passed}`");
        builder.AppendLine($"- Warnings: `{report_.Warnings}`");
        builder.AppendLine($"- Failed: `{report_.Failed}`");
        builder.AppendLine();
        builder.AppendLine("| Repo | Status | ExitCode | FailedChecks | Warnings |");
        builder.AppendLine("| --- | --- | ---: | --- | --- |");
        foreach (OrgSelfCheckRepository repo in report_.Repositories)
        {
            builder.AppendLine($"| {Cell(repo.RepoName)} | {repo.Status} | {repo.ExitCode} | {Cell(JoinOrNone(repo.FailedChecks))} | {Cell(JoinOrNone(repo.Warnings))} |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string SetupMarkdown(OrgSetupPreviewReport report_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Org Setup Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Root: `{report_.Root}`");
        builder.AppendLine($"- GeneratedAtLocal: `{report_.GeneratedAtLocal}`");
        builder.AppendLine("- Mode: `dry-run-only`");
        builder.AppendLine();
        builder.AppendLine("| Repo | Profile | Confidence | Planned command | Warnings |");
        builder.AppendLine("| --- | --- | ---: | --- | --- |");
        foreach (OrgSetupPreviewRepository repo in report_.Repositories)
        {
            builder.AppendLine($"| {Cell(repo.RepoName)} | {repo.RecommendedProfile} | {repo.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | `{FirstOrNone(repo.PlannedCommands)}` | {Cell(FirstOrNone(repo.Warnings))} |");
        }

        AppendMessages(builder, "Warnings", report_.Warnings);
        return builder.ToString().TrimEnd();
    }

    private static string EfficiencyMarkdown(OrgEfficiencyReport report_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Org Efficiency");
        builder.AppendLine();
        builder.AppendLine($"- Root: `{report_.Root}`");
        builder.AppendLine($"- GeneratedAtLocal: `{report_.GeneratedAtLocal}`");
        builder.AppendLine();
        builder.AppendLine("| Repo | Files | Raw Tokens | Context Tokens | Reduction | Context Packs | Graphs |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | --- | --- |");
        foreach (OrgEfficiencyRepository repo in report_.Repositories)
        {
            string reduction = repo.EstimatedReductionPercent.HasValue ? repo.EstimatedReductionPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "N/A";
            builder.AppendLine($"| {Cell(repo.RepoName)} | {repo.FilesAnalyzed} | {repo.EstimatedRawTokens} | {repo.EstimatedContextTokens} | {reduction} | {repo.HasContextPacks} | {repo.HasGraphs} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Opportunities");
        foreach (OrgEfficiencyRepository repo in report_.Repositories)
        {
            builder.AppendLine($"- `{repo.RepoName}`: {FirstOrNone(repo.Opportunities)}");
        }

        AppendMessages(builder, "Warnings", report_.Warnings);
        return builder.ToString().TrimEnd();
    }

    private static void AppendRepoTable(StringBuilder builder_, IReadOnlyList<OrgRepositorySummary> repositories_)
    {
        builder_.AppendLine();
        builder_.AppendLine("| Repo | Profile | Confidence | Health | Readiness | Compliance | MCP | Agents | Context | Graph |");
        builder_.AppendLine("| --- | --- | ---: | --- | ---: | ---: | --- | --- | --- | --- |");
        foreach (OrgRepositorySummary repo in repositories_)
        {
            bool agents = repo.Footprint.HasAgentsMd || repo.Footprint.HasGithubAgents || repo.Footprint.HasGithubInstructions || repo.Footprint.HasGithubPrompts;
            builder_.AppendLine($"| {Cell(repo.RepoName)} | {repo.RecommendedProfile} | {repo.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {repo.Health.Status} | {repo.Readiness.Value} | {repo.Compliance.Value} | {repo.Footprint.HasMcpConfig} | {agents} | {repo.Footprint.HasContextPacks} | {repo.Footprint.HasGraphs} |");
        }
    }

    private static string ToCsv(IReadOnlyList<OrgRepositorySummary> repositories_)
    {
        StringBuilder builder = new();
        builder.AppendLine("repoRoot,repoName,recommendedProfile,confidence,healthStatus,readinessScore,readinessStatus,complianceScore,complianceStatus,hasGit,hasAi,hasAgentsMd,hasCopilotInstructions,hasMcpConfig,hasContextPacks,hasGraphs,warnings,recommendations");
        foreach (OrgRepositorySummary repo in repositories_)
        {
            AppendCsv(builder, repo.RepoRoot, repo.RepoName, repo.RecommendedProfile, repo.Confidence.ToString("0.00", CultureInfo.InvariantCulture), repo.Health.Status, repo.Readiness.Value.ToString(CultureInfo.InvariantCulture), repo.Readiness.Status, repo.Compliance.Value.ToString(CultureInfo.InvariantCulture), repo.Compliance.Status, repo.Health.HasGit.ToString(), repo.Footprint.HasAiDirectory.ToString(), repo.Footprint.HasAgentsMd.ToString(), repo.Footprint.HasCopilotInstructions.ToString(), repo.Footprint.HasMcpConfig.ToString(), repo.Footprint.HasContextPacks.ToString(), repo.Footprint.HasGraphs.ToString(), string.Join("; ", repo.Warnings), string.Join("; ", repo.Recommendations));
        }

        return builder.ToString().TrimEnd();
    }

    private static string ToCsv(IReadOnlyList<OrgSelfCheckRepository> repositories_)
    {
        StringBuilder builder = new();
        builder.AppendLine("repoRoot,repoName,status,exitCode,failedChecks,warnings");
        foreach (OrgSelfCheckRepository repo in repositories_)
        {
            AppendCsv(builder, repo.RepoRoot, repo.RepoName, repo.Status, repo.ExitCode.ToString(CultureInfo.InvariantCulture), string.Join("; ", repo.FailedChecks), string.Join("; ", repo.Warnings));
        }

        return builder.ToString().TrimEnd();
    }

    private static string ToCsv(IReadOnlyList<OrgSetupPreviewRepository> repositories_)
    {
        StringBuilder builder = new();
        builder.AppendLine("repoRoot,repoName,recommendedProfile,confidence,mode,plannedCommands,plannedChanges,warnings");
        foreach (OrgSetupPreviewRepository repo in repositories_)
        {
            AppendCsv(builder, repo.RepoRoot, repo.RepoName, repo.RecommendedProfile, repo.Confidence.ToString("0.00", CultureInfo.InvariantCulture), repo.Mode, string.Join("; ", repo.PlannedCommands), string.Join("; ", repo.PlannedChanges), string.Join("; ", repo.Warnings));
        }

        return builder.ToString().TrimEnd();
    }

    private static string ToCsv(IReadOnlyList<OrgEfficiencyRepository> repositories_)
    {
        StringBuilder builder = new();
        builder.AppendLine("repoRoot,repoName,filesAnalyzed,filesExcluded,rawBytes,generatedContextBytes,estimatedRawTokens,estimatedContextTokens,estimatedReductionPercent,hasContextPacks,hasGraphs,opportunities,recommendations,warnings");
        foreach (OrgEfficiencyRepository repo in repositories_)
        {
            string reduction = repo.EstimatedReductionPercent.HasValue ? repo.EstimatedReductionPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;
            AppendCsv(builder, repo.RepoRoot, repo.RepoName, repo.FilesAnalyzed.ToString(CultureInfo.InvariantCulture), repo.FilesExcluded.ToString(CultureInfo.InvariantCulture), repo.RawBytes.ToString(CultureInfo.InvariantCulture), repo.GeneratedContextBytes.ToString(CultureInfo.InvariantCulture), repo.EstimatedRawTokens.ToString(CultureInfo.InvariantCulture), repo.EstimatedContextTokens.ToString(CultureInfo.InvariantCulture), reduction, repo.HasContextPacks.ToString(), repo.HasGraphs.ToString(), string.Join("; ", repo.Opportunities), string.Join("; ", repo.Recommendations), string.Join("; ", repo.Warnings));
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendCsv(StringBuilder builder_, params string[] values_)
    {
        builder_.AppendLine(string.Join(",", values_.Select(EscapeCsv)));
    }

    private static string EscapeCsv(string value_)
    {
        string value = value_ ?? string.Empty;
        return value.Contains('"', StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static void AppendMessages(StringBuilder builder_, string title_, IReadOnlyList<string> messages_)
    {
        builder_.AppendLine();
        builder_.AppendLine("## " + title_);
        if (messages_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string message in messages_)
        {
            builder_.AppendLine("- " + message);
        }
    }

    private static string NormalizeFormat(string value_)
    {
        string value = string.IsNullOrWhiteSpace(value_) || value_.Equals("all", StringComparison.OrdinalIgnoreCase) ? "markdown" : value_.ToLowerInvariant();
        return value is "markdown" or "json" or "csv" ? value : throw new InvalidOperationException("Format must be markdown, json, or csv.");
    }

    private static string FirstOrNone(IReadOnlyList<string> values_) => values_.Count == 0 ? "None" : values_[0];

    private static string JoinOrNone(IReadOnlyList<string> values_) => values_.Count == 0 ? "None" : string.Join("; ", values_);

    private static string Cell(string value_) => (value_ ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal);
}

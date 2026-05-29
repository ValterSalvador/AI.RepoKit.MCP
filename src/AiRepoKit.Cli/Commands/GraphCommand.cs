using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.Graphs;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.Graphs;

namespace AiRepoKit.Cli.Commands;

public sealed class GraphCommand
{
    private static readonly string[] AllKinds = ["project", "symbol", "risk"];

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        List<string> errors = [];
        List<string> warnings = [];
        List<GraphReport> reports = [];
        try
        {
            string format = NormalizeFormat(options_.Format);
            bool apply = options_.Apply && !options_.DryRun;
            int limit = Math.Clamp(options_.Limit, 1, 100);
            int? budget = options_.Budget > 0 ? options_.Budget : null;
            string[] kinds = string.IsNullOrWhiteSpace(options_.Kind) ? AllKinds : [NormalizeKind(options_.Kind)];
            if (apply && new GitIgnoreService().EnsureLocalGeneratedArtifactRules(options_.RepoPath, false))
            {
                warnings.Add("Updated .gitignore with AiRepoKit local/generated artifact rules.");
            }

            GraphService service = new();
            foreach (string kind in kinds)
            {
                progress.StartPhase($"Building {kind} graph");
                GraphReport report = service.Build(options_.RepoPath, kind, limit, budget);
                reports.Add(report);
                warnings.AddRange(report.Warnings);
                progress.CompletePhase($"{kind} graph built");
                if (apply)
                {
                    progress.StartPhase($"Writing {kind} graph");
                    service.Write(options_.RepoPath, report, format);
                    progress.CompletePhase($"{kind} graph written");
                }
            }

            progress.CompletePhase("Graph completed");
        }
        catch (Exception exception)
        {
            errors.Add(ProcessRunner.Redact(exception.Message));
            progress.FailPhase("Graph failed");
        }

        string markdown = options_.AuditJson
            ? JsonSerializer.Serialize(new { reports, warnings, errors }, new JsonSerializerOptions { WriteIndented = true })
            : WriteReport(options_, reports, warnings, errors);
        return errors.Count == 0 ? CommandResult.Ok(markdown) : CommandResult.Failure(markdown, 1);
    }

    private static string WriteReport(BootstrapOptions options_, IReadOnlyList<GraphReport> reports_, IReadOnlyList<string> warnings_, IReadOnlyList<string> errors_)
    {
        bool apply = options_.Apply && !options_.DryRun;
        StringBuilder builder = new();
        builder.AppendLine(apply ? "# Graph Apply" : "# Graph Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Mode: `{(apply ? "apply" : "dry-run")}`");
        builder.AppendLine($"- Kind: `{(string.IsNullOrWhiteSpace(options_.Kind) ? "project,symbol,risk" : options_.Kind)}`");
        builder.AppendLine($"- Format: `{options_.Format}`");
        builder.AppendLine($"- Limit: `{options_.Limit}`");
        builder.AppendLine($"- Budget: `{(options_.Budget > 0 ? options_.Budget.ToString() : "none")}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        foreach (GraphReport report in reports_)
        {
            builder.AppendLine($"- {report.Summary} estimatedTokens=`{report.EstimatedTokens}` truncated=`{report.Truncated}`");
        }

        builder.AppendLine();
        builder.AppendLine(apply ? "## Files Written" : "## Files Planned");
        builder.AppendLine();
        foreach (GraphReport report in reports_)
        {
            if (options_.Format is "json" or "all")
            {
                builder.AppendLine($"- `.ai/generated/graphs/{report.Kind}-graph.json`");
            }

            if (options_.Format is "markdown" or "all")
            {
                builder.AppendLine($"- `.ai/generated/graphs/{report.Kind}-graph.md`");
            }
        }

        AppendMessages(builder, "Warnings", warnings_);
        AppendMessages(builder, "Errors", errors_);
        return builder.ToString().TrimEnd();
    }

    private static void AppendMessages(StringBuilder builder_, string title_, IReadOnlyList<string> messages_)
    {
        builder_.AppendLine();
        builder_.AppendLine("## " + title_);
        builder_.AppendLine();
        if (messages_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string message in messages_.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder_.AppendLine($"- {ProcessRunner.Redact(message)}");
        }
    }

    private static string NormalizeKind(string value_)
    {
        string value = value_.ToLowerInvariant();
        return value is "project" or "symbol" or "risk" ? value : throw new InvalidOperationException("Graph kind must be project, symbol, or risk.");
    }

    private static string NormalizeFormat(string value_)
    {
        string value = string.IsNullOrWhiteSpace(value_) ? "all" : value_.ToLowerInvariant();
        return value is "json" or "markdown" or "all" ? value : throw new InvalidOperationException("Format must be json, markdown, or all.");
    }
}

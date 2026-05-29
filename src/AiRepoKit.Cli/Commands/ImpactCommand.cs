using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.Impact;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.Impact;

namespace AiRepoKit.Cli.Commands;

public sealed class ImpactCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        List<string> errors = [];
        ImpactReport? report = null;
        try
        {
            string format = NormalizeFormat(options_.Format);
            bool apply = options_.Apply && !options_.DryRun;
            int limit = Math.Clamp(options_.Limit, 1, 100);
            int? budget = options_.Budget > 0 ? options_.Budget : null;
            ImpactService service = new();
            progress.StartPhase("Analyzing impact");
            report = service.Build(options_.RepoPath, options_.Target, options_.Since, limit, budget);
            progress.CompletePhase("Impact analysis completed");
            if (apply)
            {
                progress.StartPhase("Writing impact report");
                service.Write(options_.RepoPath, report, format);
                progress.CompletePhase("Impact report written");
            }

            progress.CompletePhase("Impact completed");
        }
        catch (Exception exception)
        {
            errors.Add(ProcessRunner.Redact(exception.Message));
            progress.FailPhase("Impact failed");
        }

        string markdown = options_.AuditJson
            ? JsonSerializer.Serialize(new { report, errors }, new JsonSerializerOptions { WriteIndented = true })
            : WriteReport(options_, report, errors);
        return errors.Count == 0 ? CommandResult.Ok(markdown) : CommandResult.Failure(markdown, 1);
    }

    private static string WriteReport(BootstrapOptions options_, ImpactReport? report_, IReadOnlyList<string> errors_)
    {
        bool apply = options_.Apply && !options_.DryRun;
        StringBuilder builder = new();
        builder.AppendLine(apply ? "# Impact Apply" : "# Impact Preview");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Mode: `{(apply ? "apply" : "preview")}`");
        builder.AppendLine($"- Target: `{(string.IsNullOrWhiteSpace(options_.Target) ? "none" : options_.Target)}`");
        builder.AppendLine($"- Since: `{(string.IsNullOrWhiteSpace(options_.Since) ? "none" : options_.Since)}`");
        builder.AppendLine($"- Budget: `{(options_.Budget > 0 ? options_.Budget.ToString() : "none")}`");
        if (report_ is not null)
        {
            builder.AppendLine($"- Summary: {report_.Summary}");
            builder.AppendLine($"- EstimatedTokens: `{report_.EstimatedTokens}`");
            builder.AppendLine($"- Truncated: `{report_.Truncated}`");
        }

        if (report_ is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Changed Files");
            builder.AppendLine();
            if (report_.ChangedFiles.Count == 0)
            {
                builder.AppendLine("- None");
            }
            else
            {
                foreach (var file in report_.ChangedFiles)
                {
                    builder.AppendLine($"- `{file.Path}` [{file.Status}]");
                }
            }

            Append(builder, "Affected Projects", report_.AffectedProjects);
            Append(builder, "Affected Symbols", report_.AffectedSymbols);
            Append(builder, "Risks", report_.Risks);
            Append(builder, "Validation Commands", report_.ValidationCommands);
            if (!string.IsNullOrWhiteSpace(report_.CommitMessageSuggestion))
            {
                builder.AppendLine();
                builder.AppendLine("## Commit Message Suggestion");
                builder.AppendLine();
                builder.AppendLine(report_.CommitMessageSuggestion);
            }

            Append(builder, "Warnings", report_.Warnings);
        }

        if (apply)
        {
            builder.AppendLine();
            builder.AppendLine("## Files Written");
            builder.AppendLine();
            if (options_.Format is "json" or "all")
            {
                builder.AppendLine("- `.ai/generated/reports/impact-report.json`");
            }

            if (options_.Format is "markdown" or "all")
            {
                builder.AppendLine("- `.ai/generated/reports/impact-report.md`");
            }
        }

        Append(builder, "Errors", errors_);
        return builder.ToString().TrimEnd();
    }

    private static void Append(StringBuilder builder_, string title_, IReadOnlyList<string> values_)
    {
        builder_.AppendLine();
        builder_.AppendLine("## " + title_);
        builder_.AppendLine();
        if (values_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string value in values_)
        {
            builder_.AppendLine("- " + ProcessRunner.Redact(value));
        }
    }

    private static string NormalizeFormat(string value_)
    {
        string value = string.IsNullOrWhiteSpace(value_) ? "all" : value_.ToLowerInvariant();
        return value is "json" or "markdown" or "all" ? value : throw new InvalidOperationException("Format must be json, markdown, or all.");
    }
}

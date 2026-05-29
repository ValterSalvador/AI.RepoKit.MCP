using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.CodeIndex;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.CodeIndex;

namespace AiRepoKit.Cli.Commands;

public sealed class CodeIndexCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        List<string> errors = [];
        List<string> warnings = [];
        IReadOnlyList<string> plannedFiles = [];
        CodeIndexResult? result = null;

        try
        {
            string format = NormalizeFormat(options_.Format);
            bool dryRun = !options_.Apply || options_.DryRun;
            if (!dryRun && new GitIgnoreService().EnsureLocalGeneratedArtifactRules(options_.RepoPath, false))
            {
                warnings.Add("Updated .gitignore with AiRepoKit local/generated artifact rules.");
            }

            result = new RoslynCodeIndexService().Index(options_.RepoPath, options_.MaxFiles, options_.MaxItems, options_.IncludePrivateMembers, !options_.NoCache, options_.RebuildCache, !dryRun, progress);
            warnings.AddRange(result.CacheWarnings);
            progress.StartPhase("Writing inventories");
            plannedFiles = new CodeInventoryWriter().Write(options_.RepoPath, options_.Output, format, result, dryRun);
            progress.CompletePhase("Inventory writing completed");
            progress.CompletePhase("Code index completed");
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
            progress.FailPhase("Code index failed");
        }

        string markdown = this.WriteReport(options_, result, plannedFiles, warnings, errors);
        return errors.Count == 0 ? CommandResult.Ok(markdown) : CommandResult.Failure(markdown, 1);
    }

    private static string NormalizeFormat(string value_)
    {
        string value = string.IsNullOrWhiteSpace(value_) ? "all" : value_.ToLowerInvariant();
        if (value is "json" or "markdown" or "all")
        {
            return value;
        }

        throw new InvalidOperationException("Format must be json, markdown, or all.");
    }

    private string WriteReport(
        BootstrapOptions options_,
        CodeIndexResult? result_,
        IReadOnlyList<string> plannedFiles_,
        IReadOnlyList<string> warnings_,
        IReadOnlyList<string> errors_)
    {
        bool apply = options_.Apply && !options_.DryRun;
        StringBuilder builder = new();
        builder.AppendLine(apply ? "# Code Index Apply" : "# Code Index Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Mode: `{(apply ? "apply" : "dry-run")}`");
        builder.AppendLine($"- Indexer: `RoslynLite`");
        builder.AppendLine($"- MaxFiles: `{options_.MaxFiles}`");
        builder.AppendLine($"- MaxItems: `{options_.MaxItems}`");
        builder.AppendLine($"- Cache: `{(!options_.NoCache)}`");
        builder.AppendLine($"- RebuildCache: `{options_.RebuildCache}`");
        builder.AppendLine($"- Output: `{options_.Output}`");
        builder.AppendLine($"- Format: `{options_.Format}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        if (result_ is null)
        {
            builder.AppendLine("- Files scanned: `0`");
            builder.AppendLine("- Symbols: `0`");
            builder.AppendLine("- Endpoints: `0`");
        }
        else
        {
            builder.AppendLine($"- Files discovered: `{result_.FilesDiscovered}`");
            builder.AppendLine($"- Files scanned: `{result_.SymbolInventory.TotalFilesScanned}`");
            builder.AppendLine($"- Files indexed: `{result_.FilesIndexed}`");
            builder.AppendLine($"- Files reused: `{result_.FilesReused}`");
            builder.AppendLine($"- Files removed from cache: `{result_.FilesRemovedFromCache}`");
            builder.AppendLine($"- Cache used: `{result_.CacheUsed}`");
            builder.AppendLine($"- Cache path: `{result_.CachePath}`");
            builder.AppendLine($"- Symbols: `{result_.SymbolInventory.TotalSymbols}`");
            builder.AppendLine($"- Endpoints: `{result_.EndpointInventory.TotalEndpoints}`");
            builder.AppendLine($"- Truncated: `{result_.SymbolInventory.Truncated}`");
        }

        builder.AppendLine();
        builder.AppendLine(apply ? "## Files Written" : "## Files Planned");
        builder.AppendLine();
        if (plannedFiles_.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (string path in plannedFiles_)
            {
                builder.AppendLine($"- `{path}`");
            }
        }

        if (result_ is not null && options_.Verbose)
        {
            builder.AppendLine();
            builder.AppendLine("## Classification Counts");
            builder.AppendLine();
            foreach (KeyValuePair<string, int> count in result_.SymbolInventory.ClassificationCounts)
            {
                builder.AppendLine($"- {count.Key}: `{count.Value}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Warnings");
        builder.AppendLine();
        this.AppendMessages(builder, warnings_);
        builder.AppendLine();
        builder.AppendLine("## Errors");
        builder.AppendLine();
        this.AppendMessages(builder, errors_);
        return builder.ToString().TrimEnd();
    }

    private void AppendMessages(StringBuilder builder_, IReadOnlyList<string> messages_)
    {
        if (messages_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string message in messages_)
        {
            builder_.AppendLine($"- {ProcessRunner.Redact(message)}");
        }
    }
}

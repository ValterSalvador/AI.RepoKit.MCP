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
        CommandTimingReport? timingReport = null;

        try
        {
            string format = NormalizeFormat(options_.Format);
            bool dryRun = !options_.Apply || options_.DryRun || options_.ValidationOnly;
            if (!dryRun && new GitIgnoreService().EnsureLocalGeneratedArtifactRules(options_.RepoPath, false))
            {
                warnings.Add("Updated .gitignore with AiRepoKit local/generated artifact rules.");
            }

            result = new RoslynCodeIndexService().Index(options_.RepoPath, options_.MaxFiles, options_.MaxItems, options_.IncludePrivateMembers, !options_.NoCache, options_.RebuildCache, !dryRun, progress);
            if (result.FilesIndexed == 0 && result.FilesReused > 0)
            {
                warnings.Add("Code index fully reused the existing cache; no source files required reindexing.");
            }
            else if (result.FilesReused > 0)
            {
                warnings.Add($"Code index reused cached entries for {result.FilesReused} file(s).");
            }

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

        if (options_.Timings)
        {
            timingReport = progress.GetTimingReport();
        }

        string markdown = this.WriteReport(options_, result, plannedFiles, warnings, errors, timingReport);
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
        IReadOnlyList<string> errors_,
        CommandTimingReport? timings_)
    {
        bool apply = options_.Apply && !options_.DryRun && !options_.ValidationOnly;
        StringBuilder builder = new();
        builder.AppendLine(apply ? "# Code Index Apply" : options_.ValidationOnly ? "# Code Index Validation" : "# Code Index Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Mode: `{(apply ? "apply" : options_.ValidationOnly ? "validation" : "dry-run")}`");
        builder.AppendLine($"- Indexer: `RoslynLite`");
        builder.AppendLine($"- MaxFiles: `{options_.MaxFiles}`");
        builder.AppendLine($"- MaxItems: `{options_.MaxItems}`");
        builder.AppendLine($"- Cache: `{(!options_.NoCache)}`");
        builder.AppendLine($"- RebuildCache: `{options_.RebuildCache}`");
        builder.AppendLine($"- ValidationOnly: `{options_.ValidationOnly}`");
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
            builder.AppendLine($"- Index reuse: `{GetReuseSummary(result_)}`");
            builder.AppendLine($"- Cache used: `{result_.CacheUsed}`");
            builder.AppendLine($"- Cache path: `{result_.CachePath}`");
            builder.AppendLine($"- Symbols: `{result_.SymbolInventory.TotalSymbols}`");
            builder.AppendLine($"- Endpoints: `{result_.EndpointInventory.TotalEndpoints}`");
            builder.AppendLine($"- Truncated: `{result_.SymbolInventory.Truncated}`");
        }

        builder.AppendLine();
        if (!options_.Summary || plannedFiles_.Count > 0)
        {
            builder.AppendLine(apply ? "## Files Written" : "## Files Planned");
            builder.AppendLine();
            if (plannedFiles_.Count == 0)
            {
                builder.AppendLine("- None");
            }
            else
            {
                foreach (string path in options_.Summary ? plannedFiles_.Take(3) : plannedFiles_)
                {
                    builder.AppendLine($"- `{path}`");
                }

                if (options_.Summary && plannedFiles_.Count > 3)
                {
                    builder.AppendLine($"- ... `{plannedFiles_.Count - 3}` more");
                }
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
        if (options_.Timings && timings_ is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Timings");
            builder.AppendLine();
            builder.AppendLine($"- Total: `{timings_.TotalElapsedMilliseconds} ms`");
            foreach (CommandPhaseTiming phase in timings_.Phases)
            {
                builder.AppendLine($"- {phase.Name}: `{phase.ElapsedMilliseconds} ms` ({phase.Status})");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetReuseSummary(CodeIndexResult result_)
    {
        if (result_.FilesIndexed == 0 && result_.FilesReused > 0)
        {
            return "full-cache-reuse";
        }

        return result_.FilesReused > 0 ? "partial-cache-reuse" : "fresh-index";
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

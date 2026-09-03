using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Models.ContextPacks;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.ContextPacks;

namespace AiRepoKit.Cli.Commands;

public sealed class ContextPackCommand
{
    private readonly IContextPackSelectionService _contextPackSelectionService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] SupportedTasks =
    [
        "change-api",
        "change-ui",
        "fix-build",
        "update-package",
        "review-risk",
        "security-review",
        "test-generation",
        "changed-files"
    ];

    public ContextPackCommand()
        : this(new ContextPackSelectionService())
    {
    }

    internal ContextPackCommand(
        IContextPackSelectionService contextPackSelectionService_)
    {
        this._contextPackSelectionService = contextPackSelectionService_ ?? throw new ArgumentNullException(nameof(contextPackSelectionService_));
    }

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        List<string> warnings = [];
        List<string> errors = [];
        IReadOnlyList<string> files = [];
        ContextPack? pack = null;

        try
        {
            string task = NormalizeTask(options_.Task);
            string format = NormalizeFormat(options_.Format);
            int limit = Math.Clamp(options_.Limit, 1, 100);
            bool apply = options_.Apply && !options_.DryRun;
            if (apply && new GitIgnoreService().EnsureLocalGeneratedArtifactRules(options_.RepoPath, false))
            {
                warnings.Add("Updated .gitignore with AiRepoKit local/generated artifact rules.");
            }

            ContextPackRequest request = new(options_.RepoPath, task, options_.Target, format, limit, apply, options_.RebuildCache, options_.SkipCodeInventory, options_.Verbose, options_.NoProgress, options_.Budget);
            progress.StartPhase("Loading inventories");
            this.EnsureCodeIndex(request, warnings);
            progress.CompletePhase("Inventory loading completed");

            progress.StartPhase("Selecting context");
            ContextPackSelectionResult selection =
                this._contextPackSelectionService.Select(
                    request,
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            pack = selection.Pack;
            warnings.AddRange(selection.Warnings);
            progress.CompletePhase("Context selection completed");
            progress.StartPhase("Writing context pack");
            files = this.WritePack(request, pack);
            progress.CompletePhase("Context pack writing completed");
            progress.CompletePhase("Context-pack completed");
        }
        catch (Exception exception)
        {
            errors.Add(ProcessRunner.Redact(exception.Message));
            progress.FailPhase("Context-pack failed");
        }

        string markdown = options_.AuditJson
            ? JsonSerializer.Serialize(new { pack, files, warnings, errors }, JsonOptions)
            : this.WriteReport(options_, pack, files, warnings, errors);
        return errors.Count == 0 ? CommandResult.Ok(markdown) : CommandResult.Failure(markdown, 1);
    }

    private void EnsureCodeIndex(ContextPackRequest request_, List<string> warnings_)
    {
        ContextPackInventoryCompatibility compatibility = this._contextPackSelectionService.GetInventoryCompatibility(request_.RepoRoot);
        if (request_.RebuildIndex)
        {
            this.RunCodeIndex(request_);
            warnings_.Add("Code-index rebuilt before context-pack generation.");
            return;
        }

        if (request_.SkipCodeIndex)
        {
            warnings_.Add(compatibility.Compatible
                ? "Code-index skipped by --skip-code-index; compatible inventories were used without freshness verification."
                : "Code-index skipped by --skip-code-index; inventories are missing or incompatible and freshness was not verified.");
            return;
        }

        if (compatibility.Compatible)
        {
            warnings_.Add("Existing compatible code inventories reused before context-pack generation.");
            return;
        }

        this.RunCodeIndex(request_);
        warnings_.Add("Code-index generated before context-pack generation.");
    }

    private void RunCodeIndex(ContextPackRequest request_)
    {
        BootstrapOptions options = new(
            "code-index",
            request_.RepoRoot,
            [],
            false,
            true,
            false,
            false,
            false,
            false,
            "dotnet",
            "net10.0",
            "ai_repo_context",
            "airepo",
            "AiRepo.ContextMcp",
            "AiRepo.ContextMcp",
            "AiRepo.ContextMcp",
            "Tools/AiContextMcp/AiRepo.ContextMcp.csproj",
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            3000,
            10000,
            false,
            false,
            request_.RebuildIndex,
            ".ai/generated/inventories",
            "all",
            request_.Verbose,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            "review-risk",
            string.Empty,
            20,
            false,
            [],
            request_.NoProgress);
        CommandResult result = new CodeIndexCommand().Execute(options);
        if (!result.Success)
        {
            throw new InvalidOperationException("Unable to refresh code-index before context-pack generation.");
        }
    }

    private IReadOnlyList<string> WritePack(ContextPackRequest request_, ContextPack pack_)
    {
        string outputPath = Path.Combine(request_.RepoRoot, ".ai", "generated", "context-packs");
        this.EnsureOutputPath(request_.RepoRoot, outputPath);
        string suffix = string.IsNullOrWhiteSpace(request_.Target) ? request_.Task : $"{request_.Task}.{Slug(request_.Target)}";
        List<string> paths = [];
        bool writeJson = request_.Format is "json" or "all";
        bool writeMarkdown = request_.Format is "markdown" or "all";
        if (request_.Apply)
        {
            Directory.CreateDirectory(outputPath);
        }

        if (writeJson)
        {
            string path = Path.Combine(outputPath, $"{suffix}.json");
            if (request_.Apply)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(pack_, JsonOptions));
            }

            paths.Add(Relative(request_.RepoRoot, path));
        }

        if (writeMarkdown)
        {
            string path = Path.Combine(outputPath, $"{suffix}.md");
            if (request_.Apply)
            {
                File.WriteAllText(path, this.WriteMarkdown(pack_));
            }

            paths.Add(Relative(request_.RepoRoot, path));
        }

        return paths;
    }

    private string WriteMarkdown(ContextPack pack_)
    {
        StringBuilder builder = new();
        builder.AppendLine($"# Context Pack: {pack_.Task}");
        builder.AppendLine();
        builder.AppendLine($"Generated: {pack_.GeneratedAtLocal}");
        builder.AppendLine($"Target: {ValueOrNone(pack_.Target)}");
        builder.AppendLine($"Recommended agent: {pack_.RecommendedAgent}");
        builder.AppendLine($"Token budget hint: {pack_.TokenBudgetHint}");
        builder.AppendLine($"Estimated tokens: {pack_.EstimatedTokens}");
        builder.AppendLine($"Budget: {ValueOrNone(pack_.Budget?.ToString())}");
        builder.AppendLine($"Truncated: {pack_.Truncated}");
        builder.AppendLine();
        builder.AppendLine(pack_.Summary);
        this.AppendChangedFiles(builder, "Staged Files", pack_.StagedFiles);
        this.AppendChangedFiles(builder, "Unstaged Files", pack_.UnstagedFiles);
        this.AppendChangedFiles(builder, "Untracked Files", pack_.UntrackedFiles);
        this.AppendStrings(builder, "Affected Projects", pack_.AffectedProjects ?? []);
        this.AppendStrings(builder, "Affected Symbols", pack_.AffectedSymbols ?? []);
        this.AppendItems(builder, "Likely Files", pack_.LikelyFiles);
        this.AppendItems(builder, "Relevant Symbols", pack_.RelevantSymbols);
        this.AppendItems(builder, "Relevant Endpoints", pack_.RelevantEndpoints);
        this.AppendItems(builder, "Relevant Packages", pack_.RelevantPackages);
        this.AppendStrings(builder, "Risk Areas", pack_.RiskAreas);
        this.AppendStrings(builder, "Validation Commands", pack_.ValidationCommands);
        this.AppendStrings(builder, "Suggested MCP Calls", pack_.SuggestedMcpCalls);
        this.AppendStrings(builder, "Notes", pack_.Notes);
        if (!string.IsNullOrWhiteSpace(pack_.CommitMessageSuggestion))
        {
            builder.AppendLine();
            builder.AppendLine("## Commit Message Suggestion");
            builder.AppendLine();
            builder.AppendLine(pack_.CommitMessageSuggestion);
        }

        if (pack_.Cuts is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("## Budget Cuts");
            builder.AppendLine();
            foreach (var cut in pack_.Cuts)
            {
                builder.AppendLine($"- {cut.Path} - {cut.Reason} ({cut.RemovedEstimatedTokens} tokens)");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private void AppendChangedFiles(StringBuilder builder_, string title_, IReadOnlyList<ChangedFileItem>? files_)
    {
        if (files_ is null)
        {
            return;
        }

        builder_.AppendLine();
        builder_.AppendLine($"## {title_}");
        builder_.AppendLine();
        if (files_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (ChangedFileItem file in files_)
        {
            builder_.AppendLine($"- {file.Path} [{file.Status}]");
        }
    }

    private void AppendItems(StringBuilder builder_, string title_, IReadOnlyList<ContextPackItem> items_)
    {
        builder_.AppendLine();
        builder_.AppendLine($"## {title_}");
        builder_.AppendLine();
        if (items_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (ContextPackItem item in items_)
        {
            builder_.AppendLine($"- {item.Name} [{item.Kind}] {item.File} score={item.Score} - {item.Reason}");
        }
    }

    private void AppendStrings(StringBuilder builder_, string title_, IReadOnlyList<string> items_)
    {
        builder_.AppendLine();
        builder_.AppendLine($"## {title_}");
        builder_.AppendLine();
        if (items_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string item in items_)
        {
            builder_.AppendLine($"- {item}");
        }
    }

    private string WriteReport(BootstrapOptions options_, ContextPack? pack_, IReadOnlyList<string> files_, IReadOnlyList<string> warnings_, IReadOnlyList<string> errors_)
    {
        bool apply = options_.Apply && !options_.DryRun;
        StringBuilder builder = new();
        builder.AppendLine(apply ? "# Context Pack Apply" : "# Context Pack Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Mode: `{(apply ? "apply" : "dry-run")}`");
        builder.AppendLine($"- Task: `{(pack_?.Task ?? options_.Task)}`");
        builder.AppendLine($"- Target: `{ValueOrNone(pack_?.Target ?? options_.Target)}`");
        builder.AppendLine($"- Format: `{options_.Format}`");
        builder.AppendLine($"- Limit: `{options_.Limit}`");
        builder.AppendLine($"- Budget: `{(options_.Budget > 0 ? options_.Budget.ToString() : "none")}`");
        if (pack_ is not null)
        {
            builder.AppendLine($"- Summary: {pack_.Summary}");
            builder.AppendLine($"- EstimatedTokens: `{pack_.EstimatedTokens}`");
            builder.AppendLine($"- Truncated: `{pack_.Truncated}`");
        }

        builder.AppendLine();
        builder.AppendLine(apply ? "## Files Written" : "## Files Planned");
        builder.AppendLine();
        if (files_.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (string path in files_)
            {
                builder.AppendLine($"- `{path}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Warnings");
        this.AppendMessages(builder, warnings_);
        builder.AppendLine();
        builder.AppendLine("## Errors");
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

    private void EnsureOutputPath(string repoRoot_, string outputPath_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        string outputPath = Path.GetFullPath(outputPath_);
        string root = repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Output path must stay inside the target repository.");
        }

        string relative = Path.GetRelativePath(repoRoot, outputPath).Replace('\\', '/');
        if (!relative.Equals(".ai/generated/context-packs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Context-pack output path must be .ai/generated/context-packs.");
        }
    }

    private static string NormalizeTask(string value_)
    {
        string value = string.IsNullOrWhiteSpace(value_) ? "review-risk" : value_.ToLowerInvariant();
        if (SupportedTasks.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return value;
        }

        throw new InvalidOperationException($"Task must be one of: {string.Join(", ", SupportedTasks)}.");
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

    private static string Slug(string value_)
    {
        string slug = Regex.Replace(value_.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "target" : slug;
    }

    private static string Relative(string repoRoot_, string path_)
    {
        return Path.GetRelativePath(Path.GetFullPath(repoRoot_), Path.GetFullPath(path_)).Replace('\\', '/');
    }

    private static string ValueOrNone(string? value_)
    {
        return string.IsNullOrWhiteSpace(value_) ? "none" : value_;
    }


}

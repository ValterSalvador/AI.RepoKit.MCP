using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Models.ContextPacks;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.ChangedFiles;
using AiRepoKit.Cli.Services.ContextBudget;
using AiRepoKit.Cli.Services.Impact;

namespace AiRepoKit.Cli.Commands;

public sealed class ContextPackCommand
{
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
            if (!request.SkipCodeIndex)
            {
                progress.StartPhase("Loading inventories");
                this.EnsureCodeIndex(request, warnings);
                progress.CompletePhase("Inventory loading completed");
            }

            progress.StartPhase("Selecting context");
            pack = this.BuildPack(request, warnings);
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
        string symbolPath = Path.Combine(request_.RepoRoot, ".ai", "generated", "inventories", "symbol-inventory.json");
        string endpointPath = Path.Combine(request_.RepoRoot, ".ai", "generated", "inventories", "endpoint-inventory.json");
        if (!request_.RebuildIndex && File.Exists(symbolPath) && File.Exists(endpointPath))
        {
            return;
        }

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

        warnings_.Add(request_.RebuildIndex ? "Code-index rebuilt before context-pack generation." : "Code-index generated before context-pack generation.");
    }

    private ContextPack BuildPack(ContextPackRequest request_, List<string> warnings_)
    {
        JsonObject? symbolsRoot = this.ReadJson(request_.RepoRoot, ".ai/generated/inventories/symbol-inventory.json", true, warnings_);
        JsonObject? endpointsRoot = this.ReadJson(request_.RepoRoot, ".ai/generated/inventories/endpoint-inventory.json", true, warnings_);
        JsonObject? packagesRoot = this.ReadJson(request_.RepoRoot, ".ai/generated/inventories/package-inventory.json", false, warnings_);
        JsonObject? buildRoot = this.ReadJson(request_.RepoRoot, ".ai/generated/reports/latest-build-summary.json", false, warnings_);
        JsonObject? secretRoot = this.ReadJson(request_.RepoRoot, ".ai/generated/reports/secret-scan-report.json", false, warnings_);
        JsonObject? manifestRoot = this.ReadJson(request_.RepoRoot, ".ai/manifests/mcp-context-manifest.json", false, warnings_);
        JsonArray symbols = GetArray(symbolsRoot, "Symbols");
        if (request_.Task == "changed-files")
        {
            return this.BuildChangedFilesPack(request_, symbols, warnings_);
        }

        JsonArray endpoints = GetArray(endpointsRoot, "Endpoints");
        JsonArray packages = GetArray(packagesRoot, "packages");
        if (packages.Count == 0)
        {
            packages = GetArray(packagesRoot, "Packages");
        }

        IReadOnlyList<ContextPackItem> symbolItems = this.ScoreSymbols(request_, symbols);
        IReadOnlyList<ContextPackItem> endpointItems = this.ScoreEndpoints(request_, endpoints);
        IReadOnlyList<ContextPackItem> packageItems = this.ScorePackages(request_, packages);
        IReadOnlyList<ContextPackItem> likelyFiles = this.GetLikelyFiles(request_, symbolItems, endpointItems, packageItems);
        IReadOnlyList<string> riskAreas = this.GetRiskAreas(request_, buildRoot, secretRoot, manifestRoot, symbolItems, packageItems);
        IReadOnlyList<string> validation = this.GetValidationCommands(request_, symbolItems);
        IReadOnlyList<string> calls = this.GetSuggestedMcpCalls(request_);
        IReadOnlyList<string> notes = this.GetNotes(request_, buildRoot, secretRoot, manifestRoot);
        string summary = this.GetSummary(request_, symbolItems, endpointItems, packageItems, riskAreas);

        ContextPack pack = new(
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            ".",
            request_.Task,
            request_.Target,
            this.GetRecommendedAgent(request_.Task),
            request_.Limit <= 20 ? "compact" : "standard",
            summary,
            likelyFiles,
            symbolItems.Take(request_.Limit).ToArray(),
            endpointItems.Take(request_.Limit).ToArray(),
            packageItems.Take(request_.Limit).ToArray(),
            riskAreas.Take(request_.Limit).ToArray(),
            validation,
            calls,
            notes);
        return this.ApplyBudget(pack, request_.Budget);
    }

    private ContextPack BuildChangedFilesPack(ContextPackRequest request_, JsonArray symbols_, List<string> warnings_)
    {
        ChangedFilesResult changed = new ChangedFilesService().GetChangedFiles(request_.RepoRoot);
        warnings_.AddRange(changed.Warnings);
        HashSet<string> files = new(changed.Files.Select(file_ => file_.Path), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ContextPackItem> symbolItems = symbols_
            .OfType<JsonObject>()
            .Where(symbol_ => files.Contains(CleanPath(GetString(symbol_, "File"))))
            .Select(symbol_ => new ContextPackItem(
                GetString(symbol_, "Name"),
                FirstString(symbol_, "Classification", "Kind"),
                CleanPath(GetString(symbol_, "File")),
                "Symbol is in a changed file.",
                90))
            .OrderBy(item_ => item_.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item_ => item_.Name, StringComparer.OrdinalIgnoreCase)
            .Take(request_.Limit)
            .ToArray();
        IReadOnlyList<ContextPackItem> likelyFiles = changed.Files
            .Take(request_.Limit)
            .Select(file_ => new ContextPackItem(Path.GetFileName(file_.Path), "File", file_.Path, file_.Status, file_.Staged ? 100 : file_.Unstaged ? 80 : 60))
            .ToArray();
        IReadOnlyList<string> affectedProjects = likelyFiles.Select(item_ => GuessProject(item_.File)).Where(value_ => !string.IsNullOrWhiteSpace(value_)).Distinct(StringComparer.OrdinalIgnoreCase).Take(request_.Limit).ToArray();
        IReadOnlyList<string> risks = GetChangedFileRisks(changed.Files, symbolItems).Take(request_.Limit).ToArray();
        IReadOnlyList<string> validation = GetChangedFileValidation(affectedProjects, changed.Files);
        string summary = changed.Files.Count == 0
            ? "changed-files context: no local changed files detected."
            : $"changed-files context: {changed.StagedFiles.Count} staged, {changed.UnstagedFiles.Count} unstaged, {changed.UntrackedFiles.Count} untracked files.";
        ContextPack pack = new(
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            ".",
            request_.Task,
            request_.Target,
            "reviewer",
            request_.Budget > 0 ? "budgeted" : request_.Limit <= 20 ? "compact" : "standard",
            summary,
            likelyFiles,
            symbolItems,
            [],
            [],
            risks,
            validation,
            ["get_context changed-files brief", "get_context impact brief", "search_context changed-file"],
            ["Generated from Git status and local regenerable inventories.", "No source method bodies are included."],
            changed.Files.Where(file_ => file_.Staged).ToArray(),
            changed.Files.Where(file_ => file_.Unstaged).ToArray(),
            changed.Files.Where(file_ => file_.Untracked).ToArray(),
            affectedProjects,
            symbolItems.Select(item_ => $"{item_.Name} {item_.File}").ToArray(),
            summary,
            changed.Files.Count == 0 ? string.Empty : $"Review changed files ({changed.Files.Count} files)");
        return this.ApplyBudget(pack, request_.Budget);
    }

    private ContextPack ApplyBudget(ContextPack pack_, int budget_)
    {
        ContextBudgeter budgeter = new();
        var result = budgeter.Report(pack_, budget_ > 0 ? budget_ : null);
        IReadOnlyList<AiRepoKit.Cli.Models.ContextBudget.BudgetCut> cuts = result.Cuts;
        if (budget_ > 0 && result.EstimatedTokens > budget_ && cuts.Count == 0)
        {
            cuts = [new("context-pack", "Estimated output exceeds budget; compact fields should be preferred by consumers.", result.EstimatedTokens - budget_)];
        }

        return pack_ with
        {
            EstimatedTokens = result.EstimatedTokens,
            Budget = result.Budget,
            Truncated = result.Truncated || cuts.Count > 0,
            Cuts = cuts
        };
    }

    private IReadOnlyList<ContextPackItem> ScoreSymbols(ContextPackRequest request_, JsonArray symbols_)
    {
        return symbols_
            .OfType<JsonObject>()
            .Select(symbol_ => this.ScoreSymbol(request_, symbol_))
            .Where(item_ => item_.Score > 0)
            .OrderByDescending(item_ => item_.Score)
            .ThenBy(item_ => item_.File, StringComparer.OrdinalIgnoreCase)
            .Take(request_.Limit)
            .ToArray();
    }

    private ContextPackItem ScoreSymbol(ContextPackRequest request_, JsonObject symbol_)
    {
        string name = GetString(symbol_, "Name");
        string kind = GetString(symbol_, "Classification");
        if (string.IsNullOrWhiteSpace(kind))
        {
            kind = GetString(symbol_, "Kind");
        }

        string file = CleanPath(GetString(symbol_, "File"));
        string text = $"{name} {kind} {file}";
        int score = this.ScoreText(request_, text, kind);
        string reason = score > 0 ? this.GetReason(request_.Task, kind, request_.Target) : string.Empty;
        return new ContextPackItem(name, kind, file, reason, score);
    }

    private IReadOnlyList<ContextPackItem> ScoreEndpoints(ContextPackRequest request_, JsonArray endpoints_)
    {
        if (request_.Task is "change-ui" or "update-package")
        {
            return [];
        }

        return endpoints_
            .OfType<JsonObject>()
            .Select(endpoint_ =>
            {
                string method = GetString(endpoint_, "Method");
                string route = GetString(endpoint_, "Route");
                string handler = GetString(endpoint_, "HandlerOrController");
                string file = CleanPath(GetString(endpoint_, "File"));
                string text = $"{method} {route} {handler} {file}";
                int score = this.ScoreText(request_, text, "Endpoint");
                if (request_.Task == "change-api")
                {
                    score += 35;
                }

                return new ContextPackItem($"{method} {route}".Trim(), "Endpoint", file, $"Endpoint related to {request_.Task}.", score);
            })
            .Where(item_ => item_.Score > 0)
            .OrderByDescending(item_ => item_.Score)
            .ThenBy(item_ => item_.Name, StringComparer.OrdinalIgnoreCase)
            .Take(request_.Limit)
            .ToArray();
    }

    private IReadOnlyList<ContextPackItem> ScorePackages(ContextPackRequest request_, JsonArray packages_)
    {
        return packages_
            .OfType<JsonObject>()
            .Select(package_ =>
            {
                string name = FirstString(package_, "package", "Package", "Name", "id", "Id");
                string version = FirstString(package_, "version", "Version", "ResolvedVersion");
                string project = CleanPath(FirstString(package_, "project", "Project", "File"));
                string text = $"{name} {version} {project}";
                int score = request_.Task == "update-package" ? 60 : 0;
                if (request_.Task == "change-ui" && name.Contains("MudBlazor", StringComparison.OrdinalIgnoreCase))
                {
                    score += 70;
                }

                if (TargetMatches(request_.Target, text))
                {
                    score += 50;
                }

                if (request_.Task is "review-risk" or "security-review")
                {
                    score += 10;
                }

                return new ContextPackItem(string.IsNullOrWhiteSpace(version) ? name : $"{name} {version}", "Package", project, $"Package signal for {request_.Task}.", score);
            })
            .Where(item_ => item_.Score > 0 && !string.IsNullOrWhiteSpace(item_.Name))
            .OrderByDescending(item_ => item_.Score)
            .ThenBy(item_ => item_.Name, StringComparer.OrdinalIgnoreCase)
            .Take(request_.Limit)
            .ToArray();
    }

    private IReadOnlyList<ContextPackItem> GetLikelyFiles(ContextPackRequest request_, params IReadOnlyList<ContextPackItem>[] groups_)
    {
        return groups_
            .SelectMany(group_ => group_)
            .Where(item_ => !string.IsNullOrWhiteSpace(item_.File))
            .GroupBy(item_ => item_.File, StringComparer.OrdinalIgnoreCase)
            .Select(group_ => new ContextPackItem(Path.GetFileName(group_.Key), "File", group_.Key, string.Join("; ", group_.Select(item_ => item_.Reason).Distinct(StringComparer.OrdinalIgnoreCase).Take(2)), group_.Max(item_ => item_.Score)))
            .OrderByDescending(item_ => item_.Score)
            .ThenBy(item_ => item_.File, StringComparer.OrdinalIgnoreCase)
            .Take(request_.Limit)
            .ToArray();
    }

    private int ScoreText(ContextPackRequest request_, string text_, string kind_)
    {
        int score = 0;
        if (TargetMatches(request_.Target, text_))
        {
            score += 60;
        }

        string[] preferred = request_.Task switch
        {
            "change-api" => ["Controller", "MinimalApi", "Handler", "Service", "Dto", "Request", "Response"],
            "change-ui" => ["UI", "Page", "Component", "View", "Razor", "Blazor", "MudBlazor", "Form", "Dialog"],
            "fix-build" => ["Project", "Service", "Handler", "Controller", "Unknown"],
            "test-generation" => ["Service", "Handler", "Controller"],
            "review-risk" => ["Controller", "Service", "Handler", "Repository", "Configuration", "Middleware"],
            "security-review" => ["Controller", "Service", "Configuration", "Middleware"],
            _ => []
        };
        foreach (string value in preferred)
        {
            if (text_.Contains(value, StringComparison.OrdinalIgnoreCase) || kind_.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }
        }

        if (request_.Task == "test-generation" && text_.Contains("Tests", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }

    private IReadOnlyList<string> GetRiskAreas(ContextPackRequest request_, JsonObject? buildRoot_, JsonObject? secretRoot_, JsonObject? manifestRoot_, IReadOnlyList<ContextPackItem> symbols_, IReadOnlyList<ContextPackItem> packages_)
    {
        List<string> risks = [];
        if (request_.Task == "update-package")
        {
            risks.Add("Transitive package changes");
            risks.Add("Breaking API changes");
            risks.Add("Test project compatibility");
        }

        if (request_.Task == "security-review")
        {
            risks.Add("Restricted path policy");
            risks.Add("Redacted security findings only");
        }

        if (buildRoot_ is not null)
        {
            int errors = FirstInt(buildRoot_, "ErrorCount", "errors", "Errors");
            int warnings = FirstInt(buildRoot_, "WarningCount", "warnings", "Warnings");
            if (errors > 0)
            {
                risks.Add($"Latest build errors: {errors}");
            }

            if (warnings > 0)
            {
                risks.Add($"Latest build warnings: {warnings}");
            }
        }

        if (secretRoot_ is not null)
        {
            bool redacted = GetBool(secretRoot_, "RedactedOnly");
            int count = FirstInt(secretRoot_, "FindingCount", "findingCount");
            risks.Add(redacted ? $"Redacted secret-scan findings: {count}" : "Secret-scan report was not marked redacted-only");
        }

        if (manifestRoot_ is not null && GetArray(manifestRoot_, "RestrictedPaths").Count > 0)
        {
            risks.Add("Manifest restricted paths apply");
        }

        if (symbols_.Any(item_ => item_.Kind.Contains("Repository", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("Persistence boundary symbols nearby");
        }

        if (packages_.Any())
        {
            risks.Add("Package references may affect runtime or build behavior");
        }

        if (risks.Count == 0)
        {
            risks.Add("No generated build or security risk report was available");
        }

        return risks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> GetValidationCommands(ContextPackRequest request_, IReadOnlyList<ContextPackItem> symbols_)
    {
        return request_.Task switch
        {
            "fix-build" => ["dotnet build -c Debug"],
            "update-package" => ["dotnet restore", "dotnet build"],
            "review-risk" => ["airepo audit --repo .", "airepo self-check --repo . --skip-build-mcp"],
            "security-review" => ["airepo audit --repo .", "powershell -ExecutionPolicy Bypass -File Tools/AiContext/CheckSecrets.ps1"],
            "test-generation" => symbols_.Any(item_ => item_.File.Contains("Tests", StringComparison.OrdinalIgnoreCase)) ? ["dotnet build", "dotnet test"] : ["dotnet build", "dotnet test when explicitly allowed"],
            "change-api" => symbols_.Any(item_ => item_.File.Contains("Tests", StringComparison.OrdinalIgnoreCase)) ? ["dotnet build", "relevant dotnet test project"] : ["dotnet build"],
            _ => ["dotnet build"]
        };
    }

    private IReadOnlyList<string> GetSuggestedMcpCalls(ContextPackRequest request_)
    {
        List<string> calls = request_.Task switch
        {
            "change-api" => ["get_context endpoints brief", "get_context symbols brief"],
            "change-ui" => ["get_context symbols brief"],
            "fix-build" => ["get_context symbols brief", "search_context build"],
            "update-package" => ["get_context packages brief"],
            "security-review" => ["get_policy security", "get_context security brief"],
            "test-generation" => ["get_context symbols brief"],
            _ => ["get_repo_brief", "get_health", "get_context symbols brief"]
        };
        if (!string.IsNullOrWhiteSpace(request_.Target))
        {
            calls.Add($"search_context {request_.Target}");
        }

        return calls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> GetNotes(ContextPackRequest request_, JsonObject? buildRoot_, JsonObject? secretRoot_, JsonObject? manifestRoot_)
    {
        List<string> notes = [];
        notes.Add("Generated from local regenerable inventories and reports.");
        notes.Add("Source method bodies are not included.");
        if (request_.SkipCodeIndex)
        {
            notes.Add("Code-index refresh was skipped.");
        }

        if (buildRoot_ is null)
        {
            notes.Add("Latest build summary was not present.");
        }

        if (secretRoot_ is null)
        {
            notes.Add("Secret-scan report was not present.");
        }

        if (manifestRoot_ is null)
        {
            notes.Add("MCP context manifest was not present.");
        }

        return notes.ToArray();
    }

    private string GetSummary(ContextPackRequest request_, IReadOnlyList<ContextPackItem> symbols_, IReadOnlyList<ContextPackItem> endpoints_, IReadOnlyList<ContextPackItem> packages_, IReadOnlyList<string> risks_)
    {
        string target = string.IsNullOrWhiteSpace(request_.Target) ? "repository" : request_.Target;
        return $"{request_.Task} context for {target}: {symbols_.Count} symbols, {endpoints_.Count} endpoints, {packages_.Count} packages, {risks_.Count} risk notes.";
    }

    private string GetRecommendedAgent(string task_)
    {
        return task_ switch
        {
            "fix-build" => "test-fixer",
            "review-risk" or "security-review" => "reviewer",
            "test-generation" => "implementer",
            _ => "implementer"
        };
    }

    private string GetReason(string task_, string kind_, string target_)
    {
        string target = string.IsNullOrWhiteSpace(target_) ? string.Empty : $" and target `{target_}`";
        return $"{kind_} matched {task_} heuristics{target}.";
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

    private JsonObject? ReadJson(string repoRoot_, string relativePath_, bool required_, List<string> warnings_)
    {
        string normalized = relativePath_.Replace('\\', '/').TrimStart('/');
        if (IsRestricted(normalized))
        {
            throw new InvalidOperationException($"Refusing to read restricted path: {normalized}");
        }

        string fullPath = Path.GetFullPath(Path.Combine(repoRoot_, normalized));
        string root = Path.GetFullPath(repoRoot_).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Context-pack input path must stay inside the repository.");
        }

        if (!File.Exists(fullPath))
        {
            if (required_)
            {
                throw new InvalidOperationException($"Missing required context-pack input: {normalized}");
            }

            return null;
        }

        try
        {
            JsonObject? json = JsonNode.Parse(File.ReadAllText(fullPath)) as JsonObject;
            if (relativePath_.Contains("secret-scan-report", StringComparison.OrdinalIgnoreCase) && json is not null && !GetBool(json, "RedactedOnly"))
            {
                warnings_.Add("Secret-scan report was present but not marked redacted-only; only summary risk was used.");
                return new JsonObject { ["RedactedOnly"] = false };
            }

            return json;
        }
        catch (JsonException exception)
        {
            if (required_)
            {
                throw new InvalidOperationException($"Invalid JSON in {normalized}: {exception.Message}");
            }

            warnings_.Add($"Optional JSON could not be read: {normalized}");
            return null;
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

    private static bool TargetMatches(string target_, string text_)
    {
        return !string.IsNullOrWhiteSpace(target_) && text_.Contains(target_, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetChangedFileRisks(IReadOnlyList<ChangedFileItem> files_, IReadOnlyList<ContextPackItem> symbols_)
    {
        List<string> risks = [];
        if (files_.Any(file_ => file_.Untracked))
        {
            risks.Add("Untracked files need review before commit.");
        }

        if (symbols_.Any(symbol_ => symbol_.Kind.Contains("Repository", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("Persistence boundary symbols changed.");
        }

        if (symbols_.Any(symbol_ => symbol_.Kind.Contains("Configuration", StringComparison.OrdinalIgnoreCase)) || files_.Any(file_ => file_.Path.Contains("Program.cs", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("Configuration-sensitive files changed.");
        }

        if (files_.Any(file_ => file_.Path.Contains("Test", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("Test files changed.");
        }

        if (files_.Count == 0)
        {
            risks.Add("No changed files detected.");
        }
        else if (risks.Count == 0)
        {
            risks.Add("General review required for changed files.");
        }

        return risks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> GetChangedFileValidation(IReadOnlyList<string> affectedProjects_, IReadOnlyList<ChangedFileItem> files_)
    {
        List<string> commands = ["dotnet build"];
        if (files_.Any(file_ => file_.Path.Contains("Test", StringComparison.OrdinalIgnoreCase)) || affectedProjects_.Any(project_ => project_.Contains("Test", StringComparison.OrdinalIgnoreCase)))
        {
            commands.Add("dotnet test");
        }
        else
        {
            commands.Add("dotnet test when explicitly allowed");
        }

        commands.Add("airepo impact");
        commands.Add("airepo self-check --repo . --skip-build-mcp");
        return commands;
    }

    private static string GuessProject(string file_)
    {
        string path = file_.Replace('\\', '/');
        if (path.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = path.Split('/');
            return parts.Length >= 2 ? $"src/{parts[1]}/{parts[1]}.csproj" : string.Empty;
        }

        if (path.StartsWith("Tools/AiContextMcp/", StringComparison.OrdinalIgnoreCase))
        {
            return "Tools/AiContextMcp/AiRepo.ContextMcp.csproj";
        }

        return string.Empty;
    }

    private static string Slug(string value_)
    {
        string slug = Regex.Replace(value_.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "target" : slug;
    }

    private static string CleanPath(string value_)
    {
        string value = value_.Replace('\\', '/').TrimStart('/');
        return IsRestricted(value) ? string.Empty : value;
    }

    private static bool IsRestricted(string relativePath_)
    {
        string path = relativePath_.Replace('\\', '/').TrimStart('/');
        return path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("oracle-data/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("wwwroot/uploads/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Tools/AISandbox/", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase);
    }

    private static string Relative(string repoRoot_, string path_)
    {
        return Path.GetRelativePath(Path.GetFullPath(repoRoot_), Path.GetFullPath(path_)).Replace('\\', '/');
    }

    private static string ValueOrNone(string? value_)
    {
        return string.IsNullOrWhiteSpace(value_) ? "none" : value_;
    }

    private static JsonArray GetArray(JsonObject? value_, string name_)
    {
        return value_ is not null && value_.TryGetPropertyValue(name_, out JsonNode? node) && node is JsonArray array ? array : [];
    }

    private static string FirstString(JsonObject value_, params string[] names_)
    {
        foreach (string name in names_)
        {
            string value = GetString(value_, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string GetString(JsonObject value_, string name_)
    {
        return value_.TryGetPropertyValue(name_, out JsonNode? node) ? node?.GetValue<string>() ?? string.Empty : string.Empty;
    }

    private static int FirstInt(JsonObject value_, params string[] names_)
    {
        foreach (string name in names_)
        {
            int value = GetInt(value_, name);
            if (value != 0)
            {
                return value;
            }
        }

        return 0;
    }

    private static int GetInt(JsonObject value_, string name_)
    {
        if (!value_.TryGetPropertyValue(name_, out JsonNode? node) || node is null)
        {
            return 0;
        }

        return node.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : 0;
    }

    private static bool GetBool(JsonObject value_, string name_)
    {
        return value_.TryGetPropertyValue(name_, out JsonNode? node) && node is not null && node.GetValueKind() == JsonValueKind.True;
    }
}

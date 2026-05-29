using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Models.ContextBudget;
using AiRepoKit.Cli.Models.Impact;
using AiRepoKit.Cli.Services.ChangedFiles;
using AiRepoKit.Cli.Services.ContextBudget;

namespace AiRepoKit.Cli.Services.Impact;

public sealed class ImpactService
{
    private readonly ContextBudgeter budgeter = new();

    public ImpactReport Build(string repoRoot_, string target_, string since_, int limit_, int? budget_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        ChangedFilesResult changed = new ChangedFilesService().GetChangedFiles(repoRoot, since_);
        JsonObject? symbolsRoot = ReadJson(repoRoot, ".ai/generated/inventories/symbol-inventory.json");
        JsonArray symbols = GetArray(symbolsRoot, "Symbols");
        List<string> warnings = [.. changed.Warnings];
        List<string> fileScope = changed.Files.Select(file_ => file_.Path).ToList();
        if (!string.IsNullOrWhiteSpace(target_))
        {
            fileScope.AddRange(symbols.OfType<JsonObject>()
                .Where(symbol_ => MatchesTarget(symbol_, target_))
                .Select(symbol_ => GetString(symbol_, "File")));
        }

        fileScope = fileScope.Where(value_ => !string.IsNullOrWhiteSpace(value_)).Distinct(StringComparer.OrdinalIgnoreCase).Take(limit_).ToList();
        IReadOnlyList<string> affectedProjects = fileScope.Select(GuessProject).Where(value_ => !string.IsNullOrWhiteSpace(value_)).Distinct(StringComparer.OrdinalIgnoreCase).Take(limit_).ToArray();
        IReadOnlyList<string> affectedSymbols = symbols.OfType<JsonObject>()
            .Where(symbol_ => fileScope.Contains(GetString(symbol_, "File"), StringComparer.OrdinalIgnoreCase) || MatchesTarget(symbol_, target_))
            .Select(symbol_ => $"{GetString(symbol_, "Name")} [{GetString(symbol_, "Classification")}] {GetString(symbol_, "File")}")
            .Take(limit_)
            .ToArray();
        IReadOnlyList<string> risks = this.GetRisks(fileScope, affectedSymbols, changed.Files).Take(limit_).ToArray();
        IReadOnlyList<string> validation = this.GetValidationCommands(affectedProjects, fileScope).Take(limit_).ToArray();
        string summary = fileScope.Count == 0
            ? "No local changed files were detected. Use --target or --since to analyze a specific surface."
            : $"Impact preview: {fileScope.Count} files, {affectedProjects.Count} projects, {affectedSymbols.Count} symbols.";
        string commit = fileScope.Count == 0 ? string.Empty : this.SuggestCommitMessage(fileScope, risks);
        var shape = new
        {
            changed.Files,
            affectedProjects,
            affectedSymbols,
            risks,
            validation
        };
        BudgetResult<object> budget = this.budgeter.Report((object)shape, budget_);
        IReadOnlyList<BudgetCut> cuts = budget.Cuts;
        if (budget_.HasValue && budget.EstimatedTokens > budget_.Value && cuts.Count == 0)
        {
            cuts = [new("impact", "Estimated output exceeds budget; compact MCP detail should be preferred by consumers.", budget.EstimatedTokens - budget_.Value)];
        }

        return new ImpactReport(
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            repoRoot,
            target_,
            since_,
            summary,
            changed.Files.Take(limit_).ToArray(),
            affectedProjects,
            affectedSymbols,
            risks,
            ["changed-files", "review-risk", "test-generation"],
            risks.Any(risk_ => risk_.Contains("security", StringComparison.OrdinalIgnoreCase)) ? ["security-reviewer", "reviewer", "test-fixer"] : ["reviewer", "test-fixer", "implementer"],
            validation,
            summary,
            commit,
            budget.EstimatedTokens,
            budget.Budget,
            budget.Truncated || cuts.Count > 0,
            cuts,
            warnings);
    }

    public void Write(string repoRoot_, ImpactReport report_, string format_)
    {
        string output = Path.Combine(repoRoot_, ".ai", "generated", "reports");
        Directory.CreateDirectory(output);
        if (format_ is "json" or "all")
        {
            File.WriteAllText(Path.Combine(output, "impact-report.json"), JsonSerializer.Serialize(report_, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (format_ is "markdown" or "all")
        {
            File.WriteAllText(Path.Combine(output, "impact-report.md"), ToMarkdown(report_));
        }
    }

    public static string ToMarkdown(ImpactReport report_)
    {
        List<string> lines =
        [
            "# Impact Report",
            string.Empty,
            report_.Summary,
            string.Empty,
            $"Estimated tokens: `{report_.EstimatedTokens}`",
            $"Budget: `{(report_.Budget?.ToString() ?? "none")}`",
            $"Truncated: `{report_.Truncated}`",
            string.Empty,
            "## Changed Files"
        ];
        lines.AddRange(report_.ChangedFiles.Count == 0 ? ["- None"] : report_.ChangedFiles.Select(file_ => $"- `{file_.Path}` [{file_.Status}]"));
        Append(lines, "Affected Projects", report_.AffectedProjects);
        Append(lines, "Affected Symbols", report_.AffectedSymbols);
        Append(lines, "Risks", report_.Risks);
        Append(lines, "Recommended Context Packs", report_.RecommendedContextPacks);
        Append(lines, "Recommended Agents", report_.RecommendedAgents);
        Append(lines, "Validation Commands", report_.ValidationCommands);
        if (!string.IsNullOrWhiteSpace(report_.CommitMessageSuggestion))
        {
            lines.Add(string.Empty);
            lines.Add("## Commit Message Suggestion");
            lines.Add(report_.CommitMessageSuggestion);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private IReadOnlyList<string> GetRisks(IReadOnlyList<string> files_, IReadOnlyList<string> symbols_, IReadOnlyList<ChangedFileItem> changed_)
    {
        List<string> risks = [];
        if (files_.Any(file_ => file_.Contains("Program.cs", StringComparison.OrdinalIgnoreCase) || file_.Contains("appsettings", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("configuration");
        }

        if (symbols_.Any(symbol_ => symbol_.Contains("Controller", StringComparison.OrdinalIgnoreCase) || symbol_.Contains("Middleware", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("aspnet-core");
        }

        if (symbols_.Any(symbol_ => symbol_.Contains("Repository", StringComparison.OrdinalIgnoreCase) || symbol_.Contains("DbContext", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("data-layer");
        }

        if (files_.Any(file_ => file_.Contains("Test", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("test");
        }

        if (changed_.Any(file_ => file_.Untracked))
        {
            risks.Add("untracked-files");
        }

        if (risks.Count == 0 && files_.Count > 0)
        {
            risks.Add("general-review");
        }

        return risks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> GetValidationCommands(IReadOnlyList<string> projects_, IReadOnlyList<string> files_)
    {
        List<string> commands = ["dotnet build"];
        if (files_.Any(file_ => file_.Contains("Test", StringComparison.OrdinalIgnoreCase)) || projects_.Any(project_ => project_.Contains("Test", StringComparison.OrdinalIgnoreCase)))
        {
            commands.Add("dotnet test");
        }
        else
        {
            commands.Add("dotnet test when explicitly allowed");
        }

        commands.Add("airepo self-check --repo . --skip-build-mcp");
        return commands;
    }

    private string SuggestCommitMessage(IReadOnlyList<string> files_, IReadOnlyList<string> risks_)
    {
        string area = risks_.FirstOrDefault() ?? "context";
        return $"Update {area} context analysis ({files_.Count} files)";
    }

    private static void Append(List<string> lines_, string title_, IReadOnlyList<string> values_)
    {
        lines_.Add(string.Empty);
        lines_.Add("## " + title_);
        lines_.AddRange(values_.Count == 0 ? ["- None"] : values_.Select(value_ => "- " + value_));
    }

    private static bool MatchesTarget(JsonObject symbol_, string target_)
    {
        if (string.IsNullOrWhiteSpace(target_))
        {
            return false;
        }

        return $"{GetString(symbol_, "Name")} {GetString(symbol_, "Namespace")} {GetString(symbol_, "File")}".Contains(target_, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject? ReadJson(string repoRoot_, string relativePath_)
    {
        string fullPath = Path.Combine(repoRoot_, relativePath_.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(fullPath)) as JsonObject;
        }
        catch
        {
            return null;
        }
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

    private static JsonArray GetArray(JsonObject? value_, string name_)
    {
        return value_ is not null && value_.TryGetPropertyValue(name_, out JsonNode? node) && node is JsonArray array ? array : [];
    }

    private static string GetString(JsonObject value_, string name_)
    {
        return value_.TryGetPropertyValue(name_, out JsonNode? node) ? node?.GetValue<string>() ?? string.Empty : string.Empty;
    }
}

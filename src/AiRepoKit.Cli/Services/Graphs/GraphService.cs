using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Models.ContextBudget;
using AiRepoKit.Cli.Models.Graphs;
using AiRepoKit.Cli.Services.ChangedFiles;
using AiRepoKit.Cli.Services.ContextBudget;

namespace AiRepoKit.Cli.Services.Graphs;

public sealed class GraphService
{
    private readonly ContextBudgeter budgeter = new();

    public GraphReport Build(string repoRoot_, string kind_, int limit_, int? budget_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        string kind = NormalizeKind(kind_);
        JsonObject? symbols = ReadJson(repoRoot, ".ai/generated/inventories/symbol-inventory.json");
        JsonObject? projects = ReadJson(repoRoot, ".ai/generated/inventories/project-inventory.json");
        JsonObject? references = ReadJson(repoRoot, ".ai/generated/inventories/project-references.json");
        ChangedFilesResult changed = new ChangedFilesService().GetChangedFiles(repoRoot);
        List<string> warnings = [.. changed.Warnings];

        (IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges) = kind switch
        {
            "project" => this.BuildProjectGraph(projects, references, limit_),
            "symbol" => this.BuildSymbolGraph(symbols, limit_),
            "risk" => this.BuildRiskGraph(symbols, changed, limit_),
            _ => ([], [])
        };

        BudgetResult<IReadOnlyList<GraphNode>> nodeBudget = this.budgeter.Apply(nodes, budget_, node_ => node_.Path + node_.Id, node_ => node_.Tags.Count + (node_.Kind == "project" ? 10 : 0));
        HashSet<string> selected = new(nodeBudget.Value.Select(node_ => node_.Id), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<GraphEdge> selectedEdges = edges.Where(edge_ => selected.Contains(edge_.From) && selected.Contains(edge_.To)).Take(limit_ * 3).ToArray();
        var budgetShape = new { Nodes = nodeBudget.Value, Edges = selectedEdges };
        BudgetResult<object> reportBudget = this.budgeter.Report((object)budgetShape, budget_, nodeBudget.Cuts);
        string summary = $"{kind} graph: {nodeBudget.Value.Count} nodes, {selectedEdges.Count} edges.";
        return new GraphReport(
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            repoRoot,
            kind,
            summary,
            nodeBudget.Value,
            selectedEdges,
            reportBudget.EstimatedTokens,
            reportBudget.Budget,
            reportBudget.Truncated,
            reportBudget.Cuts,
            warnings);
    }

    private (IReadOnlyList<GraphNode>, IReadOnlyList<GraphEdge>) BuildProjectGraph(JsonObject? projects_, JsonObject? references_, int limit_)
    {
        JsonArray projects = GetArray(projects_, "projects");
        JsonArray references = GetArray(references_, "references");
        List<GraphNode> nodes = [];
        foreach (JsonObject project in projects.OfType<JsonObject>().Take(limit_))
        {
            string path = GetString(project, "path");
            IReadOnlyList<string> frameworks = GetStringArray(project, "targetFrameworks");
            nodes.Add(new GraphNode(path, "project", Path.GetFileNameWithoutExtension(path), path, [.. frameworks, GuessProjectType(path)]));
        }

        List<GraphEdge> edges = [];
        foreach (JsonObject reference in references.OfType<JsonObject>().Take(limit_ * 3))
        {
            string from = FirstString(reference, "from", "Project", "project", "source");
            string to = FirstString(reference, "to", "Reference", "reference", "target");
            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
            {
                edges.Add(new GraphEdge(from, to, "project-reference"));
            }
        }

        return (nodes, edges);
    }

    private (IReadOnlyList<GraphNode>, IReadOnlyList<GraphEdge>) BuildSymbolGraph(JsonObject? symbols_, int limit_)
    {
        JsonArray symbols = GetArray(symbols_, "Symbols");
        List<GraphNode> nodes = [];
        List<GraphEdge> edges = [];
        Dictionary<string, string> projectNodes = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonObject symbol in symbols.OfType<JsonObject>().Take(limit_))
        {
            string name = GetString(symbol, "Name");
            string file = GetString(symbol, "File");
            string classification = GetString(symbol, "Classification");
            string id = $"{file}#{name}";
            string project = GuessProject(file);
            string[] tags = new[] { classification, GetString(symbol, "Kind") }.Where(value_ => !string.IsNullOrWhiteSpace(value_)).ToArray();
            nodes.Add(new GraphNode(id, "symbol", name, file, tags));
            if (!string.IsNullOrWhiteSpace(project))
            {
                if (!projectNodes.ContainsKey(project))
                {
                    projectNodes[project] = project;
                    nodes.Add(new GraphNode(project, "project", Path.GetFileNameWithoutExtension(project), project, [GuessProjectType(project)]));
                }

                edges.Add(new GraphEdge(project, id, "contains-symbol"));
            }
        }

        return (nodes, edges);
    }

    private (IReadOnlyList<GraphNode>, IReadOnlyList<GraphEdge>) BuildRiskGraph(JsonObject? symbols_, ChangedFilesResult changed_, int limit_)
    {
        JsonArray symbols = GetArray(symbols_, "Symbols");
        HashSet<string> changedFiles = new(changed_.Files.Select(file_ => file_.Path), StringComparer.OrdinalIgnoreCase);
        List<GraphNode> nodes = [];
        foreach (JsonObject symbol in symbols.OfType<JsonObject>())
        {
            string file = GetString(symbol, "File");
            List<string> tags = this.GetRiskTags(symbol, file, changedFiles);
            if (tags.Count == 0)
            {
                continue;
            }

            nodes.Add(new GraphNode($"{file}#{GetString(symbol, "Name")}", "risk", GetString(symbol, "Name"), file, tags));
            if (nodes.Count >= limit_)
            {
                break;
            }
        }

        foreach (ChangedFileItem file in changed_.Files.Where(file_ => !nodes.Any(node_ => node_.Path.Equals(file_.Path, StringComparison.OrdinalIgnoreCase))).Take(limit_ - nodes.Count))
        {
            nodes.Add(new GraphNode(file.Path, "risk", Path.GetFileName(file.Path), file.Path, ["changed-file"]));
        }

        return (nodes, []);
    }

    private List<string> GetRiskTags(JsonObject symbol_, string file_, HashSet<string> changedFiles_)
    {
        List<string> tags = [];
        string name = GetString(symbol_, "Name");
        string classification = GetString(symbol_, "Classification");
        string combined = $"{name} {classification} {file_}";
        if (changedFiles_.Contains(file_))
        {
            tags.Add("changed-file");
        }

        if (GetString(symbol_, "Visibility").Equals("public", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("public-api");
        }

        if (file_.Contains(".g.", StringComparison.OrdinalIgnoreCase) || file_.Contains("Generated", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("generated-code");
        }

        if (combined.Contains("Repository", StringComparison.OrdinalIgnoreCase) || combined.Contains("DbContext", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("data-layer");
        }

        if (combined.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("oracle");
        }

        if (combined.Contains("Controller", StringComparison.OrdinalIgnoreCase) || combined.Contains("Middleware", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("aspnet-core");
        }

        if (combined.Contains("Generator", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("source-generator");
        }

        if (classification.Equals("Configuration", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("configuration");
        }

        if (combined.Contains("Auth", StringComparison.OrdinalIgnoreCase) || combined.Contains("Secret", StringComparison.OrdinalIgnoreCase) || combined.Contains("Token", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("security-sensitive");
        }

        if (file_.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("test");
        }

        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Write(string repoRoot_, GraphReport report_, string format_)
    {
        string output = Path.Combine(repoRoot_, ".ai", "generated", "graphs");
        Directory.CreateDirectory(output);
        string prefix = report_.Kind + "-graph";
        if (format_ is "json" or "all")
        {
            File.WriteAllText(Path.Combine(output, prefix + ".json"), JsonSerializer.Serialize(report_, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (format_ is "markdown" or "all")
        {
            File.WriteAllText(Path.Combine(output, prefix + ".md"), ToMarkdown(report_));
        }
    }

    public static string ToMarkdown(GraphReport report_)
    {
        List<string> lines =
        [
            $"# {report_.Kind} Graph",
            string.Empty,
            report_.Summary,
            string.Empty,
            $"Estimated tokens: `{report_.EstimatedTokens}`",
            $"Budget: `{(report_.Budget?.ToString() ?? "none")}`",
            $"Truncated: `{report_.Truncated}`",
            string.Empty,
            "## Nodes"
        ];
        lines.AddRange(report_.Nodes.Select(node_ => $"- {node_.Label} [{node_.Kind}] `{node_.Path}` tags={string.Join(",", node_.Tags)}"));
        lines.Add(string.Empty);
        lines.Add("## Edges");
        lines.AddRange(report_.Edges.Count == 0 ? ["- None"] : report_.Edges.Select(edge_ => $"- `{edge_.From}` -> `{edge_.To}` [{edge_.Kind}]"));
        if (report_.Cuts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Cuts");
            lines.AddRange(report_.Cuts.Select(cut_ => $"- `{cut_.Path}`: {cut_.Reason} ({cut_.RemovedEstimatedTokens} tokens)"));
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private static string NormalizeKind(string value_)
    {
        string value = string.IsNullOrWhiteSpace(value_) ? "project" : value_.ToLowerInvariant();
        return value is "project" or "symbol" or "risk" ? value : throw new InvalidOperationException("Graph kind must be project, symbol, or risk.");
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

    private static string GuessProjectType(string path_)
    {
        string path = path_.ToLowerInvariant();
        if (path.Contains("test"))
        {
            return "test";
        }

        if (path.Contains("web") || path.Contains("api"))
        {
            return "aspnet-core";
        }

        return "dotnet";
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

    private static IReadOnlyList<string> GetStringArray(JsonObject value_, string name_)
    {
        return GetArray(value_, name_).Select(node_ => node_?.GetValue<string>() ?? string.Empty).Where(value_ => !string.IsNullOrWhiteSpace(value_)).ToArray();
    }
}

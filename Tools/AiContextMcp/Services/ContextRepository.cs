using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepo.ContextMcp.Models;

namespace AiRepo.ContextMcp.Services;

public sealed record ContextRepositoryOptions(string RepoRoot);

public sealed record RepositoryResourceDescriptor(string Uri, string Name, string Description, string MimeType);

public sealed record RepositoryPromptDescriptor(string Name, string Description);

public sealed class ContextRepository
{
    public const string SafeRepoRoot = "<repo-root>";
    public const string SafeLogFile = "<temp>/ai-repo-context-mcp.log";

    private static readonly RepositoryResourceDescriptor[] ResourceDescriptors =
    [
        new("repo://brief", "Repository brief", "Compact repository overview and generated inventory summary.", "application/json"),
        new("repo://health", "MCP health", "Server capability, artifact, client, strict stdio, and budget summary.", "application/json"),
        new("repo://policy", "MCP policy", "Read-only safety policy, allowed root, restricted paths, and logging defaults.", "application/json"),
        new("repo://context/changed-files", "Changed files context", "Bounded changed-files context pack for local review.", "application/json"),
        new("repo://context/review-risk", "Review risk context", "Bounded review-risk context pack when generated.", "application/json"),
        new("repo://context/test-generation", "Test generation context", "Bounded test-generation context pack when generated.", "application/json"),
        new("repo://graph/dependencies", "Dependency graph", "Bounded generated dependency graph summary.", "application/json"),
        new("repo://impact/current", "Current impact", "Bounded generated impact report summary.", "application/json"),
        new("repo://org/report", "Organization report", "Bounded generated organization report summary.", "application/json")
    ];

    private static readonly RepositoryPromptDescriptor[] PromptDescriptors =
    [
        new("ai-repo.help", "Compact AI.RepoKit MCP help and low-token workflow reference."),
        new("ai-repo.tutorial-en", "Short English tutorial for using ai_repo_context efficiently."),
        new("ai-repo.tutorial-pt", "Tutorial curto em portugues para usar ai_repo_context com eficiencia."),
        new("ai-repo.token-efficiency-check", "Estimate MCP payload tokens versus broad file inspection and report savings."),
        new("ai-repo.review-risk", "Review changed code using MCP context before direct file inspection."),
        new("ai-repo.changed-files-review", "Review the current changed-files context with focused follow-up searches."),
        new("ai-repo.generate-tests", "Plan and generate tests from bounded context first."),
        new("ai-repo.before-commit", "Run a low-token pre-commit readiness check."),
        new("ai-repo.implementation-plan", "Create an implementation plan from MCP context before editing."),
        new("ai-repo.release-check", "Check release readiness without tagging, pushing, uploading, or releasing."),
        new("ai-repo.workflow.feature-implementation", "MCP-first workflow for narrow feature implementation."),
        new("ai-repo.workflow.bug-fix", "MCP-first workflow for focused bug fixes."),
        new("ai-repo.workflow.before-commit", "MCP-first workflow for pre-commit readiness without committing."),
        new("ai-repo.workflow.release-preparation", "MCP-first workflow for release preparation without release actions."),
        new("ai-repo.workflow.test-generation", "MCP-first workflow for proportional test generation."),
        new("ai-repo.workflow.architecture-review", "MCP-first workflow for compact architecture review."),
        new("ai-repo.workflow.migration-planning", "MCP-first workflow for migration planning without database mutation.")
    ];

    private static readonly string[] SupportedContextKinds =
    [
        "all",
        "packages",
        "security",
        "symbols",
        "endpoints",
        "context-pack",
        "context-packs",
        "changed-files",
        "graph",
        "impact",
        "org-scan",
        "org-report",
        "efficiency"
    ];

    private readonly ContextRepositoryOptions _options;
    private readonly SecretRedactor _redactor;
    private ContextManifest? _manifest;

    public ContextRepository(ContextRepositoryOptions options_, SecretRedactor redactor_)
    {
        this._options = options_;
        this._redactor = redactor_;
    }

    public string RepoRoot => this._options.RepoRoot;

    public ContextManifest GetManifest()
    {
        if (this._manifest is not null)
        {
            return this._manifest;
        }

        string preferred = Path.Combine(this.RepoRoot, ".ai", "manifests", "mcp-context-manifest.json");
        string fallback = Path.Combine(this.RepoRoot, ".ai", "mcp-context-manifest.json");
        string path = File.Exists(preferred) ? preferred : fallback;
        if (!File.Exists(path))
        {
            this._manifest = new ContextManifest();
            return this._manifest;
        }

        using FileStream stream = File.OpenRead(path);
        this._manifest = JsonSerializer.Deserialize<ContextManifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ContextManifest();
        return this._manifest;
    }

    public ContextBudget Budget()
    {
        return new ContextBudget(this.GetManifest().Budgets);
    }

    public IReadOnlyList<string> AllowedFiles()
    {
        return this.GetManifest().AllowedContextFiles
            .Where(path_ => this.TryResolveAllowedFile(path_, out _))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> SupportedKinds()
    {
        return SupportedContextKinds;
    }

    public IReadOnlyList<RepositoryResourceDescriptor> KnownResources()
    {
        return ResourceDescriptors;
    }

    public IReadOnlyList<RepositoryPromptDescriptor> KnownPrompts()
    {
        return PromptDescriptors;
    }

    public IReadOnlyList<string> ToolNames()
    {
        return
        [
            "get_repo_brief",
            "get_health",
            "get_policy",
            "get_context",
            "search_context"
        ];
    }

    public object Envelope(object data_)
    {
        object redactedPayload = this.RedactPayload(data_);
        return this.Budget().Envelope(redactedPayload, true);
    }

    public object RedactPayload(object data_)
    {
        string json = JsonSerializer.Serialize(data_);
        string redacted = this._redactor.Redact(json);
        return JsonNode.Parse(redacted) ?? new JsonObject();
    }

    public IReadOnlyList<string> GeneratedArtifactPaths()
    {
        return
        [
            ".ai/generated/inventories/symbol-inventory.json",
            ".ai/generated/inventories/endpoint-inventory.json",
            ".ai/generated/context-packs/changed-files.json",
            ".ai/generated/graphs",
            ".ai/generated/reports/impact-report.json",
            ".ai/generated/reports/org-scan.json",
            ".ai/generated/reports/org-report.json",
            ".ai/generated/reports/org-efficiency.json"
        ];
    }

    public object GetGeneratedArtifactStatus()
    {
        IReadOnlyList<string> artifacts = this.GeneratedArtifactPaths();
        return new
        {
            available = artifacts.Where(this.ArtifactExists).ToArray(),
            missing = artifacts.Where(path_ => !this.ArtifactExists(path_)).ToArray()
        };
    }

    public object GetClientConfigStatus()
    {
        string[] paths =
        [
            ".codex/config.toml",
            ".vscode/mcp.json",
            ".mcp.json",
            ".vs/mcp.json",
            ".ai/client-configs/codex.config.toml",
            ".ai/client-configs/visualstudio-mcp.snippet.json"
        ];
        return paths.Select(path_ => new
        {
            path = path_,
            exists = File.Exists(Path.Combine(this.RepoRoot, path_.Replace('/', Path.DirectorySeparatorChar)))
        }).ToArray();
    }

    public object ReadResourceObject(string uri_)
    {
        object data = uri_ switch
        {
            "repo://brief" => new
            {
                this.GetManifest().RepoName,
                this.GetManifest().MainSolution,
                this.GetManifest().SchemaVersion,
                Detail = "brief",
                Inventory = this.GetInventorySummary("resource: repo://brief"),
                AllowedFiles = this.AllowedFiles().Take(this.Budget().Options.ArrayDefaultLimit).ToArray()
            },
            "repo://health" => this.GetCapabilities(),
            "repo://policy" => this.GetPolicyObject("all"),
            "repo://context/changed-files" => this.ReadContextObject("changed-files", ContextDetail.Brief, this.Budget().Options.ArrayDefaultLimit),
            "repo://context/review-risk" => this.ReadContextObject("context-packs", ContextDetail.Brief, this.Budget().Options.ArrayDefaultLimit, "review-risk"),
            "repo://context/test-generation" => this.ReadContextObject("context-packs", ContextDetail.Brief, this.Budget().Options.ArrayDefaultLimit, "test-generation"),
            "repo://graph/dependencies" => this.ReadContextObject("graph", ContextDetail.Brief, this.Budget().Options.ArrayDefaultLimit, null, "dependencies"),
            "repo://impact/current" => this.ReadContextObject("impact", ContextDetail.Brief, this.Budget().Options.ArrayDefaultLimit),
            "repo://org/report" => this.ReadContextObject("org-report", ContextDetail.Brief, this.Budget().Options.ArrayDefaultLimit),
            _ => ToolError.Create(
                "RESOURCE_NOT_FOUND",
                "Requested MCP resource URI is not supported.",
                string.Empty,
                true,
                new { requestedUri = uri_, resourceUris = this.KnownResources().Select(resource_ => resource_.Uri).ToArray() })
        };

        return data is ToolError ? this.RedactPayload(data) : this.Envelope(data);
    }

    public object GetCapabilities(string serverVersion_ = "unknown")
    {
        return new
        {
            ok = true,
            serverVersion = serverVersion_,
            toolVersion = serverVersion_,
            repoRoot = SafeRepoRoot,
            toolsAvailable = this.ToolNames(),
            supportedContextKinds = this.SupportedKinds(),
            generatedArtifacts = this.GetGeneratedArtifactStatus(),
            supportedPolicies = new[] { "read-only", "strict-stdio", "secrets-redaction", "bounded-responses" },
            readOnlyMode = true,
            maxRecommendedDetail = "brief",
            defaultTokenBudgets = this.Budget().Options,
            supportedClients = this.GetClientConfigStatus(),
            transport = "stdio",
            strictStdio = new
            {
                stdoutReservedForMcp = true,
                stderrDefault = false,
                stderrWhen = "--debug or --verbose",
                logFileDefault = SafeLogFile
            },
            resources = true,
            prompts = true,
            resourcesSupported = true,
            promptsSupported = true,
            resourceUris = this.KnownResources().Select(resource_ => resource_.Uri).ToArray(),
            promptNames = this.KnownPrompts().Select(prompt_ => prompt_.Name).ToArray()
        };
    }

    public object GetPolicyObject(string topic_)
    {
        ContextBudget budget = this.Budget();
        ContextManifest manifest = this.GetManifest();
        return new
        {
            topic = topic_,
            serverMode = "read-only",
            fileWrite = false,
            commandExecution = false,
            databaseAccess = false,
            networkAccess = false,
            secretsRedaction = true,
            allowedRoots = new[] { SafeRepoRoot },
            deniedPaths = manifest.RestrictedPaths,
            generatedArtifactPaths = this.GeneratedArtifactPaths(),
            safeSuggestedCommands = new
            {
                maySuggest = true,
                mayExecute = false,
                requireUserPermission = true
            },
            readOnlyFirst = true,
            stdioOnly = true,
            stdoutReservedForMcp = true,
            logs = new
            {
                defaultPath = SafeLogFile,
                stderrDefault = false,
                stderrWhen = "--debug or --verbose"
            },
            secretsExposed = false,
            secretValuesReturned = false,
            redactedOnly = true,
            budgets = budget.Options,
            restrictedPaths = manifest.RestrictedPaths
        };
    }

    public IReadOnlyDictionary<string, string> ReadContext(ContextDetail detail_)
    {
        return this.ReadContext(null, detail_, null);
    }

    public IReadOnlyDictionary<string, string> ReadContext(string? kind_, ContextDetail detail_, int? limit_)
    {
        ContextBudget budget = this.Budget();
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        int combined = 0;
        int limit = Math.Clamp(limit_ ?? budget.Options.ArrayDefaultLimit, 1, budget.Options.ArrayHardLimit);
        foreach (string relativePath in this.AllowedFiles().Where(path_ => MatchesKind(path_, kind_)).Take(limit))
        {
            if (!this.TryResolveAllowedFile(relativePath, out string fullPath))
            {
                continue;
            }

            FileInfo file = new(fullPath);
            if (file.Length > budget.Options.FileReadBytes)
            {
                continue;
            }

            string content = this._redactor.Redact(File.ReadAllText(fullPath));
            string trimmed = budget.Trim(content, detail_);
            int size = Encoding.UTF8.GetByteCount(trimmed);
            if (combined + size > budget.Options.CombinedBytes)
            {
                break;
            }

            result[relativePath] = trimmed;
            combined += size;
        }

        return result;
    }

    public object ReadContextObject(string? kind_, ContextDetail detail_, int? limit_, string? task_ = null, string? target_ = null)
    {
        if (!this.IsSupportedContextKind(kind_))
        {
            return ToolError.Create(
                "INVALID_CONTEXT_KIND",
                "Requested context kind is not supported.",
                string.Empty,
                true,
                new { requestedKind = kind_, supportedKinds = this.SupportedKinds() });
        }

        if (string.Equals(kind_, "symbols", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadSymbols(detail_, limit_);
        }

        if (string.Equals(kind_, "endpoints", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadEndpoints(detail_, limit_);
        }

        if (string.Equals(kind_, "context-pack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind_, "context-packs", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadContextPacks(detail_, limit_, task_, target_);
        }

        if (string.Equals(kind_, "changed-files", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadChangedFiles(detail_, limit_);
        }

        if (string.Equals(kind_, "graph", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadGraphs(detail_, limit_, target_);
        }

        if (string.Equals(kind_, "impact", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadImpact(detail_, limit_);
        }

        if (string.Equals(kind_, "org-scan", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadGeneratedReport(".ai/generated/reports/org-scan.json", "Run `airepo org scan --apply` to persist an org scan report.", "airepo org scan --apply", detail_, limit_);
        }

        if (string.Equals(kind_, "org-report", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadGeneratedReport(".ai/generated/reports/org-report.json", "Run `airepo org report --apply` to persist an org report.", "airepo org report --apply", detail_, limit_);
        }

        if (string.Equals(kind_, "efficiency", StringComparison.OrdinalIgnoreCase))
        {
            return this.ReadGeneratedReport(".ai/generated/reports/org-efficiency.json", "Run `airepo org efficiency --apply` to persist an org efficiency report.", "airepo org efficiency --apply", detail_, limit_);
        }

        IReadOnlyDictionary<string, string> context = this.ReadContext(kind_, detail_, limit_);
        if (context.Count == 0)
        {
            return ToolError.Create(
                "CONTEXT_NOT_FOUND",
                "No matching repository context files were found.",
                "airepo bootstrap --mcp --apply",
                true,
                new { requestedKind = string.IsNullOrWhiteSpace(kind_) ? "all" : kind_ });
        }

        return context;
    }

    private object ReadGeneratedReport(string relativePath_, string message_, string suggestedCommand_, ContextDetail detail_, int? limit_)
    {
        JsonObject? report = this.ReadGeneratedJson(relativePath_, ".ai/generated/reports");
        if (report is null)
        {
            return ToolError.Create(
                relativePath_.Contains("org-", StringComparison.OrdinalIgnoreCase) ? "ORG_REPORT_NOT_GENERATED" : "CONTEXT_NOT_FOUND",
                message_,
                suggestedCommand_,
                true,
                new { artifact = relativePath_ });
        }

        int limit = Math.Clamp(limit_ ?? this.Budget().Options.ArrayDefaultLimit, 1, this.Budget().Options.ArrayHardLimit);
        JsonArray repositories = GetArray(report, "Repositories");
        object data = detail_ == ContextDetail.Brief
            ? new
            {
                available = true,
                root = GetString(report, "Root"),
                generatedAtLocal = GetString(report, "GeneratedAtLocal"),
                repositoryCount = repositories.Count,
                repositories = repositories.Take(limit).ToArray(),
                warnings = GetStringArray(report, "Warnings").Take(limit).ToArray(),
                estimatedSizeBytes = EstimateSize(report),
                tokenCostHint = "brief"
            }
            : new
            {
                available = true,
                report,
                estimatedSizeBytes = EstimateSize(report),
                tokenCostHint = "compact"
            };
        return data;
    }

    private object ReadChangedFiles(ContextDetail detail_, int? limit_)
    {
        JsonObject? pack = this.ReadGeneratedJson(".ai/generated/context-packs/changed-files.json", ".ai/generated/context-packs");
        if (pack is null)
        {
            return ToolError.Create(
                "CONTEXT_PACK_NOT_GENERATED",
                "Changed-files context pack was not generated.",
                "airepo context-pack --task changed-files --apply",
                true,
                new { artifact = ".ai/generated/context-packs/changed-files.json" });
        }

        int limit = Math.Clamp(limit_ ?? this.Budget().Options.ArrayDefaultLimit, 1, this.Budget().Options.ArrayHardLimit);
        object data = detail_ == ContextDetail.Brief
            ? new
            {
                available = true,
                task = GetString(pack, "Task"),
                summary = GetString(pack, "Summary"),
                stagedFiles = GetArray(pack, "StagedFiles").Take(limit).ToArray(),
                unstagedFiles = GetArray(pack, "UnstagedFiles").Take(limit).ToArray(),
                untrackedFiles = GetArray(pack, "UntrackedFiles").Take(limit).ToArray(),
                estimatedTokens = GetInt(pack, "EstimatedTokens"),
                budget = GetInt(pack, "Budget"),
                truncated = GetBool(pack, "Truncated")
            }
            : new
            {
                available = true,
                pack = ProjectContextPackCompact(pack, limit),
                stagedFiles = GetArray(pack, "StagedFiles").Take(limit).ToArray(),
                unstagedFiles = GetArray(pack, "UnstagedFiles").Take(limit).ToArray(),
                untrackedFiles = GetArray(pack, "UntrackedFiles").Take(limit).ToArray(),
                affectedProjects = GetStringArray(pack, "AffectedProjects").Take(limit).ToArray(),
                affectedSymbols = GetStringArray(pack, "AffectedSymbols").Take(limit).ToArray(),
                estimatedTokens = GetInt(pack, "EstimatedTokens"),
                budget = GetInt(pack, "Budget"),
                truncated = GetBool(pack, "Truncated")
            };
        return data;
    }

    private object ReadGraphs(ContextDetail detail_, int? limit_, string? graph_)
    {
        string directory = Path.Combine(this.RepoRoot, ".ai", "generated", "graphs");
        if (!Directory.Exists(directory))
        {
            return ToolError.Create(
                "GRAPH_NOT_GENERATED",
                "Graph artifacts were not generated.",
                "airepo graph --apply",
                true,
                new { artifactRoot = ".ai/generated/graphs" });
        }

        int limit = Math.Clamp(limit_ ?? this.Budget().Options.ArrayDefaultLimit, 1, this.Budget().Options.ArrayHardLimit);
        List<object> graphs = [];
        foreach (string file in Directory.GetFiles(directory, "*-graph.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            JsonObject? graph = this.ReadGeneratedJsonFromFullPath(file, ".ai/generated/graphs");
            if (graph is null)
            {
                continue;
            }

            string kind = GetString(graph, "Kind");
            if (!string.IsNullOrWhiteSpace(graph_) && !kind.Equals(graph_, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            graphs.Add(detail_ == ContextDetail.Brief
                ? new
                {
                    kind,
                    summary = GetString(graph, "Summary"),
                    nodes = GetArray(graph, "Nodes").Count,
                    edges = GetArray(graph, "Edges").Count,
                    estimatedTokens = GetInt(graph, "EstimatedTokens"),
                    budget = GetInt(graph, "Budget"),
                    truncated = GetBool(graph, "Truncated")
                }
                : new
                {
                    kind,
                    summary = GetString(graph, "Summary"),
                    nodes = GetArray(graph, "Nodes").Take(limit).ToArray(),
                    edges = GetArray(graph, "Edges").Take(limit).ToArray(),
                    estimatedTokens = GetInt(graph, "EstimatedTokens"),
                    budget = GetInt(graph, "Budget"),
                    truncated = GetBool(graph, "Truncated")
                });
        }

        return new { available = graphs.Count > 0, graphs, estimatedSizeBytes = EstimateSize(graphs), tokenCostHint = detail_ == ContextDetail.Brief ? "brief" : "compact" };
    }

    private object ReadImpact(ContextDetail detail_, int? limit_)
    {
        JsonObject? impact = this.ReadGeneratedJson(".ai/generated/reports/impact-report.json", ".ai/generated/reports");
        if (impact is null)
        {
            return ToolError.Create(
                "CONTEXT_NOT_FOUND",
                "Impact report was not generated.",
                "airepo impact --apply",
                true,
                new { artifact = ".ai/generated/reports/impact-report.json" });
        }

        int limit = Math.Clamp(limit_ ?? this.Budget().Options.ArrayDefaultLimit, 1, this.Budget().Options.ArrayHardLimit);
        return new
        {
            available = true,
            summary = GetString(impact, "Summary"),
            changedFiles = GetArray(impact, "ChangedFiles").Take(limit).ToArray(),
            affectedProjects = GetStringArray(impact, "AffectedProjects").Take(limit).ToArray(),
            affectedSymbols = detail_ == ContextDetail.Brief ? Array.Empty<string>() : GetStringArray(impact, "AffectedSymbols").Take(limit).ToArray(),
            risks = GetStringArray(impact, "Risks").Take(limit).ToArray(),
            validationCommands = GetStringArray(impact, "ValidationCommands").Take(limit).ToArray(),
            estimatedTokens = GetInt(impact, "EstimatedTokens"),
            budget = GetInt(impact, "Budget"),
            truncated = GetBool(impact, "Truncated"),
            estimatedSizeBytes = EstimateSize(impact),
            tokenCostHint = detail_ == ContextDetail.Brief ? "brief" : "compact"
        };
    }

    private object ReadContextPacks(ContextDetail detail_, int? limit_, string? task_, string? target_)
    {
        ContextBudget budget = this.Budget();
        int limit = Math.Clamp(limit_ ?? budget.Options.ArrayDefaultLimit, 1, budget.Options.ArrayHardLimit);
        string directory = Path.Combine(this.RepoRoot, ".ai", "generated", "context-packs");
        if (!Directory.Exists(directory))
        {
            return ToolError.Create(
                "CONTEXT_PACK_NOT_GENERATED",
                "Context pack artifacts were not generated.",
                "airepo context-pack --apply",
                true,
                new { artifactRoot = ".ai/generated/context-packs" });
        }

        List<object> packs = [];
        foreach (string file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!this.TryResolveGeneratedContextPack(file, out string fullPath))
            {
                continue;
            }

            JsonObject? pack = this.ReadJsonObjectFromFullPath(fullPath);
            if (pack is null || !MatchesContextPack(pack, task_, target_))
            {
                continue;
            }

            packs.Add(detail_ == ContextDetail.Brief ? ProjectContextPackBrief(pack) : ProjectContextPackCompact(pack, budget.Options.ArrayDefaultLimit));
            if (packs.Count >= limit)
            {
                break;
            }
        }

        return new
        {
            available = packs.Count > 0,
            packs,
            estimatedSizeBytes = EstimateSize(packs),
            tokenCostHint = detail_ == ContextDetail.Brief ? "brief" : "compact"
        };
    }

    public object GetInventorySummary(string? taskHint_)
    {
        JsonObject? symbols = this.ReadFirstJsonObject(".ai/generated/inventories/symbol-inventory.json", ".ai/symbol-inventory.json");
        JsonObject? endpoints = this.ReadFirstJsonObject(".ai/generated/inventories/endpoint-inventory.json", ".ai/endpoint-inventory.json");
        bool symbolAvailable = symbols is not null;
        bool endpointAvailable = endpoints is not null;
        IReadOnlyList<object> topClassifications = symbolAvailable ? GetClassificationCounts(GetArray(symbols!, "Symbols")).Take(8).ToArray() : [];
        List<string> suggestions = [];
        string hint = taskHint_ ?? string.Empty;
        if (hint.Contains("UI", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("Blazor", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("API", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("endpoint", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("service", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("handler", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("get_context symbols brief");
        }

        if (hint.Contains("API", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("endpoint", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("controller", StringComparison.OrdinalIgnoreCase)
            || hint.Contains("route", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("get_context endpoints brief");
        }

        return new
        {
            symbolInventoryAvailable = symbolAvailable,
            endpointInventoryAvailable = endpointAvailable,
            symbolIndexer = symbolAvailable ? GetString(symbols!, "Indexer") : string.Empty,
            endpointIndexer = endpointAvailable ? GetString(endpoints!, "Indexer") : string.Empty,
            symbolCount = symbolAvailable ? GetInt(symbols!, "TotalSymbols") : 0,
            endpointCount = endpointAvailable ? GetInt(endpoints!, "TotalEndpoints") : 0,
            topClassifications,
            suggestedContext = suggestions
        };
    }

    private object ReadSymbols(ContextDetail detail_, int? limit_)
    {
        ContextBudget budget = this.Budget();
        int limit = Math.Clamp(limit_ ?? budget.Options.ArrayDefaultLimit, 1, budget.Options.ArrayHardLimit);
        JsonObject? inventory = this.ReadFirstJsonObject(".ai/generated/inventories/symbol-inventory.json", ".ai/symbol-inventory.json");
        if (inventory is null)
        {
            return ToolError.Create(
                "CONTEXT_NOT_FOUND",
                "Symbol inventory was not generated.",
                "airepo code-index --apply",
                true,
                new { artifact = ".ai/generated/inventories/symbol-inventory.json" });
        }

        JsonArray symbols = GetArray(inventory, "Symbols");
        IReadOnlyList<object> counts = GetClassificationCounts(symbols).Take(16).ToArray();
        IReadOnlyList<object> topSymbols = symbols
            .OfType<JsonObject>()
            .Take(limit)
            .Select(symbol_ => ProjectSymbol(symbol_, detail_))
            .ToArray();
        IReadOnlyList<string> sourceFiles = symbols
            .OfType<JsonObject>()
            .Select(symbol_ => GetString(symbol_, "File"))
            .Where(file_ => !string.IsNullOrWhiteSpace(file_))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
        object data = new
        {
            TotalFilesScanned = GetInt(inventory, "TotalFilesScanned"),
            TotalSymbols = GetInt(inventory, "TotalSymbols"),
            Indexer = GetString(inventory, "Indexer"),
            Truncated = GetBool(inventory, "Truncated"),
            ClassificationCounts = counts,
            Symbols = topSymbols,
            sourceFiles,
            estimatedSizeBytes = EstimateSize(topSymbols),
            tokenCostHint = detail_ == ContextDetail.Brief ? "brief" : "compact"
        };
        return data;
    }

    private object ReadEndpoints(ContextDetail detail_, int? limit_)
    {
        ContextBudget budget = this.Budget();
        int limit = Math.Clamp(limit_ ?? budget.Options.ArrayDefaultLimit, 1, budget.Options.ArrayHardLimit);
        JsonObject? inventory = this.ReadFirstJsonObject(".ai/generated/inventories/endpoint-inventory.json", ".ai/endpoint-inventory.json");
        if (inventory is null)
        {
            return ToolError.Create(
                "CONTEXT_NOT_FOUND",
                "Endpoint inventory was not generated.",
                "airepo code-index --apply",
                true,
                new { artifact = ".ai/generated/inventories/endpoint-inventory.json" });
        }

        JsonArray endpoints = GetArray(inventory, "Endpoints");
        IReadOnlyList<object> selected = endpoints
            .OfType<JsonObject>()
            .Take(limit)
            .Select(endpoint_ => detail_ == ContextDetail.Brief
                ? (object)new
                {
                    Method = GetString(endpoint_, "Method"),
                    Route = GetString(endpoint_, "Route"),
                    HandlerOrController = GetString(endpoint_, "HandlerOrController"),
                    File = GetString(endpoint_, "File")
                }
                : (object)new
                {
                    Method = GetString(endpoint_, "Method"),
                    Route = GetString(endpoint_, "Route"),
                    HandlerOrController = GetString(endpoint_, "HandlerOrController"),
                    SourceKind = GetString(endpoint_, "SourceKind"),
                    File = GetString(endpoint_, "File"),
                    Line = GetInt(endpoint_, "Line"),
                    Preview = LimitText(GetString(endpoint_, "Preview"), budget.Options.PreviewChars)
                })
            .ToArray();
        IReadOnlyList<string> sourceFiles = endpoints
            .OfType<JsonObject>()
            .Select(endpoint_ => GetString(endpoint_, "File"))
            .Where(file_ => !string.IsNullOrWhiteSpace(file_))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
        return new
        {
            TotalEndpoints = GetInt(inventory, "TotalEndpoints"),
            Indexer = GetString(inventory, "Indexer"),
            Endpoints = selected,
            sourceFiles,
            estimatedSizeBytes = EstimateSize(selected),
            tokenCostHint = detail_ == ContextDetail.Brief ? "brief" : "compact"
        };
    }

    public IReadOnlyList<object> Search(string query_, int? limit_)
    {
        ContextBudget budget = this.Budget();
        int limit = Math.Clamp(limit_ ?? budget.Options.SearchDefaultLimit, 1, budget.Options.SearchHardLimit);
        List<object> matches = [];
        foreach (KeyValuePair<string, string> file in this.ReadContext(ContextDetail.Full))
        {
            foreach (string line in file.Value.Split(Environment.NewLine))
            {
                if (line.Contains(query_, StringComparison.OrdinalIgnoreCase))
                {
                    string redacted = this._redactor.Redact(line);
                    string preview = redacted.Length <= budget.Options.PreviewChars ? redacted : redacted[..budget.Options.PreviewChars];
                    matches.Add(new { file = file.Key, preview });
                    if (matches.Count >= limit)
                    {
                        return matches;
                    }
                }
            }
        }

        foreach (string relativePath in this.GetGeneratedSearchFiles())
        {
            JsonObject? json = this.ReadGeneratedJson(relativePath, Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? ".ai/generated");
            if (json is null)
            {
                continue;
            }

            string text = this._redactor.Redact(JsonSerializer.Serialize(json));
            if (text.Contains(query_, StringComparison.OrdinalIgnoreCase))
            {
                string preview = CreateSearchPreview(text, budget.Options.PreviewChars);
                matches.Add(new { file = relativePath, preview });
                if (matches.Count >= limit)
                {
                    return matches;
                }
            }
        }

        return matches;
    }

    private static string CreateSearchPreview(string text_, int maxChars_)
    {
        if (text_.Length <= maxChars_)
        {
            return text_;
        }

        int placeholderIndex = text_.IndexOf("<repo-root>", StringComparison.Ordinal);
        if (placeholderIndex < 0)
        {
            placeholderIndex = text_.IndexOf("<temp>", StringComparison.Ordinal);
        }

        if (placeholderIndex < 0)
        {
            return text_[..maxChars_];
        }

        int start = Math.Max(0, placeholderIndex - (maxChars_ / 3));
        string prefix = start > 0 ? "..." : string.Empty;
        int length = Math.Min(maxChars_ - prefix.Length, text_.Length - start);
        return prefix + text_.Substring(start, length);
    }

    private IReadOnlyList<string> GetGeneratedSearchFiles()
    {
        List<string> files = [];
        string[] roots =
        [
            ".ai/generated/graphs",
            ".ai/generated/context-packs",
            ".ai/generated/reports"
        ];
        foreach (string root in roots)
        {
            string fullRoot = Path.Combine(this.RepoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullRoot))
            {
                continue;
            }

            files.AddRange(Directory.GetFiles(fullRoot, "*.json", SearchOption.TopDirectoryOnly)
                .Select(path_ => Path.GetRelativePath(this.RepoRoot, path_).Replace('\\', '/'))
                .Where(path_ => path_.StartsWith(".ai/generated/context-packs/", StringComparison.OrdinalIgnoreCase)
                    || path_.Contains("graph", StringComparison.OrdinalIgnoreCase)
                    || path_.Contains("impact", StringComparison.OrdinalIgnoreCase)
                    || path_.Contains("org-scan", StringComparison.OrdinalIgnoreCase)
                    || path_.Contains("org-report", StringComparison.OrdinalIgnoreCase)
                    || path_.Contains("org-efficiency", StringComparison.OrdinalIgnoreCase)));
        }

        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private bool ArtifactExists(string relativePath_)
    {
        string fullPath = Path.Combine(this.RepoRoot, relativePath_.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    private bool IsSupportedContextKind(string? kind_)
    {
        return string.IsNullOrWhiteSpace(kind_)
            || SupportedContextKinds.Contains(kind_, StringComparer.OrdinalIgnoreCase);
    }

    private bool TryResolveAllowedFile(string relativePath_, out string fullPath_)
    {
        fullPath_ = string.Empty;
        string normalized = relativePath_.Replace('\\', '/').TrimStart('/');
        if (this.IsRestricted(normalized))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(Path.Combine(this.RepoRoot, normalized));
        string root = Path.GetFullPath(this.RepoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return false;
        }

        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            return false;
        }

        fullPath_ = fullPath;
        return true;
    }

    private bool IsRestricted(string relativePath_)
    {
        string fileName = Path.GetFileName(relativePath_);
        foreach (string path in this.GetManifest().RestrictedPaths)
        {
            string value = path.Replace('\\', '/').Trim('/');
            if (value.Contains('*', StringComparison.Ordinal))
            {
                string regex = "^" + System.Text.RegularExpressions.Regex.Escape(value).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(fileName, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            else if (relativePath_.Equals(value, StringComparison.OrdinalIgnoreCase)
                || relativePath_.StartsWith(value + "/", StringComparison.OrdinalIgnoreCase)
                || relativePath_.Contains("/" + value + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesKind(string relativePath_, string? kind_)
    {
        if (string.IsNullOrWhiteSpace(kind_) || string.Equals(kind_, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string path = relativePath_.Replace('\\', '/');
        string fileName = Path.GetFileName(path);
        return kind_.ToLowerInvariant() switch
        {
            "packages" => path.Contains("update-package", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("build-profile.md", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("sdk-profile.md", StringComparison.OrdinalIgnoreCase),
            "security" => path.Contains("inspect-security", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("ai-operating-rules.md", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("automation-risks.md", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("context-budget.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("mcp-context-manifest.md", StringComparison.OrdinalIgnoreCase),
            "symbols" => fileName.Equals("symbol-inventory.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("symbol-inventory.md", StringComparison.OrdinalIgnoreCase),
            "endpoints" => fileName.Equals("endpoint-inventory.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("endpoint-inventory.md", StringComparison.OrdinalIgnoreCase),
            "org-scan" => fileName.Equals("org-scan.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("org-scan.md", StringComparison.OrdinalIgnoreCase),
            "org-report" => fileName.Equals("org-report.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("org-report.md", StringComparison.OrdinalIgnoreCase),
            "efficiency" => fileName.Equals("org-efficiency.json", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("org-efficiency.md", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private JsonObject? ReadJsonObject(string relativePath_)
    {
        string normalized = relativePath_.Replace('\\', '/').TrimStart('/');
        if (!this.TryResolveAllowedFile(normalized, out string fullPath))
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

    private JsonObject? ReadJsonObjectFromFullPath(string fullPath_)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(fullPath_)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private JsonObject? ReadGeneratedJson(string relativePath_, string allowedRoot_)
    {
        string fullPath = Path.GetFullPath(Path.Combine(this.RepoRoot, relativePath_.Replace('/', Path.DirectorySeparatorChar)));
        return this.ReadGeneratedJsonFromFullPath(fullPath, allowedRoot_);
    }

    private JsonObject? ReadGeneratedJsonFromFullPath(string fullPath_, string allowedRoot_)
    {
        try
        {
            string fullPath = Path.GetFullPath(fullPath_);
            string root = Path.GetFullPath(Path.Combine(this.RepoRoot, allowedRoot_.Replace('/', Path.DirectorySeparatorChar))).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                return null;
            }

            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                return null;
            }

            return JsonNode.Parse(File.ReadAllText(fullPath)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private bool TryResolveGeneratedContextPack(string path_, out string fullPath_)
    {
        fullPath_ = string.Empty;
        string fullPath = Path.GetFullPath(path_);
        string root = Path.GetFullPath(Path.Combine(this.RepoRoot, ".ai", "generated", "context-packs")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return false;
        }

        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            return false;
        }

        fullPath_ = fullPath;
        return true;
    }

    private JsonObject? ReadFirstJsonObject(params string[] relativePaths_)
    {
        foreach (string relativePath in relativePaths_)
        {
            JsonObject? value = this.ReadJsonObject(relativePath);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static object ProjectSymbol(JsonObject symbol_, ContextDetail detail_)
    {
        object[] methods = detail_ == ContextDetail.Brief
            ? []
            : GetArray(symbol_, "Methods").OfType<JsonObject>().Take(8).Select(method_ => new
            {
                Name = GetString(method_, "Name"),
                ReturnType = GetString(method_, "ReturnType")
            }).ToArray();
        object[] properties = detail_ == ContextDetail.Brief
            ? []
            : GetArray(symbol_, "Properties").OfType<JsonObject>().Take(8).Select(property_ => new
            {
                Name = GetString(property_, "Name"),
                Type = GetString(property_, "Type")
            }).ToArray();
        return new
        {
            Name = GetString(symbol_, "Name"),
            Kind = GetString(symbol_, "Kind"),
            Namespace = GetString(symbol_, "Namespace"),
            File = GetString(symbol_, "File"),
            Line = GetInt(symbol_, "Line"),
            BaseTypes = GetStringArray(symbol_, "BaseTypes").Take(8).ToArray(),
            Attributes = GetStringArray(symbol_, "Attributes").Take(8).ToArray(),
            Classification = GetString(symbol_, "Classification"),
            Methods = methods,
            Properties = properties
        };
    }

    private static object ProjectContextPackBrief(JsonObject pack_)
    {
        return new
        {
            Task = GetString(pack_, "Task"),
            Target = GetString(pack_, "Target"),
            Summary = GetString(pack_, "Summary"),
            TokenBudgetHint = GetString(pack_, "TokenBudgetHint"),
            SuggestedMcpCalls = GetStringArray(pack_, "SuggestedMcpCalls").Take(8).ToArray()
        };
    }

    private static object ProjectContextPackCompact(JsonObject pack_, int itemLimit_)
    {
        return new
        {
            GeneratedAtLocal = GetString(pack_, "GeneratedAtLocal"),
            Task = GetString(pack_, "Task"),
            Target = GetString(pack_, "Target"),
            RecommendedAgent = GetString(pack_, "RecommendedAgent"),
            TokenBudgetHint = GetString(pack_, "TokenBudgetHint"),
            Summary = GetString(pack_, "Summary"),
            LikelyFiles = GetArray(pack_, "LikelyFiles").Take(itemLimit_).ToArray(),
            RelevantSymbols = GetArray(pack_, "RelevantSymbols").Take(itemLimit_).ToArray(),
            RelevantEndpoints = GetArray(pack_, "RelevantEndpoints").Take(itemLimit_).ToArray(),
            RelevantPackages = GetArray(pack_, "RelevantPackages").Take(itemLimit_).ToArray(),
            RiskAreas = GetStringArray(pack_, "RiskAreas").Take(itemLimit_).ToArray(),
            ValidationCommands = GetStringArray(pack_, "ValidationCommands").Take(itemLimit_).ToArray(),
            SuggestedMcpCalls = GetStringArray(pack_, "SuggestedMcpCalls").Take(itemLimit_).ToArray(),
            Notes = GetStringArray(pack_, "Notes").Take(itemLimit_).ToArray()
        };
    }

    private static bool MatchesContextPack(JsonObject pack_, string? task_, string? target_)
    {
        if (!string.IsNullOrWhiteSpace(task_) && !GetString(pack_, "Task").Equals(task_, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(target_))
        {
            string target = GetString(pack_, "Target");
            string summary = GetString(pack_, "Summary");
            return target.Contains(target_, StringComparison.OrdinalIgnoreCase)
                || summary.Contains(target_, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static IReadOnlyList<object> GetClassificationCounts(JsonArray symbols_)
    {
        return symbols_
            .OfType<JsonObject>()
            .Select(symbol_ => GetString(symbol_, "Classification"))
            .Where(value_ => !string.IsNullOrWhiteSpace(value_))
            .GroupBy(value_ => value_, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group_ => group_.Count())
            .ThenBy(group_ => group_.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group_ => new { Classification = group_.Key, Count = group_.Count() })
            .Cast<object>()
            .ToArray();
    }

    private static JsonArray GetArray(JsonObject value_, string name_)
    {
        return value_.TryGetPropertyValue(name_, out JsonNode? node) && node is JsonArray array ? array : [];
    }

    private static string GetString(JsonObject value_, string name_)
    {
        return value_.TryGetPropertyValue(name_, out JsonNode? node) ? node?.GetValue<string>() ?? string.Empty : string.Empty;
    }

    private static IReadOnlyList<string> GetStringArray(JsonObject value_, string name_)
    {
        return GetArray(value_, name_).Select(node_ => node_?.GetValue<string>() ?? string.Empty).Where(value_ => !string.IsNullOrWhiteSpace(value_)).ToArray();
    }

    private static int GetInt(JsonObject value_, string name_)
    {
        if (!value_.TryGetPropertyValue(name_, out JsonNode? node) || node is null)
        {
            return 0;
        }

        return node.GetValueKind() == JsonValueKind.Number && node.GetValue<int>() is int number ? number : 0;
    }

    private static bool GetBool(JsonObject value_, string name_)
    {
        return value_.TryGetPropertyValue(name_, out JsonNode? node) && node is not null && node.GetValueKind() == JsonValueKind.True;
    }

    private static int EstimateSize(object value_)
    {
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value_));
    }

    private static string LimitText(string value_, int max_)
    {
        if (value_.Length <= max_)
        {
            return value_;
        }

        return value_[..max_];
    }
}

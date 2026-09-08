using System.IO.Enumeration;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Models.CodeIndex;
using AiRepoKit.Cli.Models.ContextPacks;
using AiRepoKit.Cli.Models.Graphs;
using AiRepoKit.Cli.Models.Impact;
using AiRepoKit.Cli.Models.SpecContexts;
using AiRepoKit.Cli.Services.ChangedFiles;
using AiRepoKit.Cli.Services.CodeIndex;
using AiRepoKit.Cli.Services.ContextPacks;
using AiRepoKit.Cli.Services.Graphs;
using AiRepoKit.Cli.Services.Impact;
using AiRepoKit.Spec.Context;

namespace AiRepoKit.Cli.Services.SpecContexts;

public sealed class RepositoryEvidenceCollector :
    IRepositoryEvidenceCollector
{
    private const int BoundedTextLength = 240;

    private static readonly string[] CacheContentReasons =
    [
        "current C# file count differs from cache file count",
        "current C# file missing from cache",
        "cached C# file size or last-write metadata differs",
        "cached C# file no longer exists"
    ];

    private readonly IContextPackSelectionService _contextPackSelectionService;
    private readonly ICodeIndexFreshnessService _codeIndexFreshnessService;
    private readonly ChangedFilesService _changedFilesService;
    private readonly GraphService _graphService;
    private readonly ImpactService _impactService;
    private readonly FileSystemService _fileSystemService;

    public RepositoryEvidenceCollector()
        : this(
            new ContextPackSelectionService(),
            new CodeIndexFreshnessService(),
            new ChangedFilesService(),
            new GraphService(),
            new ImpactService(),
            new FileSystemService())
    {
    }

    internal RepositoryEvidenceCollector(
        IContextPackSelectionService contextPackSelectionService_,
        ICodeIndexFreshnessService codeIndexFreshnessService_,
        ChangedFilesService changedFilesService_,
        GraphService graphService_,
        ImpactService impactService_,
        FileSystemService fileSystemService_)
    {
        this._contextPackSelectionService =
            contextPackSelectionService_ ??
            throw new ArgumentNullException(nameof(contextPackSelectionService_));
        this._codeIndexFreshnessService =
            codeIndexFreshnessService_ ??
            throw new ArgumentNullException(nameof(codeIndexFreshnessService_));
        this._changedFilesService =
            changedFilesService_ ??
            throw new ArgumentNullException(nameof(changedFilesService_));
        this._graphService =
            graphService_ ??
            throw new ArgumentNullException(nameof(graphService_));
        this._impactService =
            impactService_ ??
            throw new ArgumentNullException(nameof(impactService_));
        this._fileSystemService =
            fileSystemService_ ??
            throw new ArgumentNullException(nameof(fileSystemService_));
    }

    public RepositoryEvidenceCollection Collect(
        RepositoryEvidenceCollectionRequest request_)
    {
        ArgumentNullException.ThrowIfNull(request_);
        if (string.IsNullOrWhiteSpace(request_.RepoRoot))
        {
            throw new ArgumentException(
                "Repository root must not be blank.",
                nameof(request_));
        }

        if (request_.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request_),
                "Limit must be between 1 and 100 inclusive.");
        }

        if (request_.MaxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request_),
                "Maximum files must be greater than zero.");
        }

        string repoRoot = Path.GetFullPath(request_.RepoRoot);
        List<RepositoryEvidence> evidence = [];
        List<SpecContextReference> candidates = [];

        KnownJson buildSummary = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/reports/latest-build-summary.json");
        KnownJson cache = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/cache/code-index-cache.json");
        KnownJson endpointInventory = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/inventories/endpoint-inventory.json");
        KnownJson packageInventory = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/inventories/package-inventory.json");
        KnownJson projectInventory = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/inventories/project-inventory.json");
        KnownJson projectReferences = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/inventories/project-references.json");
        KnownJson symbolInventory = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/inventories/symbol-inventory.json");
        KnownJson manifest = this.ReadKnownJson(
            repoRoot,
            ".ai/manifests/mcp-context-manifest.json");
        KnownJson secretScan = this.ReadKnownJson(
            repoRoot,
            ".ai/generated/reports/secret-scan-report.json");

        IReadOnlyList<string> restrictedPaths =
            this.GetRestrictedPaths(manifest);
        ContextPackInventoryCompatibility compatibility;
        try
        {
            compatibility =
                this._contextPackSelectionService.GetInventoryCompatibility(repoRoot);
        }
        catch
        {
            compatibility = new ContextPackInventoryCompatibility(false, false);
        }

        CodeIndexFreshnessResult? freshness = null;
        try
        {
            freshness =
                this._codeIndexFreshnessService.Check(
                    repoRoot,
                    request_.MaxFiles);
        }
        catch
        {
        }

        RepositoryEvidence cacheEvidence =
            this.CreateCacheEvidence(cache, freshness);
        RepositoryEvidence symbolEvidence =
            this.CreateCodeInventoryEvidence(
                "code-inventory:symbol",
                ".ai/generated/inventories/symbol-inventory.json",
                symbolInventory,
                compatibility.SymbolCompatible,
                cacheEvidence);
        RepositoryEvidence endpointEvidence =
            this.CreateCodeInventoryEvidence(
                "code-inventory:endpoint",
                ".ai/generated/inventories/endpoint-inventory.json",
                endpointInventory,
                compatibility.EndpointCompatible,
                cacheEvidence);

        evidence.Add(this.CreateSimpleEvidence(
            "build-summary",
            "build-summary",
            "report",
            ".ai/generated/reports/latest-build-summary.json",
            buildSummary,
            RepositoryEvidenceFreshness.Unknown));
        evidence.Add(cacheEvidence);
        evidence.Add(endpointEvidence);
        evidence.Add(this.CreateSimpleEvidence(
            "code-inventory:package",
            "repository-inventory",
            "inventory",
            ".ai/generated/inventories/package-inventory.json",
            packageInventory,
            RepositoryEvidenceFreshness.Unknown));
        evidence.Add(this.CreateSimpleEvidence(
            "code-inventory:project",
            "repository-inventory",
            "inventory",
            ".ai/generated/inventories/project-inventory.json",
            projectInventory,
            RepositoryEvidenceFreshness.Unknown));
        evidence.Add(this.CreateSimpleEvidence(
            "code-inventory:project-references",
            "repository-inventory",
            "inventory",
            ".ai/generated/inventories/project-references.json",
            projectReferences,
            RepositoryEvidenceFreshness.Unknown));
        evidence.Add(symbolEvidence);

        this.AddChangedFiles(
            repoRoot,
            restrictedPaths,
            evidence,
            candidates);
        this.AddContextPacks(
            repoRoot,
            request_,
            restrictedPaths,
            symbolEvidence,
            endpointEvidence,
            evidence,
            candidates);
        this.AddGraphs(
            repoRoot,
            request_.Limit,
            restrictedPaths,
            symbolEvidence,
            projectInventory,
            projectReferences,
            evidence,
            candidates);
        this.AddImpact(
            repoRoot,
            request_,
            restrictedPaths,
            symbolEvidence,
            evidence,
            candidates);

        evidence.Add(this.CreatePolicyEvidence(manifest));
        evidence.Add(this.CreateSecretScanEvidence(secretScan));

        return new RepositoryEvidenceCollection(
            evidence
                .OrderBy(item_ => item_.EvidenceId, StringComparer.Ordinal)
                .ToArray(),
            candidates
                .OrderBy(item_ => item_.EvidenceId, StringComparer.Ordinal)
                .ThenBy(item_ => item_.Kind, StringComparer.Ordinal)
                .ThenBy(item_ => item_.Reference, StringComparer.Ordinal)
                .ThenByDescending(item_ => item_.Priority)
                .ToArray());
    }

    private RepositoryEvidence CreateCacheEvidence(
        KnownJson cache_,
        CodeIndexFreshnessResult? freshness_)
    {
        const string evidenceId = "code-index-cache";
        const string reference = ".ai/generated/cache/code-index-cache.json";
        if (!cache_.Exists)
        {
            return CreateEvidence(
                evidenceId,
                "code-index",
                "cache",
                reference,
                RepositoryEvidenceAvailability.Missing,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Source file is missing.");
        }

        if (!cache_.Parseable ||
            freshness_ is null ||
            freshness_.Reasons.Any(IsCacheReadFailure))
        {
            return CreateEvidence(
                evidenceId,
                "code-index",
                "cache",
                reference,
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                cache_.GeneratedAt,
                "Source file is unavailable or invalid.");
        }

        bool stale = freshness_.Reasons.Any(reason_ =>
            CacheContentReasons.Contains(reason_, StringComparer.Ordinal));
        return CreateEvidence(
            evidenceId,
            "code-index",
            "cache",
            reference,
            RepositoryEvidenceAvailability.Available,
            stale
                ? RepositoryEvidenceFreshness.Stale
                : RepositoryEvidenceFreshness.Current,
            cache_.GeneratedAt,
            stale
                ? "Shared code-index freshness check reports stale cache metadata."
                : "Shared code-index freshness check reports current cache metadata.");
    }

    private RepositoryEvidence CreateCodeInventoryEvidence(
        string evidenceId_,
        string reference_,
        KnownJson source_,
        bool compatible_,
        RepositoryEvidence cacheEvidence_)
    {
        if (!source_.Exists)
        {
            return CreateEvidence(
                evidenceId_,
                "repository-inventory",
                "inventory",
                reference_,
                RepositoryEvidenceAvailability.Missing,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Source file is missing.");
        }

        if (!source_.Parseable || !compatible_)
        {
            return CreateEvidence(
                evidenceId_,
                "repository-inventory",
                "inventory",
                reference_,
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                source_.GeneratedAt,
                source_.Parseable
                    ? "Inventory is incompatible with the structured context selector."
                    : "Source file is unavailable or invalid.");
        }

        RepositoryEvidenceFreshness freshness =
            cacheEvidence_.Availability == RepositoryEvidenceAvailability.Available
                ? cacheEvidence_.Freshness
                : RepositoryEvidenceFreshness.Unknown;
        return CreateEvidence(
            evidenceId_,
            "repository-inventory",
            "inventory",
            reference_,
            RepositoryEvidenceAvailability.Available,
            freshness,
            source_.GeneratedAt,
            "Inventory is compatible with the structured context selector.");
    }

    private RepositoryEvidence CreateSimpleEvidence(
        string evidenceId_,
        string sourceName_,
        string kind_,
        string reference_,
        KnownJson source_,
        RepositoryEvidenceFreshness availableFreshness_)
    {
        if (!source_.Exists)
        {
            return CreateEvidence(
                evidenceId_, sourceName_, kind_, reference_,
                RepositoryEvidenceAvailability.Missing,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Source file is missing.");
        }

        if (!source_.Parseable)
        {
            return CreateEvidence(
                evidenceId_, sourceName_, kind_, reference_,
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                source_.GeneratedAt,
                "Source file is unavailable or invalid.");
        }

        return CreateEvidence(
            evidenceId_, sourceName_, kind_, reference_,
            RepositoryEvidenceAvailability.Available,
            availableFreshness_,
            source_.GeneratedAt,
            "Structured source metadata is available.");
    }

    private RepositoryEvidence CreatePolicyEvidence(
        KnownJson manifest_)
    {
        return this.CreateSimpleEvidence(
            "repository-policy",
            "repository-policy",
            "policy",
            ".ai/manifests/mcp-context-manifest.json",
            manifest_,
            RepositoryEvidenceFreshness.NotApplicable);
    }

    private RepositoryEvidence CreateSecretScanEvidence(
        KnownJson source_)
    {
        const string evidenceId = "secret-scan";
        const string reference = ".ai/generated/reports/secret-scan-report.json";
        if (!source_.Exists)
        {
            return CreateEvidence(
                evidenceId, "secret-scan", "report", reference,
                RepositoryEvidenceAvailability.Missing,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Source file is missing.");
        }

        if (!source_.Parseable)
        {
            return CreateEvidence(
                evidenceId, "secret-scan", "report", reference,
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                source_.GeneratedAt,
                "Source file is unavailable or invalid.");
        }

        bool redactedOnly =
            TryGetBoolean(source_.Root, out bool value, "RedactedOnly", "redactedOnly") &&
            value;
        if (!redactedOnly)
        {
            return CreateEvidence(
                evidenceId, "secret-scan", "report", reference,
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                source_.GeneratedAt,
                "Secret-scan report is not marked redacted-only.");
        }

        int findingCount = GetSafeFindingCount(source_.Root);
        return CreateEvidence(
            evidenceId, "secret-scan", "report", reference,
            RepositoryEvidenceAvailability.Available,
            RepositoryEvidenceFreshness.Unknown,
            source_.GeneratedAt,
            $"Redacted secret-scan findings: {findingCount}.");
    }

    private void AddChangedFiles(
        string repoRoot_,
        IReadOnlyList<string> restrictedPaths_,
        List<RepositoryEvidence> evidence_,
        List<SpecContextReference> candidates_)
    {
        try
        {
            ChangedFilesResult changed =
                this._changedFilesService.GetChangedFiles(repoRoot_);
            evidence_.Add(CreateEvidence(
                "changed-files", "changed-files", "changed-files", "working-tree",
                RepositoryEvidenceAvailability.Available,
                RepositoryEvidenceFreshness.Current,
                string.Empty,
                $"Changed files observed: {changed.Files.Count}."));
            foreach (ChangedFileItem item in changed.Files)
            {
                string? reference =
                    this.AdmitPath(
                        repoRoot_,
                        item.Path,
                        restrictedPaths_);
                if (reference is null)
                {
                    continue;
                }

                int priority = item.Staged ? 100 : item.Unstaged ? 80 : item.Untracked ? 60 : 50;
                candidates_.Add(CreateReference(
                    "changed-files",
                    "file",
                    reference,
                    Bounded(item.Status, "changed") + ".",
                    priority,
                    "Changed file: "));
            }
        }
        catch
        {
            evidence_.Add(CreateEvidence(
                "changed-files", "changed-files", "changed-files", "working-tree",
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Changed-files observation is unavailable."));
        }
    }

    private void AddContextPacks(
        string repoRoot_,
        RepositoryEvidenceCollectionRequest request_,
        IReadOnlyList<string> restrictedPaths_,
        RepositoryEvidence symbolEvidence_,
        RepositoryEvidence endpointEvidence_,
        List<RepositoryEvidence> evidence_,
        List<SpecContextReference> candidates_)
    {
        string[] tasks = ["change-api", "review-risk", "test-generation"];
        foreach (string task in tasks)
        {
            string evidenceId = "context-pack:" + task;
            RepositoryEvidenceAvailability dependencyAvailability =
                CombineAvailability(symbolEvidence_, endpointEvidence_);
            if (dependencyAvailability != RepositoryEvidenceAvailability.Available)
            {
                evidence_.Add(CreateEvidence(
                    evidenceId, "context-pack", "context-pack", evidenceId,
                    dependencyAvailability,
                    RepositoryEvidenceFreshness.Unknown,
                    string.Empty,
                    "Required code inventories are not available."));
                continue;
            }

            try
            {
                ContextPackSelectionResult result =
                    this._contextPackSelectionService.Select(
                        new ContextPackRequest(
                            repoRoot_,
                            task,
                            request_.Target,
                            "json",
                            request_.Limit,
                            false,
                            false,
                            false,
                            false,
                            true,
                            0),
                        string.Empty);
                RepositoryEvidenceFreshness freshness =
                    CombineFreshness(symbolEvidence_.Freshness, endpointEvidence_.Freshness);
                evidence_.Add(CreateEvidence(
                    evidenceId, "context-pack", "context-pack", evidenceId,
                    RepositoryEvidenceAvailability.Available,
                    freshness,
                    string.Empty,
                    Bounded(result.Pack.Summary, "Structured context pack selected.")));
                this.AddPackCandidates(
                    repoRoot_,
                    restrictedPaths_,
                    evidenceId,
                    result.Pack,
                    candidates_);
            }
            catch
            {
                evidence_.Add(CreateEvidence(
                    evidenceId, "context-pack", "context-pack", evidenceId,
                    RepositoryEvidenceAvailability.Unavailable,
                    RepositoryEvidenceFreshness.Unknown,
                    string.Empty,
                    "Context-pack selection is unavailable."));
            }
        }
    }

    private void AddPackCandidates(
        string repoRoot_,
        IReadOnlyList<string> restrictedPaths_,
        string evidenceId_,
        ContextPack pack_,
        List<SpecContextReference> candidates_)
    {
        foreach (ContextPackItem item in pack_.LikelyFiles)
        {
            this.AddPathReference(
                repoRoot_, restrictedPaths_, candidates_, evidenceId_, "file",
                item.File, string.Empty, item.Reason, "Selected context-pack file.", item.Score);
        }

        foreach (ContextPackItem item in pack_.RelevantSymbols)
        {
            this.AddPathReference(
                repoRoot_, restrictedPaths_, candidates_, evidenceId_, "symbol",
                item.File, item.Name, item.Reason, "Selected relevant symbol.", item.Score);
        }

        foreach (ContextPackItem item in pack_.RelevantEndpoints)
        {
            this.AddPathReference(
                repoRoot_, restrictedPaths_, candidates_, evidenceId_, "endpoint",
                item.File, item.Name, item.Reason, "Selected relevant endpoint.", item.Score);
        }

        foreach (ContextPackItem item in pack_.RelevantPackages)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            candidates_.Add(CreateReference(
                evidenceId_,
                "package",
                Bounded(item.Name, string.Empty),
                Bounded(item.Reason, "Selected relevant package."),
                Math.Max(0, item.Score)));
        }
    }

    private void AddGraphs(
        string repoRoot_,
        int limit_,
        IReadOnlyList<string> restrictedPaths_,
        RepositoryEvidence symbolEvidence_,
        KnownJson projectInventory_,
        KnownJson projectReferences_,
        List<RepositoryEvidence> evidence_,
        List<SpecContextReference> candidates_)
    {
        RepositoryEvidence projectEvidence =
            this.CreateDerivedDependencyEvidence(
                "graph:project",
                "graph",
                "graph",
                "graph:project",
                projectInventory_,
                projectReferences_);
        if (projectEvidence.Availability == RepositoryEvidenceAvailability.Available)
        {
            try
            {
                GraphReport report = this._graphService.Build(repoRoot_, "project", limit_, null);
                projectEvidence = projectEvidence with { Detail = Bounded(report.Summary, "Project graph built.") };
                foreach (GraphNode node in report.Nodes)
                {
                    this.AddPathReference(
                        repoRoot_, restrictedPaths_, candidates_, "graph:project", "graph-project",
                        node.Path, string.Empty, "Project dependency graph node.",
                        "Project dependency graph node.", 65);
                }
            }
            catch
            {
                projectEvidence = projectEvidence with
                {
                    Availability = RepositoryEvidenceAvailability.Unavailable,
                    Freshness = RepositoryEvidenceFreshness.Unknown,
                    Detail = "Project graph is unavailable."
                };
            }
        }

        evidence_.Add(projectEvidence);

        RepositoryEvidence riskEvidence;
        if (symbolEvidence_.Availability != RepositoryEvidenceAvailability.Available)
        {
            riskEvidence = CreateEvidence(
                "graph:risk", "graph", "graph", "graph:risk",
                symbolEvidence_.Availability,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Required symbol inventory is not available.");
        }
        else
        {
            try
            {
                GraphReport report = this._graphService.Build(repoRoot_, "risk", limit_, null);
                riskEvidence = CreateEvidence(
                    "graph:risk", "graph", "graph", "graph:risk",
                    RepositoryEvidenceAvailability.Available,
                    symbolEvidence_.Freshness,
                    string.Empty,
                    Bounded(report.Summary, "Risk graph built."));
                foreach (GraphNode node in report.Nodes)
                {
                    this.AddPathReference(
                        repoRoot_, restrictedPaths_, candidates_, "graph:risk", "graph-risk",
                        node.Path, string.Empty, "Risk graph node.", "Risk graph node.", 85);
                }
            }
            catch
            {
                riskEvidence = CreateEvidence(
                    "graph:risk", "graph", "graph", "graph:risk",
                    RepositoryEvidenceAvailability.Unavailable,
                    RepositoryEvidenceFreshness.Unknown,
                    string.Empty,
                    "Risk graph is unavailable.");
            }
        }

        evidence_.Add(riskEvidence);
    }

    private void AddImpact(
        string repoRoot_,
        RepositoryEvidenceCollectionRequest request_,
        IReadOnlyList<string> restrictedPaths_,
        RepositoryEvidence symbolEvidence_,
        List<RepositoryEvidence> evidence_,
        List<SpecContextReference> candidates_)
    {
        if (symbolEvidence_.Availability != RepositoryEvidenceAvailability.Available)
        {
            evidence_.Add(CreateEvidence(
                "impact", "impact", "impact", "impact",
                symbolEvidence_.Availability,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Required symbol inventory is not available."));
            return;
        }

        try
        {
            ImpactReport report =
                this._impactService.Build(
                    repoRoot_,
                    request_.Target,
                    string.Empty,
                    request_.Limit,
                    null);
            evidence_.Add(CreateEvidence(
                "impact", "impact", "impact", "impact",
                RepositoryEvidenceAvailability.Available,
                symbolEvidence_.Freshness,
                string.Empty,
                "Structured impact analysis is available."));
            foreach (ChangedFileItem item in report.ChangedFiles)
            {
                this.AddPathReference(
                    repoRoot_, restrictedPaths_, candidates_, "impact", "file",
                    item.Path, string.Empty, "Changed file from impact analysis.",
                    "Changed file from impact analysis.", 90);
            }

            foreach (string project in report.AffectedProjects)
            {
                this.AddPathReference(
                    repoRoot_, restrictedPaths_, candidates_, "impact", "project",
                    project, string.Empty, "Affected project from impact analysis.",
                    "Affected project from impact analysis.", 75);
            }
        }
        catch
        {
            evidence_.Add(CreateEvidence(
                "impact", "impact", "impact", "impact",
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Impact analysis is unavailable."));
        }
    }

    private RepositoryEvidence CreateDerivedDependencyEvidence(
        string evidenceId_,
        string source_,
        string kind_,
        string reference_,
        params KnownJson[] dependencies_)
    {
        if (dependencies_.Any(dependency_ => !dependency_.Exists))
        {
            return CreateEvidence(
                evidenceId_, source_, kind_, reference_,
                RepositoryEvidenceAvailability.Missing,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Required structured source is missing.");
        }

        if (dependencies_.Any(dependency_ => !dependency_.Parseable))
        {
            return CreateEvidence(
                evidenceId_, source_, kind_, reference_,
                RepositoryEvidenceAvailability.Unavailable,
                RepositoryEvidenceFreshness.Unknown,
                string.Empty,
                "Required structured source is unavailable.");
        }

        return CreateEvidence(
            evidenceId_, source_, kind_, reference_,
            RepositoryEvidenceAvailability.Available,
            RepositoryEvidenceFreshness.Unknown,
            string.Empty,
            "Required structured sources are available.");
    }

    private void AddPathReference(
        string repoRoot_,
        IReadOnlyList<string> restrictedPaths_,
        List<SpecContextReference> candidates_,
        string evidenceId_,
        string kind_,
        string path_,
        string suffix_,
        string reason_,
        string fallbackReason_,
        int priority_)
    {
        string? path = this.AdmitPath(repoRoot_, path_, restrictedPaths_);
        if (path is null || (!string.IsNullOrEmpty(suffix_) && string.IsNullOrWhiteSpace(suffix_)))
        {
            return;
        }

        string reference =
            string.IsNullOrEmpty(suffix_)
                ? path
                : path + "#" + Bounded(suffix_, string.Empty);
        candidates_.Add(CreateReference(
            evidenceId_,
            kind_,
            reference,
            Bounded(reason_, fallbackReason_),
            Math.Max(0, priority_)));
    }

    private string? AdmitPath(
        string repoRoot_,
        string path_,
        IReadOnlyList<string> restrictedPaths_)
    {
        string normalized = (path_ ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            string fullPath = Path.GetFullPath(
                Path.Combine(
                    repoRoot_,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!this._fileSystemService.IsInsideRoot(repoRoot_, fullPath))
            {
                return null;
            }

            string relative =
                Path.GetRelativePath(repoRoot_, fullPath)
                    .Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative) ||
                relative == "." ||
                this._fileSystemService.IsRestrictedPath(relative) ||
                restrictedPaths_.Any(pattern_ => MatchesRestriction(relative, pattern_)))
            {
                return null;
            }

            return relative;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<string> GetRestrictedPaths(
        KnownJson manifest_)
    {
        if (!manifest_.Parseable || manifest_.Root is null)
        {
            return [];
        }

        JsonNode? node = GetProperty(manifest_.Root, "restrictedPaths", "RestrictedPaths");
        if (node is not JsonArray array)
        {
            return [];
        }

        return array
            .Where(item_ => item_ is not null && item_.GetValueKind() == JsonValueKind.String)
            .Select(item_ => item_!.GetValue<string>().Replace('\\', '/').Trim('/'))
            .Where(item_ => !string.IsNullOrWhiteSpace(item_))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private KnownJson ReadKnownJson(
        string repoRoot_,
        string relativePath_)
    {
        string path = Path.Combine(
            repoRoot_,
            relativePath_.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            JsonObject? root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null)
            {
                return new KnownJson(true, false, null, string.Empty);
            }

            return new KnownJson(
                true,
                true,
                root,
                GetString(root, "GeneratedAtLocal", "generatedAtLocal"));
        }
        catch (FileNotFoundException)
        {
            return new KnownJson(false, false, null, string.Empty);
        }
        catch (DirectoryNotFoundException)
        {
            return new KnownJson(false, false, null, string.Empty);
        }
        catch
        {
            return new KnownJson(true, false, null, string.Empty);
        }
    }

    private static bool MatchesRestriction(
        string relativePath_,
        string pattern_)
    {
        string path = relativePath_.Replace('\\', '/').Trim('/');
        string pattern = pattern_.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        bool hasGlob = pattern.Contains('*') || pattern.Contains('?');
        bool hasSlash = pattern.Contains('/');
        if (hasGlob && hasSlash)
        {
            return FileSystemName.MatchesSimpleExpression(pattern, path, ignoreCase: true);
        }

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (hasGlob)
        {
            return segments.Any(segment_ =>
                FileSystemName.MatchesSimpleExpression(pattern, segment_, ignoreCase: true));
        }

        if (hasSlash)
        {
            return path.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);
        }

        return segments.Any(segment_ => segment_.Equals(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static RepositoryEvidenceAvailability CombineAvailability(
        params RepositoryEvidence[] evidence_)
    {
        if (evidence_.Any(item_ => item_.Availability == RepositoryEvidenceAvailability.Missing))
        {
            return RepositoryEvidenceAvailability.Missing;
        }

        return evidence_.Any(item_ => item_.Availability == RepositoryEvidenceAvailability.Unavailable)
            ? RepositoryEvidenceAvailability.Unavailable
            : RepositoryEvidenceAvailability.Available;
    }

    private static RepositoryEvidenceFreshness CombineFreshness(
        params RepositoryEvidenceFreshness[] freshness_)
    {
        if (freshness_.Any(item_ => item_ == RepositoryEvidenceFreshness.Stale))
        {
            return RepositoryEvidenceFreshness.Stale;
        }

        return freshness_.All(item_ => item_ == RepositoryEvidenceFreshness.Current)
            ? RepositoryEvidenceFreshness.Current
            : RepositoryEvidenceFreshness.Unknown;
    }

    private static RepositoryEvidence CreateEvidence(
        string evidenceId_,
        string source_,
        string kind_,
        string reference_,
        RepositoryEvidenceAvailability availability_,
        RepositoryEvidenceFreshness freshness_,
        string generatedAt_,
        string detail_)
    {
        return new RepositoryEvidence
        {
            EvidenceId = evidenceId_,
            Source = source_,
            Kind = kind_,
            Reference = reference_,
            Availability = availability_,
            Freshness = freshness_,
            SourceGeneratedAt = Bounded(generatedAt_, string.Empty),
            Detail = Bounded(detail_, "Evidence state recorded.")
        };
    }

    private static SpecContextReference CreateReference(
        string evidenceId_,
        string kind_,
        string reference_,
        string reason_,
        int priority_,
        string reasonPrefix_ = "")
    {
        string reason = reasonPrefix_ + reason_;
        return new SpecContextReference
        {
            EvidenceId = evidenceId_,
            Kind = kind_,
            Reference = reference_,
            Reason = Bounded(reason, "Repository evidence reference."),
            Priority = Math.Max(0, priority_)
        };
    }

    private static bool IsCacheReadFailure(
        string reason_)
    {
        return reason_.StartsWith(
            "code-index cache could not be read: ",
            StringComparison.Ordinal);
    }

    private static int GetSafeFindingCount(
        JsonObject? root_)
    {
        JsonNode? count = GetProperty(root_, "FindingCount", "findingCount");
        if (count is JsonValue jsonValue &&
            count.GetValueKind() == JsonValueKind.Number &&
            jsonValue.TryGetValue<int>(out int value))
        {
            return Math.Max(0, value);
        }

        JsonNode? findings = GetProperty(root_, "Findings", "findings");
        return findings is JsonArray array ? array.Count : 0;
    }

    private static bool TryGetBoolean(
        JsonObject? root_,
        out bool value_,
        params string[] names_)
    {
        JsonNode? node = GetProperty(root_, names_);
        if (node is not null &&
            node.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            value_ = node.GetValue<bool>();
            return true;
        }

        value_ = false;
        return false;
    }

    private static string GetString(
        JsonObject? root_,
        params string[] names_)
    {
        JsonNode? node = GetProperty(root_, names_);
        return node is not null && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : string.Empty;
    }

    private static JsonNode? GetProperty(
        JsonObject? root_,
        params string[] names_)
    {
        if (root_ is null)
        {
            return null;
        }

        foreach (string name in names_)
        {
            if (root_.TryGetPropertyValue(name, out JsonNode? node))
            {
                return node;
            }
        }

        return null;
    }

    private static string Bounded(
        string? value_,
        string fallback_)
    {
        string value = string.IsNullOrWhiteSpace(value_) ? fallback_ : value_.Trim();
        return value.Length <= BoundedTextLength
            ? value
            : value[..BoundedTextLength];
    }

    private sealed record KnownJson(
        bool Exists,
        bool Parseable,
        JsonObject? Root,
        string GeneratedAt);
}

using ModelContextProtocol.Server;
using AiRepo.ContextMcp.Models;
using AiRepo.ContextMcp.Services;

namespace AiRepo.ContextMcp.Tools;

[McpServerToolType]
public sealed class RepositoryContextTools
{
    private readonly ContextRepository _repository;

    public RepositoryContextTools(ContextRepository repository_)
    {
        this._repository = repository_;
    }

    [McpServerTool(Name = "get_repo_brief")]
    public object GetRepoBrief(string? taskHint = null, string detail = "brief")
    {
        ContextDetail parsed = ParseDetail(detail);
        ContextManifest manifest = this._repository.GetManifest();
        object inventory = this._repository.GetInventorySummary(taskHint);
        object data = new
        {
            manifest.RepoName,
            manifest.MainSolution,
            manifest.SchemaVersion,
            TaskHint = taskHint ?? string.Empty,
            Detail = parsed.ToString().ToLowerInvariant(),
            Inventory = inventory,
            AllowedFiles = this._repository.AllowedFiles().Take(this._repository.Budget().Options.ArrayDefaultLimit).ToArray()
        };
        return this._repository.Budget().Envelope(data, true);
    }

    [McpServerTool(Name = "get_context")]
    public object GetContext(string? kind = null, string detail = "brief", int? limit = null, string? task = null, string? target = null)
    {
        ContextDetail parsed = ParseDetail(detail);
        object data = this._repository.ReadContextObject(kind, parsed, limit, task, target);
        return this._repository.Budget().Envelope(data, true);
    }

    [McpServerTool(Name = "get_health")]
    public object GetHealth(string area = "all")
    {
        ContextManifest manifest = this._repository.GetManifest();
        object data = new
        {
            ok = true,
            repoRoot = this._repository.RepoRoot,
            area,
            manifestSchema = manifest.SchemaVersion,
            allowedFileCount = this._repository.AllowedFiles().Count,
            transport = "stdio",
            http = false,
            resources = false,
            prompts = false,
            persistence = false
        };
        return this._repository.Budget().Envelope(data, true);
    }

    [McpServerTool(Name = "search_context")]
    public object SearchContext(string query, int? limit = null)
    {
        IReadOnlyList<object> data = this._repository.Search(query, limit);
        return this._repository.Budget().Envelope(data, true);
    }

    [McpServerTool(Name = "get_policy")]
    public object GetPolicy(string topic = "all")
    {
        ContextBudget budget = this._repository.Budget();
        object data = new
        {
            topic,
            readOnlyFirst = true,
            stdioOnly = true,
            stdoutReservedForMcp = true,
            logs = "stderr",
            secretsExposed = false,
            secretValuesReturned = false,
            redactedOnly = true,
            budgets = budget.Options,
            restrictedPaths = this._repository.GetManifest().RestrictedPaths
        };
        return budget.Envelope(data, true);
    }

    private static ContextDetail ParseDetail(string value_)
    {
        return value_.ToLowerInvariant() switch
        {
            "brief" => ContextDetail.Brief,
            "full" => ContextDetail.Full,
            _ => ContextDetail.Compact
        };
    }
}

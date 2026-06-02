using System.ComponentModel;
using System.Reflection;
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
    [Description("Repository overview and generated inventory summary. Use first for orientation; read-only and compact by default.")]
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
        return this._repository.Envelope(data);
    }

    [McpServerTool(Name = "get_context")]
    [Description("Read bounded repository context by kind. Use for known context artifacts; missing artifacts return structured read-only errors.")]
    public object GetContext(string? kind = null, string detail = "brief", int? limit = null, string? task = null, string? target = null)
    {
        ContextDetail parsed = ParseDetail(detail);
        object data = this._repository.ReadContextObject(kind, parsed, limit, task, target);
        return data is ToolError ? this._repository.RedactPayload(data) : this._repository.Envelope(data);
    }

    [McpServerTool(Name = "get_health")]
    [Description("Server health and capabilities. Use area=capabilities for supported kinds, artifacts, policy, budgets, and client config hints.")]
    public object GetHealth(string area = "all")
    {
        ContextManifest manifest = this._repository.GetManifest();
        object data = string.Equals(area, "capabilities", StringComparison.OrdinalIgnoreCase)
            ? this._repository.GetCapabilities(GetServerVersion())
            : new
        {
            ok = true,
            repoRoot = ContextRepository.SafeRepoRoot,
            area,
            manifestSchema = manifest.SchemaVersion,
            allowedFileCount = this._repository.AllowedFiles().Count,
            transport = "stdio",
            http = false,
            resources = true,
            prompts = true,
            resourcesSupported = true,
            promptsSupported = true,
            persistence = false
        };
        return this._repository.Envelope(data);
    }

    [McpServerTool(Name = "search_context")]
    [Description("Search bounded generated and allowed context. Use for targeted lookups; returns redacted previews with small default limits.")]
    public object SearchContext(string query, int? limit = null)
    {
        IReadOnlyList<object> data = this._repository.Search(query, limit);
        return this._repository.Envelope(data);
    }

    [McpServerTool(Name = "get_policy")]
    [Description("Read MCP safety policy. Use before suggesting commands or reading sensitive paths; no file writes or command execution.")]
    public object GetPolicy(string topic = "all")
    {
        object data = this._repository.GetPolicyObject(topic);
        return this._repository.Envelope(data);
    }

    private static string GetServerVersion()
    {
        Assembly assembly = typeof(RepositoryContextTools).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
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

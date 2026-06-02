using System.ComponentModel;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using AiRepo.ContextMcp.Services;
using ModelContextProtocol.Server;

namespace AiRepo.ContextMcp.Resources;

[McpServerResourceType]
public sealed class RepositoryContextResources
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly ContextRepository _repository;

    public RepositoryContextResources(ContextRepository repository_)
    {
        this._repository = repository_;
    }

    [McpServerResource(UriTemplate = "repo://brief", Name = "Repository brief", MimeType = "application/json")]
    [Description("Compact repository overview and generated inventory summary.")]
    public string Brief()
    {
        return this.ReadJson("repo://brief");
    }

    [McpServerResource(UriTemplate = "repo://health", Name = "MCP health", MimeType = "application/json")]
    [Description("Server capability, artifact, client, strict stdio, and budget summary.")]
    public string Health()
    {
        return this.ReadJson("repo://health");
    }

    [McpServerResource(UriTemplate = "repo://policy", Name = "MCP policy", MimeType = "application/json")]
    [Description("Read-only safety policy, allowed root, restricted paths, and logging defaults.")]
    public string Policy()
    {
        return this.ReadJson("repo://policy");
    }

    [McpServerResource(UriTemplate = "repo://context/changed-files", Name = "Changed files context", MimeType = "application/json")]
    [Description("Bounded changed-files context pack for local review.")]
    public string ChangedFiles()
    {
        return this.ReadJson("repo://context/changed-files");
    }

    [McpServerResource(UriTemplate = "repo://context/review-risk", Name = "Review risk context", MimeType = "application/json")]
    [Description("Bounded review-risk context pack when generated.")]
    public string ReviewRisk()
    {
        return this.ReadJson("repo://context/review-risk");
    }

    [McpServerResource(UriTemplate = "repo://context/test-generation", Name = "Test generation context", MimeType = "application/json")]
    [Description("Bounded test-generation context pack when generated.")]
    public string TestGeneration()
    {
        return this.ReadJson("repo://context/test-generation");
    }

    [McpServerResource(UriTemplate = "repo://graph/dependencies", Name = "Dependency graph", MimeType = "application/json")]
    [Description("Bounded generated dependency graph summary.")]
    public string DependencyGraph()
    {
        return this.ReadJson("repo://graph/dependencies");
    }

    [McpServerResource(UriTemplate = "repo://impact/current", Name = "Current impact", MimeType = "application/json")]
    [Description("Bounded generated impact report summary.")]
    public string CurrentImpact()
    {
        return this.ReadJson("repo://impact/current");
    }

    [McpServerResource(UriTemplate = "repo://org/report", Name = "Organization report", MimeType = "application/json")]
    [Description("Bounded generated organization report summary.")]
    public string OrgReport()
    {
        return this.ReadJson("repo://org/report");
    }

    private string ReadJson(string uri_)
    {
        object data = uri_ == "repo://health"
            ? this._repository.Envelope(this._repository.GetCapabilities(GetServerVersion()))
            : this._repository.ReadResourceObject(uri_);
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private static string GetServerVersion()
    {
        Assembly assembly = typeof(RepositoryContextResources).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}

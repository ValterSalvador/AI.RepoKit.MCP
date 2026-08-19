namespace AiRepoKit.Cli.Services.AiContextUpdate;

public sealed class AiContextUpdateOptions
{
    public string TargetFramework { get; init; } = "net10.0";

    public string McpServerName { get; init; } =
        "ai_repo_context";

    public string McpProjectRelativePath { get; init; } =
        "Tools/AiContextMcp/AiRepo.ContextMcp.csproj";
}

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>Real session factory that creates McpStdioSession instances.</summary>
internal sealed class McpStdioSessionFactory : IMcpSessionFactory
{
    public IMcpSession Create(string dllPath, string repoRoot, int startupTimeoutSeconds)
    {
        return McpStdioSession.Start(dllPath, repoRoot);
    }
}

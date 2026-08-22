using AiRepoKit.Cli.Services.McpLaunch;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>Real session factory that creates McpStdioSession instances.</summary>
internal sealed class McpStdioSessionFactory : IMcpSessionFactory
{
    public IMcpSession Create(McpServerLaunchSpec launchSpec_, int startupTimeoutSeconds_)
    {
        return McpStdioSession.Start(launchSpec_);
    }
}

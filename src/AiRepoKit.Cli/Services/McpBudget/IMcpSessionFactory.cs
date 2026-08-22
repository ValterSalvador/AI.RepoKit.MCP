using AiRepoKit.Cli.Services.McpLaunch;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Factory that creates MCP stdio sessions. Abstracted for deterministic unit testing
/// without spawning the real MCP process.
/// </summary>
internal interface IMcpSessionFactory
{
    /// <summary>
    /// Creates and starts a new MCP session using the given launch spec.
    /// The caller is responsible for disposing the returned session.
    /// </summary>
    IMcpSession Create(McpServerLaunchSpec launchSpec_, int startupTimeoutSeconds_);
}

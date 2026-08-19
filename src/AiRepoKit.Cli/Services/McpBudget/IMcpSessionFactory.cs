namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Factory that creates MCP stdio sessions. Abstracted for deterministic unit testing
/// without spawning the real MCP process.
/// </summary>
internal interface IMcpSessionFactory
{
    /// <summary>
    /// Creates and starts a new MCP session by launching the given DLL.
    /// The caller is responsible for disposing the returned session.
    /// </summary>
    IMcpSession Create(string dllPath, string repoRoot, int startupTimeoutSeconds);
}

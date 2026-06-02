using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public static class McpBuildFailureDiagnostics
{
    public const string LockedDllMessage = "MCP build failed because the Release DLL is currently in use. Close VS Code, Codex, Copilot Agent, or any MCP client using this repository, then retry.";
    public const string LockedDllHint = "Close MCP clients or rerun with --stop-stale-mcp-hosts to stop MCP hosts for this repository.";
    public const string LockedDllRetryHint = "Close Codex, VS Code, Visual Studio, or Copilot Agent and rerun.";

    public static bool IsLockedDllFailure(ProcessResult build_)
    {
        string output = $"{build_.StandardOutput}{Environment.NewLine}{build_.StandardError}";
        return Contains(output, "because it is being used by another process")
            || Contains(output, "cannot access the file")
            || Contains(output, "MSB3021")
            || Contains(output, "MSB3027")
            || Contains(output, "MSB3026")
            || Contains(output, "AiRepo.ContextMcp.dll");
    }

    private static bool Contains(string value_, string search_)
    {
        return value_.Contains(search_, StringComparison.OrdinalIgnoreCase);
    }
}

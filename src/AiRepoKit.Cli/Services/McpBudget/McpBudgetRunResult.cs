namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Result of a native MCP budget run. Contains the typed exit class
/// (preserving the 0/1/2 numeric contract) and the full structured report.
/// </summary>
public sealed record McpBudgetRunResult(
    McpBudgetExitClass ExitClass,
    McpBudgetReport Report)
{
    /// <summary>True only when ExitClass is Success (exit code 0).</summary>
    public bool IsSuccess => ExitClass == McpBudgetExitClass.Success;
}

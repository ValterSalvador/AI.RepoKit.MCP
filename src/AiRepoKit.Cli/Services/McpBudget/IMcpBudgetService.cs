namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Native C# MCP response-budget service. Replaces the PowerShell
/// MeasureMcpResponseBudget.ps1 runtime dependency for all product call sites.
/// </summary>
public interface IMcpBudgetService
{
    /// <summary>
    /// Runs the MCP budget validation for the given repository root and returns
    /// a structured result. Never throws; fatal errors are captured in the result.
    /// </summary>
    McpBudgetRunResult Run(string repoRoot, McpBudgetOptions? options = null);
}

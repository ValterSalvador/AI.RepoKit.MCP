namespace AiRepoKit.Cli.Models;

public sealed record ScriptDefinition(
    string Name,
    string? PowerShellRelativePath = null,
    string? BashRelativePath = null)
{
    public static ScriptDefinition McpBudget { get; } = new(
        "mcp-budget",
        PowerShellRelativePath: "Tools/AiContext/MeasureMcpResponseBudget.ps1",
        BashRelativePath: "Tools/AiContext/MeasureMcpResponseBudget.sh");
}

namespace AiRepoKit.Cli.Models.McpDiagnostics;

public sealed record McpSmokeTestResult(
    string Status,
    string Message,
    IReadOnlyList<string> Details,
    IReadOnlyList<string> ToolNames)
{
    public bool Success => Status is "Passed" or "Warning";
}

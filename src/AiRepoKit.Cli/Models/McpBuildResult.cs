namespace AiRepoKit.Cli.Models;

public sealed record McpBuildResult(
    string State,
    string Message,
    string DllPath,
    string? Hint = null,
    ProcessResult? Process = null)
{
    public bool Success => State is "Built" or "SkippedCurrent" or "SkippedLockedSmokePassed";
}

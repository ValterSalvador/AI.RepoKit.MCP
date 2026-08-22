namespace AiRepoKit.Cli.McpRuntime.Models;

public sealed record ToolEnvelope<T>(
    T Data,
    int EstimatedSizeBytes,
    string TokenCostHint,
    bool SecretsExposed,
    bool SecretValuesReturned,
    bool RedactedOnly);

public sealed record ToolError(
    bool Ok,
    string Code,
    string Message,
    string SuggestedCommand,
    bool SafeToRun,
    object Details)
{
    public static ToolError Create(string code_, string message_, string suggestedCommand_ = "", bool safeToRun_ = true, object? details_ = null)
    {
        return new ToolError(false, code_, message_, suggestedCommand_, safeToRun_, details_ ?? new { });
    }
}

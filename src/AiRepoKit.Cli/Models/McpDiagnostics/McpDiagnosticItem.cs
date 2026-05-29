namespace AiRepoKit.Cli.Models.McpDiagnostics;

public sealed record McpDiagnosticItem(
    string Name,
    string Status,
    bool Required,
    string Message,
    string? Hint = null,
    IReadOnlyList<string>? Details = null);

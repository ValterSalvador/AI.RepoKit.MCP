namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>Options for a single MCP budget run.</summary>
public sealed record McpBudgetOptions(
    int StartupTimeoutSeconds = 20,
    int ToolTimeoutSeconds = 30);

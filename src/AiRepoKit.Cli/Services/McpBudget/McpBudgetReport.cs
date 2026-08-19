using System.Text.Json.Serialization;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Top-level MCP budget report. Property names intentionally use PascalCase
/// to preserve JSON compatibility with EfficiencyCommand.ReadBudgetMetric
/// and any downstream consumers of mcp-budget-report.json.
/// </summary>
public sealed class McpBudgetReport
{
    /// <summary>Local timestamp in yyyy-MM-dd HH:mm:ss format.</summary>
    [JsonPropertyName("GeneratedAtLocal")]
    public required string GeneratedAtLocal { get; init; }

    /// <summary>Normalized full path to the repository root.</summary>
    [JsonPropertyName("RepoRoot")]
    public required string RepoRoot { get; init; }

    [JsonPropertyName("McpAssembly")]
    public required string McpAssembly { get; init; }

    [JsonPropertyName("McpAssemblyExists")]
    public required bool McpAssemblyExists { get; init; }

    [JsonPropertyName("Manifest")]
    public required string? Manifest { get; init; }

    [JsonPropertyName("ToolsListed")]
    public required IReadOnlyList<string> ToolsListed { get; init; }

    [JsonPropertyName("Results")]
    public required IReadOnlyList<McpBudgetCallResult> Results { get; init; }

    [JsonPropertyName("Passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("Failures")]
    public required IReadOnlyList<string> Failures { get; init; }

    [JsonPropertyName("Warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("StderrLineCount")]
    public required int StderrLineCount { get; init; }

    [JsonPropertyName("StdoutLineCount")]
    public required int StdoutLineCount { get; init; }
}

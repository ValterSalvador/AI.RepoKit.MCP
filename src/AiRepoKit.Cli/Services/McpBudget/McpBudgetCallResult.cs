using System.Text.Json.Serialization;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Per-call result written into the MCP budget report.
/// Property names intentionally use PascalCase to match the JSON contract
/// established by MeasureMcpResponseBudget.ps1 and read by EfficiencyCommand.
/// </summary>
public sealed class McpBudgetCallResult
{
    [JsonPropertyName("Name")]
    public required string Name { get; init; }

    [JsonPropertyName("Success")]
    public required bool Success { get; init; }

    /// <summary>UTF-8 byte count of the raw JSON-RPC response line from stdout.</summary>
    [JsonPropertyName("SizeBytes")]
    public required int SizeBytes { get; init; }

    [JsonPropertyName("BudgetBytes")]
    public required int BudgetBytes { get; init; }

    [JsonPropertyName("TokenCostHint")]
    public required string TokenCostHint { get; init; }

    [JsonPropertyName("EstimatedSizeBytes")]
    public required int EstimatedSizeBytes { get; init; }

    [JsonPropertyName("HasRawLogs")]
    public required bool HasRawLogs { get; init; }

    [JsonPropertyName("HasSecretValueExposure")]
    public required bool HasSecretValueExposure { get; init; }

    [JsonPropertyName("HasRedactionMarker")]
    public required bool HasRedactionMarker { get; init; }

    [JsonPropertyName("SecretsExposed")]
    public required bool SecretsExposed { get; init; }

    [JsonPropertyName("SecretValuesReturned")]
    public required bool SecretValuesReturned { get; init; }

    [JsonPropertyName("RedactedOnly")]
    public required bool RedactedOnly { get; init; }

    [JsonPropertyName("Passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("Notes")]
    public required IReadOnlyList<string> Notes { get; init; }
}

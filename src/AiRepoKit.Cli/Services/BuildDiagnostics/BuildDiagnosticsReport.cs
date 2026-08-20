using System.Text.Json.Serialization;

namespace AiRepoKit.Cli.Services.BuildDiagnostics;

public sealed class BuildDiagnosticsReport
{
    [JsonPropertyName("generatedAtLocal")]
    public required string GeneratedAtLocal { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("restoreExitCode")]
    public int RestoreExitCode { get; init; }

    [JsonPropertyName("buildExitCode")]
    public int BuildExitCode { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(
        Condition =
            JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    [JsonPropertyName("restoreOutputTail")]
    [JsonIgnore(
        Condition =
            JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RestoreOutputTail { get; init; }

    [JsonPropertyName("buildOutputTail")]
    [JsonIgnore(
        Condition =
            JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? BuildOutputTail { get; init; }
}

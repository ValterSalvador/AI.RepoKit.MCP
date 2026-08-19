using System.Text.Json.Serialization;

namespace AiRepoKit.Cli.Services.SecretScan;

public sealed class SecretScanFinding
{
    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("category")]
    public string Category { get; init; } =
        "SecretPattern";

    [JsonPropertyName("severity")]
    public string Severity { get; init; } =
        "High";

    [JsonPropertyName("preview")]
    public string Preview { get; init; } =
        "<redacted>";
}

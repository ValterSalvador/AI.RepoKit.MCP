namespace AiRepoKit.Cli.Services.SecretScan;

public sealed class SecretScanReport
{
    public bool SecretsExposed { get; init; }

    public bool SecretValuesReturned { get; init; }

    public bool RedactedOnly { get; init; } = true;

    public int FindingCount { get; init; }

    public IReadOnlyList<SecretScanFinding> Findings { get; init; } =
        [];
}

namespace AiRepoKit.Cli.Models.Efficiency;

public sealed record EfficiencyMetric(
    long Bytes,
    long EstimatedTokens,
    int FileCount = 0);

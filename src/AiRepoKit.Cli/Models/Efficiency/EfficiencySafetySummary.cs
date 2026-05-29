namespace AiRepoKit.Cli.Models.Efficiency;

public sealed record EfficiencySafetySummary(
    bool? SecretsExposed,
    bool? SecretValuesReturned);

namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeIndexFreshnessResult(
    bool Stale,
    IReadOnlyList<string> Reasons);

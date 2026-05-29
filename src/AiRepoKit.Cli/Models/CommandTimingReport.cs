namespace AiRepoKit.Cli.Models;

public sealed record CommandTimingReport(
    long TotalElapsedMilliseconds,
    IReadOnlyList<CommandPhaseTiming> Phases);

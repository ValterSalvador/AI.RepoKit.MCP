namespace AiRepoKit.Cli.Models;

public sealed record CommandPhaseTiming(
    string Name,
    string Status,
    long ElapsedMilliseconds);

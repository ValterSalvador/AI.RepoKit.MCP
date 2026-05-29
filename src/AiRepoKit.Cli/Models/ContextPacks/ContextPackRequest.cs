namespace AiRepoKit.Cli.Models.ContextPacks;

public sealed record ContextPackRequest(
    string RepoRoot,
    string Task,
    string Target,
    string Format,
    int Limit,
    bool Apply,
    bool RebuildIndex,
    bool SkipCodeIndex,
    bool Verbose,
    bool NoProgress);

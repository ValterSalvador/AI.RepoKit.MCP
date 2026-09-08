namespace AiRepoKit.Cli.Models.SpecContexts;

public sealed record RepositoryEvidenceCollectionRequest(
    string RepoRoot,
    string Target,
    int Limit,
    int MaxFiles);

namespace AiRepoKit.Cli.Models;

public sealed record ForbiddenTermFinding(
    string File,
    int Line,
    string Term,
    string Preview,
    bool GeneratedArtifact,
    bool ManagedFile);

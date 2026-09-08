using AiRepoKit.Spec.Context;

namespace AiRepoKit.Cli.Models.SpecContexts;

public sealed record RepositoryEvidenceCollection(
    IReadOnlyList<RepositoryEvidence> Evidence,
    IReadOnlyList<SpecContextReference> ReferenceCandidates);

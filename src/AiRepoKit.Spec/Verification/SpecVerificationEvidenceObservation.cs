using AiRepoKit.Spec.Context;

namespace AiRepoKit.Spec.Verification;

public sealed record SpecVerificationEvidenceObservation
{
    public required StableEntityId EvidenceId { get; init; }
    public required string RepositoryEvidenceId { get; init; }
    public required string Source { get; init; }
    public required string Kind { get; init; }
    public required string Reference { get; init; }
    public required RepositoryEvidenceAvailability Availability { get; init; }
    public required RepositoryEvidenceFreshness Freshness { get; init; }
    public required string SourceGeneratedAt { get; init; }
    public required SpecVerificationEvidenceDisposition Disposition { get; init; }
    public required string Reason { get; init; }
}

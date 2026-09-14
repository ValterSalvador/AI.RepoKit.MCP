using System.Collections.Generic;

namespace AiRepoKit.Spec.Verification;

public sealed record SpecVerificationReport
{
    public required string SpecId { get; init; }
    public required ArtifactRevision RequirementSetRevision { get; init; }
    public required ArtifactRevision WorkSpecRevision { get; init; }
    public required ArtifactRevision ImplementationPlanRevision { get; init; }
    public required VerificationStatus OverallStatus { get; init; }
    public required IReadOnlyList<VerificationResult> Results { get; init; }
    public required IReadOnlyList<SpecVerificationEvidenceObservation> Observations { get; init; }
}

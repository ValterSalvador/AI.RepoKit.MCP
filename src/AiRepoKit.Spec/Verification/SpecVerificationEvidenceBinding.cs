namespace AiRepoKit.Spec.Verification;

public sealed record SpecVerificationEvidenceBinding
{
    public required VerificationEvidence Evidence { get; init; }
    public required string RepositoryEvidenceId { get; init; }
}

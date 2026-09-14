using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Verification;

namespace AiRepoKit.Cli.Services.SpecVerification;

public interface ISpecVerificationEvidenceAdapter
{
    string RepositoryEvidenceId { get; }

    SpecVerificationEvidenceDisposition Evaluate(
        RepositoryEvidence evidence_,
        string repoRoot_);

    string GetReason(
        RepositoryEvidence evidence_,
        SpecVerificationEvidenceDisposition disposition_);
}

using System.Collections.Generic;

namespace AiRepoKit.Spec.Verification;

public sealed record SpecVerificationRequest
{
    public required IReadOnlyList<SpecVerificationEvidenceBinding> Evidence { get; init; }
}

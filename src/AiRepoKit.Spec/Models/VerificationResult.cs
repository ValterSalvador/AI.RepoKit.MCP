namespace AiRepoKit.Spec;

public sealed record VerificationResult
{
    public required StableEntityId Id
    {
        get;
        init;
    }

    public required StableEntityId AcceptanceCriterionId
    {
        get;
        init;
    }

    public required VerificationStatus Status
    {
        get;
        init;
    }

    public required IReadOnlyList<StableEntityId> EvidenceIds
    {
        get;
        init;
    }

    public required string Summary
    {
        get;
        init;
    }
}

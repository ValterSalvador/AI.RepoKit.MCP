namespace AiRepoKit.Spec;

public sealed record VerificationEvidence
{
    public required StableEntityId Id
    {
        get;
        init;
    }

    public required string Description
    {
        get;
        init;
    }

    public required IReadOnlyList<StableEntityId> AcceptanceCriterionIds
    {
        get;
        init;
    }

    public required IReadOnlyList<StableEntityId> PlanStepIds
    {
        get;
        init;
    }
}

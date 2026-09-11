namespace AiRepoKit.Spec.Projection;

public sealed record ImplementationChecklistProjection
{
    public required string SpecId
    {
        get;
        init;
    }

    public required ArtifactRevision PlanRevision
    {
        get;
        init;
    }

    public required ArtifactRevision WorkSpecRevision
    {
        get;
        init;
    }

    public required string SemanticDigest
    {
        get;
        init;
    }

    public required bool Stale
    {
        get;
        init;
    }

    public required SpecApprovalStatus ApprovalStatus
    {
        get;
        init;
    }

    public required IReadOnlyList<ImplementationChecklistStepProjection> Steps
    {
        get;
        init;
    }
}

public sealed record ImplementationChecklistStepProjection
{
    public required StableEntityId Id
    {
        get;
        init;
    }

    public required string Statement
    {
        get;
        init;
    }

    public required IReadOnlyList<StableEntityId> RequirementIds
    {
        get;
        init;
    }

    public required IReadOnlyList<StableEntityId> AcceptanceCriterionIds
    {
        get;
        init;
    }
}
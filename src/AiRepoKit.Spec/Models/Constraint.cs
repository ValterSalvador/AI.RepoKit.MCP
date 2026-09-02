namespace AiRepoKit.Spec;

public sealed record Constraint
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
}

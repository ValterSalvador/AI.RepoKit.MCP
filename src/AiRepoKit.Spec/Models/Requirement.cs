namespace AiRepoKit.Spec;

public sealed record Requirement
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

    public required IReadOnlyList<StableEntityId> SourceInputIds
    {
        get;
        init;
    }
}

namespace AiRepoKit.Spec;

public sealed record RequirementInput
{
    public required StableEntityId Id
    {
        get;
        init;
    }

    public required string Text
    {
        get;
        init;
    }
}

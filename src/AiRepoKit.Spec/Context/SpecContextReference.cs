namespace AiRepoKit.Spec.Context;

public sealed record SpecContextReference
{
    public required string EvidenceId
    {
        get;
        init;
    }

    public required string Kind
    {
        get;
        init;
    }

    public required string Reference
    {
        get;
        init;
    }

    public required string Reason
    {
        get;
        init;
    }

    public int Priority
    {
        get;
        init;
    }
}

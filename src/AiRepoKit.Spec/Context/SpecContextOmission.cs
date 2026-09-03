namespace AiRepoKit.Spec.Context;

public sealed record SpecContextOmission
{
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

    public int RemovedEstimatedTokens
    {
        get;
        init;
    }
}

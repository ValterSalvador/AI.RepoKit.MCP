namespace AiRepoKit.Spec;

public sealed record SpecValidationError
{
    public required string Code
    {
        get;
        init;
    }

    public string SourceEntityId
    {
        get;
        init;
    } = string.Empty;

    public string TargetEntityId
    {
        get;
        init;
    } = string.Empty;

    public required string Message
    {
        get;
        init;
    }
}

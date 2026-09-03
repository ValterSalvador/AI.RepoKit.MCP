namespace AiRepoKit.Spec.Persistence;

public sealed record SpecStoreOptions
{
    public SpecWriteMode Mode
    {
        get;
        init;
    } = SpecWriteMode.DryRun;

    public ArtifactRevision? ExpectedCurrentRevision
    {
        get;
        init;
    }
}

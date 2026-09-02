namespace AiRepoKit.Spec;

public sealed record ImplementationPlan
{
    public string SchemaId
    {
        get;
        init;
    } = SpecSchema.SchemaId;

    public int SchemaVersion
    {
        get;
        init;
    } = SpecSchema.SchemaVersion;

    public string ArtifactIdentity
    {
        get;
        init;
    } = SpecArtifactIdentity.ImplementationPlan;

    public ArtifactRevision Revision
    {
        get;
        init;
    } = new(1);

    public ArtifactRevision WorkSpecRevision
    {
        get;
        init;
    } = new(1);

    public required IReadOnlyList<PlanStep> Steps
    {
        get;
        init;
    }
}

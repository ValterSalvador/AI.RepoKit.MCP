namespace AiRepoKit.Spec;

public sealed record WorkSpec
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
    } = SpecArtifactIdentity.WorkSpec;

    public ArtifactRevision Revision
    {
        get;
        init;
    } = new(1);

    public ArtifactRevision RequirementSetRevision
    {
        get;
        init;
    } = new(1);

    public required IReadOnlyList<Constraint> Constraints
    {
        get;
        init;
    }

    public required IReadOnlyList<AcceptanceCriterion> AcceptanceCriteria
    {
        get;
        init;
    }
}

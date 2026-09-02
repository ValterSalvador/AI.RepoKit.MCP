namespace AiRepoKit.Spec;

public sealed record RequirementSet
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
    } = SpecArtifactIdentity.RequirementSet;

    public ArtifactRevision Revision
    {
        get;
        init;
    } = new(1);

    public required IReadOnlyList<RequirementInput> Inputs
    {
        get;
        init;
    }

    public required IReadOnlyList<Requirement> Requirements
    {
        get;
        init;
    }
}

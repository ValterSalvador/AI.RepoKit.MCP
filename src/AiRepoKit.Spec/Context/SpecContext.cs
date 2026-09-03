namespace AiRepoKit.Spec.Context;

public sealed record SpecContext
{
    public string SchemaId
    {
        get;
        init;
    } = SpecContextSchema.SchemaId;

    public int SchemaVersion
    {
        get;
        init;
    } = SpecContextSchema.SchemaVersion;

    public required string SpecId
    {
        get;
        init;
    }

    public ArtifactRevision RequirementSetRevision
    {
        get;
        init;
    } = new(1);

    public ArtifactRevision WorkSpecRevision
    {
        get;
        init;
    } = new(1);

    public string Target
    {
        get;
        init;
    } = string.Empty;

    public required int ReferenceLimit
    {
        get;
        init;
    }

    public required int Budget
    {
        get;
        init;
    }

    public int EstimatedTokens
    {
        get;
        init;
    }

    public bool Truncated
    {
        get;
        init;
    }

    public required IReadOnlyList<RepositoryEvidence> Evidence
    {
        get;
        init;
    }

    public required IReadOnlyList<SpecContextReference> References
    {
        get;
        init;
    }

    public required IReadOnlyList<SpecContextOmission> Omissions
    {
        get;
        init;
    }
}

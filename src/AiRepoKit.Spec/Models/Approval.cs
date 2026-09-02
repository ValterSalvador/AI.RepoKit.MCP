namespace AiRepoKit.Spec;

public sealed record Approval
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

    public required StableEntityId Id
    {
        get;
        init;
    }

    public required SpecArtifactKind ArtifactKind
    {
        get;
        init;
    }

    public required string ArtifactIdentity
    {
        get;
        init;
    }

    public required ArtifactRevision ArtifactRevision
    {
        get;
        init;
    }

    public string CanonicalizationId
    {
        get;
        init;
    } = SpecSchema.CanonicalizationId;

    public int CanonicalizationVersion
    {
        get;
        init;
    } = SpecSchema.CanonicalizationVersion;

    public string DigestAlgorithm
    {
        get;
        init;
    } = SpecSchema.DigestAlgorithm;

    public required string CanonicalSemanticRepresentation
    {
        get;
        init;
    }

    public required string SemanticDigest
    {
        get;
        init;
    }
}

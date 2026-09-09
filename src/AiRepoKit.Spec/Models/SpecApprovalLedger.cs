namespace AiRepoKit.Spec;

public sealed record SpecApprovalLedger
{
    public const string DefaultArtifactIdentity =
        "approvals";

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

    public required string SpecId
    {
        get;
        init;
    }

    public string ArtifactIdentity
    {
        get;
        init;
    } = DefaultArtifactIdentity;

    public ArtifactRevision Revision
    {
        get;
        init;
    } = new(1);

    public required IReadOnlyList<Approval> Approvals
    {
        get;
        init;
    }
}

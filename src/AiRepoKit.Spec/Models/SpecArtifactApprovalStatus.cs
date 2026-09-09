namespace AiRepoKit.Spec;

public sealed record SpecArtifactApprovalStatus
{
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

    public required SpecApprovalStatus Status
    {
        get;
        init;
    }
}

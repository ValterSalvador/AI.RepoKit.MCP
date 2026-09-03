namespace AiRepoKit.Spec.Persistence;

public sealed record SpecStoreResult
{
    internal SpecStoreResult(
        SpecArtifactKind artifactKind_,
        SpecWriteMode mode_,
        bool changed_,
        bool applied_,
        ArtifactRevision? previousRevision_,
        ArtifactRevision targetRevision_,
        string semanticDigest_)
    {
        this.ArtifactKind =
            artifactKind_;
        this.Mode =
            mode_;
        this.Changed =
            changed_;
        this.Applied =
            applied_;
        this.PreviousRevision =
            previousRevision_;
        this.TargetRevision =
            targetRevision_;
        this.SemanticDigest =
            semanticDigest_;
    }

    public SpecArtifactKind ArtifactKind { get; }

    public SpecWriteMode Mode { get; }

    public bool Changed { get; }

    public bool Applied { get; }

    public ArtifactRevision? PreviousRevision { get; }

    public ArtifactRevision TargetRevision { get; }

    public string SemanticDigest { get; }
}

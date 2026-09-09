namespace AiRepoKit.Spec.Persistence;

public sealed record SpecApprovalLedgerStoreResult
{
    public SpecApprovalLedgerStoreResult(
        SpecWriteMode mode_,
        bool changed_,
        bool applied_,
        ArtifactRevision? previousRevision_,
        ArtifactRevision targetRevision_,
        Approval approval_)
    {
        ArgumentNullException.ThrowIfNull(
            approval_);

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
        this.Approval =
            approval_;
    }

    public SpecWriteMode Mode { get; }

    public bool Changed { get; }

    public bool Applied { get; }

    public ArtifactRevision? PreviousRevision { get; }

    public ArtifactRevision TargetRevision { get; }

    public Approval Approval { get; }
}

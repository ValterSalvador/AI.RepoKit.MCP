namespace AiRepoKit.Cli.Models.Audit;

public sealed record AuditSummary
{
    public AuditSummary(
        int findings_,
        int highSeverity_,
        int activeHighSeverity_,
        int acceptedFindings_,
        int falsePositiveFindings_,
        int reviewRequiredFindings_,
        int expiredBaselineEntries_,
        int baselineEntries_,
        int acceptedEntries_,
        int falsePositiveEntries_,
        int reviewRequiredEntries_)
    {
        this.Findings = findings_;
        this.HighSeverity = highSeverity_;
        this.ActiveHighSeverity = activeHighSeverity_;
        this.AcceptedFindings = acceptedFindings_;
        this.FalsePositiveFindings = falsePositiveFindings_;
        this.ReviewRequiredFindings = reviewRequiredFindings_;
        this.ExpiredBaselineEntries = expiredBaselineEntries_;
        this.BaselineEntries = baselineEntries_;
        this.AcceptedEntries = acceptedEntries_;
        this.FalsePositiveEntries = falsePositiveEntries_;
        this.ReviewRequiredEntries = reviewRequiredEntries_;
    }

    public int Findings { get; }

    public int HighSeverity { get; }

    public int ActiveHighSeverity { get; }

    public int AcceptedFindings { get; }

    public int FalsePositiveFindings { get; }

    public int ReviewRequiredFindings { get; }

    public int ExpiredBaselineEntries { get; }

    public int BaselineEntries { get; }

    public int AcceptedEntries { get; }

    public int FalsePositiveEntries { get; }

    public int ReviewRequiredEntries { get; }
}

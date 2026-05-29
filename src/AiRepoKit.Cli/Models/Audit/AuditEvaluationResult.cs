namespace AiRepoKit.Cli.Models.Audit;

public sealed record AuditEvaluationResult
{
    public AuditEvaluationResult(
        IReadOnlyList<AuditFinding> findings_,
        AuditSummary summary_,
        AuditBaseline? baseline_,
        string baselinePath_)
    {
        this.Findings = findings_;
        this.Summary = summary_;
        this.Baseline = baseline_;
        this.BaselinePath = baselinePath_;
    }

    public IReadOnlyList<AuditFinding> Findings { get; }

    public AuditSummary Summary { get; }

    public AuditBaseline? Baseline { get; }

    public string BaselinePath { get; }
}

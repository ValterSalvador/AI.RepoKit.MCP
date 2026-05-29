namespace AiRepoKit.Cli.Models.Audit;

public sealed record AuditFinding
{
    public AuditFinding(string file_, string category_, int line_, string preview_, string severity_, string previewHash_)
    {
        this.File = file_;
        this.Category = category_;
        this.Line = line_;
        this.Preview = preview_;
        this.Severity = severity_;
        this.PreviewHash = previewHash_;
    }

    public string File { get; }

    public string Category { get; }

    public int Line { get; }

    public string Preview { get; }

    public string Severity { get; }

    public string PreviewHash { get; }

    public AuditBaselineStatus? BaselineStatus { get; init; }

    public bool IsExpired { get; init; }
}

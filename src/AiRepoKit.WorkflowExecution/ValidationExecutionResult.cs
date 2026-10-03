namespace AiRepoKit.WorkflowExecution;

public sealed record ValidationExecutionResult
{
    public bool Passed
    {
        get;
    }

    public string Evidence
    {
        get;
    }

    public ValidationExecutionResult(
        bool passed_,
        string evidence_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            evidence_,
            nameof(evidence_));

        this.Passed =
            passed_;
        this.Evidence =
            evidence_;
    }
}

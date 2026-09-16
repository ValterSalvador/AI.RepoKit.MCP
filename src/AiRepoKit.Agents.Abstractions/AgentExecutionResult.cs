namespace AiRepoKit.Agents;

public sealed record AgentExecutionResult
{
    public AgentExecutionStatus Status
    {
        get;
    }

    public string? OutputText
    {
        get;
    }

    public string? DiagnosticText
    {
        get;
    }

    public AgentSessionReference? SessionReference
    {
        get;
    }

    private AgentExecutionResult(
        AgentExecutionStatus status_,
        string? outputText_,
        string? diagnosticText_,
        AgentSessionReference? sessionReference_)
    {
        if (!Enum.IsDefined(
                status_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status_),
                status_,
                "Agent execution status must be a defined terminal status.");
        }

        this.Status =
            status_;
        this.OutputText =
            outputText_;
        this.DiagnosticText =
            diagnosticText_;
        this.SessionReference =
            sessionReference_;
    }

    public static AgentExecutionResult Completed(
        string? outputText_ = null,
        AgentSessionReference? sessionReference_ = null,
        string? diagnosticText_ = null)
    {
        return new AgentExecutionResult(
            AgentExecutionStatus.Completed,
            outputText_,
            diagnosticText_,
            sessionReference_);
    }

    public static AgentExecutionResult Blocked(
        string? diagnosticText_ = null,
        AgentSessionReference? sessionReference_ = null,
        string? outputText_ = null)
    {
        return new AgentExecutionResult(
            AgentExecutionStatus.Blocked,
            outputText_,
            diagnosticText_,
            sessionReference_);
    }

    public static AgentExecutionResult NeedsInput(
        string? diagnosticText_ = null,
        AgentSessionReference? sessionReference_ = null,
        string? outputText_ = null)
    {
        return new AgentExecutionResult(
            AgentExecutionStatus.NeedsInput,
            outputText_,
            diagnosticText_,
            sessionReference_);
    }

    public static AgentExecutionResult Failed(
        string? diagnosticText_ = null,
        AgentSessionReference? sessionReference_ = null,
        string? outputText_ = null)
    {
        return new AgentExecutionResult(
            AgentExecutionStatus.Failed,
            outputText_,
            diagnosticText_,
            sessionReference_);
    }
}

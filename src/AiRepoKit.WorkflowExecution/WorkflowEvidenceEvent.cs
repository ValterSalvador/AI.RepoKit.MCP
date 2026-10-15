namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowEvidenceEvent
{
    public WorkflowId WorkflowId
    {
        get;
    }

    public long Revision
    {
        get;
    }

    public WorkflowExecutionEventKind Kind
    {
        get;
    }

    public string? TaskId
    {
        get;
    }

    public WorkflowStatus? PreviousWorkflowStatus
    {
        get;
    }

    public WorkflowStatus? TargetWorkflowStatus
    {
        get;
    }

    public WorkflowStepStatus? PreviousStepStatus
    {
        get;
    }

    public WorkflowStepStatus? TargetStepStatus
    {
        get;
    }

    internal WorkflowEvidenceEvent(
        WorkflowId workflowId_,
        long revision_,
        WorkflowExecutionEventKind kind_,
        string? taskId_,
        WorkflowStatus? previousWorkflowStatus_,
        WorkflowStatus? targetWorkflowStatus_,
        WorkflowStepStatus? previousStepStatus_,
        WorkflowStepStatus? targetStepStatus_)
    {
        ArgumentNullException.ThrowIfNull(
            workflowId_,
            nameof(workflowId_));

        if (revision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision_),
                revision_,
                "Event revision must be greater than zero.");
        }

        if (!Enum.IsDefined(typeof(WorkflowExecutionEventKind), kind_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind_),
                kind_,
                "Workflow execution event kind is not defined.");
        }

        this.WorkflowId =
            workflowId_;
        this.Revision =
            revision_;
        this.Kind =
            kind_;
        this.TaskId =
            taskId_;
        this.PreviousWorkflowStatus =
            previousWorkflowStatus_;
        this.TargetWorkflowStatus =
            targetWorkflowStatus_;
        this.PreviousStepStatus =
            previousStepStatus_;
        this.TargetStepStatus =
            targetStepStatus_;
    }
}

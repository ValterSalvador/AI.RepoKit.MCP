namespace AiRepoKit.Orchestration;

public sealed record WorkflowStepState
{
    public string TaskId
    {
        get;
    }

    public WorkflowStepStatus Status
    {
        get;
    }

    internal WorkflowStepState(
        string taskId_,
        WorkflowStepStatus status_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        if (!Enum.IsDefined(
                typeof(WorkflowStepStatus),
                status_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status_),
                status_,
                "Workflow step status must be a defined value.");
        }

        this.TaskId =
            taskId_;
        this.Status =
            status_;
    }
}
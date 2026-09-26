namespace AiRepoKit.Orchestration;

public sealed record WorkflowExecutionEvent
{
    public WorkflowId WorkflowId
    {
        get;
    }

    public long Sequence
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

    internal WorkflowExecutionEvent(
        WorkflowId workflowId_,
        long sequence_,
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

        if (sequence_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence_),
                sequence_,
                "Event sequence must be greater than zero.");
        }

        if (!Enum.IsDefined(
                typeof(WorkflowExecutionEventKind),
                kind_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind_),
                kind_,
                "Execution event kind must be a defined value.");
        }

        ValidateShape(
            kind_,
            taskId_,
            previousWorkflowStatus_,
            targetWorkflowStatus_,
            previousStepStatus_,
            targetStepStatus_);

        this.WorkflowId =
            workflowId_;
        this.Sequence =
            sequence_;
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

    private static void ValidateShape(
        WorkflowExecutionEventKind kind_,
        string? taskId_,
        WorkflowStatus? previousWorkflowStatus_,
        WorkflowStatus? targetWorkflowStatus_,
        WorkflowStepStatus? previousStepStatus_,
        WorkflowStepStatus? targetStepStatus_)
    {
        switch (kind_)
        {
            case WorkflowExecutionEventKind.WorkflowInitialized:
                if (taskId_ is not null ||
                    previousWorkflowStatus_ is not null ||
                    targetWorkflowStatus_ != WorkflowStatus.Created ||
                    previousStepStatus_ is not null ||
                    targetStepStatus_ is not null)
                {
                    throw new ArgumentException(
                        "WorkflowInitialized event shape is invalid.");
                }

                return;

            case WorkflowExecutionEventKind.WorkflowStatusTransitioned:
                if (taskId_ is not null ||
                    previousWorkflowStatus_ is null ||
                    targetWorkflowStatus_ is null ||
                    previousStepStatus_ is not null ||
                    targetStepStatus_ is not null)
                {
                    throw new ArgumentException(
                        "WorkflowStatusTransitioned event shape is invalid.");
                }

                ValidateWorkflowStatus(
                    previousWorkflowStatus_.Value,
                    nameof(previousWorkflowStatus_));
                ValidateWorkflowStatus(
                    targetWorkflowStatus_.Value,
                    nameof(targetWorkflowStatus_));

                if (previousWorkflowStatus_.Value ==
                    targetWorkflowStatus_.Value)
                {
                    throw new ArgumentException(
                        "Workflow status transition event must change status.");
                }

                return;

            case WorkflowExecutionEventKind.StepStatusTransitioned:
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    taskId_,
                    nameof(taskId_));

                if (previousWorkflowStatus_ is not null ||
                    targetWorkflowStatus_ is not null ||
                    previousStepStatus_ is null ||
                    targetStepStatus_ is null)
                {
                    throw new ArgumentException(
                        "StepStatusTransitioned event shape is invalid.");
                }

                ValidateWorkflowStepStatus(
                    previousStepStatus_.Value,
                    nameof(previousStepStatus_));
                ValidateWorkflowStepStatus(
                    targetStepStatus_.Value,
                    nameof(targetStepStatus_));

                if (previousStepStatus_.Value ==
                    targetStepStatus_.Value)
                {
                    throw new ArgumentException(
                        "Step status transition event must change status.");
                }

                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind_),
                    kind_,
                    "Execution event kind must be a defined value.");
        }
    }

    private static void ValidateWorkflowStatus(
        WorkflowStatus status_,
        string parameterName_)
    {
        if (!Enum.IsDefined(
                typeof(WorkflowStatus),
                status_))
        {
            throw new ArgumentOutOfRangeException(
                parameterName_,
                status_,
                "Workflow status must be a defined value.");
        }
    }

    private static void ValidateWorkflowStepStatus(
        WorkflowStepStatus status_,
        string parameterName_)
    {
        if (!Enum.IsDefined(
                typeof(WorkflowStepStatus),
                status_))
        {
            throw new ArgumentOutOfRangeException(
                parameterName_,
                status_,
                "Workflow step status must be a defined value.");
        }
    }
}

namespace AiRepoKit.Orchestration;

using AiRepoKit.Execution;

public static class WorkflowStateMachine
{
    public const string AlgorithmId =
        "ai.repokit.workflow-state-machine/v1";

    public static WorkflowState Create(
        ExecutableWork work_)
    {
        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));

        WorkflowStepState[] steps =
            new WorkflowStepState[work_.Tasks.Count];

        for (int index = 0; index < work_.Tasks.Count; index++)
        {
            steps[index] =
                new WorkflowStepState(
                    work_.Tasks[index].Id,
                    WorkflowStepStatus.Pending);
        }

        return new WorkflowState(
            work_.SourceImplementationPlanRevision,
            WorkflowStatus.Created,
            steps);
    }

    public static WorkflowState TransitionWorkflow(
        WorkflowState state_,
        WorkflowStatus targetStatus_)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ValidateWorkflowStatus(
            targetStatus_,
            nameof(targetStatus_));

        if (!IsWorkflowTransitionAllowed(
                state_.Status,
                targetStatus_))
        {
            throw new InvalidOperationException(
                $"Workflow transition '{state_.Status}' -> '{targetStatus_}' is not allowed.");
        }

        ValidateWorkflowTransitionPreconditions(
            state_,
            targetStatus_);

        return new WorkflowState(
            state_.SourceImplementationPlanRevision,
            targetStatus_,
            state_.Steps);
    }

    public static WorkflowState TransitionStep(
        WorkflowState state_,
        string taskId_,
        WorkflowStepStatus targetStatus_)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        ValidateWorkflowStepStatus(
            targetStatus_,
            nameof(targetStatus_));

        if (state_.Status != WorkflowStatus.Running)
        {
            throw new InvalidOperationException(
                $"Workflow step transitions require workflow status '{WorkflowStatus.Running}'. Current status is '{state_.Status}'.");
        }

        int stepIndex =
            -1;

        for (int index = 0; index < state_.Steps.Count; index++)
        {
            if (string.Equals(
                    state_.Steps[index].TaskId,
                    taskId_,
                    StringComparison.Ordinal))
            {
                stepIndex =
                    index;

                break;
            }
        }

        if (stepIndex < 0)
        {
            throw new ArgumentException(
                $"Unknown workflow task identifier '{taskId_}'.",
                nameof(taskId_));
        }

        WorkflowStepState currentStep =
            state_.Steps[stepIndex];

        if (!IsWorkflowStepTransitionAllowed(
                currentStep.Status,
                targetStatus_))
        {
            throw new InvalidOperationException(
                $"Workflow step transition '{currentStep.Status}' -> '{targetStatus_}' is not allowed for task '{taskId_}'.");
        }

        WorkflowStepState[] steps =
            state_.Steps.ToArray();

        steps[stepIndex] =
            new WorkflowStepState(
                currentStep.TaskId,
                targetStatus_);

        return new WorkflowState(
            state_.SourceImplementationPlanRevision,
            state_.Status,
            steps);
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

    private static bool IsWorkflowTransitionAllowed(
        WorkflowStatus currentStatus_,
        WorkflowStatus targetStatus_)
    {
        return (
            currentStatus_,
            targetStatus_) switch
        {
            (
                WorkflowStatus.Created,
                WorkflowStatus.Running
            ) => true,
            (
                WorkflowStatus.Created,
                WorkflowStatus.Cancelled
            ) => true,
            (
                WorkflowStatus.Running,
                WorkflowStatus.Completed
            ) => true,
            (
                WorkflowStatus.Running,
                WorkflowStatus.Failed
            ) => true,
            (
                WorkflowStatus.Running,
                WorkflowStatus.Cancelled
            ) => true,
            _ => false
        };
    }

    private static bool IsWorkflowStepTransitionAllowed(
        WorkflowStepStatus currentStatus_,
        WorkflowStepStatus targetStatus_)
    {
        return (
            currentStatus_,
            targetStatus_) switch
        {
            (
                WorkflowStepStatus.Pending,
                WorkflowStepStatus.Running
            ) => true,
            (
                WorkflowStepStatus.Pending,
                WorkflowStepStatus.Cancelled
            ) => true,
            (
                WorkflowStepStatus.Running,
                WorkflowStepStatus.AwaitingValidation
            ) => true,
            (
                WorkflowStepStatus.Running,
                WorkflowStepStatus.Blocked
            ) => true,
            (
                WorkflowStepStatus.Running,
                WorkflowStepStatus.Failed
            ) => true,
            (
                WorkflowStepStatus.Running,
                WorkflowStepStatus.Cancelled
            ) => true,
            (
                WorkflowStepStatus.AwaitingValidation,
                WorkflowStepStatus.Completed
            ) => true,
            (
                WorkflowStepStatus.AwaitingValidation,
                WorkflowStepStatus.Blocked
            ) => true,
            (
                WorkflowStepStatus.AwaitingValidation,
                WorkflowStepStatus.Failed
            ) => true,
            (
                WorkflowStepStatus.AwaitingValidation,
                WorkflowStepStatus.Cancelled
            ) => true,
            (
                WorkflowStepStatus.Blocked,
                WorkflowStepStatus.Running
            ) => true,
            (
                WorkflowStepStatus.Blocked,
                WorkflowStepStatus.Failed
            ) => true,
            (
                WorkflowStepStatus.Blocked,
                WorkflowStepStatus.Cancelled
            ) => true,
            (
                WorkflowStepStatus.Failed,
                WorkflowStepStatus.Pending
            ) => true,
            (
                WorkflowStepStatus.Failed,
                WorkflowStepStatus.Cancelled
            ) => true,
            _ => false
        };
    }

    private static void ValidateWorkflowTransitionPreconditions(
        WorkflowState state_,
        WorkflowStatus targetStatus_)
    {
        if (state_.Status != WorkflowStatus.Running)
        {
            return;
        }

        if (targetStatus_ == WorkflowStatus.Completed)
        {
            for (int index = 0; index < state_.Steps.Count; index++)
            {
                if (state_.Steps[index].Status !=
                    WorkflowStepStatus.Completed)
                {
                    throw new InvalidOperationException(
                        "A running workflow can complete only when every step is completed.");
                }
            }

            return;
        }

        if (targetStatus_ == WorkflowStatus.Failed)
        {
            bool hasFailedStep =
                false;
            bool hasActiveStep =
                false;

            for (int index = 0; index < state_.Steps.Count; index++)
            {
                WorkflowStepStatus status =
                    state_.Steps[index].Status;

                if (status == WorkflowStepStatus.Failed)
                {
                    hasFailedStep =
                        true;
                }

                if (IsActiveStep(
                        status))
                {
                    hasActiveStep =
                        true;
                }
            }

            if (!hasFailedStep)
            {
                throw new InvalidOperationException(
                    "A running workflow can fail only when at least one step is failed.");
            }

            if (hasActiveStep)
            {
                throw new InvalidOperationException(
                    "A running workflow cannot fail while a step remains active.");
            }

            return;
        }

        if (targetStatus_ == WorkflowStatus.Cancelled &&
            HasActiveStep(
                state_))
        {
            throw new InvalidOperationException(
                "A running workflow cannot be cancelled while a step remains active.");
        }
    }

    private static bool HasActiveStep(
        WorkflowState state_)
    {
        for (int index = 0; index < state_.Steps.Count; index++)
        {
            if (IsActiveStep(
                    state_.Steps[index].Status))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActiveStep(
        WorkflowStepStatus status_)
    {
        return status_ ==
                WorkflowStepStatus.Running ||
            status_ ==
                WorkflowStepStatus.AwaitingValidation;
    }
}
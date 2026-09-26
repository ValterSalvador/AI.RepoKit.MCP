namespace AiRepoKit.Orchestration.Tests;

using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using Xunit;

public sealed class WorkflowStateMachineTests
{
    [Fact]
    public void AlgorithmId_IsFrozen()
    {
        Assert.Equal(
            "ai.repokit.workflow-state-machine/v1",
            WorkflowStateMachine.AlgorithmId);
    }

    [Fact]
    public void Create_RejectsNullWork()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                WorkflowStateMachine.Create(
                    null!));
    }

    [Fact]
    public void Create_PreservesRevisionAndTaskOrder()
    {
        ExecutableWork work =
            Work(
                "task-b",
                "Task-A",
                "task-a");

        WorkflowState state =
            WorkflowStateMachine.Create(
                work);

        Assert.Equal(
            7,
            state.SourceImplementationPlanRevision);

        Assert.Equal(
            WorkflowStatus.Created,
            state.Status);

        Assert.Equal(
            [
                "task-b",
                "Task-A",
                "task-a"
            ],
            state.Steps
                .Select(
                    step_ =>
                        step_.TaskId)
                .ToArray());
    }

    [Fact]
    public void Create_InitializesEveryStepAsPending()
    {
        WorkflowState state =
            WorkflowStateMachine.Create(
                Work(
                    "task-a",
                    "task-b"));

        Assert.All(
            state.Steps,
            step_ =>
                Assert.Equal(
                    WorkflowStepStatus.Pending,
                    step_.Status));
    }

    [Fact]
    public void EmptyExecutableWork_HasDeterministicLifecycle()
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work());

        Assert.Empty(
            created.Steps);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        WorkflowState completed =
            WorkflowStateMachine.TransitionWorkflow(
                running,
                WorkflowStatus.Completed);

        Assert.Equal(
            WorkflowStatus.Completed,
            completed.Status);
    }

    [Fact]
    public void StepCollection_IsReadOnly()
    {
        WorkflowState state =
            WorkflowStateMachine.Create(
                Work(
                    "task-a"));

        IList<WorkflowStepState> list =
            Assert.IsAssignableFrom<IList<WorkflowStepState>>(
                state.Steps);

        Assert.True(
            list.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                list.RemoveAt(
                    0));
    }

    [Fact]
    public void TransitionWorkflow_AllowsCreatedToRunning()
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    "task-a"));

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        Assert.Equal(
            WorkflowStatus.Running,
            running.Status);

        Assert.Equal(
            WorkflowStatus.Created,
            created.Status);
    }

    [Fact]
    public void TransitionWorkflow_AllowsCreatedToCancelled()
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    "task-a"));

        WorkflowState cancelled =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Cancelled);

        Assert.Equal(
            WorkflowStatus.Cancelled,
            cancelled.Status);
    }

    [Theory]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    public void TransitionWorkflow_RejectsIllegalCreatedTransitions(
        WorkflowStatus targetStatus_)
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    "task-a"));

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionWorkflow(
                    created,
                    targetStatus_));
    }

    [Fact]
    public void TransitionWorkflow_RejectsUndefinedStatus()
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                WorkflowStateMachine.TransitionWorkflow(
                    created,
                    (WorkflowStatus) 999));
    }

    [Fact]
    public void TransitionWorkflow_RunningToCompletedRequiresAllCompleted()
    {
        WorkflowState state =
            RunningState(
                "task-a",
                "task-b");

        state =
            CompleteStep(
                state,
                "task-a");

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionWorkflow(
                    state,
                    WorkflowStatus.Completed));

        state =
            CompleteStep(
                state,
                "task-b");

        WorkflowState completed =
            WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Completed);

        Assert.Equal(
            WorkflowStatus.Completed,
            completed.Status);
    }

    [Fact]
    public void TransitionWorkflow_RunningToFailedRequiresFailedStep()
    {
        WorkflowState state =
            RunningState(
                "task-a");

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionWorkflow(
                    state,
                    WorkflowStatus.Failed));
    }

    [Fact]
    public void TransitionWorkflow_RunningToFailedAllowsFailedStepWithNoActiveStep()
    {
        WorkflowState state =
            RunningState(
                "task-a",
                "task-b");

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Running);

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Failed);

        WorkflowState failed =
            WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Failed);

        Assert.Equal(
            WorkflowStatus.Failed,
            failed.Status);
    }

    [Fact]
    public void TransitionWorkflow_RunningToFailedRejectsActiveStep()
    {
        WorkflowState state =
            RunningState(
                "task-a",
                "task-b");

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Running);

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Failed);

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-b",
                WorkflowStepStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionWorkflow(
                    state,
                    WorkflowStatus.Failed));
    }

    [Fact]
    public void TransitionWorkflow_RunningToCancelledAllowsNoActiveSteps()
    {
        WorkflowState state =
            RunningState(
                "task-a");

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Cancelled);

        WorkflowState cancelled =
            WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Cancelled);

        Assert.Equal(
            WorkflowStatus.Cancelled,
            cancelled.Status);
    }

    [Fact]
    public void TransitionWorkflow_RunningToCancelledRejectsActiveStep()
    {
        WorkflowState state =
            RunningState(
                "task-a");

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionWorkflow(
                    state,
                    WorkflowStatus.Cancelled));
    }

    [Theory]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Cancelled)]
    public void TerminalWorkflow_RejectsWorkflowTransitions(
        WorkflowStatus terminalStatus_)
    {
        WorkflowState terminal =
            TerminalWorkflow(
                terminalStatus_);

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionWorkflow(
                    terminal,
                    WorkflowStatus.Running));
    }

    [Theory]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Cancelled)]
    public void TerminalWorkflow_RejectsStepTransitions(
        WorkflowStatus terminalStatus_)
    {
        WorkflowState terminal =
            TerminalWorkflow(
                terminalStatus_);

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionStep(
                    terminal,
                    "task-a",
                    WorkflowStepStatus.Running));
    }

    [Fact]
    public void PendingStepTransitions_AreExact()
    {
        AssertExactStepTargets(
            WorkflowStepStatus.Pending,
            WorkflowStepStatus.Running,
            WorkflowStepStatus.Cancelled);
    }

    [Fact]
    public void RunningStepTransitions_AreExact()
    {
        AssertExactStepTargets(
            WorkflowStepStatus.Running,
            WorkflowStepStatus.AwaitingValidation,
            WorkflowStepStatus.Blocked,
            WorkflowStepStatus.Failed,
            WorkflowStepStatus.Cancelled);
    }

    [Fact]
    public void AwaitingValidationStepTransitions_AreExact()
    {
        AssertExactStepTargets(
            WorkflowStepStatus.AwaitingValidation,
            WorkflowStepStatus.Completed,
            WorkflowStepStatus.Blocked,
            WorkflowStepStatus.Failed,
            WorkflowStepStatus.Cancelled);
    }

    [Fact]
    public void BlockedStepTransitions_AreExact()
    {
        AssertExactStepTargets(
            WorkflowStepStatus.Blocked,
            WorkflowStepStatus.Running,
            WorkflowStepStatus.Failed,
            WorkflowStepStatus.Cancelled);
    }

    [Fact]
    public void FailedStepTransitions_AreExact()
    {
        AssertExactStepTargets(
            WorkflowStepStatus.Failed,
            WorkflowStepStatus.Pending,
            WorkflowStepStatus.Cancelled);
    }

    [Fact]
    public void CompletedStep_IsTerminal()
    {
        AssertExactStepTargets(
            WorkflowStepStatus.Completed);
    }

    [Fact]
    public void CancelledStep_IsTerminal()
    {
        AssertExactStepTargets(
            WorkflowStepStatus.Cancelled);
    }

    [Fact]
    public void DirectRunningToCompleted_IsRejected()
    {
        WorkflowState state =
            StateWithStepStatus(
                WorkflowStepStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Completed));
    }

    [Fact]
    public void StepTransitions_RequireRunningWorkflow()
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    "task-a"));

        Assert.Throws<InvalidOperationException>(
            () =>
                WorkflowStateMachine.TransitionStep(
                    created,
                    "task-a",
                    WorkflowStepStatus.Running));
    }

    [Fact]
    public void TransitionStep_RejectsUnknownTask()
    {
        WorkflowState state =
            RunningState(
                "task-a");

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    WorkflowStateMachine.TransitionStep(
                        state,
                        "task-b",
                        WorkflowStepStatus.Running));

        Assert.Equal(
            "taskId_",
            exception.ParamName);
    }

    [Fact]
    public void TaskLookup_IsOrdinalExact()
    {
        WorkflowState state =
            RunningState(
                "Task-A");

        Assert.Throws<ArgumentException>(
            () =>
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Running));

        WorkflowState transitioned =
            WorkflowStateMachine.TransitionStep(
                state,
                "Task-A",
                WorkflowStepStatus.Running);

        Assert.Equal(
            WorkflowStepStatus.Running,
            transitioned.Steps[0].Status);
    }

    [Fact]
    public void TransitionStep_RejectsUndefinedStatus()
    {
        WorkflowState state =
            RunningState(
                "task-a");

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    (WorkflowStepStatus) 999));
    }

    [Fact]
    public void TransitionStep_PreservesStepOrder()
    {
        WorkflowState state =
            RunningState(
                "task-c",
                "task-a",
                "task-b");

        WorkflowState transitioned =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Running);

        Assert.Equal(
            [
                "task-c",
                "task-a",
                "task-b"
            ],
            transitioned.Steps
                .Select(
                    step_ =>
                        step_.TaskId)
                .ToArray());
    }

    [Fact]
    public void TransitionStep_DoesNotMutateInputState()
    {
        WorkflowState input =
            RunningState(
                "task-a");

        WorkflowState output =
            WorkflowStateMachine.TransitionStep(
                input,
                "task-a",
                WorkflowStepStatus.Running);

        Assert.Equal(
            WorkflowStepStatus.Pending,
            input.Steps[0].Status);

        Assert.Equal(
            WorkflowStepStatus.Running,
            output.Steps[0].Status);

        Assert.NotSame(
            input,
            output);
    }

    [Fact]
    public void EqualInputTransitions_ProduceStructurallyEqualStates()
    {
        WorkflowState input =
            RunningState(
                "task-a");

        WorkflowState first =
            WorkflowStateMachine.TransitionStep(
                input,
                "task-a",
                WorkflowStepStatus.Running);

        WorkflowState second =
            WorkflowStateMachine.TransitionStep(
                input,
                "task-a",
                WorkflowStepStatus.Running);

        Assert.Equal(
            first,
            second);

        Assert.Equal(
            first.GetHashCode(),
            second.GetHashCode());
    }

    private static ExecutableWork Work(
        params string[] taskIds_)
    {
        ExecutableTask[] tasks =
            new ExecutableTask[taskIds_.Length];

        for (int index = 0; index < taskIds_.Length; index++)
        {
            string taskId =
                taskIds_[index];

            tasks[index] =
                new ExecutableTask(
                    taskId,
                    $"plan-{taskId}",
                    $"instruction-{taskId}");
        }

        return new ExecutableWork(
            7,
            tasks);
    }

    private static WorkflowState RunningState(
        params string[] taskIds_)
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    taskIds_));

        return WorkflowStateMachine.TransitionWorkflow(
            created,
            WorkflowStatus.Running);
    }

    private static WorkflowState CompleteStep(
        WorkflowState state_,
        string taskId_)
    {
        WorkflowState running =
            WorkflowStateMachine.TransitionStep(
                state_,
                taskId_,
                WorkflowStepStatus.Running);

        WorkflowState awaitingValidation =
            WorkflowStateMachine.TransitionStep(
                running,
                taskId_,
                WorkflowStepStatus.AwaitingValidation);

        return WorkflowStateMachine.TransitionStep(
            awaitingValidation,
            taskId_,
            WorkflowStepStatus.Completed);
    }

    private static WorkflowState StateWithStepStatus(
        WorkflowStepStatus status_)
    {
        WorkflowState state =
            RunningState(
                "task-a");

        return status_ switch
        {
            WorkflowStepStatus.Pending =>
                state,

            WorkflowStepStatus.Running =>
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Running),

            WorkflowStepStatus.AwaitingValidation =>
                WorkflowStateMachine.TransitionStep(
                    WorkflowStateMachine.TransitionStep(
                        state,
                        "task-a",
                        WorkflowStepStatus.Running),
                    "task-a",
                    WorkflowStepStatus.AwaitingValidation),

            WorkflowStepStatus.Blocked =>
                WorkflowStateMachine.TransitionStep(
                    WorkflowStateMachine.TransitionStep(
                        state,
                        "task-a",
                        WorkflowStepStatus.Running),
                    "task-a",
                    WorkflowStepStatus.Blocked),

            WorkflowStepStatus.Failed =>
                WorkflowStateMachine.TransitionStep(
                    WorkflowStateMachine.TransitionStep(
                        state,
                        "task-a",
                        WorkflowStepStatus.Running),
                    "task-a",
                    WorkflowStepStatus.Failed),

            WorkflowStepStatus.Completed =>
                CompleteStep(
                    state,
                    "task-a"),

            WorkflowStepStatus.Cancelled =>
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Cancelled),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(status_),
                    status_,
                    null)
        };
    }

    private static WorkflowState TerminalWorkflow(
        WorkflowStatus terminalStatus_)
    {
        WorkflowState state =
            RunningState(
                "task-a");

        if (terminalStatus_ == WorkflowStatus.Completed)
        {
            state =
                CompleteStep(
                    state,
                    "task-a");

            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Completed);
        }

        if (terminalStatus_ == WorkflowStatus.Failed)
        {
            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Running);

            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Failed);

            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Failed);
        }

        if (terminalStatus_ == WorkflowStatus.Cancelled)
        {
            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Cancelled);

            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Cancelled);
        }

        throw new ArgumentOutOfRangeException(
            nameof(terminalStatus_),
            terminalStatus_,
            null);
    }

    private static void AssertExactStepTargets(
        WorkflowStepStatus currentStatus_,
        params WorkflowStepStatus[] allowedTargets_)
    {
        HashSet<WorkflowStepStatus> allowedTargets =
            new(
                allowedTargets_);

        foreach (WorkflowStepStatus targetStatus in
                 Enum.GetValues<WorkflowStepStatus>())
        {
            WorkflowState state =
                StateWithStepStatus(
                    currentStatus_);

            if (allowedTargets.Contains(
                    targetStatus))
            {
                WorkflowState transitioned =
                    WorkflowStateMachine.TransitionStep(
                        state,
                        "task-a",
                        targetStatus);

                Assert.Equal(
                    targetStatus,
                    transitioned.Steps[0].Status);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(
                    () =>
                        WorkflowStateMachine.TransitionStep(
                            state,
                            "task-a",
                            targetStatus));
            }
        }
    }
}
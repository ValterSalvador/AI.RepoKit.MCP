namespace AiRepoKit.Orchestration.Tests;

using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using Xunit;

public sealed class WorkflowTaskDagSchedulerTests
{
    [Fact]
    public void GetReadyTaskIds_RejectsNullWork()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            RunningState(
                work);

        Assert.Throws<ArgumentNullException>(
            () =>
                WorkflowTaskDagScheduler.GetReadyTaskIds(
                    null!,
                    state));
    }

    [Fact]
    public void GetReadyTaskIds_RejectsNullState()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        Assert.Throws<ArgumentNullException>(
            () =>
                WorkflowTaskDagScheduler.GetReadyTaskIds(
                    work,
                    null!));
    }

    [Fact]
    public void SelectNextTaskId_RejectsNullWork()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            RunningState(
                work);

        Assert.Throws<ArgumentNullException>(
            () =>
                WorkflowTaskDagScheduler.SelectNextTaskId(
                    null!,
                    state));
    }

    [Fact]
    public void SelectNextTaskId_RejectsNullState()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        Assert.Throws<ArgumentNullException>(
            () =>
                WorkflowTaskDagScheduler.SelectNextTaskId(
                    work,
                    null!));
    }

    [Fact]
    public void SourceRevisionMismatch_IsRejected()
    {
        ExecutableWork stateWork =
            Work(
                7,
                [
                    Task(
                        "task-a")
                ]);

        ExecutableWork queryWork =
            Work(
                8,
                [
                    Task(
                        "task-a")
                ]);

        WorkflowState state =
            RunningState(
                stateWork);

        Assert.Throws<ArgumentException>(
            () =>
                WorkflowTaskDagScheduler.GetReadyTaskIds(
                    queryWork,
                    state));

        Assert.Throws<ArgumentException>(
            () =>
                WorkflowTaskDagScheduler.SelectNextTaskId(
                    queryWork,
                    state));
    }

    [Fact]
    public void TaskCountMismatch_IsRejected()
    {
        ExecutableWork stateWork =
            Work(
                ["task-a"]);

        ExecutableWork queryWork =
            Work(
                [
                    "task-a",
                    "task-b"
                ]);

        WorkflowState state =
            RunningState(
                stateWork);

        Assert.Throws<ArgumentException>(
            () =>
                WorkflowTaskDagScheduler.GetReadyTaskIds(
                    queryWork,
                    state));
    }

    [Fact]
    public void TaskIdentityMismatch_IsRejected()
    {
        ExecutableWork stateWork =
            Work(
                [
                    "task-a",
                    "task-b"
                ]);

        ExecutableWork queryWork =
            Work(
                [
                    "task-a",
                    "task-c"
                ]);

        WorkflowState state =
            RunningState(
                stateWork);

        Assert.Throws<ArgumentException>(
            () =>
                WorkflowTaskDagScheduler.GetReadyTaskIds(
                    queryWork,
                    state));
    }

    [Fact]
    public void TaskOrderMismatch_IsRejected()
    {
        ExecutableWork stateWork =
            Work(
                [
                    "task-a",
                    "task-b"
                ]);

        ExecutableWork queryWork =
            Work(
                [
                    "task-b",
                    "task-a"
                ]);

        WorkflowState state =
            RunningState(
                stateWork);

        Assert.Throws<ArgumentException>(
            () =>
                WorkflowTaskDagScheduler.GetReadyTaskIds(
                    queryWork,
                    state));
    }

    [Fact]
    public void TaskIdentity_IsOrdinalCaseSensitive()
    {
        ExecutableWork stateWork =
            Work(
                ["Task-A"]);

        ExecutableWork queryWork =
            Work(
                ["task-a"]);

        WorkflowState state =
            RunningState(
                stateWork);

        Assert.Throws<ArgumentException>(
            () =>
                WorkflowTaskDagScheduler.GetReadyTaskIds(
                    queryWork,
                    state));
    }

    [Fact]
    public void SourcePlanStepId_IsNotSchedulerIdentity()
    {
        ExecutableWork stateWork =
            Work(
                7,
                [
                    Task(
                        "task-a",
                        "plan-one")
                ]);

        ExecutableWork queryWork =
            Work(
                7,
                [
                    Task(
                        "task-a",
                        "plan-two")
                ]);

        WorkflowState state =
            RunningState(
                stateWork);

        Assert.Equal(
            [
                "task-a"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                queryWork,
                state));
    }

    [Fact]
    public void CreatedWorkflow_ReturnsNoReadyTasks()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            WorkflowStateMachine.Create(
                work);

        Assert.Empty(
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void CreatedWorkflow_SelectsNull()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            WorkflowStateMachine.Create(
                work);

        Assert.Null(
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    [Theory]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Cancelled)]
    public void TerminalWorkflow_ReturnsNoReadyTasks(
        WorkflowStatus status_)
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            TerminalState(
                work,
                status_);

        Assert.Empty(
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Theory]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Cancelled)]
    public void TerminalWorkflow_SelectsNull(
        WorkflowStatus status_)
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            TerminalState(
                work,
                status_);

        Assert.Null(
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    [Fact]
    public void ZeroTaskRunningWorkflow_ReturnsEmptyReadySet()
    {
        ExecutableWork work =
            Work(
                []);

        WorkflowState state =
            RunningState(
                work);

        Assert.Empty(
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));

        Assert.Null(
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    [Fact]
    public void PendingRootTask_IsReady()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            RunningState(
                work);

        Assert.Equal(
            [
                "task-a"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Theory]
    [InlineData(WorkflowStepStatus.Running)]
    [InlineData(WorkflowStepStatus.AwaitingValidation)]
    [InlineData(WorkflowStepStatus.Blocked)]
    [InlineData(WorkflowStepStatus.Failed)]
    [InlineData(WorkflowStepStatus.Completed)]
    [InlineData(WorkflowStepStatus.Cancelled)]
    public void NonPendingCandidate_IsNeverReady(
        WorkflowStepStatus status_)
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            SetStatus(
                RunningState(
                    work),
                "task-a",
                status_);

        Assert.Empty(
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Theory]
    [InlineData(WorkflowStepStatus.Pending)]
    [InlineData(WorkflowStepStatus.Running)]
    [InlineData(WorkflowStepStatus.AwaitingValidation)]
    [InlineData(WorkflowStepStatus.Blocked)]
    [InlineData(WorkflowStepStatus.Failed)]
    [InlineData(WorkflowStepStatus.Cancelled)]
    public void IncompleteImmediatePrerequisite_PreventsReadiness(
        WorkflowStepStatus prerequisiteStatus_)
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"));

        WorkflowState state =
            RunningState(
                work);

        if (prerequisiteStatus_ !=
            WorkflowStepStatus.Pending)
        {
            state =
                SetStatus(
                    state,
                    "task-a",
                    prerequisiteStatus_);
        }

        Assert.DoesNotContain(
            "task-b",
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void CompletedImmediatePrerequisite_AllowsReadiness()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"));

        WorkflowState state =
            CompleteStep(
                RunningState(
                    work),
                "task-a");

        Assert.Equal(
            [
                "task-b"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void IncompleteTransitivePrerequisite_PreventsReadiness()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b",
                    "task-c"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-b"));

        WorkflowState state =
            CompleteStep(
                RunningState(
                    work),
                "task-b");

        Assert.Equal(
            [
                "task-a"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void CompletedTransitivePrerequisites_AllowReadiness()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b",
                    "task-c"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-b"));

        WorkflowState state =
            RunningState(
                work);

        state =
            CompleteStep(
                state,
                "task-a");

        state =
            CompleteStep(
                state,
                "task-b");

        Assert.Equal(
            [
                "task-c"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void MultiplePrerequisites_AllCompleted_AllowsReadiness()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b",
                    "task-c"
                ],
                new ExecutableTaskDependency(
                    "task-c",
                    "task-a"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-b"));

        WorkflowState state =
            RunningState(
                work);

        state =
            CompleteStep(
                state,
                "task-a");

        state =
            CompleteStep(
                state,
                "task-b");

        Assert.Equal(
            [
                "task-c"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void MultiplePrerequisites_OneIncomplete_PreventsReadiness()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b",
                    "task-c"
                ],
                new ExecutableTaskDependency(
                    "task-c",
                    "task-a"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-b"));

        WorkflowState state =
            CompleteStep(
                RunningState(
                    work),
                "task-a");

        IReadOnlyList<string> ready =
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state);

        Assert.Contains(
            "task-b",
            ready);

        Assert.DoesNotContain(
            "task-c",
            ready);
    }

    [Fact]
    public void DisconnectedRoots_AreIndependentlyReady()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b",
                    "task-c"
                ],
                new ExecutableTaskDependency(
                    "task-c",
                    "task-a"));

        WorkflowState state =
            RunningState(
                work);

        Assert.Equal(
            [
                "task-a",
                "task-b"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void ExplicitTransitiveEdge_DoesNotChangeCorrectReadiness()
    {
        ExecutableWork firstWork =
            Work(
                [
                    "task-a",
                    "task-b",
                    "task-c"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-b"));

        ExecutableWork secondWork =
            Work(
                [
                    "task-a",
                    "task-b",
                    "task-c"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-b"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-a"));

        WorkflowState firstState =
            RunningState(
                firstWork);

        firstState =
            CompleteStep(
                firstState,
                "task-a");

        firstState =
            CompleteStep(
                firstState,
                "task-b");

        WorkflowState secondState =
            RunningState(
                secondWork);

        secondState =
            CompleteStep(
                secondState,
                "task-a");

        secondState =
            CompleteStep(
                secondState,
                "task-b");

        Assert.Equal(
            WorkflowTaskDagScheduler
                .GetReadyTaskIds(
                    firstWork,
                    firstState)
                .ToArray(),
            WorkflowTaskDagScheduler
                .GetReadyTaskIds(
                    secondWork,
                    secondState)
                .ToArray());
    }

    [Fact]
    public void ReadyOrdering_FollowsExecutableWorkTaskOrder()
    {
        ExecutableWork work =
            Work(
                [
                    "task-z",
                    "task-a",
                    "task-m"
                ]);

        WorkflowState state =
            RunningState(
                work);

        Assert.Equal(
            [
                "task-z",
                "task-a",
                "task-m"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));
    }

    [Fact]
    public void DependencyDeclarationOrder_DoesNotChangeReadyOrdering()
    {
        ExecutableWork firstWork =
            Work(
                [
                    "task-z",
                    "task-a",
                    "task-c",
                    "task-d"
                ],
                new ExecutableTaskDependency(
                    "task-c",
                    "task-z"),
                new ExecutableTaskDependency(
                    "task-d",
                    "task-a"));

        ExecutableWork secondWork =
            Work(
                [
                    "task-z",
                    "task-a",
                    "task-c",
                    "task-d"
                ],
                new ExecutableTaskDependency(
                    "task-d",
                    "task-a"),
                new ExecutableTaskDependency(
                    "task-c",
                    "task-z"));

        Assert.Equal(
            [
                "task-z",
                "task-a"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                firstWork,
                RunningState(
                    firstWork)));

        Assert.Equal(
            [
                "task-z",
                "task-a"
            ],
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                secondWork,
                RunningState(
                    secondWork)));
    }

    [Fact]
    public void ReadyCollection_IsReadOnly()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            RunningState(
                work);

        IReadOnlyList<string> ready =
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state);

        IList<string> list =
            Assert.IsAssignableFrom<IList<string>>(
                ready);

        Assert.True(
            list.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                list.Add(
                    "task-b"));
    }

    [Fact]
    public void SelectNextTaskId_SelectsFirstReadyTask()
    {
        ExecutableWork work =
            Work(
                [
                    "task-z",
                    "task-a"
                ]);

        WorkflowState state =
            RunningState(
                work);

        Assert.Equal(
            "task-z",
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    [Fact]
    public void SelectNextTaskId_NoReadyTask_ReturnsNull()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"));

        WorkflowState state =
            SetStatus(
                RunningState(
                    work),
                "task-a",
                WorkflowStepStatus.Blocked);

        Assert.Null(
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    [Fact]
    public void RunningStep_SuppressesSelection()
    {
        AssertStatusSuppressesSelection(
            WorkflowStepStatus.Running);
    }

    [Fact]
    public void AwaitingValidationStep_SuppressesSelection()
    {
        AssertStatusSuppressesSelection(
            WorkflowStepStatus.AwaitingValidation);
    }

    [Fact]
    public void FailedStep_SuppressesSelection()
    {
        AssertStatusSuppressesSelection(
            WorkflowStepStatus.Failed);
    }

    [Fact]
    public void CancelledStep_SuppressesSelection()
    {
        AssertStatusSuppressesSelection(
            WorkflowStepStatus.Cancelled);
    }

    [Fact]
    public void BlockedStep_DoesNotSuppressIndependentReadyTask()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ]);

        WorkflowState state =
            SetStatus(
                RunningState(
                    work),
                "task-a",
                WorkflowStepStatus.Blocked);

        Assert.Equal(
            "task-b",
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    [Fact]
    public void BlockedTask_IsNotSelected()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ]);

        WorkflowState state =
            SetStatus(
                RunningState(
                    work),
                "task-a",
                WorkflowStepStatus.Blocked);

        Assert.DoesNotContain(
            "task-a",
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state));

        Assert.Equal(
            "task-b",
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    [Fact]
    public void EqualInputs_ProduceIdenticalReadySetsAndSelection()
    {
        ExecutableWork firstWork =
            Work(
                [
                    "task-z",
                    "task-a",
                    "task-b"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-z"));

        ExecutableWork secondWork =
            Work(
                [
                    "task-z",
                    "task-a",
                    "task-b"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-z"));

        WorkflowState firstState =
            RunningState(
                firstWork);

        WorkflowState secondState =
            RunningState(
                secondWork);

        Assert.Equal(
            WorkflowTaskDagScheduler
                .GetReadyTaskIds(
                    firstWork,
                    firstState)
                .ToArray(),
            WorkflowTaskDagScheduler
                .GetReadyTaskIds(
                    secondWork,
                    secondState)
                .ToArray());

        Assert.Equal(
            WorkflowTaskDagScheduler.SelectNextTaskId(
                firstWork,
                firstState),
            WorkflowTaskDagScheduler.SelectNextTaskId(
                secondWork,
                secondState));
    }

    [Fact]
    public void SchedulingQueries_DoNotMutateWorkflowState()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ]);

        WorkflowState state =
            RunningState(
                work);

        (string TaskId, WorkflowStepStatus Status)[] before =
            state.Steps
                .Select(
                    step_ =>
                        (
                            step_.TaskId,
                            step_.Status
                        ))
                .ToArray();

        _ =
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state);

        _ =
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state);

        Assert.Equal(
            before,
            state.Steps
                .Select(
                    step_ =>
                        (
                            step_.TaskId,
                            step_.Status
                        ))
                .ToArray());
    }

    [Fact]
    public void SchedulingQueries_DoNotMutateExecutableWork()
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ],
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a"));

        WorkflowState state =
            RunningState(
                work);

        ExecutableTask[] tasksBefore =
            work.Tasks.ToArray();

        ExecutableTaskDependency[] dependenciesBefore =
            work.Dependencies.ToArray();

        _ =
            WorkflowTaskDagScheduler.GetReadyTaskIds(
                work,
                state);

        _ =
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state);

        Assert.Equal(
            tasksBefore,
            work.Tasks.ToArray());

        Assert.Equal(
            dependenciesBefore,
            work.Dependencies.ToArray());
    }

    [Fact]
    public void SelectNextTaskId_DoesNotTransitionSelectedStep()
    {
        ExecutableWork work =
            Work(
                ["task-a"]);

        WorkflowState state =
            RunningState(
                work);

        string? selected =
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state);

        Assert.Equal(
            "task-a",
            selected);

        Assert.Equal(
            WorkflowStepStatus.Pending,
            state.Steps[0].Status);
    }

    private static void AssertStatusSuppressesSelection(
        WorkflowStepStatus status_)
    {
        ExecutableWork work =
            Work(
                [
                    "task-a",
                    "task-b"
                ]);

        WorkflowState state =
            SetStatus(
                RunningState(
                    work),
                "task-a",
                status_);

        Assert.Null(
            WorkflowTaskDagScheduler.SelectNextTaskId(
                work,
                state));
    }

    private static ExecutableTask Task(
        string id_,
        string? sourcePlanStepId_ = null)
    {
        return new ExecutableTask(
            id_,
            sourcePlanStepId_ ??
                $"plan-{id_}",
            $"instruction-{id_}");
    }

    private static ExecutableWork Work(
        string[] taskIds_,
        params ExecutableTaskDependency[] dependencies_)
    {
        ExecutableTask[] tasks =
            taskIds_
                .Select(
                    taskId_ =>
                        Task(
                            taskId_))
                .ToArray();

        return Work(
            7,
            tasks,
            dependencies_);
    }

    private static ExecutableWork Work(
        int revision_,
        ExecutableTask[] tasks_,
        params ExecutableTaskDependency[] dependencies_)
    {
        return new ExecutableWork(
            revision_,
            tasks_,
            dependencies_);
    }

    private static WorkflowState RunningState(
        ExecutableWork work_)
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                work_);

        return WorkflowStateMachine.TransitionWorkflow(
            created,
            WorkflowStatus.Running);
    }

    private static WorkflowState SetStatus(
        WorkflowState state_,
        string taskId_,
        WorkflowStepStatus status_)
    {
        return status_ switch
        {
            WorkflowStepStatus.Pending =>
                state_,

            WorkflowStepStatus.Running =>
                WorkflowStateMachine.TransitionStep(
                    state_,
                    taskId_,
                    WorkflowStepStatus.Running),

            WorkflowStepStatus.AwaitingValidation =>
                WorkflowStateMachine.TransitionStep(
                    WorkflowStateMachine.TransitionStep(
                        state_,
                        taskId_,
                        WorkflowStepStatus.Running),
                    taskId_,
                    WorkflowStepStatus.AwaitingValidation),

            WorkflowStepStatus.Blocked =>
                WorkflowStateMachine.TransitionStep(
                    WorkflowStateMachine.TransitionStep(
                        state_,
                        taskId_,
                        WorkflowStepStatus.Running),
                    taskId_,
                    WorkflowStepStatus.Blocked),

            WorkflowStepStatus.Failed =>
                WorkflowStateMachine.TransitionStep(
                    WorkflowStateMachine.TransitionStep(
                        state_,
                        taskId_,
                        WorkflowStepStatus.Running),
                    taskId_,
                    WorkflowStepStatus.Failed),

            WorkflowStepStatus.Completed =>
                CompleteStep(
                    state_,
                    taskId_),

            WorkflowStepStatus.Cancelled =>
                WorkflowStateMachine.TransitionStep(
                    state_,
                    taskId_,
                    WorkflowStepStatus.Cancelled),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(status_),
                    status_,
                    null)
        };
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

    private static WorkflowState TerminalState(
        ExecutableWork work_,
        WorkflowStatus status_)
    {
        WorkflowState state =
            RunningState(
                work_);

        if (status_ ==
            WorkflowStatus.Completed)
        {
            for (int index = 0;
                 index < work_.Tasks.Count;
                 index++)
            {
                state =
                    CompleteStep(
                        state,
                        work_.Tasks[index].Id);
            }

            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Completed);
        }

        if (status_ ==
            WorkflowStatus.Failed)
        {
            if (work_.Tasks.Count == 0)
            {
                throw new InvalidOperationException(
                    "A failed workflow test requires at least one task.");
            }

            state =
                SetStatus(
                    state,
                    work_.Tasks[0].Id,
                    WorkflowStepStatus.Failed);

            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Failed);
        }

        if (status_ ==
            WorkflowStatus.Cancelled)
        {
            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Cancelled);
        }

        throw new ArgumentOutOfRangeException(
            nameof(status_),
            status_,
            null);
    }
}

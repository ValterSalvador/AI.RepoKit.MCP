namespace AiRepoKit.Orchestration.Tests;

using System.Text;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using Xunit;

public sealed class WorkflowPersistenceStoreTests
{
    [Fact]
    public void WorkflowId_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new WorkflowId(
                    null!));
    }

    [Fact]
    public void WorkflowId_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowId(
                    string.Empty));
    }

    [Fact]
    public void WorkflowId_RejectsWhitespace()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowId(
                    "   "));
    }

    [Fact]
    public void WorkflowId_AcceptsExactly64Utf8Bytes()
    {
        string value =
            new(
                'a',
                64);

        WorkflowId workflowId =
            new(
                value);

        Assert.Equal(
            value,
            workflowId.Value);
    }

    [Fact]
    public void WorkflowId_RejectsMoreThan64Utf8Bytes()
    {
        string value =
            new(
                'a',
                65);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new WorkflowId(
                    value));
    }

    [Fact]
    public void WorkflowId_PreservesExactValue()
    {
        WorkflowId workflowId =
            new(
                " Flow-A ");

        Assert.Equal(
            " Flow-A ",
            workflowId.Value);
    }

    [Fact]
    public void WorkflowId_UsesOrdinalCaseSensitiveIdentity()
    {
        WorkflowId upper =
            new(
                "Flow-A");

        WorkflowId lower =
            new(
                "flow-a");

        Assert.NotEqual(
            upper,
            lower);
    }

    [Fact]
    public void Store_RejectsMissingStorageRoot()
    {
        string missing =
            Path.Combine(
                Path.GetTempPath(),
                $"airepokit-missing-{Guid.NewGuid():N}");

        Assert.Throws<DirectoryNotFoundException>(
            () =>
                new WorkflowPersistenceStore(
                    missing,
                    new WorkflowId(
                        "workflow-a")));
    }

    [Fact]
    public void Store_ExposesNormalizedRootAndWorkflowId()
    {
        using TempDirectory temp =
            new();

        WorkflowId workflowId =
            new(
                "workflow-a");

        WorkflowPersistenceStore store =
            new(
                temp.Path,
                workflowId);

        Assert.Equal(
            Path.GetFullPath(
                temp.Path),
            store.StorageRoot);

        Assert.Same(
            workflowId,
            store.WorkflowId);
    }

    [Fact]
    public void Load_WhenAbsent_ReturnsNull()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        Assert.Null(
            store.Load());
    }

    [Fact]
    public void ReadEvents_WhenAbsent_ReturnsReadOnlyEmptyCollection()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        IReadOnlyList<WorkflowExecutionEvent> events =
            store.ReadEvents();

        Assert.Empty(
            events);

        IList<WorkflowExecutionEvent> list =
            Assert.IsAssignableFrom<IList<WorkflowExecutionEvent>>(
                events);

        Assert.True(
            list.IsReadOnly);
    }

    [Fact]
    public void Initialize_RejectsNonCreatedState()
    {
        using TempDirectory temp =
            new();

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                Store(
                    temp.Path)
                    .Initialize(
                        running));
    }

    [Fact]
    public void Initialize_ProducesRevisionOneAndInitializationEvent()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceSnapshot snapshot =
            Store(
                temp.Path)
                .Initialize(
                    CreatedState(
                        7,
                        "task-a"));

        Assert.Equal(
            1,
            snapshot.Revision);

        Assert.Equal(
            WorkflowExecutionEventKind.WorkflowInitialized,
            snapshot.LastEvent.Kind);

        Assert.Equal(
            1,
            snapshot.LastEvent.Sequence);

        Assert.Equal(
            WorkflowStatus.Created,
            snapshot.LastEvent.TargetWorkflowStatus);

        Assert.Null(
            snapshot.LastEvent.TaskId);

        Assert.Equal(
            snapshot.WorkflowId,
            snapshot.LastEvent.WorkflowId);
    }

    [Fact]
    public void Initialize_AllowsZeroTaskCreatedWorkflow()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceSnapshot snapshot =
            Store(
                temp.Path)
                .Initialize(
                    CreatedState(
                        7));

        Assert.Empty(
            snapshot.State.Steps);

        Assert.Equal(
            WorkflowStatus.Created,
            snapshot.State.Status);
    }

    [Fact]
    public void Initialize_RejectsDuplicateInitialization()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState state =
            CreatedState(
                7,
                "task-a");

        _ =
            store.Initialize(
                state);

        Assert.Throws<InvalidOperationException>(
            () =>
                store.Initialize(
                    state));
    }

    [Fact]
    public void PersistedState_SurvivesNewStoreInstance()
    {
        using TempDirectory temp =
            new();

        WorkflowId workflowId =
            new(
                "workflow-a");

        WorkflowState state =
            CreatedState(
                7,
                "task-b",
                "Task-A",
                "task-a");

        _ =
            new WorkflowPersistenceStore(
                temp.Path,
                workflowId)
                .Initialize(
                    state);

        WorkflowPersistenceSnapshot? loaded =
            new WorkflowPersistenceStore(
                temp.Path,
                new WorkflowId(
                    "workflow-a"))
                .Load();

        Assert.NotNull(
            loaded);

        Assert.Equal(
            state,
            loaded.State);
    }

    [Fact]
    public void Append_WorkflowStatusTransition_DerivesExactEvent()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        _ =
            store.Initialize(
                created);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        WorkflowPersistenceSnapshot snapshot =
            store.Append(
                running,
                expectedRevision_: 1);

        Assert.Equal(
            2,
            snapshot.Revision);

        Assert.Equal(
            WorkflowExecutionEventKind.WorkflowStatusTransitioned,
            snapshot.LastEvent.Kind);

        Assert.Null(
            snapshot.LastEvent.TaskId);

        Assert.Equal(
            WorkflowStatus.Created,
            snapshot.LastEvent.PreviousWorkflowStatus);

        Assert.Equal(
            WorkflowStatus.Running,
            snapshot.LastEvent.TargetWorkflowStatus);

        Assert.Null(
            snapshot.LastEvent.PreviousStepStatus);

        Assert.Null(
            snapshot.LastEvent.TargetStepStatus);
    }

    [Fact]
    public void Append_StepStatusTransition_DerivesExactEvent()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "Task-A");

        _ =
            store.Initialize(
                created);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        _ =
            store.Append(
                running,
                expectedRevision_: 1);

        WorkflowState stepRunning =
            WorkflowStateMachine.TransitionStep(
                running,
                "Task-A",
                WorkflowStepStatus.Running);

        WorkflowPersistenceSnapshot snapshot =
            store.Append(
                stepRunning,
                expectedRevision_: 2);

        Assert.Equal(
            3,
            snapshot.Revision);

        Assert.Equal(
            WorkflowExecutionEventKind.StepStatusTransitioned,
            snapshot.LastEvent.Kind);

        Assert.Equal(
            "Task-A",
            snapshot.LastEvent.TaskId);

        Assert.Equal(
            WorkflowStepStatus.Pending,
            snapshot.LastEvent.PreviousStepStatus);

        Assert.Equal(
            WorkflowStepStatus.Running,
            snapshot.LastEvent.TargetStepStatus);

        Assert.Null(
            snapshot.LastEvent.PreviousWorkflowStatus);

        Assert.Null(
            snapshot.LastEvent.TargetWorkflowStatus);
    }

    [Fact]
    public void Append_IncrementsRevisionAndEventSequenceTogether()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        WorkflowPersistenceSnapshot first =
            store.Initialize(
                created);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        WorkflowPersistenceSnapshot second =
            store.Append(
                running,
                first.Revision);

        WorkflowState stepRunning =
            WorkflowStateMachine.TransitionStep(
                running,
                "task-a",
                WorkflowStepStatus.Running);

        WorkflowPersistenceSnapshot third =
            store.Append(
                stepRunning,
                second.Revision);

        Assert.Equal(
            [
                1L,
                2L,
                3L
            ],
            [
                first.Revision,
                second.Revision,
                third.Revision
            ]);

        Assert.Equal(
            [
                1L,
                2L,
                3L
            ],
            [
                first.LastEvent.Sequence,
                second.LastEvent.Sequence,
                third.LastEvent.Sequence
            ]);
    }

    [Fact]
    public void ReadEvents_PreservesAscendingSequenceOrder()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        _ =
            store.Initialize(
                created);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        _ =
            store.Append(
                running,
                1);

        WorkflowState stepRunning =
            WorkflowStateMachine.TransitionStep(
                running,
                "task-a",
                WorkflowStepStatus.Running);

        _ =
            store.Append(
                stepRunning,
                2);

        Assert.Equal(
            [
                1L,
                2L,
                3L
            ],
            store
                .ReadEvents()
                .Select(
                    event_ =>
                        event_.Sequence)
                .ToArray());
    }

    [Fact]
    public void ReadEvents_ReturnsReadOnlyCollection()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        _ =
            store.Initialize(
                CreatedState(
                    7,
                    "task-a"));

        IReadOnlyList<WorkflowExecutionEvent> events =
            store.ReadEvents();

        IList<WorkflowExecutionEvent> list =
            Assert.IsAssignableFrom<IList<WorkflowExecutionEvent>>(
                events);

        Assert.True(
            list.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                list.Clear());
    }

    [Fact]
    public void Append_RejectsStaleExpectedRevisionWithoutMutation()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        _ =
            store.Initialize(
                created);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                store.Append(
                    running,
                    expectedRevision_: 2));

        Assert.Single(
            RecordFiles(
                temp.Path,
                store.WorkflowId));

        Assert.Equal(
            1,
            store.Load()!.Revision);
    }

    [Fact]
    public void Append_RejectsNoOpState()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        _ =
            store.Initialize(
                created);

        Assert.Throws<InvalidOperationException>(
            () =>
                store.Append(
                    created,
                    1));
    }

    [Fact]
    public void Append_RejectsSkippedStepTransition()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        _ =
            store.Initialize(
                created);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        _ =
            store.Append(
                running,
                1);

        WorkflowState stepRunning =
            WorkflowStateMachine.TransitionStep(
                running,
                "task-a",
                WorkflowStepStatus.Running);

        WorkflowState awaitingValidation =
            WorkflowStateMachine.TransitionStep(
                stepRunning,
                "task-a",
                WorkflowStepStatus.AwaitingValidation);

        Assert.Throws<InvalidOperationException>(
            () =>
                store.Append(
                    awaitingValidation,
                    2));

        Assert.Equal(
            2,
            store.Load()!.Revision);
    }

    [Fact]
    public void Append_RejectsChangedSourceImplementationPlanRevision()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        _ =
            store.Initialize(
                CreatedState(
                    7,
                    "task-a"));

        WorkflowState differentRevisionRunning =
            WorkflowStateMachine.TransitionWorkflow(
                CreatedState(
                    8,
                    "task-a"),
                WorkflowStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                store.Append(
                    differentRevisionRunning,
                    1));
    }

    [Fact]
    public void Append_RejectsChangedTaskIdentity()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        _ =
            store.Initialize(
                CreatedState(
                    7,
                    "task-a",
                    "task-b"));

        WorkflowState changedIdentityRunning =
            WorkflowStateMachine.TransitionWorkflow(
                CreatedState(
                    7,
                    "task-a",
                    "task-c"),
                WorkflowStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                store.Append(
                    changedIdentityRunning,
                    1));
    }

    [Fact]
    public void Append_RejectsChangedTaskOrder()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        _ =
            store.Initialize(
                CreatedState(
                    7,
                    "task-a",
                    "task-b"));

        WorkflowState reorderedRunning =
            WorkflowStateMachine.TransitionWorkflow(
                CreatedState(
                    7,
                    "task-b",
                    "task-a"),
                WorkflowStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                store.Append(
                    reorderedRunning,
                    1));
    }

    [Fact]
    public void Append_DoesNotRewritePreviousCanonicalRecord()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        _ =
            store.Initialize(
                created);

        string firstPath =
            RecordFiles(
                temp.Path,
                store.WorkflowId)
                .Single();

        byte[] before =
            File.ReadAllBytes(
                firstPath);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        _ =
            store.Append(
                running,
                1);

        byte[] after =
            File.ReadAllBytes(
                firstPath);

        Assert.Equal(
            before,
            after);
    }

    [Fact]
    public void CanonicalRecordFilename_IsDeterministicTwentyDigitRevision()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        _ =
            store.Initialize(
                CreatedState(
                    7,
                    "task-a"));

        Assert.Equal(
            "00000000000000000001.json",
            Path.GetFileName(
                RecordFiles(
                    temp.Path,
                    store.WorkflowId)
                    .Single()));
    }

    [Fact]
    public void EqualLogicalInitialization_ProducesIdenticalCanonicalBytes()
    {
        using TempDirectory firstTemp =
            new();

        using TempDirectory secondTemp =
            new();

        WorkflowState state =
            CreatedState(
                7,
                "task-b",
                "Task-A",
                "task-a");

        WorkflowPersistenceStore first =
            Store(
                firstTemp.Path);

        WorkflowPersistenceStore second =
            Store(
                secondTemp.Path);

        _ =
            first.Initialize(
                state);

        _ =
            second.Initialize(
                state);

        byte[] firstBytes =
            File.ReadAllBytes(
                RecordFiles(
                    firstTemp.Path,
                    first.WorkflowId)
                    .Single());

        byte[] secondBytes =
            File.ReadAllBytes(
                RecordFiles(
                    secondTemp.Path,
                    second.WorkflowId)
                    .Single());

        Assert.Equal(
            firstBytes,
            secondBytes);
    }

    [Fact]
    public void CanonicalJson_HasNoTimeGuidOrRandomFields()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        _ =
            store.Initialize(
                CreatedState(
                    7,
                    "task-a"));

        string json =
            File.ReadAllText(
                RecordFiles(
                    temp.Path,
                    store.WorkflowId)
                    .Single(),
                Encoding.UTF8);

        Assert.False(
            json.Contains(
                "timestamp",
                StringComparison.OrdinalIgnoreCase));

        Assert.False(
            json.Contains(
                "guid",
                StringComparison.OrdinalIgnoreCase));

        Assert.False(
            json.Contains(
                "random",
                StringComparison.OrdinalIgnoreCase));

        Assert.False(
            json.Contains(
                "clock",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CaseDistinctWorkflowIds_UseDistinctEncodedDirectories()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore upper =
            new(
                temp.Path,
                new WorkflowId(
                    "Case-A"));

        WorkflowPersistenceStore lower =
            new(
                temp.Path,
                new WorkflowId(
                    "case-a"));

        _ =
            upper.Initialize(
                CreatedState(
                    7,
                    "task-a"));

        _ =
            lower.Initialize(
                CreatedState(
                    7,
                    "task-a"));

        string upperDirectory =
            Path.GetDirectoryName(
                RecordFiles(
                    temp.Path,
                    upper.WorkflowId)
                    .Single())!;

        string lowerDirectory =
            Path.GetDirectoryName(
                RecordFiles(
                    temp.Path,
                    lower.WorkflowId)
                    .Single())!;

        Assert.False(
            string.Equals(
                upperDirectory,
                lowerDirectory,
                StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            upper.WorkflowId.Value,
            upperDirectory,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyWorkflow_CanPersistCompleteLifecycle()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            Store(
                temp.Path);

        WorkflowState created =
            CreatedState(
                7);

        _ =
            store.Initialize(
                created);

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        _ =
            store.Append(
                running,
                1);

        WorkflowState completed =
            WorkflowStateMachine.TransitionWorkflow(
                running,
                WorkflowStatus.Completed);

        WorkflowPersistenceSnapshot snapshot =
            store.Append(
                completed,
                2);

        Assert.Equal(
            WorkflowStatus.Completed,
            snapshot.State.Status);

        Assert.Equal(
            3,
            snapshot.Revision);
    }

    [Fact]
    public void Append_RejectsMissingWorkflow()
    {
        using TempDirectory temp =
            new();

        WorkflowState created =
            CreatedState(
                7,
                "task-a");

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        Assert.Throws<InvalidOperationException>(
            () =>
                Store(
                    temp.Path)
                    .Append(
                        running,
                        1));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Append_RejectsNonPositiveExpectedRevision(
        long expectedRevision_)
    {
        using TempDirectory temp =
            new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                Store(
                    temp.Path)
                    .Append(
                        CreatedState(
                            7,
                            "task-a"),
                        expectedRevision_));
    }

    private static WorkflowPersistenceStore Store(
        string root_)
    {
        return new WorkflowPersistenceStore(
            root_,
            new WorkflowId(
                "workflow-a"));
    }

    private static WorkflowState CreatedState(
        int revision_,
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

        return WorkflowStateMachine.Create(
            new ExecutableWork(
                revision_,
                tasks));
    }

    private static string[] RecordFiles(
        string root_,
        WorkflowId workflowId_)
    {
        string encoded =
            Convert
                .ToHexString(
                    Encoding.UTF8.GetBytes(
                        workflowId_.Value))
                .ToLowerInvariant();

        string directory =
            Path.Combine(
                Path.GetFullPath(
                    root_),
                "workflows",
                encoded,
                "records");

        if (!Directory.Exists(
                directory))
        {
            return [];
        }

        return Directory
            .GetFiles(
                directory)
            .OrderBy(
                path_ =>
                    path_,
                StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class TempDirectory :
        IDisposable
    {
        public string Path
        {
            get;
        }

        public TempDirectory()
        {
            this.Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"airepokit-orchestration-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                this.Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(
                    this.Path,
                    recursive: true);
            }
            catch
            {
            }
        }
    }
}

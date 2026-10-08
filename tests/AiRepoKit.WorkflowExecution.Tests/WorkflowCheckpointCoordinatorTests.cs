namespace AiRepoKit.WorkflowExecution.Tests;

using System.Collections;
using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Xunit;

public sealed class WorkflowCheckpointCoordinatorTests : IDisposable
{
    private readonly string _tempDirectory;

    public WorkflowCheckpointCoordinatorTests()
    {
        this._tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "repokit-p09-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDirectory))
        {
            try
            {
                Directory.Delete(this._tempDirectory, recursive: true);
            }
            catch
            {
                // Best effort test cleanup
            }
        }
    }

    // =========================================================================
    // Category 1: Construction & Registry Validation
    // =========================================================================

    [Fact]
    public void Constructor_NullStore_ThrowsArgumentNullException()
    {
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowCheckpointCoordinator(
                null!,
                [agent],
                [],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_NullExecutors_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                null!,
                [],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_EmptyExecutors_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [],
                [],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_NullExecutorElement_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [null!],
                [],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_NullValidators_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent],
                null!,
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_EmptyValidators_Allowed()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        WorkflowCheckpointCoordinator coordinator = new(
            store,
            [agent],
            [],
            policy,
            reconciler);

        Assert.NotNull(coordinator);
    }

    [Fact]
    public void Constructor_NullValidatorElement_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent],
                [null!],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_NullPolicy_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent],
                [],
                null!,
                reconciler));
    }

    [Fact]
    public void Constructor_NullReconciler_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent],
                [],
                policy,
                null!));
    }

    [Fact]
    public void Constructor_DuplicateProviderId_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent1 = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeAgent agent2 = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent1, agent2],
                [],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_DuplicateProviderId_OrdinalComparison_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent1 = new(new AgentProviderId("my-provider"), AgentCapabilitySet.Empty);
        FakeAgent agent2 = new(new AgentProviderId("my-provider"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent1, agent2],
                [],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_DuplicateValidationStrategy_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val1 = new(ValidationStrategy.Test);
        FakeValidator val2 = new(ValidationStrategy.Test);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent],
                [val1, val2],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_UndefinedValidationStrategy_ThrowsArgumentOutOfRangeException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new((ValidationStrategy) 999);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowCheckpointCoordinator(
                store,
                [agent],
                [val],
                policy,
                reconciler));
    }

    [Fact]
    public void Constructor_SnapshotsExecutors_CallerMutationDoesNotAffectCoordinator()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        List<IAgentExecutor> list = [agent];
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        WorkflowCheckpointCoordinator coordinator = new(
            store,
            list,
            [],
            policy,
            reconciler);

        list.Clear();
        Assert.NotNull(coordinator);
    }

    [Fact]
    public void Constructor_SnapshotsValidators_CallerMutationDoesNotAffectCoordinator()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator validator = new(ValidationStrategy.Test);
        List<IValidationExecutor> list = [validator];
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        WorkflowCheckpointCoordinator coordinator = new(
            store,
            [agent],
            list,
            policy,
            reconciler);

        list.Clear();
        Assert.NotNull(coordinator);
    }

    [Fact]
    public void Constructor_EnumeratesExecutorsExactlyOnce()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        SingleEnumerationEnumerable<IAgentExecutor> enumerable = new(agent);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        _ = new WorkflowCheckpointCoordinator(
            store,
            enumerable,
            [],
            policy,
            reconciler);

        Assert.Equal(1, enumerable.EnumerationCount);
    }

    [Fact]
    public void Constructor_EnumeratesValidatorsExactlyOnce()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator validator = new(ValidationStrategy.Test);
        SingleEnumerationEnumerable<IValidationExecutor> enumerable = new(validator);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        _ = new WorkflowCheckpointCoordinator(
            store,
            [agent],
            enumerable,
            policy,
            reconciler);

        Assert.Equal(1, enumerable.EnumerationCount);
    }

    // =========================================================================
    // Category 2: Canonical Preconditions & Drift
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_NullWork_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.ExecuteAsync(null!, envelope));
    }

    [Fact]
    public async Task ExecuteAsync_NullEnvelope_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.ExecuteAsync(work, null!));
    }

    [Fact]
    public async Task ExecuteAsync_UninitializedStore_ThrowsInvalidOperationException()
    {
        WorkflowPersistenceStore store = new(this._tempDirectory, new WorkflowId("wf-1"));
        ExecutableWork work = CreateWork("task-1", 1);
        ExecutionEnvelope envelope = Envelope(work);
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope));
    }

    [Fact]
    public async Task ExecuteAsync_SourceRevisionMismatch_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) = this.CreateInitializedSetup(revision: 1);
        ExecutableWork workWithDiffRev = CreateWork("task-1", 2);
        ExecutionEnvelope envWithDiffRev = Envelope(workWithDiffRev);
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(workWithDiffRev, envWithDiffRev));
    }

    [Fact]
    public async Task ExecuteAsync_TaskNotInWork_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup(taskId: "task-1");
        ExecutableWork workWithBoth = new(
            1,
            [
                new ExecutableTask("task-1", "s1", "i1"),
                new ExecutableTask("task-2", "s2", "i2")
            ]);

        ExecutableWork workWithOnlyTask1 = CreateWork("task-1", 1);

        ExecutionEnvelope envForTask2 = ExecutionEnvelope.Create(
            workWithBoth,
            new CompiledPrompt("task-2", "content"),
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.ExecuteAsync(workWithOnlyTask1, envForTask2));
    }

    [Fact]
    public async Task ExecuteAsync_TaskNotInCanonicalState_ThrowsArgumentException()
    {
        ExecutableWork workWithTwoTasks = new(
            1,
            [
                new ExecutableTask("task-1", "s1", "i1"),
                new ExecutableTask("task-2", "s2", "i2")
            ]);

        WorkflowId wfId = new("wf-1");
        WorkflowPersistenceStore store = new(this._tempDirectory, wfId);
        ExecutableWork workSingle = CreateWork("task-1", 1);
        WorkflowState created = WorkflowStateMachine.Create(workSingle);
        store.Initialize(created);
        WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWf, 1);
        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWf, "task-1", WorkflowStepStatus.Running);
        store.Append(runningStep, 2);

        ExecutionEnvelope env = ExecutionEnvelope.Create(
            workWithTwoTasks,
            new CompiledPrompt("task-2", "content"),
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.ExecuteAsync(workWithTwoTasks, env));
    }

    [Fact]
    public async Task ExecuteAsync_SessionReferenceNotNull_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, _) = this.CreateInitializedSetup();
        ExecutionEnvelope envWithSession = ExecutionEnvelope.Create(
            work,
            new CompiledPrompt("task-1", "content"),
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: new AgentSessionReference("session-1"),
            structuredOutput_: null,
            timeout_: null);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envWithSession));
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowNotRunning_Created_ThrowsInvalidOperationException()
    {
        WorkflowId wfId = new("wf-1");
        WorkflowPersistenceStore store = new(this._tempDirectory, wfId);
        ExecutableWork work = CreateWork("task-1", 1);
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);
        ExecutionEnvelope env = Envelope(work);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowNotRunning_Completed_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snap = store.Load()!;
        WorkflowState awaitingStep = WorkflowStateMachine.TransitionStep(snap.State, "task-1", WorkflowStepStatus.AwaitingValidation);
        store.Append(awaitingStep, snap.Revision);
        WorkflowPersistenceSnapshot snapAwaiting = store.Load()!;
        WorkflowState completedStep = WorkflowStateMachine.TransitionStep(snapAwaiting.State, "task-1", WorkflowStepStatus.Completed);
        store.Append(completedStep, snapAwaiting.Revision);
        WorkflowPersistenceSnapshot snapCompleted = store.Load()!;
        WorkflowState completedWf = WorkflowStateMachine.TransitionWorkflow(snapCompleted.State, WorkflowStatus.Completed);
        store.Append(completedWf, snapCompleted.Revision);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task ExecuteAsync_WorkflowNotRunning_Failed_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snap = store.Load()!;
        WorkflowState failedStep = WorkflowStateMachine.TransitionStep(snap.State, "task-1", WorkflowStepStatus.Failed);
        store.Append(failedStep, snap.Revision);
        WorkflowPersistenceSnapshot snap2 = store.Load()!;
        WorkflowState failedWf = WorkflowStateMachine.TransitionWorkflow(snap2.State, WorkflowStatus.Failed);
        store.Append(failedWf, snap2.Revision);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task ExecuteAsync_TargetStepNotRunning_Pending_ThrowsInvalidOperationException()
    {
        WorkflowId wfId = new("wf-1");
        WorkflowPersistenceStore store = new(this._tempDirectory, wfId);
        ExecutableWork work = CreateWork("task-1", 1);
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);
        WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWf, 1);
        ExecutionEnvelope env = Envelope(work);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task ExecuteAsync_MissingValidator_FailsClosedBeforeFirstCheckpointWrite()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        // Zero validators registered
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));

        IReadOnlyList<long> candidateRevisions = WorkflowCheckpointJournal.DiscoverCandidateRevisions(
            store.StorageRoot,
            store.WorkflowId,
            "task-1");

        Assert.Empty(candidateRevisions);
    }

    [Fact]
    public async Task ExecuteAsync_NoEligibleAgent_FailsClosedBeforeFirstCheckpointWrite()
    {
        AgentRequirement req = new("task-1", new AgentCapabilitySet([new AgentCapability("special-cap")]));
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            agentRequirements: [req]);

        AgentCapabilitySet agentCaps = AgentCapabilitySet.Empty;
        FakeAgent agent = new(new AgentProviderId("prov-1"), agentCaps);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));

        IReadOnlyList<long> candidateRevisions = WorkflowCheckpointJournal.DiscoverCandidateRevisions(
            store.StorageRoot,
            store.WorkflowId,
            "task-1");

        Assert.Empty(candidateRevisions);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationBeforeExecution_ThrowsOperationCanceledException_NoJournalWritten()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.ExecuteAsync(work, env, cts.Token));

        IReadOnlyList<long> candidateRevisions = WorkflowCheckpointJournal.DiscoverCandidateRevisions(
            store.StorageRoot,
            store.WorkflowId,
            "task-1");

        Assert.Empty(candidateRevisions);
    }

    // =========================================================================
    // Category 3: Journal Storage, Path, Contiguous Sequence, Atomic Write
    // =========================================================================

    [Fact]
    public async Task Journal_CanonicalPath_MatchesExpectedDirectoryAndFileFormat()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        Assert.True(Directory.Exists(recordsDir));
        string firstRecord = Path.Combine(recordsDir, $"{1:D20}.json");
        Assert.True(File.Exists(firstRecord));
    }

    [Fact]
    public async Task Journal_SequenceStartsAtOne_AndIncrementsStrictlyByOne()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.OpenExisting(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        Assert.True(journal.Records.Count > 0);

        for (int i = 0; i < journal.Records.Count; i++)
        {
            Assert.Equal(i + 1, journal.Records[i].Sequence);
        }
    }

    [Fact]
    public async Task Journal_CorruptRecord_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        Directory.CreateDirectory(recordsDir);
        File.WriteAllText(Path.Combine(recordsDir, $"{1:D20}.json"), "{ invalid-json }");

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task Journal_SequenceGap_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        Directory.CreateDirectory(recordsDir);
        File.WriteAllText(Path.Combine(recordsDir, $"{2:D20}.json"), "{}");

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task Journal_NonCanonicalFileName_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        Directory.CreateDirectory(recordsDir);
        File.WriteAllText(Path.Combine(recordsDir, "non_canonical.json"), "{}");

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task Journal_NoOverwrite_ThrowsWhenExistingFileExists()
    {
        WorkflowId wfId = new("wf-test");
        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
            this._tempDirectory,
            wfId,
            "task-1",
            1,
            "base-fp",
            "in-fp",
            "reg-fp");

        // Attempting to append with sequence 1 (which already exists as CheckpointStarted) throws
        CheckpointStartedRecord startRecord = new()
        {
            Sequence = 1,
            Kind = JournalRecordKind.CheckpointStarted,
            WorkflowId = "wf-test",
            TaskId = "task-1",
            BasePersistenceRevision = 1
        };

        Assert.Throws<InvalidOperationException>(() =>
            journal.AppendRecord(startRecord));
    }

    [Fact]
    public void Journal_AppendCheckpointStarted_PersistsValidSchemaAndPayload()
    {
        WorkflowId wfId = new("wf-test-2");
        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
            this._tempDirectory,
            wfId,
            "task-1",
            3,
            "base-fp",
            "in-fp",
            "reg-fp");

        Assert.Single(journal.Records);
        CheckpointStartedRecord loadedRecord = Assert.IsType<CheckpointStartedRecord>(journal.Records[0]);
        Assert.Equal("in-fp", loadedRecord.InputFingerprint);
        Assert.Equal("reg-fp", loadedRecord.RegistryFingerprint);
    }

    [Fact]
    public void Journal_AppendExecutionCompleted_PersistsValidPayload()
    {
        WorkflowId wfId = new("wf-test-3");
        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
            this._tempDirectory,
            wfId,
            "task-1",
            1,
            "base-fp",
            "in-fp",
            "reg-fp");

        ExecutionCompletedRecord completed = new()
        {
            Sequence = 2,
            Kind = JournalRecordKind.ExecutionCompleted,
            BudgetExhausted = true,
            AttemptCount = 3
        };

        journal.AppendRecord(completed);

        WorkflowCheckpointJournal reloaded = WorkflowCheckpointJournal.OpenExisting(
            this._tempDirectory,
            wfId,
            "task-1",
            1);

        Assert.Equal(2, reloaded.Records.Count);
        ExecutionCompletedRecord loaded = Assert.IsType<ExecutionCompletedRecord>(reloaded.Records[1]);
        Assert.True(loaded.BudgetExhausted);
        Assert.Equal(3, loaded.AttemptCount);
    }

    [Fact]
    public void Journal_AppendProjectionCompleted_PersistsValidPayload()
    {
        WorkflowId wfId = new("wf-test-4");
        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
            this._tempDirectory,
            wfId,
            "task-1",
            1,
            "base-fp",
            "in-fp",
            "reg-fp");

        ProjectionCompletedRecord projRecord = new()
        {
            Sequence = 2,
            Kind = JournalRecordKind.ProjectionCompleted,
            FinalPersistenceRevision = 5
        };

        journal.AppendRecord(projRecord);

        WorkflowCheckpointJournal reloaded = WorkflowCheckpointJournal.OpenExisting(
            this._tempDirectory,
            wfId,
            "task-1",
            1);

        Assert.Equal(2, reloaded.Records.Count);
        ProjectionCompletedRecord loaded = Assert.IsType<ProjectionCompletedRecord>(reloaded.Records[1]);
        Assert.Equal(5, loaded.FinalPersistenceRevision);
    }

    // =========================================================================
    // Category 4: Operation Journaling, Ordinals, IDs, Generations
    // =========================================================================

    [Fact]
    public async Task OperationOrdinal_SharedSequenceAcrossAgentAndValidation()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(ValidationStrategy.Test);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.OpenExisting(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        InvocationIntentRecord agentIntent = journal.Records
            .OfType<InvocationIntentRecord>()
            .First(r => r.OperationKind == "Agent");

        InvocationIntentRecord valIntent = journal.Records
            .OfType<InvocationIntentRecord>()
            .First(r => r.OperationKind == "Validation");

        Assert.Equal(1, agentIntent.OperationOrdinal);
        Assert.Equal(2, valIntent.OperationOrdinal);
    }

    [Fact]
    public async Task OperationId_DeterministicFormat_MatchesWorkflowTaskOrdinal()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.OpenExisting(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        InvocationIntentRecord agentIntent = journal.Records.OfType<InvocationIntentRecord>().First();

        Assert.NotNull(agentIntent.OperationId);
        Assert.Equal(64, agentIntent.OperationId.Length);
        Assert.Matches("^[0-9a-f]{64}$", agentIntent.OperationId);
    }

    [Fact]
    public async Task InvocationGeneration_StartsAtOne()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.OpenExisting(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        InvocationIntentRecord agentIntent = journal.Records.OfType<InvocationIntentRecord>().First();

        Assert.Equal(1, agentIntent.InvocationGeneration);
    }

    [Fact]
    public async Task AgentIntent_PersistedBeforeExternalInvocation()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        bool intentRecordedBeforeCall = false;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) =>
            {
                string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(
                    store.StorageRoot,
                    store.WorkflowId,
                    "task-1",
                    3);

                if (Directory.Exists(recordsDir))
                {
                    WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.OpenExisting(
                        store.StorageRoot,
                        store.WorkflowId,
                        "task-1",
                        3);

                    intentRecordedBeforeCall = journal.Records.OfType<InvocationIntentRecord>().Any(r => r.OperationKind == "Agent");
                }

                return Task.FromResult(AgentExecutionResult.Completed());
            });

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        Assert.True(intentRecordedBeforeCall);
    }

    [Fact]
    public async Task ValidationIntent_PersistedBeforeExternalInvocation()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        bool valIntentRecordedBeforeCall = false;

        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(
                    store.StorageRoot,
                    store.WorkflowId,
                    "task-1",
                    3);

                if (Directory.Exists(recordsDir))
                {
                    WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.OpenExisting(
                        store.StorageRoot,
                        store.WorkflowId,
                        "task-1",
                        3);

                    valIntentRecordedBeforeCall = journal.Records.OfType<InvocationIntentRecord>().Any(rec => rec.OperationKind == "Validation");
                }

                return Task.FromResult(new ValidationExecutionResult(true, "ok"));
            });

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        Assert.True(valIntentRecordedBeforeCall);
    }

    [Fact]
    public async Task PersistedAgentResult_ReplaysWithoutExternalReinvocation()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        int agentCalls = 0;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) =>
            {
                agentCalls++;
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);
        Assert.Equal(1, agentCalls);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        await coordinator2.ExecuteAsync(work, env);
        Assert.Equal(1, agentCalls);
    }

    [Fact]
    public async Task PersistedValidationResult_ReplaysWithoutExternalReinvocation()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        int validatorCalls = 0;

        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                validatorCalls++;
                return Task.FromResult(new ValidationExecutionResult(true, "ok"));
            });

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);
        Assert.Equal(1, validatorCalls);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [val], policy, reconciler);
        await coordinator2.ExecuteAsync(work, env);
        Assert.Equal(1, validatorCalls);
    }

    // =========================================================================
    // Category 5: Ambiguity & Reconciliation Protocol
    // =========================================================================

    [Fact]
    public async Task AgentException_BecomesAmbiguous_RequiresReconciliation()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        int reconcilerCalls = 0;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) => throw new InvalidOperationException("simulated crash"));

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new(
            agentHandler_: (wf, task, opId, ord, gen, prov, req, ct) =>
            {
                reconcilerCalls++;
                return Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));
            });

        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(work, env));

        Assert.True(reconcilerCalls > 0);
    }

    [Fact]
    public async Task ValidationException_BecomesAmbiguous_RequiresReconciliation()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) => throw new InvalidOperationException("validator crash"));

        int reconcilerCalls = 0;
        FakeReconciler reconciler = new(
            validationHandler_: (wf, task, opId, ord, gen, r, envP, ct) =>
            {
                reconcilerCalls++;
                return Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));
            });

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [val], policy, reconciler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(work, env));

        Assert.True(reconcilerCalls > 0);
    }

    [Fact]
    public async Task Reconciler_Unresolved_StopsExecutionWithoutReinvocation()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        int agentCalls = 0;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) =>
            {
                agentCalls++;
                throw new TimeoutException("timeout");
            });

        FakeReconciler reconciler = new(
            agentHandler_: (wf, task, opId, ord, gen, prov, req, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(false, null, null)));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        Assert.Equal(1, agentCalls);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(work, env));

        Assert.Equal(1, agentCalls);
    }

    [Fact]
    public async Task Reconciler_ProvenNotExecuted_IncrementsGeneration_AndReinvokesAgent()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        int agentCalls = 0;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) =>
            {
                agentCalls++;
                if (agentCalls == 1)
                {
                    throw new TimeoutException("timeout on first call");
                }
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeReconciler reconciler = new(
            agentHandler_: (wf, task, opId, ord, gen, prov, req, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null)));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        Assert.Equal(1, agentCalls);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        WorkflowCheckpointResult result = await coordinator2.ExecuteAsync(work, env);

        Assert.Equal(2, agentCalls);
        Assert.True(result.Resumed);
        Assert.True(result.ReconciliationUsed);
    }

    [Fact]
    public async Task Reconciler_ObservedAgent_PersistsOutcome_ReplaysWithoutReinvocation()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        int agentCalls = 0;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) =>
            {
                agentCalls++;
                throw new TimeoutException("timeout");
            });

        FakeReconciler reconciler = new(
            agentHandler_: (wf, task, opId, ord, gen, prov, req, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(
                    false,
                    AgentExecutionResult.Completed(),
                    null)));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        Assert.Equal(1, agentCalls);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        WorkflowCheckpointResult result = await coordinator2.ExecuteAsync(work, env);

        Assert.Equal(1, agentCalls);
        Assert.True(result.ReconciliationUsed);
    }

    [Fact]
    public async Task Reconciler_ObservedValidation_PersistsOutcome_ReplaysWithoutReinvocation()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        int validatorCalls = 0;

        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                validatorCalls++;
                throw new TimeoutException("validation timeout");
            });

        FakeReconciler reconciler = new(
            validationHandler_: (wf, task, opId, ord, gen, r, e, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(
                    false,
                    null,
                    new ValidationExecutionResult(true, "reconciled pass"))));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        Assert.Equal(1, validatorCalls);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [val], policy, reconciler);
        WorkflowCheckpointResult result = await coordinator2.ExecuteAsync(work, env);

        Assert.Equal(1, validatorCalls);
        Assert.True(result.ReconciliationUsed);
    }

    [Fact]
    public async Task Reconciler_WrongResultKind_ValidationForAgent_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) => throw new TimeoutException("timeout"));

        FakeReconciler reconciler = new(
            agentHandler_: (wf, task, opId, ord, gen, prov, req, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(
                    false,
                    null,
                    new ValidationExecutionResult(true, "wrong"))));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task Reconciler_WrongResultKind_AgentForValidation_ThrowsInvalidOperationException()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) => throw new TimeoutException("val timeout"));

        FakeReconciler reconciler = new(
            validationHandler_: (wf, task, opId, ord, gen, r, e, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(
                    false,
                    AgentExecutionResult.Completed(),
                    null)));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [val], policy, reconciler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(work, env));
    }

    [Fact]
    public void Reconciler_ProvenNotExecutedWithResult_Rejected_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSideEffectReconciliationResult(
                true,
                AgentExecutionResult.Completed(),
                null));
    }

    [Fact]
    public void Reconciler_BothResultsNonNull_Rejected_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSideEffectReconciliationResult(
                false,
                AgentExecutionResult.Completed(),
                new ValidationExecutionResult(true, "ok")));
    }

    [Fact]
    public async Task Reconciler_Exception_BubblesUp_ZeroReinvocation()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        int agentCalls = 0;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) =>
            {
                agentCalls++;
                throw new TimeoutException("timeout");
            });

        FakeReconciler reconciler = new(
            agentHandler_: (wf, task, opId, ord, gen, prov, req, ct) =>
                throw new ApplicationException("reconciler network error"));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        Assert.Equal(1, agentCalls);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        await Assert.ThrowsAsync<ApplicationException>(() =>
            coordinator2.ExecuteAsync(work, env));

        Assert.Equal(1, agentCalls);
    }

    // =========================================================================
    // Category 6: Semantic Repair & P08 Integration
    // =========================================================================

    [Fact]
    public async Task ProviderOrder_MultiProvider_PreservesP08PriorityOrder()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        List<string> providerExecutionOrder = [];

        FakeAgent agent1 = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                providerExecutionOrder.Add("prov-1");
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeAgent agent2 = new(
            new AgentProviderId("prov-2"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                providerExecutionOrder.Add("prov-2");
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                bool pass = count > 1;
                return Task.FromResult(new ValidationExecutionResult(pass, pass ? "pass" : "fail"));
            });

        WorkflowRepairPolicy policy = new(1, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent1, agent2], [val], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(["prov-1", "prov-2"], providerExecutionOrder);
        Assert.False(result.RepairResult.BudgetExhausted);
    }

    [Fact]
    public async Task RetryBudget_MaxAttemptsPerProvider_Preserved()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        int agent1Calls = 0;
        FakeAgent agent1 = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                agent1Calls++;
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) => Task.FromResult(new ValidationExecutionResult(false, "fail")));

        WorkflowRepairPolicy policy = new(2, 5);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent1], [val], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(2, agent1Calls);
        Assert.True(result.RepairResult.BudgetExhausted);
    }

    [Fact]
    public async Task RetryBudget_MaxTotalAttempts_Preserved()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        int totalCalls = 0;
        FakeAgent agent1 = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                totalCalls++;
                return Task.FromResult(AgentExecutionResult.Completed());
            });
        FakeAgent agent2 = new(
            new AgentProviderId("prov-2"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                totalCalls++;
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) => Task.FromResult(new ValidationExecutionResult(false, "fail")));

        WorkflowRepairPolicy policy = new(5, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent1, agent2], [val], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(2, totalCalls);
        Assert.True(result.RepairResult.BudgetExhausted);
    }

    [Fact]
    public async Task SemanticRepair_FailedValidation_InjectsSemanticRepairPrompt()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        List<string> instructions = [];

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                instructions.Add(r.Instruction);
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                bool pass = count == 2;
                return Task.FromResult(new ValidationExecutionResult(pass, pass ? "ok" : "custom-error-evidence"));
            });

        WorkflowRepairPolicy policy = new(2, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        Assert.Equal(2, instructions.Count);
        Assert.Contains("custom-error-evidence", instructions[1], StringComparison.Ordinal);
        Assert.Contains("AIRepoKit.SemanticRepair/v1", instructions[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionCompleted_WrittenBeforeCanonicalProjection()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await coordinator.ExecuteAsync(work, env);

        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.OpenExisting(
            store.StorageRoot,
            store.WorkflowId,
            "task-1",
            3);

        int execCompletedIndex = -1;
        int projCompletedIndex = -1;

        for (int i = 0; i < journal.Records.Count; i++)
        {
            if (journal.Records[i].Kind == JournalRecordKind.ExecutionCompleted)
            {
                execCompletedIndex = i;
            }
            if (journal.Records[i].Kind == JournalRecordKind.ProjectionCompleted)
            {
                projCompletedIndex = i;
            }
        }

        Assert.True(execCompletedIndex >= 0);
        Assert.True(projCompletedIndex > execCompletedIndex);
    }

    [Fact]
    public async Task InputDrift_FailsClosedOnResume()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) => throw new TimeoutException("crash"));

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        ExecutableWork driftedWork = new(
            work.SourceImplementationPlanRevision,
            [new ExecutableTask("task-1", "s1", "drifted instruction")]);

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(driftedWork, env));
    }

    [Fact]
    public async Task RegistryDrift_FailsClosedOnResume()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) => throw new TimeoutException("crash"));

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator.ExecuteAsync(work, env));

        FakeAgent differentAgent = new(new AgentProviderId("prov-2"), AgentCapabilitySet.Empty);
        WorkflowCheckpointCoordinator coordinator2 = new(store, [differentAgent], [], policy, reconciler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(work, env));
    }

    // =========================================================================
    // Category 7: Canonical Projection Transitions
    // =========================================================================

    [Fact]
    public async Task Projection_ValidationPass_StepTransitionsToCompleted()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(ValidationStrategy.Test);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(WorkflowStepStatus.Completed, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task Projection_ValidationFail_StepTransitionsToFailed()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) => Task.FromResult(new ValidationExecutionResult(false, "fail")));

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(WorkflowStepStatus.Failed, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task Projection_AgentFailed_StepTransitionsToFailed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) => Task.FromResult(AgentExecutionResult.Failed(diagnosticText_: "agent error")));

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(WorkflowStepStatus.Failed, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task Projection_AgentBlocked_StepTransitionsToBlocked()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) => Task.FromResult(AgentExecutionResult.Blocked(diagnosticText_: "blocked")));

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(WorkflowStepStatus.Blocked, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task Projection_AgentNeedsInput_StepTransitionsToBlocked()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) => Task.FromResult(AgentExecutionResult.NeedsInput(diagnosticText_: "needs input")));

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(WorkflowStepStatus.Blocked, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task Projection_NoCanonicalOverwrite_StoreRevisionsIncreaseMonotonically()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        long initialRevision = store.Load()!.Revision;
        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.True(result.Snapshot.Revision > initialRevision);
    }

    // =========================================================================
    // Category 8: Restart Windows & Crash Recovery
    // =========================================================================

    [Fact]
    public async Task Restart_CrashAfterProjectionCompleted_IdempotentReentry()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        int agentCalls = 0;

        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                agentCalls++;
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        WorkflowCheckpointResult firstResult = await coordinator.ExecuteAsync(work, env);
        Assert.Equal(1, agentCalls);
        Assert.False(firstResult.Resumed);

        long revisionAfterFirst = firstResult.Snapshot.Revision;

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        WorkflowCheckpointResult secondResult = await coordinator2.ExecuteAsync(work, env);

        Assert.Equal(1, agentCalls);
        Assert.True(secondResult.Resumed);
        Assert.Equal(revisionAfterFirst, secondResult.Snapshot.Revision);
    }

    [Fact]
    public async Task Result_ResumedFlag_TrueWhenJournalHadPreExistingRecords()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                if (count == 1)
                {
                    throw new TimeoutException("timeout");
                }
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeReconciler reconciler = new(
            agentHandler_: (wf, t, op, ord, gen, prov, req, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null)));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator1 = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator1.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        WorkflowCheckpointResult result = await coordinator2.ExecuteAsync(work, env);

        Assert.True(result.Resumed);
    }

    [Fact]
    public async Task Result_ReconciliationUsedFlag_TrueWhenReconcilerWasInvoked()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                if (count == 1)
                {
                    throw new TimeoutException("timeout");
                }
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeReconciler reconciler = new(
            agentHandler_: (wf, t, op, ord, gen, prov, req, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null)));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator1 = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator1.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        WorkflowCheckpointResult result = await coordinator2.ExecuteAsync(work, env);

        Assert.True(result.ReconciliationUsed);
    }

    // =========================================================================
    // Category 8: Resilience, Journal Integrity & Additional Edge Cases
    // =========================================================================

    [Fact]
    public void Constructor_DistinctProviderIds_Success()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent1 = new(new AgentProviderId("prov-a"), AgentCapabilitySet.Empty);
        FakeAgent agent2 = new(new AgentProviderId("prov-b"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        WorkflowCheckpointCoordinator coordinator = new(store, [agent1, agent2], [], policy, reconciler);
        Assert.NotNull(coordinator);
    }

    [Fact]
    public void Constructor_AllDistinctValidationStrategies_Success()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val1 = new(ValidationStrategy.Test);
        FakeValidator val2 = new(ValidationStrategy.Build);
        FakeValidator val3 = new(ValidationStrategy.Policy);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();

        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val1, val2, val3], policy, reconciler);
        Assert.NotNull(coordinator);
    }

    [Fact]
    public void ReconciliationResult_InvalidShapes_BothProvenNotExecutedAndValidation_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSideEffectReconciliationResult(true, null, new ValidationExecutionResult(false, "err")));
    }

    [Fact]
    public void ReconciliationResult_InvalidShapes_BothProvenNotExecutedAndAgent_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSideEffectReconciliationResult(true, AgentExecutionResult.Failed("err"), null));
    }

    [Fact]
    public void Journal_ReadRecords_DuplicateSequenceNumber_ThrowsInvalidOperationException()
    {
        WorkflowId wfId = new("wf-dup-seq");
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(this._tempDirectory, wfId, "task-1", 1);
        Directory.CreateDirectory(recordsDir);

        string json1 = "{\"schemaId\":\"ai.repokit.workflow-execution-journal-record\",\"schemaVersion\":1,\"sequence\":1,\"kind\":\"CheckpointStarted\",\"workflowId\":\"wf-dup-seq\",\"taskId\":\"task-1\",\"basePersistenceRevision\":1,\"baseWorkflowStateFingerprint\":\"fp\",\"inputFingerprint\":\"in-fp\",\"registryFingerprint\":\"reg-fp\"}";
        File.WriteAllText(Path.Combine(recordsDir, "00000000000000000001.json"), json1);
        File.WriteAllText(Path.Combine(recordsDir, "00000000000000000001-dup.json"), json1);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowCheckpointJournal.OpenExisting(this._tempDirectory, wfId, "task-1", 1));
    }

    [Fact]
    public void ReconciliationResult_InvalidShapes_IsProvenNotExecuted_WithAgentResult_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSideEffectReconciliationResult(true, AgentExecutionResult.Completed(), null));
    }

    [Fact]
    public void ReconciliationResult_InvalidShapes_IsProvenNotExecuted_WithValidationResult_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSideEffectReconciliationResult(true, null, new ValidationExecutionResult(true, "ok")));
    }

    [Fact]
    public void ReconciliationResult_InvalidShapes_BothResultsNonNull_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSideEffectReconciliationResult(false, AgentExecutionResult.Completed(), new ValidationExecutionResult(true, "ok")));
    }

    [Fact]
    public void ReconciliationResult_ValidShape_Unresolved()
    {
        WorkflowSideEffectReconciliationResult result = new(false, null, null);
        Assert.False(result.IsProvenNotExecuted);
        Assert.Null(result.AgentResult);
        Assert.Null(result.ValidationResult);
    }

    [Fact]
    public void ReconciliationResult_ValidShape_ProvenNotExecuted()
    {
        WorkflowSideEffectReconciliationResult result = new(true, null, null);
        Assert.True(result.IsProvenNotExecuted);
        Assert.Null(result.AgentResult);
        Assert.Null(result.ValidationResult);
    }

    [Fact]
    public void ReconciliationResult_ValidShape_AgentObserved()
    {
        AgentExecutionResult agentRes = AgentExecutionResult.Completed();
        WorkflowSideEffectReconciliationResult result = new(false, agentRes, null);
        Assert.False(result.IsProvenNotExecuted);
        Assert.Same(agentRes, result.AgentResult);
        Assert.Null(result.ValidationResult);
    }

    [Fact]
    public void ReconciliationResult_ValidShape_ValidationObserved()
    {
        ValidationExecutionResult valRes = new(true, "ok");
        WorkflowSideEffectReconciliationResult result = new(false, null, valRes);
        Assert.False(result.IsProvenNotExecuted);
        Assert.Null(result.AgentResult);
        Assert.Same(valRes, result.ValidationResult);
    }

    [Fact]
    public void Journal_ReadRecords_SequenceGap_ThrowsInvalidOperationException()
    {
        WorkflowId wfId = new("wf-seq-gap");
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(this._tempDirectory, wfId, "task-1", 1);
        Directory.CreateDirectory(recordsDir);

        string json1 = "{\"schemaId\":\"ai.repokit.workflow-execution-journal-record\",\"schemaVersion\":1,\"sequence\":1,\"kind\":\"CheckpointStarted\",\"workflowId\":\"wf-seq-gap\",\"taskId\":\"task-1\",\"basePersistenceRevision\":1,\"baseWorkflowStateFingerprint\":\"fp\",\"inputFingerprint\":\"in-fp\",\"registryFingerprint\":\"reg-fp\"}";
        File.WriteAllText(Path.Combine(recordsDir, "00000000000000000001.json"), json1);

        string json3 = "{\"schemaId\":\"ai.repokit.workflow-execution-journal-record\",\"schemaVersion\":1,\"sequence\":3,\"kind\":\"ExecutionCompleted\",\"repairAttemptCount\":1,\"isSuccess\":true}";
        File.WriteAllText(Path.Combine(recordsDir, "00000000000000000003.json"), json3);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowCheckpointJournal.OpenExisting(this._tempDirectory, wfId, "task-1", 1));
    }

    [Fact]
    public void Journal_ReadRecords_DoesNotStartAtOne_ThrowsInvalidOperationException()
    {
        WorkflowId wfId = new("wf-no-one");
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(this._tempDirectory, wfId, "task-1", 1);
        Directory.CreateDirectory(recordsDir);

        string json2 = "{\"schemaId\":\"ai.repokit.workflow-execution-journal-record\",\"schemaVersion\":1,\"sequence\":2,\"kind\":\"ExecutionCompleted\",\"repairAttemptCount\":1,\"isSuccess\":true}";
        File.WriteAllText(Path.Combine(recordsDir, "00000000000000000002.json"), json2);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowCheckpointJournal.OpenExisting(this._tempDirectory, wfId, "task-1", 1));
    }

    [Fact]
    public void Journal_ReadRecords_CorruptJson_ThrowsInvalidOperationException()
    {
        WorkflowId wfId = new("wf-corrupt-json");
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(this._tempDirectory, wfId, "task-1", 1);
        Directory.CreateDirectory(recordsDir);

        File.WriteAllText(Path.Combine(recordsDir, "00000000000000000001.json"), "{ invalid json");

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowCheckpointJournal.OpenExisting(this._tempDirectory, wfId, "task-1", 1));
    }

    [Fact]
    public void Journal_ReadRecords_MissingKindProperty_ThrowsInvalidOperationException()
    {
        WorkflowId wfId = new("wf-missing-kind");
        string recordsDir = WorkflowCheckpointJournal.GetRecordsDirectory(this._tempDirectory, wfId, "task-1", 1);
        Directory.CreateDirectory(recordsDir);

        File.WriteAllText(Path.Combine(recordsDir, "00000000000000000001.json"), "{\"schemaId\":\"ai.repokit.workflow-execution-journal-record\",\"schemaVersion\":1,\"sequence\":1}");

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowCheckpointJournal.OpenExisting(this._tempDirectory, wfId, "task-1", 1));
    }

    [Fact]
    public async Task ExecuteAsync_ReconciliationAgent_ThrowsException_PropagatedDirectly()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, r, ct) =>
            {
                if (count == 1) throw new TimeoutException("agent-timeout");
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        FakeReconciler reconciler = new(
            agentHandler_: (wf, t, op, ord, gen, prov, req, ct) =>
                throw new ApplicationException("reconciliation-failed"));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator1 = new(store, [agent], [], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator1.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [], policy, reconciler);
        await Assert.ThrowsAsync<ApplicationException>(() =>
            coordinator2.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task ExecuteAsync_ReconciliationValidation_ThrowsException_PropagatedDirectly()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                if (count == 1) throw new TimeoutException("val-timeout");
                return Task.FromResult(new ValidationExecutionResult(true, "ok"));
            });

        FakeReconciler reconciler = new(
            validationHandler_: (wf, t, op, ord, gen, r, e, ct) =>
                throw new ApplicationException("val-reconciliation-failed"));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator1 = new(store, [agent], [val], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator1.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [val], policy, reconciler);
        await Assert.ThrowsAsync<ApplicationException>(() =>
            coordinator2.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task ExecuteAsync_ReconciliationValidation_Unresolved_ThrowsInvalidOperationException()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                if (count == 1) throw new TimeoutException("val-timeout");
                return Task.FromResult(new ValidationExecutionResult(true, "ok"));
            });

        FakeReconciler reconciler = new(
            validationHandler_: (wf, t, op, ord, gen, r, e, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(false, null, null)));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator1 = new(store, [agent], [val], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator1.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [val], policy, reconciler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator2.ExecuteAsync(work, env));
    }

    [Fact]
    public async Task ExecuteAsync_ReconciliationValidation_ValidationObserved_AdoptsResultWithoutCallingValidatorAgain()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                if (count == 1) throw new TimeoutException("val-timeout");
                return Task.FromResult(new ValidationExecutionResult(true, "ok"));
            });

        FakeReconciler reconciler = new(
            validationHandler_: (wf, t, op, ord, gen, r, e, ct) =>
                Task.FromResult(new WorkflowSideEffectReconciliationResult(false, null, new ValidationExecutionResult(true, "observed-ok"))));

        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowCheckpointCoordinator coordinator1 = new(store, [agent], [val], policy, reconciler);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            coordinator1.ExecuteAsync(work, env));

        WorkflowCheckpointCoordinator coordinator2 = new(store, [agent], [val], policy, reconciler);
        WorkflowCheckpointResult result = await coordinator2.ExecuteAsync(work, env);

        Assert.True(result.Resumed);
        Assert.True(result.ReconciliationUsed);
        Assert.Equal(1, val.InvocationCount);
        Assert.Equal(WorkflowStepStatus.Completed, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_MultiAttempt_AgentFailureThenSuccess_Completed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) =>
            {
                if (count == 1) return Task.FromResult(AgentExecutionResult.Failed("first attempt failed"));
                return Task.FromResult(AgentExecutionResult.Completed());
            });

        WorkflowRepairPolicy policy = new(2, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(2, agent.InvocationCount);
        Assert.Equal(WorkflowStepStatus.Completed, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_MultiAttempt_ValidationFailureThenSuccess_Completed()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) =>
            {
                if (count == 1) return Task.FromResult(new ValidationExecutionResult(false, "first check failed"));
                return Task.FromResult(new ValidationExecutionResult(true, "second check passed"));
            });

        WorkflowRepairPolicy policy = new(2, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(2, agent.InvocationCount);
        Assert.Equal(2, val.InvocationCount);
        Assert.Equal(WorkflowStepStatus.Completed, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_MultiAttempt_ExhaustsMaxAttempts_TransitionsToFailed()
    {
        ValidationRequirement req = CreateValidationRequirement("task-1", ValidationStrategy.Test);
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup(
            validationRequirements: [req]);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(
            ValidationStrategy.Test,
            handler_: (count, r, e, ct) => Task.FromResult(new ValidationExecutionResult(false, "always fails")));

        WorkflowRepairPolicy policy = new(2, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [val], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(2, agent.InvocationCount);
        Assert.Equal(2, val.InvocationCount);
        Assert.Equal(WorkflowStepStatus.Failed, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_AgentReturnsBlocked_StepTransitionsToBlocked()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) => Task.FromResult(AgentExecutionResult.Blocked("blocked by external dependency")));

        WorkflowRepairPolicy policy = new(2, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(WorkflowStepStatus.Blocked, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_AgentReturnsNeedsInput_StepTransitionsToBlocked()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope env) = this.CreateInitializedSetup();
        FakeAgent agent = new(
            new AgentProviderId("prov-1"),
            AgentCapabilitySet.Empty,
            handler_: (count, req, ct) => Task.FromResult(AgentExecutionResult.NeedsInput("user input required")));

        WorkflowRepairPolicy policy = new(2, 2);
        FakeReconciler reconciler = new();
        WorkflowCheckpointCoordinator coordinator = new(store, [agent], [], policy, reconciler);

        WorkflowCheckpointResult result = await coordinator.ExecuteAsync(work, env);

        Assert.Equal(WorkflowStepStatus.Blocked, result.Snapshot.State.Steps[0].Status);
    }

    [Fact]
    public void ExecuteAsync_Resume_SelectsHighestCandidateRevision()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();

        string dir2 = WorkflowCheckpointJournal.GetRecordsDirectory(store.StorageRoot, store.WorkflowId, "task-1", 2);
        Directory.CreateDirectory(dir2);
        string json1 = "{\"schemaId\":\"ai.repokit.workflow-execution-journal-record\",\"schemaVersion\":1,\"sequence\":1,\"kind\":\"CheckpointStarted\",\"workflowId\":\"wf-1\",\"taskId\":\"task-1\",\"basePersistenceRevision\":2,\"baseWorkflowStateFingerprint\":\"fp\",\"inputFingerprint\":\"dummy-input\",\"registryFingerprint\":\"dummy-reg\"}";
        File.WriteAllText(Path.Combine(dir2, "00000000000000000001.json"), json1);

        IReadOnlyList<long> candidates = WorkflowCheckpointJournal.DiscoverCandidateRevisions(
            store.StorageRoot,
            store.WorkflowId,
            "task-1");

        Assert.Contains(2L, candidates);
    }

    // =========================================================================
    // Helpers & Test Fakes
    // =========================================================================

    private (WorkflowPersistenceStore Store, ExecutableWork Work, ExecutionEnvelope Envelope) CreateInitializedSetup(
        string taskId = "task-1",
        int revision = 1,
        IReadOnlyList<ValidationRequirement>? validationRequirements = null,
        IReadOnlyList<AgentRequirement>? agentRequirements = null)
    {
        WorkflowId workflowId = new("wf-1");
        ExecutableWork work = CreateWork(taskId, revision, validationRequirements, agentRequirements);
        WorkflowPersistenceStore store = new(this._tempDirectory, workflowId);

        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowState runningWorkflow = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWorkflow, 1);

        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWorkflow, taskId, WorkflowStepStatus.Running);
        store.Append(runningStep, 2);

        ExecutionEnvelope envelope = Envelope(work);

        return (store, work, envelope);
    }

    private static ExecutableWork CreateWork(
        string taskId = "task-1",
        int revision = 1,
        IReadOnlyList<ValidationRequirement>? validationRequirements = null,
        IReadOnlyList<AgentRequirement>? agentRequirements = null)
    {
        return new ExecutableWork(
            revision,
            [
                new ExecutableTask(
                    taskId,
                    "step-1",
                    "do work")
            ],
            [],
            validationRequirements ?? [],
            [],
            agentRequirements ?? []);
    }

    private static ValidationRequirement CreateValidationRequirement(
        string taskId = "task-1",
        ValidationStrategy strategy = ValidationStrategy.Test)
    {
        return new ValidationRequirement(
            "val-req-1",
            taskId,
            "criterion-1",
            strategy,
            "validation statement");
    }

    private static ExecutionEnvelope Envelope(
        ExecutableWork work_)
    {
        CompiledPrompt prompt = new(
            work_.Tasks[0].Id,
            "instruction");

        return ExecutionEnvelope.Create(
            work_,
            prompt,
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);
    }

    private sealed class FakeAgent : IAgentExecutor
    {
        private readonly Func<int, AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> _handler;

        public AgentProviderId ProviderId
        {
            get;
        }

        public AgentCapabilitySet Capabilities
        {
            get;
        }

        public int InvocationCount
        {
            get;
            private set;
        }

        public FakeAgent(
            AgentProviderId providerId_,
            AgentCapabilitySet capabilities_,
            Func<int, AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>>? handler_ = null)
        {
            this.ProviderId = providerId_;
            this.Capabilities = capabilities_;
            this._handler = handler_ ?? ((_, _, _) => Task.FromResult(AgentExecutionResult.Completed()));
        }

        public async Task<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;
            return await this._handler(this.InvocationCount, request_, cancellationToken_).ConfigureAwait(false);
        }
    }

    private sealed class FakeValidator : IValidationExecutor
    {
        private readonly Func<int, ValidationRequirement, ExecutionEnvironment, CancellationToken, Task<ValidationExecutionResult>> _handler;

        public ValidationStrategy Strategy
        {
            get;
        }

        public int InvocationCount
        {
            get;
            private set;
        }

        public FakeValidator(
            ValidationStrategy strategy_,
            Func<int, ValidationRequirement, ExecutionEnvironment, CancellationToken, Task<ValidationExecutionResult>>? handler_ = null)
        {
            this.Strategy = strategy_;
            this._handler = handler_ ?? ((_, _, _, _) => Task.FromResult(new ValidationExecutionResult(true, "ok")));
        }

        public async Task<ValidationExecutionResult> ValidateAsync(
            ValidationRequirement requirement_,
            ExecutionEnvironment environment_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;
            return await this._handler(this.InvocationCount, requirement_, environment_, cancellationToken_).ConfigureAwait(false);
        }
    }

    private sealed class FakeReconciler : IWorkflowSideEffectReconciler
    {
        private readonly Func<WorkflowId, string, string, long, int, AgentProviderId, AgentExecutionRequest, CancellationToken, Task<WorkflowSideEffectReconciliationResult>>? _agentHandler;
        private readonly Func<WorkflowId, string, string, long, int, ValidationRequirement, ExecutionEnvironment, CancellationToken, Task<WorkflowSideEffectReconciliationResult>>? _validationHandler;

        public FakeReconciler(
            Func<WorkflowId, string, string, long, int, AgentProviderId, AgentExecutionRequest, CancellationToken, Task<WorkflowSideEffectReconciliationResult>>? agentHandler_ = null,
            Func<WorkflowId, string, string, long, int, ValidationRequirement, ExecutionEnvironment, CancellationToken, Task<WorkflowSideEffectReconciliationResult>>? validationHandler_ = null)
        {
            this._agentHandler = agentHandler_;
            this._validationHandler = validationHandler_;
        }

        public Task<WorkflowSideEffectReconciliationResult> ReconcileAgentAsync(
            WorkflowId workflowId_,
            string taskId_,
            string operationId_,
            long operationOrdinal_,
            int invocationGeneration_,
            AgentProviderId providerId_,
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            if (this._agentHandler is not null)
            {
                return this._agentHandler(workflowId_, taskId_, operationId_, operationOrdinal_, invocationGeneration_, providerId_, request_, cancellationToken_);
            }

            return Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));
        }

        public Task<WorkflowSideEffectReconciliationResult> ReconcileValidationAsync(
            WorkflowId workflowId_,
            string taskId_,
            string operationId_,
            long operationOrdinal_,
            int invocationGeneration_,
            ValidationRequirement requirement_,
            ExecutionEnvironment environment_,
            CancellationToken cancellationToken_ = default)
        {
            if (this._validationHandler is not null)
            {
                return this._validationHandler(workflowId_, taskId_, operationId_, operationOrdinal_, invocationGeneration_, requirement_, environment_, cancellationToken_);
            }

            return Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));
        }
    }

    private sealed class SingleEnumerationEnumerable<T> : IEnumerable<T>
    {
        private readonly T[] _items;

        public int EnumerationCount
        {
            get;
            private set;
        }

        public SingleEnumerationEnumerable(params T[] items_)
        {
            this._items = items_;
        }

        public IEnumerator<T> GetEnumerator()
        {
            this.EnumerationCount++;
            if (this.EnumerationCount > 1)
            {
                throw new InvalidOperationException("Collection was enumerated more than once.");
            }
            return ((IEnumerable<T>) this._items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}

namespace AiRepoKit.Orchestration;

public sealed class WorkflowPersistenceStore
{
    public const string SchemaId =
        "ai.repokit.workflow-persistence-record";

    public const int CurrentSchemaVersion =
        1;

    private readonly WorkflowPersistencePaths _paths;

    public string StorageRoot =>
        this._paths.StorageRoot;

    public WorkflowId WorkflowId =>
        this._paths.WorkflowId;

    public WorkflowPersistenceStore(
        string storageRoot_,
        WorkflowId workflowId_)
    {
        this._paths =
            new WorkflowPersistencePaths(
                storageRoot_,
                workflowId_);
    }

    public WorkflowPersistenceSnapshot? Load()
    {
        IReadOnlyList<WorkflowPersistenceSnapshot> snapshots =
            this.LoadSnapshots();

        if (snapshots.Count == 0)
        {
            return null;
        }

        return snapshots[
            snapshots.Count - 1];
    }

    public IReadOnlyList<WorkflowExecutionEvent> ReadEvents()
    {
        IReadOnlyList<WorkflowPersistenceSnapshot> snapshots =
            this.LoadSnapshots();

        WorkflowExecutionEvent[] events =
            new WorkflowExecutionEvent[snapshots.Count];

        for (int index = 0; index < snapshots.Count; index++)
        {
            events[index] =
                snapshots[index].LastEvent;
        }

        return Array.AsReadOnly(
            events);
    }

    public WorkflowPersistenceSnapshot Initialize(
        WorkflowState state_)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        if (state_.Status !=
            WorkflowStatus.Created)
        {
            throw new InvalidOperationException(
                "Workflow persistence can initialize only from Created state.");
        }

        for (int index = 0; index < state_.Steps.Count; index++)
        {
            if (state_.Steps[index].Status !=
                WorkflowStepStatus.Pending)
            {
                throw new InvalidOperationException(
                    "Created workflow persistence state must contain only Pending steps.");
            }
        }

        IReadOnlyList<WorkflowPersistenceSnapshot> existing =
            this.LoadSnapshots();

        if (existing.Count != 0)
        {
            throw new InvalidOperationException(
                "Workflow persistence has already been initialized.");
        }

        const long revision = 1;

        WorkflowExecutionEvent eventValue =
            new(
                this.WorkflowId,
                revision,
                WorkflowExecutionEventKind.WorkflowInitialized,
                taskId_: null,
                previousWorkflowStatus_: null,
                targetWorkflowStatus_: WorkflowStatus.Created,
                previousStepStatus_: null,
                targetStepStatus_: null);

        WorkflowPersistenceSnapshot snapshot =
            new(
                this.WorkflowId,
                revision,
                state_,
                eventValue);

        byte[] payload =
            WorkflowPersistenceJson.Serialize(
                this.WorkflowId,
                revision,
                eventValue,
                state_);

        this._paths.EnsureRecordsDirectory();

        WorkflowPersistenceAtomicWriter.WriteNew(
            this._paths.GetRecordPath(
                revision),
            payload);

        return snapshot;
    }

    public WorkflowPersistenceSnapshot Append(
        WorkflowState state_,
        long expectedRevision_)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        if (expectedRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedRevision_),
                expectedRevision_,
                "Expected revision must be greater than zero.");
        }

        IReadOnlyList<WorkflowPersistenceSnapshot> snapshots =
            this.LoadSnapshots();

        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException(
                "Workflow persistence has not been initialized.");
        }

        WorkflowPersistenceSnapshot current =
            snapshots[
                snapshots.Count - 1];

        if (current.Revision !=
            expectedRevision_)
        {
            throw new InvalidOperationException(
                "Expected workflow persistence revision does not match the current revision.");
        }

        long nextRevision;

        try
        {
            nextRevision =
                checked(current.Revision + 1);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "Workflow persistence revision cannot be incremented.",
                exception);
        }

        WorkflowExecutionEvent eventValue =
            this.DeriveEvent(
                current.State,
                state_,
                nextRevision);

        WorkflowPersistenceSnapshot snapshot =
            new(
                this.WorkflowId,
                nextRevision,
                state_,
                eventValue);

        byte[] payload =
            WorkflowPersistenceJson.Serialize(
                this.WorkflowId,
                nextRevision,
                eventValue,
                state_);

        this._paths.EnsureRecordsDirectory();

        WorkflowPersistenceAtomicWriter.WriteNew(
            this._paths.GetRecordPath(
                nextRevision),
            payload);

        return snapshot;
    }

    private IReadOnlyList<WorkflowPersistenceSnapshot> LoadSnapshots()
    {
        if (!Directory.Exists(
                this._paths.RecordsDirectory))
        {
            return Array.Empty<WorkflowPersistenceSnapshot>();
        }

        string[] files =
            Directory.GetFiles(
                this._paths.RecordsDirectory);

        if (files.Length == 0)
        {
            return Array.Empty<WorkflowPersistenceSnapshot>();
        }

        List<(long Revision, string Path)> orderedFiles =
            new(
                files.Length);

        for (int index = 0; index < files.Length; index++)
        {
            string fileName =
                Path.GetFileName(
                    files[index]);

            if (!WorkflowPersistencePaths.TryParseRecordFileName(
                    fileName,
                    out long revision))
            {
                throw new InvalidDataException(
                    $"Workflow persistence contains a non-canonical record filename: '{fileName}'.");
            }

            orderedFiles.Add(
                (
                    revision,
                    files[index]
                ));
        }

        orderedFiles.Sort(
            static (left_, right_) =>
                left_.Revision.CompareTo(
                    right_.Revision));

        List<WorkflowPersistenceSnapshot> snapshots =
            new(
                orderedFiles.Count);

        long expectedRevision =
            1;

        for (int index = 0; index < orderedFiles.Count; index++)
        {
            (
                long revision,
                string path
            ) =
                orderedFiles[index];

            if (revision !=
                expectedRevision)
            {
                throw new InvalidDataException(
                    $"Workflow persistence revisions must be contiguous from 1. Expected '{expectedRevision}', found '{revision}'.");
            }

            byte[] payload =
                File.ReadAllBytes(
                    path);

            WorkflowPersistenceSnapshot snapshot =
                WorkflowPersistenceJson.Deserialize(
                    payload,
                    this.WorkflowId);

            if (snapshot.Revision !=
                revision)
            {
                throw new InvalidDataException(
                    "Workflow persistence record revision does not match its canonical filename.");
            }

            if (index == 0)
            {
                ValidateInitialSnapshot(
                    snapshot);
            }
            else
            {
                ValidatePersistedTransition(
                    snapshots[index - 1],
                    snapshot);
            }

            snapshots.Add(
                snapshot);

            if (index <
                orderedFiles.Count - 1)
            {
                try
                {
                    expectedRevision =
                        checked(expectedRevision + 1);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException(
                        "Workflow persistence revision sequence overflowed.",
                        exception);
                }
            }
        }

        return Array.AsReadOnly(
            snapshots.ToArray());
    }

    private WorkflowExecutionEvent DeriveEvent(
        WorkflowState current_,
        WorkflowState target_,
        long sequence_)
    {
        EnsureStableWorkflowIdentity(
            current_,
            target_);

        if (current_.Status !=
            target_.Status)
        {
            for (int index = 0; index < current_.Steps.Count; index++)
            {
                if (current_.Steps[index].Status !=
                    target_.Steps[index].Status)
                {
                    throw new InvalidOperationException(
                        "A persisted workflow-status append cannot also change step status.");
                }
            }

            WorkflowState expected;

            try
            {
                expected =
                    WorkflowStateMachine.TransitionWorkflow(
                        current_,
                        target_.Status);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "Target state does not represent a legal workflow-status transition.",
                    exception);
            }

            if (!expected.Equals(
                    target_))
            {
                throw new InvalidOperationException(
                    "Target state is not the exact result of the requested workflow-status transition.");
            }

            return new WorkflowExecutionEvent(
                this.WorkflowId,
                sequence_,
                WorkflowExecutionEventKind.WorkflowStatusTransitioned,
                taskId_: null,
                previousWorkflowStatus_: current_.Status,
                targetWorkflowStatus_: target_.Status,
                previousStepStatus_: null,
                targetStepStatus_: null);
        }

        int changedStepIndex =
            -1;

        for (int index = 0; index < current_.Steps.Count; index++)
        {
            if (current_.Steps[index].Status ==
                target_.Steps[index].Status)
            {
                continue;
            }

            if (changedStepIndex >= 0)
            {
                throw new InvalidOperationException(
                    "A persisted append may change exactly one step status.");
            }

            changedStepIndex =
                index;
        }

        if (changedStepIndex < 0)
        {
            throw new InvalidOperationException(
                "No-op workflow persistence append is not allowed.");
        }

        WorkflowStepState currentStep =
            current_.Steps[
                changedStepIndex];

        WorkflowStepState targetStep =
            target_.Steps[
                changedStepIndex];

        WorkflowState expectedStepState;

        try
        {
            expectedStepState =
                WorkflowStateMachine.TransitionStep(
                    current_,
                    currentStep.TaskId,
                    targetStep.Status);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "Target state does not represent a legal step-status transition.",
                exception);
        }

        if (!expectedStepState.Equals(
                target_))
        {
            throw new InvalidOperationException(
                "Target state is not the exact result of the requested step-status transition.");
        }

        return new WorkflowExecutionEvent(
            this.WorkflowId,
            sequence_,
            WorkflowExecutionEventKind.StepStatusTransitioned,
            currentStep.TaskId,
            previousWorkflowStatus_: null,
            targetWorkflowStatus_: null,
            previousStepStatus_: currentStep.Status,
            targetStepStatus_: targetStep.Status);
    }

    private static void EnsureStableWorkflowIdentity(
        WorkflowState current_,
        WorkflowState target_)
    {
        if (current_.SourceImplementationPlanRevision !=
            target_.SourceImplementationPlanRevision)
        {
            throw new InvalidOperationException(
                "Persistence append cannot change source implementation plan revision.");
        }

        if (current_.Steps.Count !=
            target_.Steps.Count)
        {
            throw new InvalidOperationException(
                "Persistence append cannot change workflow step count.");
        }

        for (int index = 0; index < current_.Steps.Count; index++)
        {
            if (!string.Equals(
                    current_.Steps[index].TaskId,
                    target_.Steps[index].TaskId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Persistence append cannot add, remove, rename or reorder workflow task identities.");
            }
        }
    }

    private static void ValidateInitialSnapshot(
        WorkflowPersistenceSnapshot snapshot_)
    {
        if (snapshot_.Revision != 1 ||
            snapshot_.LastEvent.Kind !=
                WorkflowExecutionEventKind.WorkflowInitialized ||
            snapshot_.State.Status !=
                WorkflowStatus.Created)
        {
            throw new InvalidDataException(
                "First workflow persistence record must be a Created initialization at revision 1.");
        }

        for (int index = 0; index < snapshot_.State.Steps.Count; index++)
        {
            if (snapshot_.State.Steps[index].Status !=
                WorkflowStepStatus.Pending)
            {
                throw new InvalidDataException(
                    "Initial persisted workflow state must contain only Pending steps.");
            }
        }
    }

    private static void ValidatePersistedTransition(
        WorkflowPersistenceSnapshot previous_,
        WorkflowPersistenceSnapshot current_)
    {
        if (current_.Revision !=
            previous_.Revision + 1)
        {
            throw new InvalidDataException(
                "Workflow persistence revision sequence is not contiguous.");
        }

        WorkflowExecutionEvent eventValue =
            current_.LastEvent;

        WorkflowState expected;

        try
        {
            switch (eventValue.Kind)
            {
                case WorkflowExecutionEventKind.WorkflowStatusTransitioned:
                    if (eventValue.PreviousWorkflowStatus !=
                        previous_.State.Status)
                    {
                        throw new InvalidDataException(
                            "Persisted workflow event previous status does not match prior state.");
                    }

                    expected =
                        WorkflowStateMachine.TransitionWorkflow(
                            previous_.State,
                            eventValue.TargetWorkflowStatus!.Value);

                    break;

                case WorkflowExecutionEventKind.StepStatusTransitioned:
                    int stepIndex =
                        FindStepIndex(
                            previous_.State,
                            eventValue.TaskId!);

                    if (stepIndex < 0)
                    {
                        throw new InvalidDataException(
                            "Persisted step event references an unknown task.");
                    }

                    if (eventValue.PreviousStepStatus !=
                        previous_.State.Steps[stepIndex].Status)
                    {
                        throw new InvalidDataException(
                            "Persisted step event previous status does not match prior state.");
                    }

                    expected =
                        WorkflowStateMachine.TransitionStep(
                            previous_.State,
                            eventValue.TaskId!,
                            eventValue.TargetStepStatus!.Value);

                    break;

                case WorkflowExecutionEventKind.WorkflowInitialized:
                    throw new InvalidDataException(
                        "WorkflowInitialized event may appear only at revision 1.");

                default:
                    throw new InvalidDataException(
                        "Persisted workflow event kind is invalid.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException)
        {
            throw new InvalidDataException(
                "Persisted workflow transition is not legal under the P01 state machine.",
                exception);
        }

        if (!expected.Equals(
                current_.State))
        {
            throw new InvalidDataException(
                "Persisted workflow state does not match its execution event.");
        }
    }

    private static int FindStepIndex(
        WorkflowState state_,
        string taskId_)
    {
        for (int index = 0; index < state_.Steps.Count; index++)
        {
            if (string.Equals(
                    state_.Steps[index].TaskId,
                    taskId_,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

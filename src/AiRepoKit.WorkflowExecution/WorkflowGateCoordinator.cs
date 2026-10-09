namespace AiRepoKit.WorkflowExecution;

using System.Security.Cryptography;
using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public sealed class WorkflowGateCoordinator
{
    public const string AlgorithmId =
        "ai.repokit.workflow-gate-coordinator/v1";

    public const string JournalSchemaId =
        "ai.repokit.workflow-gate-journal-record";

    public const int JournalSchemaVersion =
        1;

    private readonly WorkflowPersistenceStore _persistenceStore;
    private readonly IAgentExecutor[] _executors;
    private readonly IValidationExecutor[] _validators;
    private readonly WorkflowRepairPolicy _repairPolicy;
    private readonly IWorkflowSideEffectReconciler _reconciler;
    private readonly WorkflowGatePolicy _gatePolicy;
    private readonly IWorkflowGateVerifier _verifier;
    private readonly WorkflowCheckpointCoordinator _checkpointCoordinator;

    public WorkflowGateCoordinator(
        WorkflowPersistenceStore persistenceStore_,
        IEnumerable<IAgentExecutor> executors_,
        IEnumerable<IValidationExecutor> validators_,
        WorkflowRepairPolicy repairPolicy_,
        IWorkflowSideEffectReconciler reconciler_,
        WorkflowGatePolicy gatePolicy_,
        IWorkflowGateVerifier verifier_)
    {
        ArgumentNullException.ThrowIfNull(
            persistenceStore_,
            nameof(persistenceStore_));
        ArgumentNullException.ThrowIfNull(
            executors_,
            nameof(executors_));
        ArgumentNullException.ThrowIfNull(
            validators_,
            nameof(validators_));
        ArgumentNullException.ThrowIfNull(
            repairPolicy_,
            nameof(repairPolicy_));
        ArgumentNullException.ThrowIfNull(
            reconciler_,
            nameof(reconciler_));
        ArgumentNullException.ThrowIfNull(
            gatePolicy_,
            nameof(gatePolicy_));
        ArgumentNullException.ThrowIfNull(
            verifier_,
            nameof(verifier_));

        List<IAgentExecutor> executorList = [];
        foreach (IAgentExecutor? executor in executors_)
        {
            if (executor is null)
            {
                throw new ArgumentException(
                    "Executor collection cannot contain null elements.",
                    nameof(executors_));
            }

            if (executor.ProviderId is null)
            {
                throw new InvalidOperationException(
                    "Agent executor provider ID cannot be null.");
            }

            if (executor.Capabilities is null)
            {
                throw new InvalidOperationException(
                    $"Agent executor '{executor.ProviderId.Value}' capability set cannot be null.");
            }

            executorList.Add(executor);
        }

        if (executorList.Count == 0)
        {
            throw new ArgumentException(
                "At least one agent executor is required.",
                nameof(executors_));
        }

        executorList.Sort(
            static (left_, right_) =>
                string.CompareOrdinal(
                    left_.ProviderId.Value,
                    right_.ProviderId.Value));

        for (int index = 1; index < executorList.Count; index++)
        {
            if (string.Equals(
                    executorList[index - 1].ProviderId.Value,
                    executorList[index].ProviderId.Value,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Duplicate agent provider ID '{executorList[index].ProviderId.Value}'.",
                    nameof(executors_));
            }
        }

        List<IValidationExecutor> validatorList = [];
        HashSet<ValidationStrategy> strategies = [];

        foreach (IValidationExecutor? validator in validators_)
        {
            if (validator is null)
            {
                throw new ArgumentException(
                    "Validator collection cannot contain null elements.",
                    nameof(validators_));
            }

            ValidationStrategy strategy = validator.Strategy;

            if (!Enum.IsDefined(typeof(ValidationStrategy), strategy))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(validators_),
                    strategy,
                    "Validator strategy must be a defined ValidationStrategy value.");
            }

            if (!strategies.Add(strategy))
            {
                throw new ArgumentException(
                    $"Duplicate validator strategy detected: '{strategy}'.",
                    nameof(validators_));
            }

            validatorList.Add(validator);
        }

        this._persistenceStore = persistenceStore_;
        this._executors = executorList.ToArray();
        this._validators = validatorList.ToArray();
        this._repairPolicy = repairPolicy_;
        this._reconciler = reconciler_;
        this._gatePolicy = gatePolicy_;
        this._verifier = verifier_;

        this._checkpointCoordinator = new WorkflowCheckpointCoordinator(
            this._persistenceStore,
            this._executors,
            this._validators,
            this._repairPolicy,
            this._reconciler);
    }

    public async Task<WorkflowGateResult> ExecuteAsync(
        ExecutableWork work_,
        ExecutionEnvelope envelope_,
        WorkflowGateProof? proof_ = null,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));
        ArgumentNullException.ThrowIfNull(
            envelope_,
            nameof(envelope_));

        WorkflowPersistenceSnapshot? currentSnapshot =
            this._persistenceStore.Load();

        if (currentSnapshot is null)
        {
            throw new InvalidOperationException(
                "Workflow persistence store must be initialized.");
        }

        if (work_.SourceImplementationPlanRevision !=
            currentSnapshot.State.SourceImplementationPlanRevision)
        {
            throw new InvalidOperationException(
                "Executable work source implementation plan revision does not match canonical workflow state.");
        }

        bool taskExistsInWork =
            work_.Tasks.Any(task_ => string.Equals(task_.Id, envelope_.TaskId, StringComparison.Ordinal));

        if (!taskExistsInWork)
        {
            throw new ArgumentException(
                $"Executable work does not contain task identifier '{envelope_.TaskId}'.",
                nameof(work_));
        }

        WorkflowStepState? targetStep =
            currentSnapshot.State.Steps.FirstOrDefault(step_ => string.Equals(step_.TaskId, envelope_.TaskId, StringComparison.Ordinal));

        if (targetStep is null)
        {
            throw new ArgumentException(
                $"Canonical workflow state does not contain task identifier '{envelope_.TaskId}'.",
                nameof(envelope_));
        }

        if (envelope_.SessionReference is not null)
        {
            throw new InvalidOperationException(
                "Workflow checkpoint execution requires a null execution-envelope session reference.");
        }

        bool requiresGate =
            this._gatePolicy.RequiresGate(envelope_.Permission);

        if (!requiresGate)
        {
            if (proof_ is not null)
            {
                throw new InvalidOperationException(
                    "Workflow gate proof was provided for an operation that does not require a human gate.");
            }

            WorkflowCheckpointResult bypassedResult =
                await this._checkpointCoordinator.ExecuteAsync(
                    work_,
                    envelope_,
                    cancellationToken_).ConfigureAwait(false);

            return WorkflowGateResult.CreateBypassed(bypassedResult);
        }

        cancellationToken_.ThrowIfCancellationRequested();

        string inputFingerprint =
            WorkflowCheckpointCoordinatorContext.ComputeInputFingerprint(
                work_,
                envelope_,
                this._repairPolicy);

        string registryFingerprint =
            ComputeRegistryFingerprint(
                this._executors,
                this._validators);

        string policyFingerprint =
            ComputePolicyFingerprint(
                this._gatePolicy);

        string storageRoot =
            this._persistenceStore.StorageRoot;

        WorkflowId workflowId =
            this._persistenceStore.WorkflowId;

        IReadOnlyList<long> candidateRevisions =
            WorkflowGateJournal.DiscoverCandidateRevisions(
                storageRoot,
                workflowId,
                envelope_.TaskId);

        WorkflowGateJournal? journal = null;
        WorkflowGateChallenge? challenge = null;

        IReadOnlyList<WorkflowExecutionEvent> events =
            this._persistenceStore.ReadEvents();

        foreach (long candidateRevision in candidateRevisions)
        {
            WorkflowGateJournal candidateJournal =
                WorkflowGateJournal.OpenExisting(
                    storageRoot,
                    workflowId,
                    envelope_.TaskId,
                    candidateRevision);

            ChallengeCreatedRecord challengeRecord =
                candidateJournal.ChallengeRecord;

            if (!string.Equals(challengeRecord.InputFingerprint, inputFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compatible human gate input fingerprint mismatch.");
            }

            if (!string.Equals(challengeRecord.RegistryFingerprint, registryFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compatible human gate registry fingerprint mismatch.");
            }

            if (!string.Equals(challengeRecord.PolicyFingerprint, policyFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compatible human gate policy fingerprint mismatch.");
            }

            if (challengeRecord.RequiredPermission != (int) envelope_.Permission)
            {
                throw new InvalidOperationException(
                    "Compatible human gate required permission mismatch.");
            }

            if (candidateRevision > currentSnapshot.Revision)
            {
                throw new InvalidOperationException(
                    "Candidate human gate base revision exceeds current persistence revision.");
            }

            WorkflowState baseState =
                this.GetStateAtRevision(
                    candidateRevision,
                    work_,
                    currentSnapshot);

            string baseStateFingerprint =
                ComputeBaseStateFingerprint(baseState);

            if (!string.Equals(challengeRecord.BaseStateFingerprint, baseStateFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compatible human gate base state fingerprint mismatch.");
            }

            WorkflowGateChallenge candidateChallenge =
                new(
                    challengeRecord.GateId,
                    workflowId,
                    envelope_.TaskId,
                    challengeRecord.BasePersistenceRevision,
                    work_.SourceImplementationPlanRevision,
                    envelope_.Permission,
                    challengeRecord.GateOrdinal,
                    challengeRecord.BaseStateFingerprint,
                    challengeRecord.InputFingerprint,
                    challengeRecord.RegistryFingerprint,
                    challengeRecord.PolicyFingerprint);

            if (candidateJournal.DeniedRecord is not null)
            {
                if (currentSnapshot.Revision != candidateRevision + 1)
                {
                    throw new InvalidOperationException(
                        "Canonical history diverged from expected denied human gate prefix.");
                }

                ValidateCanonicalBlockedTransition(
                    events,
                    candidateRevision,
                    envelope_.TaskId);

                return WorkflowGateResult.CreateDenied(
                    candidateChallenge,
                    currentSnapshot);
            }

            if (candidateJournal.GrantedRecord is not null)
            {
                ValidateCanonicalBlockedTransition(
                    events,
                    candidateRevision,
                    envelope_.TaskId);

                if (currentSnapshot.Revision == candidateRevision + 1)
                {
                    WorkflowState unblockedState =
                        WorkflowStateMachine.TransitionStep(
                            currentSnapshot.State,
                            envelope_.TaskId,
                            WorkflowStepStatus.Running);

                    currentSnapshot =
                        this._persistenceStore.Append(
                            unblockedState,
                            currentSnapshot.Revision);

                    WorkflowCheckpointResult checkpointResult =
                        await this._checkpointCoordinator.ExecuteAsync(
                            work_,
                            envelope_,
                            cancellationToken_).ConfigureAwait(false);

                    return WorkflowGateResult.CreateApproved(
                        candidateChallenge,
                        checkpointResult);
                }

                if (currentSnapshot.Revision >= candidateRevision + 2)
                {
                    ValidateCanonicalUnblockedTransition(
                        events,
                        candidateRevision,
                        envelope_.TaskId);

                    WorkflowCheckpointResult checkpointResult =
                        await this._checkpointCoordinator.ExecuteAsync(
                            work_,
                            envelope_,
                            cancellationToken_).ConfigureAwait(false);

                    return WorkflowGateResult.CreateApproved(
                        candidateChallenge,
                        checkpointResult);
                }

                throw new InvalidOperationException(
                    "Canonical history revision is behind expected granted gate state.");
            }

            // Challenge only (sequence 1)
            if (currentSnapshot.Revision == candidateRevision)
            {
                WorkflowState blockedState =
                    WorkflowStateMachine.TransitionStep(
                        currentSnapshot.State,
                        envelope_.TaskId,
                        WorkflowStepStatus.Blocked);

                currentSnapshot =
                    this._persistenceStore.Append(
                        blockedState,
                        currentSnapshot.Revision);
            }
            else if (currentSnapshot.Revision == candidateRevision + 1)
            {
                ValidateCanonicalBlockedTransition(
                    events,
                    candidateRevision,
                    envelope_.TaskId);
            }
            else
            {
                throw new InvalidOperationException(
                    "Canonical history diverged from expected challenge-only human gate prefix.");
            }

            journal = candidateJournal;
            challenge = candidateChallenge;
            break;
        }

        if (journal is null || challenge is null)
        {
            if (targetStep.Status != WorkflowStepStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Cannot create human gate: target step status is '{targetStep.Status}'. It must be Running.");
            }

            long basePersistenceRevision =
                currentSnapshot.Revision;

            string baseStateFingerprint =
                ComputeBaseStateFingerprint(currentSnapshot.State);

            string gateId =
                ComputeGateId(
                    workflowId,
                    envelope_.TaskId,
                    basePersistenceRevision,
                    work_.SourceImplementationPlanRevision,
                    envelope_.Permission,
                    baseStateFingerprint,
                    inputFingerprint,
                    registryFingerprint,
                    policyFingerprint);

            challenge =
                new WorkflowGateChallenge(
                    gateId,
                    workflowId,
                    envelope_.TaskId,
                    basePersistenceRevision,
                    work_.SourceImplementationPlanRevision,
                    envelope_.Permission,
                    1L,
                    baseStateFingerprint,
                    inputFingerprint,
                    registryFingerprint,
                    policyFingerprint);

            journal =
                WorkflowGateJournal.CreateNew(
                    storageRoot,
                    workflowId,
                    envelope_.TaskId,
                    basePersistenceRevision,
                    challenge);

            WorkflowState blockedState =
                WorkflowStateMachine.TransitionStep(
                    currentSnapshot.State,
                    envelope_.TaskId,
                    WorkflowStepStatus.Blocked);

            currentSnapshot =
                this._persistenceStore.Append(
                    blockedState,
                    currentSnapshot.Revision);
        }

        if (proof_ is null)
        {
            return WorkflowGateResult.CreatePending(
                challenge,
                currentSnapshot);
        }

        if (!string.Equals(proof_.GateId, challenge.GateId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Workflow gate proof GateId does not match current challenge GateId.",
                nameof(proof_));
        }

        cancellationToken_.ThrowIfCancellationRequested();

        WorkflowGateVerificationResult verificationResult =
            await this._verifier.VerifyAsync(
                challenge,
                proof_,
                cancellationToken_).ConfigureAwait(false);

        if (verificationResult is null)
        {
            throw new InvalidOperationException(
                "Workflow gate verifier returned null verification result.");
        }

        if (!verificationResult.IsAuthenticated)
        {
            throw new InvalidOperationException(
                $"Workflow gate verification unauthenticated: {verificationResult.FailureReason}");
        }

        if (!verificationResult.IsAuthorized)
        {
            AuthorizationDeniedRecord denialRecord =
                new()
                {
                    Sequence = 2,
                    GateId = challenge.GateId,
                    PrincipalId = verificationResult.PrincipalId!,
                    MechanismId = verificationResult.MechanismId!,
                    EvidenceFingerprint = verificationResult.EvidenceFingerprint!
                };

            journal.AppendRecord(denialRecord);

            return WorkflowGateResult.CreateDenied(
                challenge,
                currentSnapshot);
        }

        AuthorizationGrantedRecord grantRecord =
            new()
            {
                Sequence = 2,
                GateId = challenge.GateId,
                PrincipalId = verificationResult.PrincipalId!,
                MechanismId = verificationResult.MechanismId!,
                EvidenceFingerprint = verificationResult.EvidenceFingerprint!
            };

        journal.AppendRecord(grantRecord);

        WorkflowState runningState =
            WorkflowStateMachine.TransitionStep(
                currentSnapshot.State,
                envelope_.TaskId,
                WorkflowStepStatus.Running);

        currentSnapshot =
            this._persistenceStore.Append(
                runningState,
                currentSnapshot.Revision);

        WorkflowCheckpointResult executionResult =
            await this._checkpointCoordinator.ExecuteAsync(
                work_,
                envelope_,
                cancellationToken_).ConfigureAwait(false);

        return WorkflowGateResult.CreateApproved(
            challenge,
            executionResult);
    }

    private static void ValidateCanonicalBlockedTransition(
        IReadOnlyList<WorkflowExecutionEvent> events_,
        long candidateRevision_,
        string taskId_)
    {
        if (events_.Count <= (int) candidateRevision_)
        {
            throw new InvalidOperationException(
                "Canonical events list does not contain expected blocked transition event.");
        }

        WorkflowExecutionEvent blockedEvent =
            events_[(int) candidateRevision_];

        if (!string.Equals(blockedEvent.TaskId, taskId_, StringComparison.Ordinal) ||
            blockedEvent.PreviousStepStatus != WorkflowStepStatus.Running ||
            blockedEvent.TargetStepStatus != WorkflowStepStatus.Blocked)
        {
            throw new InvalidOperationException(
                $"Canonical event at revision {candidateRevision_ + 1} does not match expected Running -> Blocked transition for task '{taskId_}'.");
        }
    }

    private static void ValidateCanonicalUnblockedTransition(
        IReadOnlyList<WorkflowExecutionEvent> events_,
        long candidateRevision_,
        string taskId_)
    {
        if (events_.Count <= (int) candidateRevision_ + 1)
        {
            throw new InvalidOperationException(
                "Canonical events list does not contain expected unblocked transition event.");
        }

        WorkflowExecutionEvent unblockedEvent =
            events_[(int) candidateRevision_ + 1];

        if (!string.Equals(unblockedEvent.TaskId, taskId_, StringComparison.Ordinal) ||
            unblockedEvent.PreviousStepStatus != WorkflowStepStatus.Blocked ||
            unblockedEvent.TargetStepStatus != WorkflowStepStatus.Running)
        {
            throw new InvalidOperationException(
                $"Canonical event at revision {candidateRevision_ + 2} does not match expected Blocked -> Running transition for task '{taskId_}'.");
        }
    }

    private WorkflowState GetStateAtRevision(
        long revision_,
        ExecutableWork work_,
        WorkflowPersistenceSnapshot currentSnapshot_)
    {
        if (revision_ == currentSnapshot_.Revision)
        {
            return currentSnapshot_.State;
        }

        IReadOnlyList<WorkflowExecutionEvent> events =
            this._persistenceStore.ReadEvents();

        WorkflowState state =
            WorkflowStateMachine.Create(work_);

        for (int index = 0; index < (int) revision_; index++)
        {
            if (index >= events.Count)
            {
                break;
            }

            WorkflowExecutionEvent executionEvent =
                events[index];

            if (executionEvent.Kind == WorkflowExecutionEventKind.WorkflowInitialized)
            {
                continue;
            }

            if (executionEvent.TargetWorkflowStatus.HasValue)
            {
                state = WorkflowStateMachine.TransitionWorkflow(
                    state,
                    executionEvent.TargetWorkflowStatus.Value);
            }
            else if (executionEvent.TargetStepStatus.HasValue && executionEvent.TaskId is not null)
            {
                state = WorkflowStateMachine.TransitionStep(
                    state,
                    executionEvent.TaskId,
                    executionEvent.TargetStepStatus.Value);
            }
        }

        return state;
    }

    internal static string ComputeBaseStateFingerprint(
        WorkflowState state_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sourceImplementationPlanRevision", state_.SourceImplementationPlanRevision);
            writer.WriteNumber("status", (int) state_.Status);
            writer.WriteStartArray("steps");
            foreach (WorkflowStepState step in state_.Steps.OrderBy(s => s.TaskId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", step.TaskId);
                writer.WriteNumber("status", (int) step.Status);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string ComputePolicyFingerprint(
        WorkflowGatePolicy policy_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("requireGateForReadOnly", policy_.RequireGateForReadOnly);
            writer.WriteBoolean("requireGateForWorkspaceWrite", policy_.RequireGateForWorkspaceWrite);
            writer.WriteBoolean("requireGateForUnrestricted", policy_.RequireGateForUnrestricted);
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string ComputeRegistryFingerprint(
        IEnumerable<IAgentExecutor> executors_,
        IEnumerable<IValidationExecutor> validators_)
    {
        IAgentExecutor[] sortedExecutors =
            executors_
                .OrderBy(e => e.ProviderId.Value, StringComparer.Ordinal)
                .ToArray();

        ValidationStrategy[] sortedStrategies =
            validators_
                .Select(v => v.Strategy)
                .Distinct()
                .OrderBy(s => (int) s)
                .ToArray();

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("providers");
            foreach (IAgentExecutor executor in sortedExecutors)
            {
                writer.WriteStartObject();
                writer.WriteString("providerId", executor.ProviderId.Value);
                writer.WriteStartArray("capabilities");
                foreach (AgentCapability capability in executor.Capabilities.OrderBy(c => c.Value, StringComparer.Ordinal))
                {
                    writer.WriteStringValue(capability.Value);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("validationStrategies");
            foreach (ValidationStrategy strategy in sortedStrategies)
            {
                writer.WriteNumberValue((int) strategy);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string ComputeGateId(
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_,
        int sourceImplementationPlanRevision_,
        ExecutionPermission requiredPermission_,
        string baseStateFingerprint_,
        string inputFingerprint_,
        string registryFingerprint_,
        string policyFingerprint_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("algorithmId", AlgorithmId);
            writer.WriteString("workflowId", workflowId_.Value);
            writer.WriteString("taskId", taskId_);
            writer.WriteNumber("basePersistenceRevision", basePersistenceRevision_);
            writer.WriteNumber("gateOrdinal", 1L);
            writer.WriteNumber("sourceImplementationPlanRevision", sourceImplementationPlanRevision_);
            writer.WriteNumber("requiredPermission", (int) requiredPermission_);
            writer.WriteString("baseStateFingerprint", baseStateFingerprint_);
            writer.WriteString("inputFingerprint", inputFingerprint_);
            writer.WriteString("registryFingerprint", registryFingerprint_);
            writer.WriteString("policyFingerprint", policyFingerprint_);
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

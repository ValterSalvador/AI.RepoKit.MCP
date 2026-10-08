using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AiRepoKit.WorkflowExecution.Tests")]

namespace AiRepoKit.WorkflowExecution;

using System.Security.Cryptography;
using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public sealed class WorkflowCheckpointCoordinator
{
    public const string AlgorithmId =
        "ai.repokit.workflow-checkpoint-coordinator/v1";

    public const string JournalSchemaId =
        "ai.repokit.workflow-execution-journal-record";

    public const int JournalSchemaVersion =
        1;

    private readonly WorkflowPersistenceStore _persistenceStore;
    private readonly IAgentExecutor[] _executors;
    private readonly IValidationExecutor[] _validators;
    private readonly HashSet<ValidationStrategy> _validationStrategies;
    private readonly WorkflowRepairPolicy _policy;
    private readonly IWorkflowSideEffectReconciler _reconciler;

    public WorkflowCheckpointCoordinator(
        WorkflowPersistenceStore persistenceStore_,
        IEnumerable<IAgentExecutor> executors_,
        IEnumerable<IValidationExecutor> validators_,
        WorkflowRepairPolicy policy_,
        IWorkflowSideEffectReconciler reconciler_)
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
            policy_,
            nameof(policy_));
        ArgumentNullException.ThrowIfNull(
            reconciler_,
            nameof(reconciler_));

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

        for (int i = 1; i < executorList.Count; i++)
        {
            if (string.Equals(
                    executorList[i - 1].ProviderId.Value,
                    executorList[i].ProviderId.Value,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Duplicate agent provider ID '{executorList[i].ProviderId.Value}'.",
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
        this._validationStrategies = strategies;
        this._policy = policy_;
        this._reconciler = reconciler_;
    }

    public async Task<WorkflowCheckpointResult> ExecuteAsync(
        ExecutableWork work_,
        ExecutionEnvelope envelope_,
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
            work_.Tasks.Any(t => string.Equals(t.Id, envelope_.TaskId, StringComparison.Ordinal));

        if (!taskExistsInWork)
        {
            throw new ArgumentException(
                $"Executable work does not contain task identifier '{envelope_.TaskId}'.",
                nameof(work_));
        }

        WorkflowStepState? targetStep =
            currentSnapshot.State.Steps.FirstOrDefault(s => string.Equals(s.TaskId, envelope_.TaskId, StringComparison.Ordinal));

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

        for (int i = 0; i < work_.ValidationRequirements.Count; i++)
        {
            ValidationRequirement requirement = work_.ValidationRequirements[i];
            if (string.Equals(requirement.TaskId, envelope_.TaskId, StringComparison.Ordinal) &&
                !this._validationStrategies.Contains(requirement.Strategy))
            {
                throw new InvalidOperationException(
                    $"No validator is registered for validation strategy '{requirement.Strategy}'.");
            }
        }

        bool hasEligibleAgent =
            this._executors.Any(e => e.Capabilities.SupportsAll(envelope_.AgentRequiredCapabilities));

        if (!hasEligibleAgent)
        {
            throw new InvalidOperationException(
                $"No eligible agent executor satisfies the required capabilities {envelope_.AgentRequiredCapabilities}.");
        }

        cancellationToken_.ThrowIfCancellationRequested();

        string inputFingerprint =
            WorkflowCheckpointCoordinatorContext.ComputeInputFingerprint(
                work_,
                envelope_,
                this._policy);

        string registryFingerprint =
            WorkflowCheckpointCoordinatorContext.ComputeRegistryFingerprint(
                this._executors,
                this._validators);

        string baseWorkflowStateFingerprint =
            WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(
                currentSnapshot.State);

        string storageRoot =
            this._persistenceStore.StorageRoot;

        WorkflowId workflowId =
            this._persistenceStore.WorkflowId;

        IReadOnlyList<long> candidateRevisions =
            WorkflowCheckpointJournal.DiscoverCandidateRevisions(
                storageRoot,
                workflowId,
                envelope_.TaskId);

        WorkflowCheckpointJournal? journal = null;
        bool resumed = false;
        long basePersistenceRevision = 0;

        foreach (long candidateRevision in candidateRevisions)
        {
            WorkflowCheckpointJournal candidateJournal =
                WorkflowCheckpointJournal.OpenExisting(
                    storageRoot,
                    workflowId,
                    envelope_.TaskId,
                    candidateRevision);

            if (!string.Equals(candidateJournal.StartedRecord.InputFingerprint, inputFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compatible checkpoint input fingerprint mismatch.");
            }

            if (!string.Equals(candidateJournal.StartedRecord.RegistryFingerprint, registryFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compatible checkpoint registry fingerprint mismatch.");
            }

            // Verify base workflow state of candidate
            WorkflowState candidateBaseState =
                this.GetStateAtRevision(candidateRevision, work_, currentSnapshot);

            string expectedBaseStateFingerprint =
                WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(candidateBaseState);

            if (!string.Equals(candidateJournal.StartedRecord.BaseWorkflowStateFingerprint, expectedBaseStateFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Compatible checkpoint base workflow state mismatch.");
            }

            if (candidateJournal.ProjectionCompletedRecord is not null)
            {
                // Completed checkpoint: check if canonical state matches expected projection prefix
                if (currentSnapshot.Revision >= candidateRevision)
                {
                    journal = candidateJournal;
                    resumed = true;
                    basePersistenceRevision = candidateRevision;
                    break;
                }
            }
            else
            {
                // Incomplete checkpoint: must match base revision
                if (candidateRevision <= currentSnapshot.Revision)
                {
                    journal = candidateJournal;
                    resumed = true;
                    basePersistenceRevision = candidateRevision;
                    break;
                }
            }
        }

        if (journal is null)
        {
            if (currentSnapshot.State.Status != WorkflowStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Cannot create new checkpoint: workflow status is '{currentSnapshot.State.Status}'. It must be Running.");
            }

            if (targetStep.Status != WorkflowStepStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Cannot create new checkpoint: target step status is '{targetStep.Status}'. It must be Running.");
            }

            basePersistenceRevision =
                currentSnapshot.Revision;

            journal =
                WorkflowCheckpointJournal.CreateNew(
                    storageRoot,
                    workflowId,
                    envelope_.TaskId,
                    basePersistenceRevision,
                    baseWorkflowStateFingerprint,
                    inputFingerprint,
                    registryFingerprint);

            resumed = false;
        }

        WorkflowCheckpointCoordinatorContext context =
            new(
                journal,
                this._reconciler,
                workflowId,
                envelope_.TaskId,
                basePersistenceRevision,
                inputFingerprint);

        WorkflowState baseState =
            this.GetStateAtRevision(basePersistenceRevision, work_, currentSnapshot);

        WorkflowCheckpointAgentExecutor[] wrappedExecutors =
            this._executors
                .Select(e => new WorkflowCheckpointAgentExecutor(e, context))
                .ToArray();

        WorkflowCheckpointValidationExecutor[] wrappedValidators =
            this._validators
                .Select(v => new WorkflowCheckpointValidationExecutor(v, context))
                .ToArray();

        WorkflowRepairCoordinator repairCoordinator =
            new(
                wrappedExecutors,
                wrappedValidators,
                this._policy);

        WorkflowRepairResult repairResult =
            await repairCoordinator.ExecuteAsync(
                baseState,
                work_,
                envelope_,
                cancellationToken_).ConfigureAwait(false);

        IReadOnlyList<WorkflowState> expectedTransitions =
            DeriveExpectedProjection(
                baseState,
                envelope_.TaskId,
                repairResult);

        if (journal.CompletedRecord is null)
        {
            string finalStateFingerprint =
                WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(
                    repairResult.State);

            ExecutionCompletedRecord completedRecord =
                new()
                {
                    Sequence = journal.NextSequence,
                    FinalStateFingerprint = finalStateFingerprint,
                    AttemptCount = repairResult.Attempts.Count,
                    BudgetExhausted = repairResult.BudgetExhausted,
                    ExpectedProjectionTransitionCount = expectedTransitions.Count
                };

            journal.AppendRecord(completedRecord);
        }

        // Canonical projection
        IReadOnlyList<WorkflowExecutionEvent> events =
            this._persistenceStore.ReadEvents();

        long currentRevision =
            events.Count == 0 ? 0 : events[events.Count - 1].Sequence;

        int alreadyPersistedCount =
            (int) (currentRevision - basePersistenceRevision);

        if (alreadyPersistedCount > expectedTransitions.Count)
        {
            throw new InvalidOperationException(
                "Canonical persistence store has more transitions than expected projection.");
        }

        for (int i = 0; i < alreadyPersistedCount; i++)
        {
            long rev = basePersistenceRevision + 1 + i;
            WorkflowExecutionEvent evt = events[(int) rev - 1];
            WorkflowStepState expectedStep =
                expectedTransitions[i].Steps.First(s => string.Equals(s.TaskId, envelope_.TaskId, StringComparison.Ordinal));

            if (!string.Equals(evt.TaskId, envelope_.TaskId, StringComparison.Ordinal) ||
                evt.TargetStepStatus != expectedStep.Status)
            {
                throw new InvalidOperationException(
                    $"Canonical projection divergence detected at revision {rev}.");
            }
        }

        for (int i = alreadyPersistedCount; i < expectedTransitions.Count; i++)
        {
            long expectedRevision =
                basePersistenceRevision + i;

            this._persistenceStore.Append(
                expectedTransitions[i],
                expectedRevision);
        }

        WorkflowPersistenceSnapshot finalSnapshot =
            this._persistenceStore.Load()!;

        if (!AreStatesEqual(finalSnapshot.State, repairResult.State))
        {
            throw new InvalidOperationException(
                "Final canonical state does not match expected repair result state.");
        }

        if (journal.ProjectionCompletedRecord is null)
        {
            ProjectionCompletedRecord projectionCompletedRecord =
                new()
                {
                    Sequence = journal.NextSequence,
                    FinalPersistenceRevision = finalSnapshot.Revision
                };

            journal.AppendRecord(projectionCompletedRecord);
        }

        return new WorkflowCheckpointResult(
            finalSnapshot,
            repairResult,
            resumed,
            context.ReconciliationUsed);
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

        for (int i = 0; i < (int) revision_; i++)
        {
            if (i >= events.Count)
            {
                break;
            }

            WorkflowExecutionEvent evt = events[i];

            if (evt.Kind == WorkflowExecutionEventKind.WorkflowInitialized)
            {
                continue;
            }

            if (evt.TargetWorkflowStatus.HasValue)
            {
                state = WorkflowStateMachine.TransitionWorkflow(state, evt.TargetWorkflowStatus.Value);
            }
            else if (evt.TargetStepStatus.HasValue && evt.TaskId is not null)
            {
                state = WorkflowStateMachine.TransitionStep(state, evt.TaskId, evt.TargetStepStatus.Value);
            }
        }

        return state;
    }

    private static IReadOnlyList<WorkflowState> DeriveExpectedProjection(
        WorkflowState baseState_,
        string taskId_,
        WorkflowRepairResult repairResult_)
    {
        List<WorkflowState> states = [];
        WorkflowState currentState = baseState_;

        for (int attemptIndex = 0; attemptIndex < repairResult_.Attempts.Count; attemptIndex++)
        {
            WorkflowRepairAttempt attempt = repairResult_.Attempts[attemptIndex];
            AgentExecutionResult agentResult = attempt.AgentResult;

            if (agentResult.Status == AgentExecutionStatus.Completed)
            {
                currentState = WorkflowStateMachine.TransitionStep(
                    currentState,
                    taskId_,
                    WorkflowStepStatus.AwaitingValidation);
                states.Add(currentState);

                bool anyFailed =
                    attempt.ValidationEvidence.Any(e => !e.ValidationResult.Passed);

                if (!anyFailed)
                {
                    currentState = WorkflowStateMachine.TransitionStep(
                        currentState,
                        taskId_,
                        WorkflowStepStatus.Completed);
                    states.Add(currentState);
                }
                else
                {
                    currentState = WorkflowStateMachine.TransitionStep(
                        currentState,
                        taskId_,
                        WorkflowStepStatus.Failed);
                    states.Add(currentState);
                }
            }
            else if (agentResult.Status == AgentExecutionStatus.Failed)
            {
                currentState = WorkflowStateMachine.TransitionStep(
                    currentState,
                    taskId_,
                    WorkflowStepStatus.Failed);
                states.Add(currentState);
            }
            else if (agentResult.Status == AgentExecutionStatus.Blocked ||
                     agentResult.Status == AgentExecutionStatus.NeedsInput)
            {
                currentState = WorkflowStateMachine.TransitionStep(
                    currentState,
                    taskId_,
                    WorkflowStepStatus.Blocked);
                states.Add(currentState);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected agent execution status '{agentResult.Status}'.");
            }

            if (attemptIndex + 1 < repairResult_.Attempts.Count)
            {
                currentState = WorkflowStateMachine.TransitionStep(
                    currentState,
                    taskId_,
                    WorkflowStepStatus.Pending);
                states.Add(currentState);

                currentState = WorkflowStateMachine.TransitionStep(
                    currentState,
                    taskId_,
                    WorkflowStepStatus.Running);
                states.Add(currentState);
            }
        }

        if (!AreStatesEqual(currentState, repairResult_.State))
        {
            throw new InvalidOperationException(
                "Derived projection final state does not match repair result state.");
        }

        return states;
    }

    private static bool AreStatesEqual(WorkflowState a, WorkflowState b)
    {
        if (a.Status != b.Status ||
            a.SourceImplementationPlanRevision != b.SourceImplementationPlanRevision ||
            a.Steps.Count != b.Steps.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Steps.Count; i++)
        {
            if (!string.Equals(a.Steps[i].TaskId, b.Steps[i].TaskId, StringComparison.Ordinal) ||
                a.Steps[i].Status != b.Steps[i].Status)
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class WorkflowCheckpointCoordinatorContext
{
    private readonly WorkflowCheckpointJournal _journal;
    private readonly IWorkflowSideEffectReconciler _reconciler;
    private readonly WorkflowId _workflowId;
    private readonly string _taskId;
    private readonly long _basePersistenceRevision;
    private readonly string _inputFingerprint;
    private long _currentOperationOrdinal;
    private bool _reconciliationUsed;

    public WorkflowCheckpointJournal Journal => this._journal;
    public IWorkflowSideEffectReconciler Reconciler => this._reconciler;
    public WorkflowId WorkflowId => this._workflowId;
    public string TaskId => this._taskId;
    public long BasePersistenceRevision => this._basePersistenceRevision;
    public string InputFingerprint => this._inputFingerprint;
    public bool ReconciliationUsed => this._reconciliationUsed;

    public WorkflowCheckpointCoordinatorContext(
        WorkflowCheckpointJournal journal_,
        IWorkflowSideEffectReconciler reconciler_,
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_,
        string inputFingerprint_)
    {
        this._journal = journal_;
        this._reconciler = reconciler_;
        this._workflowId = workflowId_;
        this._taskId = taskId_;
        this._basePersistenceRevision = basePersistenceRevision_;
        this._inputFingerprint = inputFingerprint_;
    }

    public long GetNextOperationOrdinal()
    {
        return Interlocked.Increment(ref this._currentOperationOrdinal);
    }

    public void RecordReconciliationUsed()
    {
        this._reconciliationUsed = true;
    }

    public string ComputeOperationId(
        long operationOrdinal_,
        string operationKind_,
        string externalIdentity_,
        string requestFingerprint_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("workflowId", this._workflowId.Value);
            writer.WriteString("taskId", this._taskId);
            writer.WriteNumber("basePersistenceRevision", this._basePersistenceRevision);
            writer.WriteString("inputFingerprint", this._inputFingerprint);
            writer.WriteNumber("operationOrdinal", operationOrdinal_);
            writer.WriteString("operationKind", operationKind_);
            writer.WriteString("externalIdentity", externalIdentity_);
            writer.WriteString("requestFingerprint", requestFingerprint_);
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeAgentRequestFingerprint(AgentExecutionRequest request_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("instruction", request_.Instruction);
            writer.WriteNumber("permission", (int) request_.Permission);
            writer.WriteStartObject("environment");
            writer.WriteString("workingDirectory", request_.Environment.WorkingDirectory);
            writer.WriteEndObject();
            if (request_.SessionReference is not null)
            {
                writer.WriteString("sessionReference", request_.SessionReference.Value);
            }
            else
            {
                writer.WriteNull("sessionReference");
            }
            if (request_.StructuredOutput is not null)
            {
                writer.WriteString("structuredOutputJsonSchema", request_.StructuredOutput.JsonSchema);
            }
            else
            {
                writer.WriteNull("structuredOutputJsonSchema");
            }
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeValidationRequestFingerprint(
        ValidationRequirement requirement_,
        ExecutionEnvironment environment_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("requirement");
            writer.WriteString("id", requirement_.Id);
            writer.WriteString("taskId", requirement_.TaskId);
            writer.WriteString("sourceAcceptanceCriterionId", requirement_.SourceAcceptanceCriterionId);
            writer.WriteNumber("strategy", (int) requirement_.Strategy);
            writer.WriteString("statement", requirement_.Statement);
            writer.WriteEndObject();
            writer.WriteStartObject("environment");
            writer.WriteString("workingDirectory", environment_.WorkingDirectory);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeBaseWorkflowStateFingerprint(WorkflowState state_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sourceImplementationPlanRevision", state_.SourceImplementationPlanRevision);
            writer.WriteNumber("status", (int) state_.Status);
            writer.WriteStartArray("steps");
            for (int i = 0; i < state_.Steps.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", state_.Steps[i].TaskId);
                writer.WriteNumber("status", (int) state_.Steps[i].Status);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeInputFingerprint(
        ExecutableWork work_,
        ExecutionEnvelope envelope_,
        WorkflowRepairPolicy policy_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            // executableWork
            writer.WriteStartObject("executableWork");
            writer.WriteString("schemaId", work_.SchemaId);
            writer.WriteNumber("schemaVersion", work_.SchemaVersion);
            writer.WriteNumber("sourceImplementationPlanRevision", work_.SourceImplementationPlanRevision);

            writer.WriteStartArray("tasks");
            for (int i = 0; i < work_.Tasks.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("id", work_.Tasks[i].Id);
                writer.WriteString("sourcePlanStepId", work_.Tasks[i].SourcePlanStepId);
                writer.WriteString("instruction", work_.Tasks[i].Instruction);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("dependencies");
            for (int i = 0; i < work_.Dependencies.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", work_.Dependencies[i].TaskId);
                writer.WriteString("dependsOnTaskId", work_.Dependencies[i].DependsOnTaskId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("validationRequirements");
            for (int i = 0; i < work_.ValidationRequirements.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("id", work_.ValidationRequirements[i].Id);
                writer.WriteString("taskId", work_.ValidationRequirements[i].TaskId);
                writer.WriteString("sourceAcceptanceCriterionId", work_.ValidationRequirements[i].SourceAcceptanceCriterionId);
                writer.WriteNumber("strategy", (int) work_.ValidationRequirements[i].Strategy);
                writer.WriteString("statement", work_.ValidationRequirements[i].Statement);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("modelRequirements");
            for (int i = 0; i < work_.ModelRequirements.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", work_.ModelRequirements[i].TaskId);
                writer.WriteStartArray("capabilities");
                foreach (AgentCapability cap in work_.ModelRequirements[i].RequiredCapabilities)
                {
                    writer.WriteStringValue(cap.Value);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("agentRequirements");
            for (int i = 0; i < work_.AgentRequirements.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", work_.AgentRequirements[i].TaskId);
                writer.WriteStartArray("capabilities");
                foreach (AgentCapability cap in work_.AgentRequirements[i].RequiredCapabilities)
                {
                    writer.WriteStringValue(cap.Value);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject(); // executableWork

            // executionEnvelope
            writer.WriteStartObject("executionEnvelope");
            writer.WriteString("taskId", envelope_.TaskId);

            writer.WriteStartObject("prompt");
            writer.WriteString("taskId", envelope_.Prompt.TaskId);
            writer.WriteString("content", envelope_.Prompt.Content);
            writer.WriteNumber("estimatedTokens", envelope_.Prompt.EstimatedTokens);
            writer.WriteEndObject();

            writer.WriteStartArray("modelRequiredCapabilities");
            foreach (AgentCapability cap in envelope_.ModelRequiredCapabilities)
            {
                writer.WriteStringValue(cap.Value);
            }
            writer.WriteEndArray();

            writer.WriteStartArray("agentRequiredCapabilities");
            foreach (AgentCapability cap in envelope_.AgentRequiredCapabilities)
            {
                writer.WriteStringValue(cap.Value);
            }
            writer.WriteEndArray();

            writer.WriteNumber("permissionLevel", (int) envelope_.Permission);

            writer.WriteStartObject("environment");
            writer.WriteString("workingDirectory", envelope_.Environment.WorkingDirectory);
            writer.WriteEndObject();

            if (envelope_.SessionReference is not null)
            {
                writer.WriteString("sessionReference", envelope_.SessionReference.Value);
            }
            else
            {
                writer.WriteNull("sessionReference");
            }

            if (envelope_.StructuredOutput is not null)
            {
                writer.WriteString("structuredOutputJsonSchema", envelope_.StructuredOutput.JsonSchema);
            }
            else
            {
                writer.WriteNull("structuredOutputJsonSchema");
            }

            if (envelope_.Timeout.HasValue)
            {
                writer.WriteNumber("timeout", envelope_.Timeout.Value.Ticks);
            }
            else
            {
                writer.WriteNull("timeout");
            }

            writer.WriteEndObject(); // executionEnvelope

            // repairPolicy
            writer.WriteStartObject("repairPolicy");
            writer.WriteNumber("maxAttemptsPerProvider", policy_.MaxAttemptsPerProvider);
            writer.WriteNumber("maxTotalAttempts", policy_.MaxTotalAttempts);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeRegistryFingerprint(
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
            foreach (IAgentExecutor exec in sortedExecutors)
            {
                writer.WriteStartObject();
                writer.WriteString("providerId", exec.ProviderId.Value);
                writer.WriteStartArray("capabilities");
                foreach (AgentCapability cap in exec.Capabilities)
                {
                    writer.WriteStringValue(cap.Value);
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
}

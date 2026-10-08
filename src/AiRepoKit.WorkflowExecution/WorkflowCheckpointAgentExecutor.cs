namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;

internal sealed class WorkflowCheckpointAgentExecutor : IAgentExecutor
{
    private readonly IAgentExecutor _inner;
    private readonly WorkflowCheckpointCoordinatorContext _context;

    public AgentProviderId ProviderId =>
        this._inner.ProviderId;

    public AgentCapabilitySet Capabilities =>
        this._inner.Capabilities;

    public WorkflowCheckpointAgentExecutor(
        IAgentExecutor inner_,
        WorkflowCheckpointCoordinatorContext context_)
    {
        ArgumentNullException.ThrowIfNull(
            inner_,
            nameof(inner_));
        ArgumentNullException.ThrowIfNull(
            context_,
            nameof(context_));

        this._inner =
            inner_;
        this._context =
            context_;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        long ordinal =
            this._context.GetNextOperationOrdinal();

        const string operationKind =
            "Agent";

        string externalIdentity =
            this.ProviderId.Value;

        string requestFingerprint =
            WorkflowCheckpointCoordinatorContext.ComputeAgentRequestFingerprint(
                request_);

        string operationId =
            this._context.ComputeOperationId(
                ordinal,
                operationKind,
                externalIdentity,
                requestFingerprint);

        // 1. Check if outcome is already persisted
        InvocationOutcomeRecord? existingOutcome =
            this._context.Journal.FindOutcome(
                ordinal);

        if (existingOutcome is not null)
        {
            if (!string.Equals(existingOutcome.OperationId, operationId, StringComparison.Ordinal) ||
                !string.Equals(existingOutcome.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Persisted outcome mismatch for agent operation ordinal {ordinal}.");
            }

            if (existingOutcome.IsNullReturn)
            {
                return null!;
            }

            return ReconstructAgentResult(
                existingOutcome);
        }

        // 2. Check if an ambiguous in-flight intent exists
        InvocationIntentRecord? ambiguousIntent =
            this._context.Journal.FindActiveAmbiguousIntent(
                ordinal);

        if (ambiguousIntent is not null)
        {
            if (!string.Equals(ambiguousIntent.OperationId, operationId, StringComparison.Ordinal) ||
                !string.Equals(ambiguousIntent.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Ambiguous intent mismatch for agent operation ordinal {ordinal}.");
            }

            WorkflowSideEffectReconciliationResult reconciliation =
                await this._context.Reconciler.ReconcileAgentAsync(
                    this._context.WorkflowId,
                    this._context.TaskId,
                    operationId,
                    ordinal,
                    ambiguousIntent.InvocationGeneration,
                    this.ProviderId,
                    request_,
                    cancellationToken_).ConfigureAwait(false);

            if (reconciliation is null)
            {
                throw new InvalidOperationException(
                    $"Reconciler returned null result for agent operation '{operationId}'.");
            }

            if (reconciliation.ValidationResult is not null)
            {
                throw new InvalidOperationException(
                    $"Reconciler returned validation result for agent operation '{operationId}'.");
            }

            this._context.RecordReconciliationUsed();

            if (reconciliation.AgentResult is not null)
            {
                // Observed completed: persist outcome then replay without external call
                this.PersistOutcome(
                    ordinal,
                    operationId,
                    operationKind,
                    ambiguousIntent.InvocationGeneration,
                    requestFingerprint,
                    reconciliation.AgentResult);

                return reconciliation.AgentResult;
            }

            if (reconciliation.IsProvenNotExecuted)
            {
                // Proven not executed: record reconciliation, advance generation, create new intent, then execute once
                ReconciliationNotExecutedRecord rne =
                    new()
                    {
                        Sequence = this._context.Journal.NextSequence,
                        OperationOrdinal = ordinal,
                        OperationId = operationId,
                        InvocationGeneration = ambiguousIntent.InvocationGeneration
                    };
                this._context.Journal.AppendRecord(rne);

                int nextGeneration =
                    ambiguousIntent.InvocationGeneration + 1;

                this.PersistIntent(
                    ordinal,
                    operationId,
                    operationKind,
                    nextGeneration,
                    externalIdentity,
                    requestFingerprint);

                AgentExecutionResult? result =
                    await this._inner.ExecuteAsync(
                        request_,
                        cancellationToken_).ConfigureAwait(false);

                this.PersistOutcome(
                    ordinal,
                    operationId,
                    operationKind,
                    nextGeneration,
                    requestFingerprint,
                    result);

                return result!;
            }

            // Unresolved: stop without reinvocation
            throw new InvalidOperationException(
                $"Agent operation '{operationId}' side effect is unresolved.");
        }

        // 3. New execution: write intent, execute, write outcome
        int generation =
            this._context.Journal.GetNextGenerationForOperation(
                ordinal);

        this.PersistIntent(
            ordinal,
            operationId,
            operationKind,
            generation,
            externalIdentity,
            requestFingerprint);

        AgentExecutionResult? agentResult =
            await this._inner.ExecuteAsync(
                request_,
                cancellationToken_).ConfigureAwait(false);

        this.PersistOutcome(
            ordinal,
            operationId,
            operationKind,
            generation,
            requestFingerprint,
            agentResult);

        return agentResult!;
    }

    private void PersistIntent(
        long operationOrdinal_,
        string operationId_,
        string operationKind_,
        int invocationGeneration_,
        string externalIdentity_,
        string requestFingerprint_)
    {
        InvocationIntentRecord intent =
            new()
            {
                Sequence = this._context.Journal.NextSequence,
                OperationOrdinal = operationOrdinal_,
                OperationId = operationId_,
                OperationKind = operationKind_,
                InvocationGeneration = invocationGeneration_,
                ExternalIdentity = externalIdentity_,
                RequestFingerprint = requestFingerprint_
            };

        this._context.Journal.AppendRecord(intent);
    }

    private void PersistOutcome(
        long operationOrdinal_,
        string operationId_,
        string operationKind_,
        int invocationGeneration_,
        string requestFingerprint_,
        AgentExecutionResult? result_)
    {
        InvocationOutcomeRecord outcome =
            new()
            {
                Sequence = this._context.Journal.NextSequence,
                OperationOrdinal = operationOrdinal_,
                OperationId = operationId_,
                OperationKind = operationKind_,
                InvocationGeneration = invocationGeneration_,
                RequestFingerprint = requestFingerprint_,
                IsNullReturn = result_ is null,
                AgentStatus = result_ is null ? null : (int) result_.Status,
                AgentOutputText = result_?.OutputText,
                AgentDiagnosticText = result_?.DiagnosticText,
                AgentSessionReference = result_?.SessionReference?.Value
            };

        this._context.Journal.AppendRecord(outcome);
    }

    private static AgentExecutionResult ReconstructAgentResult(
        InvocationOutcomeRecord record_)
    {
        AgentExecutionStatus status =
            (AgentExecutionStatus) (record_.AgentStatus ?? 0);

        AgentSessionReference? session =
            record_.AgentSessionReference is not null
                ? new AgentSessionReference(record_.AgentSessionReference)
                : null;

        return status switch
        {
            AgentExecutionStatus.Completed =>
                AgentExecutionResult.Completed(
                    outputText_: record_.AgentOutputText,
                    sessionReference_: session,
                    diagnosticText_: record_.AgentDiagnosticText),
            AgentExecutionStatus.Failed =>
                AgentExecutionResult.Failed(
                    diagnosticText_: record_.AgentDiagnosticText,
                    sessionReference_: session,
                    outputText_: record_.AgentOutputText),
            AgentExecutionStatus.Blocked =>
                AgentExecutionResult.Blocked(
                    diagnosticText_: record_.AgentDiagnosticText,
                    sessionReference_: session,
                    outputText_: record_.AgentOutputText),
            AgentExecutionStatus.NeedsInput =>
                AgentExecutionResult.NeedsInput(
                    diagnosticText_: record_.AgentDiagnosticText,
                    sessionReference_: session,
                    outputText_: record_.AgentOutputText),
            _ =>
                throw new InvalidOperationException($"Unsupported agent execution status '{status}'.")
        };
    }
}

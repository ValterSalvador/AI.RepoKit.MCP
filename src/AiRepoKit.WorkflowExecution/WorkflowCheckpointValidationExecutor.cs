namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Execution;

internal sealed class WorkflowCheckpointValidationExecutor : IValidationExecutor
{
    private readonly IValidationExecutor _inner;
    private readonly WorkflowCheckpointCoordinatorContext _context;

    public ValidationStrategy Strategy =>
        this._inner.Strategy;

    public WorkflowCheckpointValidationExecutor(
        IValidationExecutor inner_,
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

    public async Task<ValidationExecutionResult> ValidateAsync(
        ValidationRequirement requirement_,
        ExecutionEnvironment environment_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            requirement_,
            nameof(requirement_));
        ArgumentNullException.ThrowIfNull(
            environment_,
            nameof(environment_));

        long ordinal =
            this._context.GetNextOperationOrdinal();

        const string operationKind =
            "Validation";

        string externalIdentity =
            $"{(int) this.Strategy}:{requirement_.Id}";

        string requestFingerprint =
            WorkflowCheckpointCoordinatorContext.ComputeValidationRequestFingerprint(
                requirement_,
                environment_);

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
                    $"Persisted outcome mismatch for validation operation ordinal {ordinal}.");
            }

            if (existingOutcome.IsNullReturn)
            {
                return null!;
            }

            return ReconstructValidationResult(
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
                    $"Ambiguous intent mismatch for validation operation ordinal {ordinal}.");
            }

            WorkflowSideEffectReconciliationResult reconciliation =
                await this._context.Reconciler.ReconcileValidationAsync(
                    this._context.WorkflowId,
                    this._context.TaskId,
                    operationId,
                    ordinal,
                    ambiguousIntent.InvocationGeneration,
                    requirement_,
                    environment_,
                    cancellationToken_).ConfigureAwait(false);

            if (reconciliation is null)
            {
                throw new InvalidOperationException(
                    $"Reconciler returned null result for validation operation '{operationId}'.");
            }

            if (reconciliation.AgentResult is not null)
            {
                throw new InvalidOperationException(
                    $"Reconciler returned agent result for validation operation '{operationId}'.");
            }

            this._context.RecordReconciliationUsed();

            if (reconciliation.ValidationResult is not null)
            {
                // Observed completed: persist outcome then replay without external call
                this.PersistOutcome(
                    ordinal,
                    operationId,
                    operationKind,
                    ambiguousIntent.InvocationGeneration,
                    requestFingerprint,
                    reconciliation.ValidationResult);

                return reconciliation.ValidationResult;
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

                ValidationExecutionResult? result =
                    await this._inner.ValidateAsync(
                        requirement_,
                        environment_,
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
                $"Validation operation '{operationId}' side effect is unresolved.");
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

        ValidationExecutionResult? validationResult =
            await this._inner.ValidateAsync(
                requirement_,
                environment_,
                cancellationToken_).ConfigureAwait(false);

        this.PersistOutcome(
            ordinal,
            operationId,
            operationKind,
            generation,
            requestFingerprint,
            validationResult);

        return validationResult!;
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
        ValidationExecutionResult? result_)
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
                ValidationPassed = result_?.Passed,
                ValidationEvidence = result_?.Evidence
            };

        this._context.Journal.AppendRecord(outcome);
    }

    private static ValidationExecutionResult ReconstructValidationResult(
        InvocationOutcomeRecord record_)
    {
        return new ValidationExecutionResult(
            record_.ValidationPassed ?? false,
            record_.ValidationEvidence ?? string.Empty);
    }
}

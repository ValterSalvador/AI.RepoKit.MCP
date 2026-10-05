namespace AiRepoKit.WorkflowExecution;

using System.Text;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public sealed class WorkflowRepairCoordinator
{
    public const string AlgorithmId =
        "ai.repokit.workflow-repair-coordinator/v1";

    private readonly ExecutorRegistration[] _executors;
    private readonly HashSet<ValidationStrategy> _validationStrategies;
    private readonly WorkflowValidationEngine _validationEngine;
    private readonly WorkflowRepairPolicy _policy;

    public WorkflowRepairCoordinator(
        IEnumerable<IAgentExecutor> executors_,
        IEnumerable<IValidationExecutor> validators_,
        WorkflowRepairPolicy policy_)
    {
        ArgumentNullException.ThrowIfNull(
            executors_,
            nameof(executors_));

        ArgumentNullException.ThrowIfNull(
            validators_,
            nameof(validators_));

        ArgumentNullException.ThrowIfNull(
            policy_,
            nameof(policy_));

        List<ExecutorRegistration> registrations =
            [];

        foreach (IAgentExecutor? executor in executors_)
        {
            if (executor is null)
            {
                throw new ArgumentException(
                    "Executor collection cannot contain null elements.",
                    nameof(executors_));
            }

            AgentProviderId? providerId =
                executor.ProviderId;

            if (providerId is null)
            {
                throw new InvalidOperationException(
                    "Agent executor provider ID cannot be null.");
            }

            AgentCapabilitySet? capabilities =
                executor.Capabilities;

            if (capabilities is null)
            {
                throw new InvalidOperationException(
                    $"Agent executor '{providerId.Value}' capability set cannot be null.");
            }

            registrations.Add(
                new ExecutorRegistration(
                    providerId,
                    capabilities,
                    new WorkflowAgentStepExecutor(
                        [
                            executor
                        ])));
        }

        if (registrations.Count == 0)
        {
            throw new ArgumentException(
                "At least one agent executor is required.",
                nameof(executors_));
        }

        registrations.Sort(
            static (
                left_,
                right_) =>
                    string.CompareOrdinal(
                        left_.ProviderId.Value,
                        right_.ProviderId.Value));

        for (int index = 1; index < registrations.Count; index++)
        {
            if (string.Equals(
                    registrations[index - 1].ProviderId.Value,
                    registrations[index].ProviderId.Value,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Duplicate agent provider ID '{registrations[index].ProviderId.Value}'.",
                    nameof(executors_));
            }
        }

        List<IValidationExecutor> validators =
            [];

        HashSet<ValidationStrategy> validationStrategies =
            [];

        foreach (IValidationExecutor? validator in validators_)
        {
            if (validator is null)
            {
                throw new ArgumentException(
                    "Validator collection cannot contain null elements.",
                    nameof(validators_));
            }

            ValidationStrategy strategy =
                validator.Strategy;

            if (!Enum.IsDefined(
                    typeof(ValidationStrategy),
                    strategy))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(validators_),
                    strategy,
                    "Validator strategy must be a defined ValidationStrategy value.");
            }

            if (!validationStrategies.Add(
                    strategy))
            {
                throw new ArgumentException(
                    $"Duplicate validator strategy detected: '{strategy}'.",
                    nameof(validators_));
            }

            validators.Add(
                validator);
        }

        this._executors =
            registrations.ToArray();
        this._validationStrategies =
            validationStrategies;
        this._validationEngine =
            new WorkflowValidationEngine(
                validators);
        this._policy =
            policy_;
    }

    public async Task<WorkflowRepairResult> ExecuteAsync(
        WorkflowState state_,
        ExecutableWork work_,
        ExecutionEnvelope envelope_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));

        ArgumentNullException.ThrowIfNull(
            envelope_,
            nameof(envelope_));

        if (state_.Status != WorkflowStatus.Running)
        {
            throw new InvalidOperationException(
                $"Workflow repair execution requires workflow status '{WorkflowStatus.Running}'. Current status is '{state_.Status}'.");
        }

        if (state_.SourceImplementationPlanRevision !=
            work_.SourceImplementationPlanRevision)
        {
            throw new InvalidOperationException(
                "Workflow state and executable work must reference the same implementation plan revision.");
        }

        WorkflowStepState? targetStep =
            null;

        for (int index = 0; index < state_.Steps.Count; index++)
        {
            WorkflowStepState candidate =
                state_.Steps[index];

            if (string.Equals(
                    candidate.TaskId,
                    envelope_.TaskId,
                    StringComparison.Ordinal))
            {
                targetStep =
                    candidate;

                break;
            }
        }

        if (targetStep is null)
        {
            throw new ArgumentException(
                $"Execution envelope task identifier '{envelope_.TaskId}' does not exist in the workflow state.",
                nameof(envelope_));
        }

        if (targetStep.Status != WorkflowStepStatus.Running)
        {
            throw new InvalidOperationException(
                $"Workflow repair execution requires target step status '{WorkflowStepStatus.Running}'. Current status for task '{targetStep.TaskId}' is '{targetStep.Status}'.");
        }

        bool taskExistsInWork =
            false;

        for (int index = 0; index < work_.Tasks.Count; index++)
        {
            if (string.Equals(
                    work_.Tasks[index].Id,
                    envelope_.TaskId,
                    StringComparison.Ordinal))
            {
                taskExistsInWork =
                    true;

                break;
            }
        }

        if (!taskExistsInWork)
        {
            throw new ArgumentException(
                $"Executable work does not contain task identifier '{envelope_.TaskId}'.",
                nameof(work_));
        }

        if (envelope_.SessionReference is not null)
        {
            throw new InvalidOperationException(
                "Workflow repair requires a null execution-envelope session reference.");
        }

        for (int index = 0; index < work_.ValidationRequirements.Count; index++)
        {
            ValidationRequirement requirement =
                work_.ValidationRequirements[index];

            if (string.Equals(
                    requirement.TaskId,
                    envelope_.TaskId,
                    StringComparison.Ordinal) &&
                !this._validationStrategies.Contains(
                    requirement.Strategy))
            {
                throw new InvalidOperationException(
                    $"No validator is registered for validation strategy '{requirement.Strategy}'.");
            }
        }

        ExecutorRegistration[] eligible =
            this._executors
                .Where(
                    registration_ =>
                        registration_
                            .Capabilities
                            .SupportsAll(
                                envelope_.AgentRequiredCapabilities))
                .ToArray();

        if (eligible.Length == 0)
        {
            throw new InvalidOperationException(
                $"No eligible agent executor satisfies the required capabilities {envelope_.AgentRequiredCapabilities}.");
        }

        cancellationToken_.ThrowIfCancellationRequested();

        WorkflowState currentState =
            state_;

        ExecutionEnvelope currentEnvelope =
            envelope_;

        bool semanticRepair =
            false;

        List<WorkflowRepairAttempt> attempts =
            [];

        int attemptNumber =
            0;

        for (
            int providerIndex = 0;
            providerIndex < eligible.Length;
            providerIndex++)
        {
            ExecutorRegistration registration =
                eligible[providerIndex];

            for (
                int providerAttemptNumber = 1;
                providerAttemptNumber <=
                    this._policy.MaxAttemptsPerProvider &&
                attemptNumber <
                    this._policy.MaxTotalAttempts;
                providerAttemptNumber++)
            {
                cancellationToken_.ThrowIfCancellationRequested();

                attemptNumber++;

                WorkflowAgentStepResult agentStep =
                    await registration
                        .StepExecutor
                        .ExecuteAsync(
                            currentState,
                            currentEnvelope,
                            cancellationToken_)
                        .ConfigureAwait(false);

                AgentExecutionResult agentResult =
                    agentStep.AgentResult;

                if (agentResult.Status ==
                    AgentExecutionStatus.Completed)
                {
                    WorkflowValidationResult validation =
                        await this
                            ._validationEngine
                            .ValidateAsync(
                                agentStep.State,
                                work_,
                                currentEnvelope.Environment,
                                currentEnvelope.TaskId,
                                cancellationToken_)
                            .ConfigureAwait(false);

                    attempts.Add(
                        new WorkflowRepairAttempt(
                            attemptNumber,
                            providerAttemptNumber,
                            agentStep.ProviderId,
                            currentEnvelope.Prompt,
                            agentResult,
                            semanticRepair,
                            validation.Evidence));

                    WorkflowStepStatus validationStatus =
                        FindStepStatus(
                            validation.State,
                            envelope_.TaskId);

                    if (validationStatus ==
                        WorkflowStepStatus.Completed)
                    {
                        return new WorkflowRepairResult(
                            validation.State,
                            attempts,
                            false);
                    }

                    if (validationStatus !=
                        WorkflowStepStatus.Failed)
                    {
                        throw new InvalidOperationException(
                            $"Unexpected validation step status '{validationStatus}'.");
                    }

                    cancellationToken_.ThrowIfCancellationRequested();

                    if (!HasNextAttempt(
                            attemptNumber,
                            providerIndex,
                            providerAttemptNumber,
                            eligible.Length,
                            this._policy))
                    {
                        return new WorkflowRepairResult(
                            validation.State,
                            attempts,
                            true);
                    }

                    WorkflowValidationEvidence[] failedEvidence =
                        validation
                            .Evidence
                            .Where(
                                evidence_ =>
                                    !evidence_
                                        .ValidationResult
                                        .Passed)
                            .OrderBy(
                                evidence_ =>
                                    evidence_
                                        .Requirement
                                        .Id,
                                StringComparer.Ordinal)
                            .ToArray();

                    if (failedEvidence.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "Failed workflow validation must contain at least one failed validation evidence item.");
                    }

                    currentState =
                        ResetFailedStep(
                            validation.State,
                            envelope_.TaskId);

                    CompiledPrompt repairPrompt =
                        CreateRepairPrompt(
                            envelope_.Prompt,
                            attemptNumber + 1,
                            failedEvidence);

                    currentEnvelope =
                        ExecutionEnvelope.Create(
                            work_,
                            repairPrompt,
                            envelope_.Permission,
                            envelope_.Environment,
                            null,
                            envelope_.StructuredOutput,
                            envelope_.Timeout);

                    semanticRepair =
                        true;

                    continue;
                }

                attempts.Add(
                    new WorkflowRepairAttempt(
                        attemptNumber,
                        providerAttemptNumber,
                        agentStep.ProviderId,
                        currentEnvelope.Prompt,
                        agentResult,
                        semanticRepair,
                        Array.Empty<WorkflowValidationEvidence>()));

                cancellationToken_.ThrowIfCancellationRequested();

                if (agentResult.Status ==
                    AgentExecutionStatus.Failed)
                {
                    if (!HasNextAttempt(
                            attemptNumber,
                            providerIndex,
                            providerAttemptNumber,
                            eligible.Length,
                            this._policy))
                    {
                        return new WorkflowRepairResult(
                            agentStep.State,
                            attempts,
                            true);
                    }

                    currentState =
                        ResetFailedStep(
                            agentStep.State,
                            envelope_.TaskId);

                    continue;
                }

                if (agentResult.Status ==
                        AgentExecutionStatus.Blocked ||
                    agentResult.Status ==
                        AgentExecutionStatus.NeedsInput)
                {
                    return new WorkflowRepairResult(
                        agentStep.State,
                        attempts,
                        false);
                }

                throw new InvalidOperationException(
                    $"Unsupported agent execution status '{agentResult.Status}'.");
            }
        }

        throw new InvalidOperationException(
            "Workflow repair attempt scheduling terminated without a result.");
    }

    private static bool HasNextAttempt(
        int attemptNumber_,
        int providerIndex_,
        int providerAttemptNumber_,
        int eligibleProviderCount_,
        WorkflowRepairPolicy policy_)
    {
        if (attemptNumber_ >=
            policy_.MaxTotalAttempts)
        {
            return false;
        }

        if (providerAttemptNumber_ <
            policy_.MaxAttemptsPerProvider)
        {
            return true;
        }

        return providerIndex_ + 1 <
            eligibleProviderCount_;
    }

    private static WorkflowState ResetFailedStep(
        WorkflowState state_,
        string taskId_)
    {
        WorkflowState pending =
            WorkflowStateMachine.TransitionStep(
                state_,
                taskId_,
                WorkflowStepStatus.Pending);

        return WorkflowStateMachine.TransitionStep(
            pending,
            taskId_,
            WorkflowStepStatus.Running);
    }

    private static WorkflowStepStatus FindStepStatus(
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
                return state_.Steps[index].Status;
            }
        }

        throw new InvalidOperationException(
            $"Workflow state no longer contains task identifier '{taskId_}'.");
    }

    private static CompiledPrompt CreateRepairPrompt(
        CompiledPrompt originalPrompt_,
        int nextAttemptNumber_,
        IReadOnlyList<WorkflowValidationEvidence> failedEvidence_)
    {
        StringBuilder builder =
            new(
                originalPrompt_.Content);

        builder.Append(
            "\n\n[AIRepoKit.SemanticRepair/v1]\n");

        builder
            .Append(
                "nextAttempt=")
            .Append(
                nextAttemptNumber_)
            .Append(
                '\n');

        builder
            .Append(
                "failedValidationCount=")
            .Append(
                failedEvidence_.Count);

        for (int index = 0; index < failedEvidence_.Count; index++)
        {
            WorkflowValidationEvidence evidence =
                failedEvidence_[index];

            ValidationRequirement requirement =
                evidence.Requirement;

            builder
                .Append(
                    '\n')
                .Append(
                    "validation[")
                .Append(
                    index)
                .Append(
                    "].id=")
                .Append(
                    requirement.Id);

            builder
                .Append(
                    '\n')
                .Append(
                    "validation[")
                .Append(
                    index)
                .Append(
                    "].sourceAcceptanceCriterionId=")
                .Append(
                    requirement.SourceAcceptanceCriterionId);

            builder
                .Append(
                    '\n')
                .Append(
                    "validation[")
                .Append(
                    index)
                .Append(
                    "].strategy=")
                .Append(
                    requirement.Strategy);

            builder
                .Append(
                    '\n')
                .Append(
                    "validation[")
                .Append(
                    index)
                .Append(
                    "].statement=")
                .Append(
                    requirement.Statement);

            builder
                .Append(
                    '\n')
                .Append(
                    "validation[")
                .Append(
                    index)
                .Append(
                    "].evidence=")
                .Append(
                    evidence.ValidationResult.Evidence);
        }

        return new CompiledPrompt(
            originalPrompt_.TaskId,
            builder.ToString());
    }

    private sealed record ExecutorRegistration(
        AgentProviderId ProviderId,
        AgentCapabilitySet Capabilities,
        WorkflowAgentStepExecutor StepExecutor);
}

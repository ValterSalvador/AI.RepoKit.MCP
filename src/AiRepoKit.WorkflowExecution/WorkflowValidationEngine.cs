namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public sealed class WorkflowValidationEngine
{
    public const string AlgorithmId =
        "ai.repokit.workflow-validation-engine/v1";

    private readonly Dictionary<
        ValidationStrategy,
        IValidationExecutor> _validators;

    public WorkflowValidationEngine(
        IEnumerable<IValidationExecutor> validators_)
    {
        ArgumentNullException.ThrowIfNull(
            validators_,
            nameof(validators_));

        Dictionary<
            ValidationStrategy,
            IValidationExecutor> validators =
            new();

        foreach (IValidationExecutor validator in validators_)
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

            if (!validators.TryAdd(
                    strategy,
                    validator))
            {
                throw new ArgumentException(
                    $"Duplicate validator strategy detected: '{strategy}'.",
                    nameof(validators_));
            }
        }

        this._validators =
            validators;
    }

    public async Task<WorkflowValidationResult> ValidateAsync(
        WorkflowState state_,
        ExecutableWork work_,
        ExecutionEnvironment environment_,
        string taskId_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));

        ArgumentNullException.ThrowIfNull(
            environment_,
            nameof(environment_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        if (state_.Status != WorkflowStatus.Running)
        {
            throw new InvalidOperationException(
                $"Workflow validation requires workflow status '{WorkflowStatus.Running}'. Current status is '{state_.Status}'.");
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
            if (string.Equals(
                    state_.Steps[index].TaskId,
                    taskId_,
                    StringComparison.Ordinal))
            {
                targetStep =
                    state_.Steps[index];

                break;
            }
        }

        if (targetStep is null)
        {
            throw new ArgumentException(
                $"Unknown workflow task identifier '{taskId_}'.",
                nameof(taskId_));
        }

        if (targetStep.Status !=
            WorkflowStepStatus.AwaitingValidation)
        {
            throw new InvalidOperationException(
                $"Workflow validation requires target step status '{WorkflowStepStatus.AwaitingValidation}'. Current status is '{targetStep.Status}'.");
        }

        bool taskExistsInWork =
            false;

        for (int index = 0; index < work_.Tasks.Count; index++)
        {
            if (string.Equals(
                    work_.Tasks[index].Id,
                    taskId_,
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
                $"Executable work does not contain task identifier '{taskId_}'.",
                nameof(taskId_));
        }

        ValidationRequirement[] requirements =
            work_
                .ValidationRequirements
                .Where(
                    requirement_ =>
                        string.Equals(
                            requirement_.TaskId,
                            taskId_,
                            StringComparison.Ordinal))
                .OrderBy(
                    requirement_ =>
                        requirement_.Id,
                    StringComparer.Ordinal)
                .ToArray();

        for (int index = 0; index < requirements.Length; index++)
        {
            if (!this._validators.ContainsKey(
                    requirements[index].Strategy))
            {
                throw new InvalidOperationException(
                    $"No validator is registered for validation strategy '{requirements[index].Strategy}'.");
            }
        }

        cancellationToken_.ThrowIfCancellationRequested();

        List<WorkflowValidationEvidence> evidence =
            new(
                requirements.Length);

        bool allPassed =
            true;

        for (int index = 0; index < requirements.Length; index++)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            ValidationRequirement requirement =
                requirements[index];

            IValidationExecutor validator =
                this._validators[requirement.Strategy];

            ValidationExecutionResult? validationResult =
                await validator
                    .ValidateAsync(
                        requirement,
                        environment_,
                        cancellationToken_)
                    .ConfigureAwait(false);

            if (validationResult is null)
            {
                throw new InvalidOperationException(
                    $"Validator for strategy '{requirement.Strategy}' returned a null validation result.");
            }

            evidence.Add(
                new WorkflowValidationEvidence(
                    requirement,
                    validationResult));

            if (!validationResult.Passed)
            {
                allPassed =
                    false;
            }
        }

        cancellationToken_.ThrowIfCancellationRequested();

        WorkflowStepStatus targetStatus =
            allPassed
                ? WorkflowStepStatus.Completed
                : WorkflowStepStatus.Failed;

        WorkflowState transitionedState =
            WorkflowStateMachine.TransitionStep(
                state_,
                taskId_,
                targetStatus);

        return new WorkflowValidationResult(
            transitionedState,
            evidence);
    }
}

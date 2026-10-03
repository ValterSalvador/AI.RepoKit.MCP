namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public sealed class WorkflowAgentStepExecutor
{
    public const string AlgorithmId =
        "ai.repokit.workflow-agent-step-executor/v1";

    private readonly ExecutorRegistration[] _executors;

    public WorkflowAgentStepExecutor(
        IEnumerable<IAgentExecutor> executors_)
    {
        ArgumentNullException.ThrowIfNull(
            executors_,
            nameof(executors_));

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
                    executor,
                    providerId,
                    capabilities));
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

        this._executors =
            registrations.ToArray();
    }

    public async Task<WorkflowAgentStepResult> ExecuteAsync(
        WorkflowState state_,
        ExecutionEnvelope envelope_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ArgumentNullException.ThrowIfNull(
            envelope_,
            nameof(envelope_));

        if (state_.Status != WorkflowStatus.Running)
        {
            throw new InvalidOperationException(
                $"Workflow agent execution requires workflow status '{WorkflowStatus.Running}'. Current status is '{state_.Status}'.");
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
                $"Workflow agent execution requires target step status '{WorkflowStepStatus.Running}'. Current status for task '{targetStep.TaskId}' is '{targetStep.Status}'.");
        }

        cancellationToken_.ThrowIfCancellationRequested();

        ExecutorRegistration? selected =
            null;

        for (int index = 0; index < this._executors.Length; index++)
        {
            ExecutorRegistration candidate =
                this._executors[index];

            if (candidate.Capabilities.SupportsAll(
                    envelope_.AgentRequiredCapabilities))
            {
                selected =
                    candidate;

                break;
            }
        }

        if (selected is null)
        {
            throw new InvalidOperationException(
                $"No eligible agent executor satisfies the required capabilities {envelope_.AgentRequiredCapabilities}.");
        }

        AgentExecutionRequest request =
            new(
                envelope_.Prompt.Content,
                envelope_.Permission,
                envelope_.Environment,
                envelope_.SessionReference,
                envelope_.StructuredOutput);

        AgentExecutionResult? agentResult =
            await InvokeAgentAsync(
                    selected,
                    request,
                    envelope_.Timeout,
                    cancellationToken_)
                .ConfigureAwait(false);

        if (agentResult is null)
        {
            throw new InvalidOperationException(
                $"Agent executor '{selected.ProviderId.Value}' returned a null result.");
        }

        WorkflowStepStatus targetStatus =
            agentResult.Status switch
            {
                AgentExecutionStatus.Completed =>
                    WorkflowStepStatus.AwaitingValidation,

                AgentExecutionStatus.Blocked =>
                    WorkflowStepStatus.Blocked,

                AgentExecutionStatus.NeedsInput =>
                    WorkflowStepStatus.Blocked,

                AgentExecutionStatus.Failed =>
                    WorkflowStepStatus.Failed,

                _ =>
                    throw new InvalidOperationException(
                        $"Unsupported agent execution status '{agentResult.Status}'.")
            };

        WorkflowState transitionedState =
            WorkflowStateMachine.TransitionStep(
                state_,
                envelope_.TaskId,
                targetStatus);

        return new WorkflowAgentStepResult(
            transitionedState,
            selected.ProviderId,
            agentResult);
    }

    private static async Task<AgentExecutionResult?> InvokeAgentAsync(
        ExecutorRegistration selected_,
        AgentExecutionRequest request_,
        TimeSpan? timeout_,
        CancellationToken cancellationToken_)
    {
        if (!timeout_.HasValue)
        {
            return await selected_
                .Executor
                .ExecuteAsync(
                    request_,
                    cancellationToken_)
                .ConfigureAwait(false);
        }

        using CancellationTokenSource timeoutSource =
            new(timeout_.Value);

        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken_,
                timeoutSource.Token);

        try
        {
            return await selected_
                .Executor
                .ExecuteAsync(
                    request_,
                    linkedSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken_.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Workflow agent step execution was cancelled by the caller.",
                exception,
                cancellationToken_);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Workflow agent step execution exceeded timeout '{timeout_.Value}'.",
                exception);
        }
    }

    private sealed record ExecutorRegistration(
        IAgentExecutor Executor,
        AgentProviderId ProviderId,
        AgentCapabilitySet Capabilities);
}

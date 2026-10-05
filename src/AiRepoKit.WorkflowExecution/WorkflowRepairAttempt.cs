namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Execution;

public sealed record WorkflowRepairAttempt
{
    public int AttemptNumber
    {
        get;
    }

    public int ProviderAttemptNumber
    {
        get;
    }

    public AgentProviderId ProviderId
    {
        get;
    }

    public CompiledPrompt Prompt
    {
        get;
    }

    public AgentExecutionResult AgentResult
    {
        get;
    }

    public bool SemanticRepair
    {
        get;
    }

    public IReadOnlyList<WorkflowValidationEvidence> ValidationEvidence
    {
        get;
    }

    internal WorkflowRepairAttempt(
        int attemptNumber_,
        int providerAttemptNumber_,
        AgentProviderId providerId_,
        CompiledPrompt prompt_,
        AgentExecutionResult agentResult_,
        bool semanticRepair_,
        IReadOnlyList<WorkflowValidationEvidence> validationEvidence_)
    {
        if (attemptNumber_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptNumber_),
                attemptNumber_,
                "Attempt number must be greater than zero.");
        }

        if (providerAttemptNumber_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerAttemptNumber_),
                providerAttemptNumber_,
                "Provider attempt number must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(
            providerId_,
            nameof(providerId_));

        ArgumentNullException.ThrowIfNull(
            prompt_,
            nameof(prompt_));

        ArgumentNullException.ThrowIfNull(
            agentResult_,
            nameof(agentResult_));

        ArgumentNullException.ThrowIfNull(
            validationEvidence_,
            nameof(validationEvidence_));

        for (int index = 0; index < validationEvidence_.Count; index++)
        {
            if (validationEvidence_[index] is null)
            {
                throw new ArgumentException(
                    "Validation evidence cannot contain null elements.",
                    nameof(validationEvidence_));
            }
        }

        this.AttemptNumber =
            attemptNumber_;
        this.ProviderAttemptNumber =
            providerAttemptNumber_;
        this.ProviderId =
            providerId_;
        this.Prompt =
            prompt_;
        this.AgentResult =
            agentResult_;
        this.SemanticRepair =
            semanticRepair_;
        this.ValidationEvidence =
            validationEvidence_;
    }
}

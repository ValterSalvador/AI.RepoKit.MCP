namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Orchestration;

public sealed record WorkflowAgentStepResult
{
    public WorkflowState State
    {
        get;
    }

    public AgentProviderId ProviderId
    {
        get;
    }

    public AgentExecutionResult AgentResult
    {
        get;
    }

    internal WorkflowAgentStepResult(
        WorkflowState state_,
        AgentProviderId providerId_,
        AgentExecutionResult agentResult_)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ArgumentNullException.ThrowIfNull(
            providerId_,
            nameof(providerId_));

        ArgumentNullException.ThrowIfNull(
            agentResult_,
            nameof(agentResult_));

        this.State =
            state_;
        this.ProviderId =
            providerId_;
        this.AgentResult =
            agentResult_;
    }
}

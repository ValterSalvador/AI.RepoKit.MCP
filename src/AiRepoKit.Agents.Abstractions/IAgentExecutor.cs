namespace AiRepoKit.Agents;

public interface IAgentExecutor
{
    AgentProviderId ProviderId
    {
        get;
    }

    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}

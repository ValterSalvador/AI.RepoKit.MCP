namespace AiRepoKit.Agents.Runtime;

using AiRepoKit.Agents;

public interface IModelSessionRuntime : IModelExecutionRuntime
{
    AgentSessionReference CreateSession();

    Task<ModelExecutionResult> ExecuteInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);

    Task<bool> EndSessionAsync(
        AgentSessionReference sessionReference_,
        CancellationToken cancellationToken_ = default);
}

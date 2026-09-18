namespace AiRepoKit.Agents.Runtime;

using AiRepoKit.Agents;

public interface IModelStreamingExecutionRuntime : IModelSessionRuntime
{
    IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);

    IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}

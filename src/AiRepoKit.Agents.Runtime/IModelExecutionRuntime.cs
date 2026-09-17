namespace AiRepoKit.Agents.Runtime;

public interface IModelExecutionRuntime
{
    Task<ModelExecutionResult> ExecuteAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}

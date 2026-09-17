namespace AiRepoKit.Agents.Runtime;

public interface IProcessExecutionRuntime
{
    Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}

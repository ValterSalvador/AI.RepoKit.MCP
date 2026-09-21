namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents.Runtime;

internal sealed class TestModelExecutionRuntime : IModelExecutionRuntime
{
    public int InvocationCount
    {
        get;
        private set;
    }

    public Task<ModelExecutionResult> ExecuteAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        this.InvocationCount++;

        throw new InvalidOperationException(
            "Model execution must not be invoked during discovery or health check.");
    }
}

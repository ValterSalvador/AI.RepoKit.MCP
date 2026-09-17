using AiRepoKit.Agents.Runtime;

namespace AiRepoKit.Agents.Antigravity.Tests;

internal sealed class FakeAntigravityProcessRunner : IProcessExecutionRuntime
{
    public ProcessExecutionRequest? LastInvocation
    {
        get;
        private set;
    }

    public int InvocationCount
    {
        get;
        private set;
    }

    public bool TerminationInvoked
    {
        get;
        private set;
    }

    public ProcessExecutionResult? ResultToReturn
    {
        get;
        set;
    }

    public Exception? ExceptionToThrow
    {
        get;
        set;
    }

    public Func<ProcessExecutionRequest, CancellationToken, Task<ProcessExecutionResult>>? CustomHandler
    {
        get;
        set;
    }

    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        this.InvocationCount++;
        this.LastInvocation =
            request_;

        cancellationToken_.ThrowIfCancellationRequested();

        using CancellationTokenRegistration registration =
            cancellationToken_.Register(() =>
            {
                this.TerminationInvoked =
                    true;
            });

        if (this.ExceptionToThrow is not null)
        {
            throw this.ExceptionToThrow;
        }

        if (this.CustomHandler is not null)
        {
            return await this.CustomHandler(
                request_,
                cancellationToken_).ConfigureAwait(false);
        }

        return this.ResultToReturn ??
               new ProcessExecutionResult(
                   0,
                   "{\"status\":\"SUCCESS\",\"response\":\"ok\"}",
                   string.Empty);
    }
}

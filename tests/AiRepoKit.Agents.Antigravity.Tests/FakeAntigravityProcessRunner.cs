using AiRepoKit.Agents.Antigravity;

namespace AiRepoKit.Agents.Antigravity.Tests;

internal sealed class FakeAntigravityProcessRunner : IAntigravityProcessRunner
{
    public AntigravityProcessInvocation? LastInvocation
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

    public AntigravityProcessResult? ResultToReturn
    {
        get;
        set;
    }

    public Exception? ExceptionToThrow
    {
        get;
        set;
    }

    public Func<AntigravityProcessInvocation, CancellationToken, Task<AntigravityProcessResult>>? CustomHandler
    {
        get;
        set;
    }

    public async Task<AntigravityProcessResult> RunAsync(
        AntigravityProcessInvocation invocation_,
        CancellationToken cancellationToken_ = default)
    {
        this.InvocationCount++;
        this.LastInvocation =
            invocation_;

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
                invocation_,
                cancellationToken_).ConfigureAwait(false);
        }

        return this.ResultToReturn ??
               new AntigravityProcessResult(
                   0,
                   "{\"status\":\"SUCCESS\",\"response\":\"ok\"}",
                   string.Empty);
    }
}

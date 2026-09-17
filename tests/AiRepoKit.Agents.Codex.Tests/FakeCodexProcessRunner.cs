using AiRepoKit.Agents.Codex;

namespace AiRepoKit.Agents.Codex.Tests;

internal sealed class FakeCodexProcessRunner : ICodexProcessRunner
{
    public CodexProcessInvocation? LastInvocation
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

    public CodexProcessResult? ResultToReturn
    {
        get;
        set;
    }

    public Exception? ExceptionToThrow
    {
        get;
        set;
    }

    public Func<CodexProcessInvocation, CancellationToken, Task<CodexProcessResult>>? CustomHandler
    {
        get;
        set;
    }

    public async Task<CodexProcessResult> RunAsync(
        CodexProcessInvocation invocation_,
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
               new CodexProcessResult(
                   0,
                   "{\"type\":\"turn.completed\"}",
                   string.Empty);
    }
}

namespace AiRepoKit.Agents.Antigravity;

internal interface IAntigravityProcessRunner
{
    Task<AntigravityProcessResult> RunAsync(
        AntigravityProcessInvocation invocation_,
        CancellationToken cancellationToken_ = default);
}

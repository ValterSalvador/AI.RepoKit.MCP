namespace AiRepoKit.Agents.Codex;

internal interface ICodexProcessRunner
{
    Task<CodexProcessResult> RunAsync(
        CodexProcessInvocation invocation_,
        CancellationToken cancellationToken_ = default);
}

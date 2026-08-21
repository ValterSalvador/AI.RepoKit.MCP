namespace AiRepoKit.Cli.Services;

public sealed record McpHostProcessInfo(
    int ProcessId,
    string Name,
    string CommandLine);

public interface IMcpHostProcessAccessor
{
    IReadOnlyList<McpHostProcessInfo> GetProcesses();

    bool TryStopProcess(int processId_);
}

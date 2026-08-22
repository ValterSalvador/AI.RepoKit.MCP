namespace AiRepoKit.Cli.Services.McpLaunch;

internal sealed record McpServerLaunchSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    McpRuntimeKind RuntimeKind);

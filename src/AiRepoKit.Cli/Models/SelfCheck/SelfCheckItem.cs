namespace AiRepoKit.Cli.Models.SelfCheck;

public sealed record SelfCheckItem(
    string Name,
    string Status,
    bool Required,
    string Message,
    int ExitCode,
    string? Hint = null);

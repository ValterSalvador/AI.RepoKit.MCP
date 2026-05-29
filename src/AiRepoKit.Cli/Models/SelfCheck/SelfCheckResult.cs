namespace AiRepoKit.Cli.Models.SelfCheck;

public sealed record SelfCheckResult(
    string Status,
    string RepoPath,
    int ExitCode,
    IReadOnlyList<SelfCheckItem> Checks);

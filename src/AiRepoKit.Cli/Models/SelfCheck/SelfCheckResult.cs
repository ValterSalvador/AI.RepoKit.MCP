using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Models.SelfCheck;

public sealed record SelfCheckResult(
    string Status,
    string Mode,
    string RepoPath,
    int ExitCode,
    IReadOnlyList<SelfCheckItem> Checks,
    CommandTimingReport? Timings = null);

using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Models.McpDiagnostics;

public sealed record McpDiagnosticResult(
    string Status,
    string Mode,
    string RepoPath,
    int ExitCode,
    IReadOnlyList<string> Clients,
    IReadOnlyList<McpDiagnosticItem> Checks,
    IReadOnlyList<string> ClientHints,
    CommandTimingReport? Timings = null);

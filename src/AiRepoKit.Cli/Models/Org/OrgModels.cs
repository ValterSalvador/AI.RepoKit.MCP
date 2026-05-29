namespace AiRepoKit.Cli.Models.Org;

public sealed record OrgScanReport(
    string Root,
    string GeneratedAtLocal,
    int MaxDepth,
    IReadOnlyList<OrgRepositorySummary> Repositories,
    IReadOnlyList<string> Warnings);

public sealed record OrgRepositorySummary(
    string RepoRoot,
    string RepoName,
    string RecommendedProfile,
    double Confidence,
    IReadOnlyList<string> DetectedProfiles,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Signals,
    OrgRepositoryHealth Health,
    OrgRepositoryFootprint Footprint,
    OrgScore Readiness,
    OrgScore Compliance,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Recommendations);

public sealed record OrgRepositoryHealth(
    string Status,
    bool Exists,
    bool HasGit,
    bool HasDetectableProject);

public sealed record OrgRepositoryFootprint(
    bool HasAiDirectory,
    bool HasAgentsMd,
    bool HasCopilotInstructions,
    bool HasGithubAgents,
    bool HasGithubInstructions,
    bool HasGithubPrompts,
    bool HasVsCodeMcpConfig,
    bool HasCodexConfig,
    bool HasContextPacks,
    bool HasGraphs,
    bool HasMcpConfig,
    bool HasToolManifest);

public sealed record OrgScore(
    int Value,
    string Status,
    IReadOnlyList<string> PositiveSignals,
    IReadOnlyList<string> NegativeSignals,
    IReadOnlyList<string> Recommendations);

public sealed record OrgReport(
    string Root,
    string GeneratedAtLocal,
    IReadOnlyList<OrgRepositorySummary> Repositories,
    OrgScoreSummary Readiness,
    OrgScoreSummary Compliance,
    IReadOnlyList<string> Warnings);

public sealed record OrgScoreSummary(
    double Average,
    int Ok,
    int Warning,
    int Failed,
    int Unknown);

public sealed record OrgSelfCheckReport(
    string Root,
    string GeneratedAtLocal,
    IReadOnlyList<OrgSelfCheckRepository> Repositories,
    int Passed,
    int Warnings,
    int Failed);

public sealed record OrgSelfCheckRepository(
    string RepoRoot,
    string RepoName,
    string Status,
    int ExitCode,
    IReadOnlyList<string> FailedChecks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<OrgSelfCheckCheck> Checks);

public sealed record OrgSelfCheckCheck(
    string Name,
    string Status,
    bool Required,
    string Message,
    int ExitCode,
    string? Hint);

public sealed record OrgSetupPreviewReport(
    string Root,
    string GeneratedAtLocal,
    IReadOnlyList<OrgSetupPreviewRepository> Repositories,
    IReadOnlyList<string> Warnings);

public sealed record OrgSetupPreviewRepository(
    string RepoRoot,
    string RepoName,
    string RecommendedProfile,
    double Confidence,
    string Mode,
    IReadOnlyList<string> PlannedCommands,
    IReadOnlyList<string> PlannedChanges,
    IReadOnlyList<string> Warnings);

public sealed record OrgEfficiencyReport(
    string Root,
    string GeneratedAtLocal,
    IReadOnlyList<OrgEfficiencyRepository> Repositories,
    IReadOnlyList<string> Warnings);

public sealed record OrgEfficiencyRepository(
    string RepoRoot,
    string RepoName,
    int FilesAnalyzed,
    int FilesExcluded,
    long RawBytes,
    long GeneratedContextBytes,
    long EstimatedRawTokens,
    long EstimatedContextTokens,
    double? EstimatedReductionPercent,
    bool HasContextPacks,
    bool HasGraphs,
    IReadOnlyList<string> Opportunities,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> Warnings);

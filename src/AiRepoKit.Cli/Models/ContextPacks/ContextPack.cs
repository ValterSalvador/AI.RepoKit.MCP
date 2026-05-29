namespace AiRepoKit.Cli.Models.ContextPacks;

using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Models.ContextBudget;

public sealed record ContextPack(
    string GeneratedAtLocal,
    string RepoRoot,
    string Task,
    string Target,
    string RecommendedAgent,
    string TokenBudgetHint,
    string Summary,
    IReadOnlyList<ContextPackItem> LikelyFiles,
    IReadOnlyList<ContextPackItem> RelevantSymbols,
    IReadOnlyList<ContextPackItem> RelevantEndpoints,
    IReadOnlyList<ContextPackItem> RelevantPackages,
    IReadOnlyList<string> RiskAreas,
    IReadOnlyList<string> ValidationCommands,
    IReadOnlyList<string> SuggestedMcpCalls,
    IReadOnlyList<string> Notes,
    IReadOnlyList<ChangedFileItem>? StagedFiles = null,
    IReadOnlyList<ChangedFileItem>? UnstagedFiles = null,
    IReadOnlyList<ChangedFileItem>? UntrackedFiles = null,
    IReadOnlyList<string>? AffectedProjects = null,
    IReadOnlyList<string>? AffectedSymbols = null,
    string ReviewSummary = "",
    string CommitMessageSuggestion = "",
    int EstimatedTokens = 0,
    int? Budget = null,
    bool Truncated = false,
    IReadOnlyList<BudgetCut>? Cuts = null);

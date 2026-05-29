using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Models.ContextBudget;

namespace AiRepoKit.Cli.Models.Impact;

public sealed record ImpactReport(
    string GeneratedAtLocal,
    string RepoRoot,
    string Target,
    string Since,
    string Summary,
    IReadOnlyList<ChangedFileItem> ChangedFiles,
    IReadOnlyList<string> AffectedProjects,
    IReadOnlyList<string> AffectedSymbols,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> RecommendedContextPacks,
    IReadOnlyList<string> RecommendedAgents,
    IReadOnlyList<string> ValidationCommands,
    string ReviewSummary,
    string CommitMessageSuggestion,
    int EstimatedTokens,
    int? Budget,
    bool Truncated,
    IReadOnlyList<BudgetCut> Cuts,
    IReadOnlyList<string> Warnings);

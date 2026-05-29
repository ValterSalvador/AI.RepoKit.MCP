namespace AiRepoKit.Cli.Models.ContextPacks;

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
    IReadOnlyList<string> Notes);

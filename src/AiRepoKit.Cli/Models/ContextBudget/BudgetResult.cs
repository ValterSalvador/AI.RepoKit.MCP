namespace AiRepoKit.Cli.Models.ContextBudget;

public sealed record BudgetResult<T>(
    T Value,
    int EstimatedTokens,
    int? Budget,
    bool Truncated,
    IReadOnlyList<BudgetCut> Cuts);

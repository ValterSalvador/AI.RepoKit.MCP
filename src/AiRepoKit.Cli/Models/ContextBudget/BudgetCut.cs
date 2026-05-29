namespace AiRepoKit.Cli.Models.ContextBudget;

public sealed record BudgetCut(string Path, string Reason, int RemovedEstimatedTokens);

namespace AiRepoKit.Cli.Models.Efficiency;

public sealed record EfficiencyRefreshSummary(
    string Mode,
    bool CodeIndexAttempted,
    bool CodeIndexRefreshed,
    bool ContextPacksAttempted,
    bool ContextPacksRefreshed,
    bool McpBudgetAttempted,
    bool McpBudgetRefreshed,
    bool CacheStale,
    IReadOnlyList<string> Reasons);

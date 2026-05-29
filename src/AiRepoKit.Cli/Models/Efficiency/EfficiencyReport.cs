namespace AiRepoKit.Cli.Models.Efficiency;

public sealed record EfficiencyReport(
    string Repo,
    string Profile,
    string SampleQuery,
    string GeneratedAtLocal,
    EfficiencyMetric RawSource,
    EfficiencyMetric GeneratedContext,
    EfficiencyMetric McpBudget,
    EfficiencyMetric CodeIndexCache,
    string CodeIndexCacheHitRate,
    EfficiencyRefreshSummary Refresh,
    double? EstimatedSavingsPercent,
    EfficiencySafetySummary Safety,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Notes);

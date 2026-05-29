using AiRepoKit.Cli.Models.ContextBudget;

namespace AiRepoKit.Cli.Models.Graphs;

public sealed record GraphReport(
    string GeneratedAtLocal,
    string RepoRoot,
    string Kind,
    string Summary,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    int EstimatedTokens,
    int? Budget,
    bool Truncated,
    IReadOnlyList<BudgetCut> Cuts,
    IReadOnlyList<string> Warnings);

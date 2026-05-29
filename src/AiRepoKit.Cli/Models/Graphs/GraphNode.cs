namespace AiRepoKit.Cli.Models.Graphs;

public sealed record GraphNode(
    string Id,
    string Kind,
    string Label,
    string Path,
    IReadOnlyList<string> Tags);

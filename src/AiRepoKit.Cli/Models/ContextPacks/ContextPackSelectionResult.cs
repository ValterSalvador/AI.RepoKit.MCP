namespace AiRepoKit.Cli.Models.ContextPacks;

public sealed record ContextPackSelectionResult(
    ContextPack Pack,
    IReadOnlyList<string> Warnings);

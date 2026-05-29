namespace AiRepoKit.Cli.Models.ContextPacks;

public sealed record ContextPackItem(
    string Name,
    string Kind,
    string File,
    string Reason,
    int Score);

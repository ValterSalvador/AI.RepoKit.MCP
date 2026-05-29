namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeIndexCacheEntry(
    string File,
    string Sha256,
    long SizeBytes,
    string LastWriteTimeUtc,
    IReadOnlyList<CodeSymbol> Symbols,
    IReadOnlyList<CodeEndpoint> Endpoints);

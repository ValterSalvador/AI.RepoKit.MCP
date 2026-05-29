namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeIndexResult(
    string RepoRoot,
    IReadOnlyList<string> Files,
    int FilesDiscovered,
    int FilesIndexed,
    int FilesReused,
    int FilesRemovedFromCache,
    bool CacheUsed,
    string CachePath,
    IReadOnlyList<string> CacheWarnings,
    CodeInventorySummary SymbolInventory,
    EndpointInventorySummary EndpointInventory);

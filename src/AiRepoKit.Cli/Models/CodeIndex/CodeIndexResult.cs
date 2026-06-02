namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeIndexResult(
    string RepoRoot,
    IReadOnlyList<string> Files,
    int FilesDiscovered,
    int FilesIndexed,
    int FilesReused,
    int FastPathReusedFiles,
    int HashValidatedFiles,
    int ParsedFiles,
    int FilesRemovedFromCache,
    bool CacheUsed,
    string CachePath,
    string CacheInvalidationReason,
    IReadOnlyList<string> CacheWarnings,
    CodeInventorySummary SymbolInventory,
    EndpointInventorySummary EndpointInventory);

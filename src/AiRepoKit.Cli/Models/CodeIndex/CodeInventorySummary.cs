namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeInventorySummary(
    string GeneratedAtLocal,
    string RepoRoot,
    string Indexer,
    int TotalFilesScanned,
    bool CacheUsed,
    int FilesIndexed,
    int FilesReused,
    int FilesRemovedFromCache,
    int TotalSymbols,
    bool Truncated,
    IReadOnlyList<string> IgnoredDirectories,
    IReadOnlyList<string> IgnoredFiles,
    IReadOnlyDictionary<string, int> ClassificationCounts,
    IReadOnlyList<CodeSymbol> Symbols);

public sealed record EndpointInventorySummary(
    string GeneratedAtLocal,
    string RepoRoot,
    string Indexer,
    bool CacheUsed,
    int FilesIndexed,
    int FilesReused,
    int FilesRemovedFromCache,
    int TotalEndpoints,
    IReadOnlyList<CodeEndpoint> Endpoints);

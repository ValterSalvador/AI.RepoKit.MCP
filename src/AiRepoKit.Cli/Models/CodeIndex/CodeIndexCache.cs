namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeIndexCache(
    string GeneratedAtLocal,
    string Indexer,
    string ToolVersion,
    string RepoRoot,
    bool IncludePrivateMembers,
    IReadOnlyList<CodeIndexCacheEntry> Files);

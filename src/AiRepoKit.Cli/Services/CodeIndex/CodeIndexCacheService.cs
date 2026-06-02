using System.Security.Cryptography;
using System.Text.Json;
using AiRepoKit.Cli.Models.CodeIndex;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Services.CodeIndex;

public sealed class CodeIndexCacheService
{
    public const string CacheRelativePath = ".ai/generated/cache/code-index-cache.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public CodeIndexCacheLoadResult Load(string repoRoot_, bool enabled_, bool rebuild_, bool includePrivateMembers_)
    {
        if (!enabled_)
        {
            return new CodeIndexCacheLoadResult(null, [], "cache-disabled");
        }

        if (rebuild_)
        {
            return new CodeIndexCacheLoadResult(null, [], "rebuild-cache");
        }

        string path = this.GetCachePath(repoRoot_);
        if (!File.Exists(path))
        {
            return new CodeIndexCacheLoadResult(null, [], "cache-missing");
        }

        try
        {
            CodeIndexCache? cache = JsonSerializer.Deserialize<CodeIndexCache>(File.ReadAllText(path), JsonOptions);
            if (cache is not null && cache.IncludePrivateMembers != includePrivateMembers_)
            {
                return new CodeIndexCacheLoadResult(null, [], "include-private-members-changed");
            }

            if (cache is not null && !string.Equals(cache.ToolVersion, TemplateService.GetToolVersion(), StringComparison.OrdinalIgnoreCase))
            {
                return new CodeIndexCacheLoadResult(null, [], "tool-version-changed");
            }

            return new CodeIndexCacheLoadResult(cache, [], cache is null ? "cache-empty" : string.Empty);
        }
        catch (Exception exception)
        {
            return new CodeIndexCacheLoadResult(null, [$"Code index cache could not be read and will be rebuilt: {exception.Message}"], "cache-read-failed");
        }
    }

    public void Save(string repoRoot_, CodeIndexCache cache_, bool enabled_)
    {
        if (!enabled_)
        {
            return;
        }

        string path = this.GetCachePath(repoRoot_);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? repoRoot_);
        File.WriteAllText(path, JsonSerializer.Serialize(cache_, JsonOptions));
    }

    public CodeIndexCacheEntry? FindEntry(CodeIndexCache? cache_, string file_)
    {
        return cache_?.Files.FirstOrDefault(entry_ => string.Equals(entry_.File, file_, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsMetadataMatch(CodeIndexCacheEntry? entry_, long sizeBytes_, string lastWriteTimeUtc_)
    {
        return entry_ is not null
            && entry_.SizeBytes == sizeBytes_
            && string.Equals(entry_.LastWriteTimeUtc, lastWriteTimeUtc_, StringComparison.Ordinal);
    }

    public bool IsHashMatch(CodeIndexCacheEntry? entry_, string sha256_)
    {
        return entry_ is not null
            && string.Equals(entry_.Sha256, sha256_, StringComparison.OrdinalIgnoreCase);
    }

    public CodeIndexCacheEntry? GetReusableEntry(CodeIndexCache? cache_, string file_, string sha256_, long sizeBytes_, string lastWriteTimeUtc_)
    {
        CodeIndexCacheEntry? entry = this.FindEntry(cache_, file_);
        if (entry is null)
        {
            return null;
        }

        return this.IsHashMatch(entry, sha256_)
            && this.IsMetadataMatch(entry, sizeBytes_, lastWriteTimeUtc_)
                ? entry
                : null;
    }

    public CodeIndexFileMetadata GetFileMetadata(string repoRoot_, string relativePath_)
    {
        string fullPath = Path.Combine(Path.GetFullPath(repoRoot_), relativePath_.Replace('/', Path.DirectorySeparatorChar));
        FileInfo file = new(fullPath);
        return new CodeIndexFileMetadata(relativePath_, file.Length, file.LastWriteTimeUtc.ToString("O"));
    }

    public CodeIndexFileState GetFileState(string repoRoot_, string relativePath_)
    {
        CodeIndexFileMetadata metadata = this.GetFileMetadata(repoRoot_, relativePath_);
        return this.GetFileState(repoRoot_, metadata);
    }

    public CodeIndexFileState GetFileState(string repoRoot_, CodeIndexFileMetadata metadata_)
    {
        string fullPath = Path.Combine(Path.GetFullPath(repoRoot_), metadata_.File.Replace('/', Path.DirectorySeparatorChar));
        return new CodeIndexFileState(
            metadata_.File,
            this.ComputeSha256(fullPath),
            metadata_.SizeBytes,
            metadata_.LastWriteTimeUtc);
    }

    public string GetCachePath(string repoRoot_)
    {
        return Path.Combine(Path.GetFullPath(repoRoot_), CacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private string ComputeSha256(string path_)
    {
        using FileStream stream = File.OpenRead(path_);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record CodeIndexCacheLoadResult(CodeIndexCache? Cache, IReadOnlyList<string> Warnings, string InvalidationReason);

public sealed record CodeIndexFileMetadata(string File, long SizeBytes, string LastWriteTimeUtc);

public sealed record CodeIndexFileState(string File, string Sha256, long SizeBytes, string LastWriteTimeUtc);

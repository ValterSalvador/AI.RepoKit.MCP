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
        if (!enabled_ || rebuild_)
        {
            return new CodeIndexCacheLoadResult(null, []);
        }

        string path = this.GetCachePath(repoRoot_);
        if (!File.Exists(path))
        {
            return new CodeIndexCacheLoadResult(null, []);
        }

        try
        {
            CodeIndexCache? cache = JsonSerializer.Deserialize<CodeIndexCache>(File.ReadAllText(path), JsonOptions);
            if (cache is not null && cache.IncludePrivateMembers != includePrivateMembers_)
            {
                return new CodeIndexCacheLoadResult(null, []);
            }

            if (cache is not null && !string.Equals(cache.ToolVersion, TemplateService.GetToolVersion(), StringComparison.OrdinalIgnoreCase))
            {
                return new CodeIndexCacheLoadResult(null, []);
            }

            return new CodeIndexCacheLoadResult(cache, []);
        }
        catch (Exception exception)
        {
            return new CodeIndexCacheLoadResult(null, [$"Code index cache could not be read and will be rebuilt: {exception.Message}"]);
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

    public CodeIndexCacheEntry? GetReusableEntry(CodeIndexCache? cache_, string file_, string sha256_, long sizeBytes_, string lastWriteTimeUtc_)
    {
        CodeIndexCacheEntry? entry = cache_?.Files.FirstOrDefault(entry_ => string.Equals(entry_.File, file_, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        return string.Equals(entry.Sha256, sha256_, StringComparison.OrdinalIgnoreCase)
            && entry.SizeBytes == sizeBytes_
            && string.Equals(entry.LastWriteTimeUtc, lastWriteTimeUtc_, StringComparison.Ordinal)
                ? entry
                : null;
    }

    public CodeIndexFileState GetFileState(string repoRoot_, string relativePath_)
    {
        string fullPath = Path.Combine(Path.GetFullPath(repoRoot_), relativePath_.Replace('/', Path.DirectorySeparatorChar));
        FileInfo file = new(fullPath);
        return new CodeIndexFileState(
            relativePath_,
            this.ComputeSha256(fullPath),
            file.Length,
            file.LastWriteTimeUtc.ToString("O"));
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

public sealed record CodeIndexCacheLoadResult(CodeIndexCache? Cache, IReadOnlyList<string> Warnings);

public sealed record CodeIndexFileState(string File, string Sha256, long SizeBytes, string LastWriteTimeUtc);

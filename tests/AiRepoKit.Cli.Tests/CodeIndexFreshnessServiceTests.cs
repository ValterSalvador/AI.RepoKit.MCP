using System.Text.Json;
using AiRepoKit.Cli.Models.CodeIndex;
using AiRepoKit.Cli.Services.CodeIndex;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class CodeIndexFreshnessServiceTests
{
    private readonly CodeIndexFreshnessService service = new();

    [Fact]
    public void Check_AllGeneratedInputsAbsent_ReturnsReasonsInOrder()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.True(result.Stale);
            Assert.Equal(
                new[] { "symbol inventory missing", "endpoint inventory missing", "code-index cache missing" },
                result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_EmptyInputsAndNoCSharpFiles_IsFresh()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteCache(repoRoot, []);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.False(result.Stale);
            Assert.Empty(result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Theory]
    [InlineData(false, true, true, "symbol inventory missing")]
    [InlineData(true, false, true, "endpoint inventory missing")]
    [InlineData(true, true, false, "code-index cache missing")]
    public void Check_OneGeneratedInputAbsent_ReturnsExpectedReason(
        bool includeSymbol,
        bool includeEndpoint,
        bool includeCache,
        string expectedReason)
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteGeneratedInputs(repoRoot, includeSymbol, includeEndpoint, includeCache);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.True(result.Stale);
            Assert.Equal(new[] { expectedReason }, result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_CurrentFileCountDiffers_ReturnsCountReason()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteSource(repoRoot, "Current.cs", "internal class Current { }");
            WriteCache(repoRoot, []);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.Contains("current C# file count differs from cache file count", result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_CurrentFileAbsentFromSameCountCache_ReturnsMissingReason()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteSource(repoRoot, "Current.cs", "internal class Current { }");
            WriteCache(repoRoot, [CacheEntry("Other.cs", 0, string.Empty)]);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.Contains("current C# file missing from cache", result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_CurrentFileSizeDiffers_ReturnsMetadataReason()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            string sourcePath = WriteSource(repoRoot, "Current.cs", "internal class Current { }");
            FileInfo info = new(sourcePath);
            WriteCache(repoRoot, [CacheEntry("Current.cs", info.Length + 1, info.LastWriteTimeUtc.ToString("O"))]);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.Contains("cached C# file size or last-write metadata differs", result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_CurrentFileLastWriteDiffers_ReturnsMetadataReason()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            string sourcePath = WriteSource(repoRoot, "Current.cs", "internal class Current { }");
            FileInfo info = new(sourcePath);
            WriteCache(repoRoot, [CacheEntry("Current.cs", info.Length, "2000-01-01T00:00:00.0000000Z")]);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.Contains("cached C# file size or last-write metadata differs", result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_CachedFileNoLongerExists_ReturnsRemovedReason()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteCache(repoRoot, [CacheEntry("Removed.cs", 0, string.Empty)]);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.Contains("cached C# file no longer exists", result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_MalformedCacheJson_ReturnsNonFatalReadFailure()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteRawCache(repoRoot, "{not-json");

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.True(result.Stale);
            Assert.StartsWith("code-index cache could not be read: ", Assert.Single(result.Reasons));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_DuplicateCacheFiles_ReturnsNonFatalReadFailure()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteCache(
                repoRoot,
                [CacheEntry("Duplicate.cs", 0, string.Empty), CacheEntry("duplicate.cs", 0, string.Empty)]);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.True(result.Stale);
            Assert.StartsWith("code-index cache could not be read: ", Assert.Single(result.Reasons));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Check_MatchingOneFileCache_IsFresh()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            string sourcePath = WriteSource(repoRoot, "Current.cs", "internal class Current { }");
            FileInfo info = new(sourcePath);
            WriteCache(repoRoot, [CacheEntry("current.cs", info.Length, info.LastWriteTimeUtc.ToString("O"))]);

            CodeIndexFreshnessResult result = service.Check(repoRoot, 100);

            Assert.False(result.Stale);
            Assert.Empty(result.Reasons);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    private static object CacheEntry(string file, long sizeBytes, string lastWriteTimeUtc)
    {
        return new { File = file, SizeBytes = sizeBytes, LastWriteTimeUtc = lastWriteTimeUtc };
    }

    private static string CreateTempRepo()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_freshness_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRepo(string path)
    {
        if (Directory.Exists(path))
        {
            try { Directory.Delete(path, true); } catch { }
        }
    }

    private static void WriteGeneratedInputs(string repoRoot, bool includeSymbol, bool includeEndpoint, bool includeCache)
    {
        string inventories = Path.Combine(repoRoot, ".ai", "generated", "inventories");
        Directory.CreateDirectory(inventories);
        if (includeSymbol)
        {
            File.WriteAllText(Path.Combine(inventories, "symbol-inventory.json"), "{}");
        }

        if (includeEndpoint)
        {
            File.WriteAllText(Path.Combine(inventories, "endpoint-inventory.json"), "{}");
        }

        if (includeCache)
        {
            WriteCache(repoRoot, []);
        }
    }

    private static void WriteInventories(string repoRoot)
    {
        WriteGeneratedInputs(repoRoot, includeSymbol: true, includeEndpoint: true, includeCache: false);
    }

    private static void WriteCache(string repoRoot, IReadOnlyList<object> files)
    {
        WriteRawCache(repoRoot, JsonSerializer.Serialize(new { Files = files }));
    }

    private static void WriteRawCache(string repoRoot, string contents)
    {
        string cacheDirectory = Path.Combine(repoRoot, ".ai", "generated", "cache");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(cacheDirectory, "code-index-cache.json"), contents);
    }

    private static string WriteSource(string repoRoot, string relativePath, string contents)
    {
        string path = Path.Combine(repoRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }
}

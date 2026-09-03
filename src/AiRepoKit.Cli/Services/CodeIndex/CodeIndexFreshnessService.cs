using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.Models.CodeIndex;

namespace AiRepoKit.Cli.Services.CodeIndex;

public sealed class CodeIndexFreshnessService : ICodeIndexFreshnessService
{
    public CodeIndexFreshnessResult Check(string repoRoot_, int maxFiles_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        List<string> reasons = [];
        string symbolPath = Path.Combine(repoRoot, ".ai", "generated", "inventories", "symbol-inventory.json");
        string endpointPath = Path.Combine(repoRoot, ".ai", "generated", "inventories", "endpoint-inventory.json");
        string cachePath = Path.Combine(repoRoot, ".ai", "generated", "cache", "code-index-cache.json");
        if (!File.Exists(symbolPath))
        {
            reasons.Add("symbol inventory missing");
        }

        if (!File.Exists(endpointPath))
        {
            reasons.Add("endpoint inventory missing");
        }

        if (!File.Exists(cachePath))
        {
            reasons.Add("code-index cache missing");
            return new CodeIndexFreshnessResult(reasons.Count > 0, reasons);
        }

        try
        {
            JsonObject? cache = JsonNode.Parse(File.ReadAllText(cachePath)) as JsonObject;
            JsonArray files = GetArray(cache, "Files");
            Dictionary<string, JsonObject> cached = files.OfType<JsonObject>().ToDictionary(file_ => GetString(file_, "File"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> current = new(new CodeFileDiscoveryService().Discover(repoRoot, maxFiles_).Files, StringComparer.OrdinalIgnoreCase);
            if (current.Count != cached.Count)
            {
                reasons.Add("current C# file count differs from cache file count");
            }

            foreach (string file in current)
            {
                if (!cached.TryGetValue(file, out JsonObject? entry))
                {
                    reasons.Add("current C# file missing from cache");
                    break;
                }

                FileInfo info = new(Path.Combine(repoRoot, file.Replace('/', Path.DirectorySeparatorChar)));
                long cachedSize = GetLong(entry, "SizeBytes");
                string cachedLastWrite = GetString(entry, "LastWriteTimeUtc");
                if (cachedSize != info.Length || !string.Equals(cachedLastWrite, info.LastWriteTimeUtc.ToString("O"), StringComparison.Ordinal))
                {
                    reasons.Add("cached C# file size or last-write metadata differs");
                    break;
                }
            }

            foreach (string cachedFile in cached.Keys)
            {
                if (!current.Contains(cachedFile))
                {
                    reasons.Add("cached C# file no longer exists");
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            reasons.Add("code-index cache could not be read: " + exception.Message);
        }

        return new CodeIndexFreshnessResult(reasons.Count > 0, reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static JsonArray GetArray(JsonObject? value_, string name_)
    {
        return value_ is not null && value_.TryGetPropertyValue(name_, out JsonNode? node) && node is JsonArray array ? array : [];
    }

    private static string GetString(JsonObject value_, string name_)
    {
        return value_.TryGetPropertyValue(name_, out JsonNode? node) && node is not null && node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : string.Empty;
    }

    private static long GetLong(JsonObject? value_, string name_)
    {
        return value_ is not null && value_.TryGetPropertyValue(name_, out JsonNode? node) && node is not null && node.GetValueKind() == JsonValueKind.Number ? node.GetValue<long>() : 0;
    }
}

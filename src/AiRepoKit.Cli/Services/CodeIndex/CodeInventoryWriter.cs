using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models.CodeIndex;

namespace AiRepoKit.Cli.Services.CodeIndex;

public sealed class CodeInventoryWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public IReadOnlyList<string> Write(string repoRoot_, string output_, string format_, CodeIndexResult result_, bool dryRun_)
    {
        string outputPath = this.ResolveOutputPath(repoRoot_, output_);
        List<string> paths = [];
        bool writeJson = format_.Equals("json", StringComparison.OrdinalIgnoreCase) || format_.Equals("all", StringComparison.OrdinalIgnoreCase);
        bool writeMarkdown = format_.Equals("markdown", StringComparison.OrdinalIgnoreCase) || format_.Equals("all", StringComparison.OrdinalIgnoreCase);

        if (!dryRun_)
        {
            Directory.CreateDirectory(outputPath);
        }

        if (writeJson)
        {
            paths.Add(this.WriteFile(outputPath, "symbol-inventory.json", JsonSerializer.Serialize(result_.SymbolInventory, JsonOptions), dryRun_));
            paths.Add(this.WriteFile(outputPath, "endpoint-inventory.json", JsonSerializer.Serialize(result_.EndpointInventory, JsonOptions), dryRun_));
        }

        if (writeMarkdown)
        {
            paths.Add(this.WriteFile(outputPath, "symbol-inventory.md", this.WriteSymbolMarkdown(result_.SymbolInventory), dryRun_));
            paths.Add(this.WriteFile(outputPath, "endpoint-inventory.md", this.WriteEndpointMarkdown(result_.EndpointInventory), dryRun_));
        }

        return paths.Select(path_ => Path.GetRelativePath(Path.GetFullPath(repoRoot_), path_).Replace('\\', '/')).ToArray();
    }

    private string ResolveOutputPath(string repoRoot_, string output_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        if (string.Equals(output_, ".ai", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(output_))
        {
            output_ = ".ai/generated/inventories";
        }

        string outputPath = Path.IsPathRooted(output_) ? Path.GetFullPath(output_) : Path.GetFullPath(Path.Combine(repoRoot, output_));
        string root = repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Output path must stay inside the target repository.");
        }

        string relative = Path.GetRelativePath(repoRoot, outputPath).Replace('\\', '/');
        if (!relative.Equals(".ai/generated", StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith(".ai/generated/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Output path must be under .ai/generated.");
        }

        if (relative.StartsWith(".git", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("bin", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("oracle-data", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("wwwroot/uploads", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("Tools/AISandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Output path is restricted.");
        }

        return outputPath;
    }

    private string WriteFile(string outputPath_, string fileName_, string content_, bool dryRun_)
    {
        string path = Path.Combine(outputPath_, fileName_);
        if (!dryRun_)
        {
            File.WriteAllText(path, content_);
        }

        return path;
    }

    private string WriteSymbolMarkdown(CodeInventorySummary inventory_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Symbol Inventory");
        builder.AppendLine();
        builder.AppendLine($"Generated: {inventory_.GeneratedAtLocal}");
        builder.AppendLine($"Indexer: {inventory_.Indexer}");
        builder.AppendLine($"Files scanned: {inventory_.TotalFilesScanned}");
        builder.AppendLine($"Cache used: {inventory_.CacheUsed}");
        builder.AppendLine($"Files indexed: {inventory_.FilesIndexed}");
        builder.AppendLine($"Files reused: {inventory_.FilesReused}");
        builder.AppendLine($"Files removed from cache: {inventory_.FilesRemovedFromCache}");
        builder.AppendLine($"Symbols: {inventory_.TotalSymbols}");
        builder.AppendLine($"Truncated: {inventory_.Truncated}");
        builder.AppendLine();
        builder.AppendLine("This inventory is RoslynLite syntax-based and does not require full semantic compilation.");
        builder.AppendLine();
        builder.AppendLine("## Classification Counts");
        builder.AppendLine();
        foreach (KeyValuePair<string, int> count in inventory_.ClassificationCounts)
        {
            builder.AppendLine($"- {count.Key}: {count.Value}");
        }

        builder.AppendLine();
        builder.AppendLine("## Top Controllers");
        builder.AppendLine();
        this.AppendSymbols(builder, inventory_.Symbols.Where(symbol_ => symbol_.Classification == "Controller").Take(20));
        builder.AppendLine();
        builder.AppendLine("## Top Services, Handlers, DbContexts");
        builder.AppendLine();
        this.AppendSymbols(builder, inventory_.Symbols.Where(symbol_ => symbol_.Classification is "Service" or "Handler" or "DbContext" or "Repository").Take(40));
        return builder.ToString().TrimEnd();
    }

    private string WriteEndpointMarkdown(EndpointInventorySummary inventory_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Endpoint Inventory");
        builder.AppendLine();
        builder.AppendLine($"Generated: {inventory_.GeneratedAtLocal}");
        builder.AppendLine($"Indexer: {inventory_.Indexer}");
        builder.AppendLine($"Cache used: {inventory_.CacheUsed}");
        builder.AppendLine($"Files indexed: {inventory_.FilesIndexed}");
        builder.AppendLine($"Files reused: {inventory_.FilesReused}");
        builder.AppendLine($"Files removed from cache: {inventory_.FilesRemovedFromCache}");
        builder.AppendLine($"Endpoints: {inventory_.TotalEndpoints}");
        builder.AppendLine();
        builder.AppendLine("This inventory is RoslynLite syntax-based and does not require full semantic compilation.");
        builder.AppendLine();
        builder.AppendLine("## Top Endpoints");
        builder.AppendLine();
        if (inventory_.Endpoints.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (CodeEndpoint endpoint in inventory_.Endpoints.Take(60))
            {
                builder.AppendLine($"- {endpoint.Method} {endpoint.Route} -> {endpoint.HandlerOrController} ({endpoint.File}:{endpoint.Line})");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private void AppendSymbols(StringBuilder builder_, IEnumerable<CodeSymbol> symbols_)
    {
        bool any = false;
        foreach (CodeSymbol symbol in symbols_)
        {
            any = true;
            builder_.AppendLine($"- {symbol.Name} [{symbol.Kind}] {symbol.File}:{symbol.Line}");
        }

        if (!any)
        {
            builder_.AppendLine("- None");
        }
    }
}

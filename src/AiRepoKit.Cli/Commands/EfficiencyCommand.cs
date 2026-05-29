using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.Efficiency;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.CodeIndex;

namespace AiRepoKit.Cli.Commands;

public sealed class EfficiencyCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] IgnoredDirectories =
    [
        "bin",
        "obj",
        ".git",
        ".vs",
        ".idea",
        ".ai/generated",
        ".dotnet-home",
        "artifacts",
        ".tmp",
        "node_modules"
    ];

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        List<string> warnings = [];
        List<string> refreshReasons = [];
        bool codeIndexAttempted = false;
        bool codeIndexRefreshed = false;
        bool contextPacksAttempted = false;
        bool contextPacksRefreshed = false;
        bool budgetAttempted = false;
        bool budgetRefreshed = false;
        bool cacheStale = false;

        try
        {
            progress.StartPhase("Resolving repository");
            string repoPath = Path.GetFullPath(options_.RepoPath);
            progress.CompletePhase("Repository resolved");

            progress.StartPhase("Checking generated data freshness");
            SourceScan rawSource = ScanSource(repoPath);
            FreshnessResult freshness = CheckFreshness(repoPath, options_.MaxFiles);
            cacheStale = freshness.Stale;
            refreshReasons.AddRange(freshness.Reasons);
            progress.CompletePhase("Generated data freshness checked");

            if (options_.NoRefresh)
            {
                warnings.Add("Refresh disabled by --no-refresh; existing generated data was used.");
                if (freshness.Stale)
                {
                    warnings.Add("Generated data is missing or stale: " + string.Join("; ", freshness.Reasons) + ".");
                }
            }
            else
            {
                bool forceRefresh = options_.Refresh;
                bool refreshCodeIndex = forceRefresh || freshness.Stale;
                progress.StartPhase("Refreshing code index");
                if (refreshCodeIndex)
                {
                    codeIndexAttempted = true;
                    CommandResult codeIndex = new CodeIndexCommand().Execute(CreateCodeIndexOptions(options_));
                    codeIndexRefreshed = codeIndex.Success;
                    if (!codeIndex.Success)
                    {
                        warnings.Add("Code-index refresh failed; existing generated data will be used where available.");
                    }
                }
                progress.CompletePhase(refreshCodeIndex ? "Code index refresh completed" : "Code index refresh not needed");

                progress.StartPhase("Refreshing context packs");
                IReadOnlyList<string> missingPacks = MissingContextPacks(repoPath);
                if (forceRefresh || missingPacks.Count > 0)
                {
                    contextPacksAttempted = true;
                    foreach (string task in new[] { "review-risk", "fix-build" })
                    {
                        if (!forceRefresh && !missingPacks.Contains(task, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        CommandResult pack = new ContextPackCommand().Execute(CreateContextPackOptions(options_, task));
                        if (!pack.Success)
                        {
                            warnings.Add($"Context pack `{task}` refresh failed; existing generated data will be used where available.");
                        }
                        else
                        {
                            contextPacksRefreshed = true;
                        }
                    }
                }
                progress.CompletePhase(contextPacksAttempted ? "Context pack refresh completed" : "Context pack refresh not needed");

                progress.StartPhase("Refreshing MCP budget");
                if (options_.SkipBudget)
                {
                    warnings.Add("MCP budget refresh skipped by --skip-budget.");
                }
                else
                {
                    string script = Path.Combine(repoPath, "Tools", "AiContext", "MeasureMcpResponseBudget.ps1");
                    if (File.Exists(script))
                    {
                        budgetAttempted = true;
                        ProcessResult budget = new ProcessRunner().Run("powershell", ["-ExecutionPolicy", "Bypass", "-File", script, "-RepoRoot", repoPath], repoPath);
                        budgetRefreshed = budget.Success;
                        if (!budget.Success)
                        {
                            warnings.Add("MCP budget refresh failed; using existing budget report or fallback generated context estimate.");
                        }
                    }
                    else
                    {
                        warnings.Add("MCP budget script was not found; using fallback generated context estimate.");
                    }
                }
                progress.CompletePhase(budgetAttempted ? "MCP budget refresh completed" : "MCP budget refresh not run");
            }

            progress.StartPhase("Scanning source metadata");
            rawSource = ScanSource(repoPath);
            EfficiencyMetric rawMetric = new(rawSource.Bytes, EstimateTokens(rawSource.Bytes), rawSource.CSharpFileCount);
            progress.CompletePhase("Source metadata scanned");

            progress.StartPhase("Reading generated context");
            GeneratedContext generated = ReadGeneratedContext(repoPath, warnings);
            progress.CompletePhase("Generated context read");

            progress.StartPhase("Calculating estimates");
            long compactTokens = generated.McpBudget.EstimatedTokens > 0 ? generated.McpBudget.EstimatedTokens : generated.Context.EstimatedTokens;
            double? savings = rawMetric.EstimatedTokens > 0 ? Math.Clamp(100.0 * (1.0 - compactTokens / (double)rawMetric.EstimatedTokens), 0.0, 100.0) : null;
            if (rawMetric.EstimatedTokens == 0 && generated.Context.EstimatedTokens == 0)
            {
                warnings.Add("No C# source metadata or generated context data was found.");
            }

            if (generated.Safety.SecretsExposed == true)
            {
                warnings.Add("MCP budget report indicates secretsExposed=true.");
            }

            if (generated.Safety.SecretValuesReturned == true)
            {
                warnings.Add("MCP budget report indicates secretValuesReturned=true.");
            }

            EfficiencyRefreshSummary refresh = new(
                options_.NoRefresh ? "no-refresh" : options_.Refresh ? "refresh" : "auto",
                codeIndexAttempted,
                codeIndexRefreshed,
                contextPacksAttempted,
                contextPacksRefreshed,
                budgetAttempted,
                budgetRefreshed,
                cacheStale,
                refreshReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            EfficiencyReport report = new(
                repoPath,
                options_.Profile,
                string.IsNullOrWhiteSpace(options_.SampleQuery) ? "architecture services controllers data access" : options_.SampleQuery,
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                rawMetric,
                generated.Context,
                generated.McpBudget,
                generated.CodeIndexCache,
                generated.CodeIndexCacheHitRate,
                refresh,
                savings,
                generated.Safety,
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).Select(ProcessRunner.Redact).ToArray(),
                [
                    "Token estimates use bytes / 4.",
                    "This is an approximation, not exact tokenizer output."
                ]);
            progress.CompletePhase("Estimates calculated");
            progress.CompletePhase("Completed");
            return CommandResult.Ok(options_.AuditJson ? JsonSerializer.Serialize(report, JsonOptions) : WriteMarkdown(report));
        }
        catch (Exception exception)
        {
            progress.FailPhase("Efficiency report failed");
            string message = ProcessRunner.Redact(exception.Message);
            if (options_.AuditJson)
            {
                var failure = new
                {
                    error = message,
                    warnings
                };
                return CommandResult.Failure(JsonSerializer.Serialize(failure, JsonOptions), 1);
            }

            return CommandResult.Failure("# Efficiency Report" + Environment.NewLine + Environment.NewLine + "Unable to calculate efficiency report: " + message, 1);
        }
    }

    private static BootstrapOptions CreateCodeIndexOptions(BootstrapOptions options_)
    {
        return new BootstrapOptions("code-index", options_.RepoPath, [], false, true, false, false, false, false, options_.Profile, options_.TargetFramework, options_.McpServerName, options_.ToolCommandName, options_.McpProjectName, options_.McpNamespace, options_.McpAssemblyName, options_.McpProjectRelativePath, false, false, false, false, false, false, false, options_.MaxFiles, options_.MaxItems, options_.IncludePrivateMembers, false, options_.RebuildCache, ".ai/generated/inventories", "all", options_.Verbose, false, false, false, false, false, false, false, false, "review-risk", string.Empty, 20, false, [], true);
    }

    private static BootstrapOptions CreateContextPackOptions(BootstrapOptions options_, string task_)
    {
        return new BootstrapOptions("context-pack", options_.RepoPath, [], false, true, false, false, false, false, options_.Profile, options_.TargetFramework, options_.McpServerName, options_.ToolCommandName, options_.McpProjectName, options_.McpNamespace, options_.McpAssemblyName, options_.McpProjectRelativePath, false, false, false, false, false, false, false, options_.MaxFiles, options_.MaxItems, options_.IncludePrivateMembers, false, options_.RebuildCache, ".ai/generated/inventories", "all", options_.Verbose, false, false, false, false, false, false, false, false, task_, string.Empty, 20, false, [], true);
    }

    private static SourceScan ScanSource(string repoPath_)
    {
        if (!Directory.Exists(repoPath_))
        {
            return new SourceScan([], 0, 0);
        }

        List<string> csharpFiles = [];
        long bytes = 0;
        int csharpCount = 0;
        foreach (string path in EnumerateFiles(repoPath_))
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase))
            {
                FileInfo file = new(path);
                bytes += file.Length;
                if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    csharpCount++;
                    csharpFiles.Add(Relative(repoPath_, path));
                }
            }
        }

        return new SourceScan(csharpFiles.OrderBy(value_ => value_, StringComparer.OrdinalIgnoreCase).ToArray(), csharpCount, bytes);
    }

    private static IEnumerable<string> EnumerateFiles(string repoPath_)
    {
        Stack<string> pending = new();
        pending.Push(Path.GetFullPath(repoPath_));
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            foreach (string directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                string relative = Relative(repoPath_, directory);
                if (!IgnoredDirectories.Any(ignored_ => relative.Equals(ignored_, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(ignored_ + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static FreshnessResult CheckFreshness(string repoPath_, int maxFiles_)
    {
        List<string> reasons = [];
        string symbolPath = Path.Combine(repoPath_, ".ai", "generated", "inventories", "symbol-inventory.json");
        string endpointPath = Path.Combine(repoPath_, ".ai", "generated", "inventories", "endpoint-inventory.json");
        string cachePath = Path.Combine(repoPath_, ".ai", "generated", "cache", "code-index-cache.json");
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
            return new FreshnessResult(reasons.Count > 0, reasons);
        }

        try
        {
            JsonObject? cache = JsonNode.Parse(File.ReadAllText(cachePath)) as JsonObject;
            JsonArray files = GetArray(cache, "Files");
            Dictionary<string, JsonObject> cached = files.OfType<JsonObject>().ToDictionary(file_ => GetString(file_, "File"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> current = new(new CodeFileDiscoveryService().Discover(repoPath_, maxFiles_).Files, StringComparer.OrdinalIgnoreCase);
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

                FileInfo info = new(Path.Combine(repoPath_, file.Replace('/', Path.DirectorySeparatorChar)));
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

        return new FreshnessResult(reasons.Count > 0, reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<string> MissingContextPacks(string repoPath_)
    {
        List<string> missing = [];
        foreach (string task in new[] { "review-risk", "fix-build" })
        {
            string path = Path.Combine(repoPath_, ".ai", "generated", "context-packs", task + ".json");
            if (!File.Exists(path))
            {
                missing.Add(task);
            }
        }

        return missing;
    }

    private static GeneratedContext ReadGeneratedContext(string repoPath_, List<string> warnings_)
    {
        string generatedRoot = Path.Combine(repoPath_, ".ai", "generated");
        long contextBytes = 0;
        AddFileSize(Path.Combine(generatedRoot, "inventories", "symbol-inventory.json"), ref contextBytes);
        AddFileSize(Path.Combine(generatedRoot, "inventories", "endpoint-inventory.json"), ref contextBytes);
        string packsRoot = Path.Combine(generatedRoot, "context-packs");
        if (Directory.Exists(packsRoot))
        {
            foreach (string pack in Directory.EnumerateFiles(packsRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                AddFileSize(pack, ref contextBytes);
            }
        }

        string budgetPath = Path.Combine(generatedRoot, "reports", "mcp-budget-report.json");
        AddFileSize(budgetPath, ref contextBytes);
        EfficiencyMetric contextMetric = new(contextBytes, EstimateTokens(contextBytes));
        EfficiencyMetric budgetMetric = ReadBudgetMetric(budgetPath, warnings_, out EfficiencySafetySummary safety);
        string cachePath = Path.Combine(generatedRoot, "cache", "code-index-cache.json");
        EfficiencyMetric cacheMetric = ReadCacheMetric(cachePath, warnings_);
        string hitRate = ReadCodeIndexHitRate(Path.Combine(generatedRoot, "inventories", "symbol-inventory.json"));
        return new GeneratedContext(contextMetric, budgetMetric, cacheMetric, hitRate, safety);
    }

    private static EfficiencyMetric ReadBudgetMetric(string path_, List<string> warnings_, out EfficiencySafetySummary safety_)
    {
        safety_ = new EfficiencySafetySummary(null, null);
        if (!File.Exists(path_))
        {
            warnings_.Add("MCP budget report is missing; safety flags are unknown.");
            return new EfficiencyMetric(0, 0);
        }

        try
        {
            JsonObject? root = JsonNode.Parse(File.ReadAllText(path_)) as JsonObject;
            JsonArray results = GetArray(root, "Results");
            long bytes = results.OfType<JsonObject>().Sum(item_ => GetLong(item_, "SizeBytes"));
            bool? secretsExposed = AnyBool(root, results, "SecretsExposed");
            bool? secretValuesReturned = AnyBool(root, results, "SecretValuesReturned");
            safety_ = new EfficiencySafetySummary(secretsExposed, secretValuesReturned);
            return new EfficiencyMetric(bytes, EstimateTokens(bytes), results.Count);
        }
        catch (Exception exception)
        {
            warnings_.Add("MCP budget report could not be read: " + exception.Message);
            return new EfficiencyMetric(0, 0);
        }
    }

    private static EfficiencyMetric ReadCacheMetric(string path_, List<string> warnings_)
    {
        if (!File.Exists(path_))
        {
            return new EfficiencyMetric(0, 0, 0);
        }

        try
        {
            long bytes = new FileInfo(path_).Length;
            JsonObject? root = JsonNode.Parse(File.ReadAllText(path_)) as JsonObject;
            int files = GetArray(root, "Files").Count;
            return new EfficiencyMetric(bytes, EstimateTokens(bytes), files);
        }
        catch (Exception exception)
        {
            warnings_.Add("Code-index cache could not be read: " + exception.Message);
            long bytes = new FileInfo(path_).Length;
            return new EfficiencyMetric(bytes, EstimateTokens(bytes), 0);
        }
    }

    private static void AddFileSize(string path_, ref long bytes_)
    {
        if (File.Exists(path_))
        {
            bytes_ += new FileInfo(path_).Length;
        }
    }

    private static long EstimateTokens(long bytes_) => (long)Math.Ceiling(bytes_ / 4.0);

    private static string WriteMarkdown(EfficiencyReport report_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Efficiency Report");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{report_.Repo}`");
        builder.AppendLine($"- Profile: `{report_.Profile}`");
        builder.AppendLine($"- Sample query: `{report_.SampleQuery}`");
        builder.AppendLine($"- GeneratedAtLocal: `{report_.GeneratedAtLocal}`");
        builder.AppendLine($"- Refresh mode: `{report_.Refresh.Mode}`");
        builder.AppendLine($"- Raw source files: `{report_.RawSource.FileCount}`");
        builder.AppendLine($"- Raw source bytes: `{report_.RawSource.Bytes}`");
        builder.AppendLine($"- Estimated raw source tokens: `{report_.RawSource.EstimatedTokens}`");
        builder.AppendLine($"- Compact context bytes: `{(report_.McpBudget.Bytes > 0 ? report_.McpBudget.Bytes : report_.GeneratedContext.Bytes)}`");
        builder.AppendLine($"- Estimated compact context tokens: `{(report_.McpBudget.EstimatedTokens > 0 ? report_.McpBudget.EstimatedTokens : report_.GeneratedContext.EstimatedTokens)}`");
        builder.AppendLine($"- Estimated token reduction: `{(report_.EstimatedSavingsPercent.HasValue ? report_.EstimatedSavingsPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "N/A")}`");
        builder.AppendLine($"- Code index cache hit rate: `{report_.CodeIndexCacheHitRate}`");
        builder.AppendLine($"- MCP budget status: `{(report_.McpBudget.FileCount > 0 ? "available" : "fallback")}`");
        builder.AppendLine();
        builder.AppendLine("## Safety");
        builder.AppendLine();
        builder.AppendLine($"- secretsExposed: `{FormatNullable(report_.Safety.SecretsExposed)}`");
        builder.AppendLine($"- secretValuesReturned: `{FormatNullable(report_.Safety.SecretValuesReturned)}`");
        builder.AppendLine();
        builder.AppendLine("## Warnings");
        AppendStrings(builder, report_.Warnings);
        builder.AppendLine();
        builder.AppendLine("## Notes");
        AppendStrings(builder, report_.Notes);
        return builder.ToString().TrimEnd();
    }

    private static string ReadCodeIndexHitRate(string path_)
    {
        if (!File.Exists(path_))
        {
            return "N/A";
        }

        try
        {
            JsonObject? root = JsonNode.Parse(File.ReadAllText(path_)) as JsonObject;
            long indexed = GetLong(root, "FilesIndexed");
            long reused = GetLong(root, "FilesReused");
            long total = indexed + reused;
            if (total <= 0)
            {
                return "N/A";
            }

            return (100.0 * reused / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
        catch
        {
            return "N/A";
        }
    }

    private static string FormatNullable(bool? value_) => value_.HasValue ? value_.Value.ToString().ToLowerInvariant() : "unknown";

    private static void AppendStrings(StringBuilder builder_, IReadOnlyList<string> values_)
    {
        if (values_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string value in values_)
        {
            builder_.AppendLine($"- {value}");
        }
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

    private static bool? AnyBool(JsonObject? root_, JsonArray results_, string name_)
    {
        bool? root = GetNullableBool(root_, name_);
        bool anyKnown = root.HasValue;
        bool anyTrue = root == true;
        foreach (JsonObject result in results_.OfType<JsonObject>())
        {
            bool? value = GetNullableBool(result, name_);
            anyKnown |= value.HasValue;
            anyTrue |= value == true;
        }

        return anyKnown ? anyTrue : null;
    }

    private static bool? GetNullableBool(JsonObject? value_, string name_)
    {
        if (value_ is null || !value_.TryGetPropertyValue(name_, out JsonNode? node) || node is null)
        {
            return null;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string Relative(string repoPath_, string path_)
    {
        return Path.GetRelativePath(Path.GetFullPath(repoPath_), Path.GetFullPath(path_)).Replace('\\', '/');
    }

    private sealed record SourceScan(IReadOnlyList<string> CSharpFiles, int CSharpFileCount, long Bytes);

    private sealed record FreshnessResult(bool Stale, IReadOnlyList<string> Reasons);

    private sealed record GeneratedContext(EfficiencyMetric Context, EfficiencyMetric McpBudget, EfficiencyMetric CodeIndexCache, string CodeIndexCacheHitRate, EfficiencySafetySummary Safety);
}

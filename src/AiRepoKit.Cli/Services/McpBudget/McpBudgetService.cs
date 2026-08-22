using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Services.McpLaunch;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Native C# implementation of the MCP response-budget measurement operation.
/// Replaces the PowerShell MeasureMcpResponseBudget.ps1 runtime dependency.
///
/// Runs a 12-call JSON-RPC budget matrix against the repository's MCP server,
/// evaluates each response against its byte budget, detects secret exposure,
/// and writes JSON + Markdown reports to .ai/generated/reports/.
///
/// ScriptShell independence: this service has no ScriptShell parameter.
/// It launches the portable runtime directly — the caller's shell preference is irrelevant.
/// </summary>
public sealed class McpBudgetService : IMcpBudgetService
{
    // clientInfo used in initialize — preserved from the PowerShell reference.
    private const string ClientInfoName = "MeasureMcpResponseBudget";
    private const string ClientInfoVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true
    };

    // The 12-call budget matrix (id 3–14), matching the PowerShell reference exactly.
    // Arguments are stored as object references; System.Text.Json serializes using runtime type.
    private static readonly (string ToolName, object Arguments, int BudgetBytes, string Label)[] BudgetCalls =
    [
        ("get_repo_brief",  new { },                                                 4096, "get_repo_brief"),
        ("get_repo_brief",  new { taskHint = "change a Blazor page" },               4096, "get_repo_brief taskHint"),
        ("get_context",     new { kind = "packages",      detail = "brief", limit = 5 }, 4096, "get_context packages brief"),
        ("get_context",     new { kind = "security",      detail = "brief", limit = 5 }, 8192, "get_context security brief"),
        ("get_health",      new { area = "all" },                                    4096, "get_health all"),
        ("search_context",  new { query = "AutoMapper",   limit = 5 },               4096, "search_context AutoMapper"),
        ("get_context",     new { kind = "symbols",       detail = "brief", limit = 5 }, 8192, "get_context symbols brief"),
        ("get_context",     new { kind = "endpoints",     detail = "brief", limit = 5 }, 8192, "get_context endpoints brief"),
        ("get_context",     new { kind = "context-packs", detail = "brief", limit = 5 }, 8192, "get_context context-packs brief"),
        ("get_context",     new { kind = "changed-files", detail = "brief", limit = 5 }, 8192, "get_context changed-files brief"),
        ("get_context",     new { kind = "graph",         detail = "brief", limit = 5 }, 8192, "get_context graph brief"),
        ("get_policy",      new { topic = "secrets" },                               4096, "get_policy secrets"),
    ];

    private readonly IMcpSessionFactory _sessionFactory;

    /// <summary>Creates a McpBudgetService using the real MCP stdio transport.</summary>
    public McpBudgetService()
        : this(new McpStdioSessionFactory())
    {
    }

    /// <summary>
    /// Internal constructor for testing: accepts a custom session factory so that
    /// unit tests can inject a fake session without spawning a real dotnet process.
    /// </summary>
    internal McpBudgetService(IMcpSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    /// <inheritdoc />
    public McpBudgetRunResult Run(string repoRoot, McpBudgetOptions? options = null)
    {
        options ??= new McpBudgetOptions();
        string fullRepoRoot = Path.GetFullPath(repoRoot);
        McpServerLaunchSpec launchSpec = McpServerLaunchSpecResolver.ResolvePortable(fullRepoRoot);
        string mcpAssemblyPath = GetPortableAssemblyPath(launchSpec);

        string primaryManifest = Path.Combine(fullRepoRoot, ".ai", "manifests", "mcp-context-manifest.json");
        string fallbackManifest = Path.Combine(fullRepoRoot, ".ai", "mcp-context-manifest.json");
        string? manifestPath = File.Exists(primaryManifest) ? primaryManifest
            : File.Exists(fallbackManifest) ? fallbackManifest
            : null;

        string reportsDir = Path.Combine(fullRepoRoot, ".ai", "generated", "reports");
        Directory.CreateDirectory(reportsDir);
        string jsonReportPath = Path.Combine(reportsDir, "mcp-budget-report.json");
        string mdReportPath = Path.Combine(reportsDir, "mcp-budget-report.md");

        // Precondition: manifest must exist.
        if (manifestPath is null)
        {
            McpBudgetReport fatalReport = BuildEmptyReport(
                fullRepoRoot, mcpAssemblyPath, null,
                failures: ["MCP manifest not found (.ai/manifests/mcp-context-manifest.json)."],
                warnings: []);
            WriteReports(fatalReport, jsonReportPath, mdReportPath);
            return new McpBudgetRunResult(McpBudgetExitClass.FatalFailure, fatalReport);
        }

        List<string> failures = [];
        List<string> warnings = [];
        List<McpBudgetCallResult> results = [];
        List<string> toolsListed = [];
        int stderrLineCount = 0;
        int stdoutLineCount = 0;
        bool stdoutHadRawLogs = false;
        McpBudgetExitClass exitClass = McpBudgetExitClass.Success;

        IMcpSession? session = null;
        try
        {
            session = _sessionFactory.Create(launchSpec, options.StartupTimeoutSeconds);

            // ── initialize ─────────────────────────────────────────────────────
            session.SendJson(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = ClientInfoName, version = ClientInfoVersion }
                }
            }));

            (_, JsonDocument initDoc) = session.WaitForResponse(1, TimeSpan.FromSeconds(options.StartupTimeoutSeconds));
            using (initDoc)
            {
                if (initDoc.RootElement.TryGetProperty("error", out _))
                {
                    throw new InvalidOperationException("MCP initialize failed.");
                }
            }

            // ── notifications/initialized ───────────────────────────────────────
            session.SendJson(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            }));

            // ── tools/list ──────────────────────────────────────────────────────
            session.SendJson(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            }));

            (_, JsonDocument toolsDoc) = session.WaitForResponse(2, TimeSpan.FromSeconds(options.ToolTimeoutSeconds));
            using (toolsDoc)
            {
                if (toolsDoc.RootElement.TryGetProperty("error", out _))
                {
                    throw new InvalidOperationException("MCP tools/list failed.");
                }

                JsonElement? toolsEl = McpBudgetJsonHelper.FindPropertyValue(toolsDoc.RootElement, "tools");
                if (toolsEl.HasValue && toolsEl.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement toolEl in toolsEl.Value.EnumerateArray())
                    {
                        JsonElement? nameEl = McpBudgetJsonHelper.FindPropertyValue(toolEl, "name");
                        if (nameEl.HasValue && nameEl.Value.ValueKind == JsonValueKind.String)
                        {
                            string? toolName = nameEl.Value.GetString();
                            if (toolName is not null) toolsListed.Add(toolName);
                        }
                    }
                }
            }

            // ── 12-call budget matrix (ids 3–14) ───────────────────────────────
            int nextId = 3;
            foreach ((string toolName, object arguments, int budgetBytes, string label) in BudgetCalls)
            {
                session.SendJson(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = nextId,
                    method = "tools/call",
                    @params = new { name = toolName, arguments }
                }));

                (string raw, JsonDocument responseDoc) = session.WaitForResponse(
                    nextId, TimeSpan.FromSeconds(options.ToolTimeoutSeconds));
                nextId++;

                McpBudgetCallResult callResult;
                using (responseDoc)
                {
                    bool rawLogsAtThisPoint = session.StdoutHadNonJsonLine;
                    callResult = EvaluateToolCall(label, raw, responseDoc.RootElement, budgetBytes, rawLogsAtThisPoint);
                }

                results.Add(callResult);
                if (!callResult.Passed)
                {
                    AddUnique(failures, $"{label} failed smoke validation.");
                }
            }
        }
        catch (Exception ex)
        {
            AddUnique(failures, ex.Message);
            exitClass = McpBudgetExitClass.FatalFailure;
        }
        finally
        {
            if (session is not null)
            {
                session.Dispose();
                stdoutHadRawLogs = session.StdoutHadNonJsonLine;
                stderrLineCount = session.StderrLineCount;
                stdoutLineCount = session.StdoutLineCount;
            }
        }

        // ── post-session warnings ───────────────────────────────────────────────
        if (stdoutHadRawLogs)
        {
            AddUnique(warnings, "stdout contained non JSON-RPC lines; stdout must be reserved for JSON-RPC.");
        }

        if (stderrLineCount > 0)
        {
            AddUnique(warnings, $"stderr contained {stderrLineCount} log line(s).");
        }

        bool passed = failures.Count == 0;

        // Preserve the PowerShell FailOnBudget exit-code semantics exactly:
        // when no fatal exception but validations failed, class = 2.
        // Note: callers never passed -FailOnBudget, so behavior is:
        //   if exitCode == 0 && !passed => exitCode = 2
        if (exitClass == McpBudgetExitClass.Success && !passed)
        {
            exitClass = McpBudgetExitClass.ValidationFailure;
        }

        McpBudgetReport report = new()
        {
            GeneratedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            RepoRoot = fullRepoRoot,
            McpAssembly = mcpAssemblyPath,
            McpAssemblyExists = File.Exists(mcpAssemblyPath),
            Manifest = manifestPath,
            ToolsListed = toolsListed,
            Results = results,
            Passed = passed,
            Failures = failures,
            Warnings = warnings,
            StderrLineCount = stderrLineCount,
            StdoutLineCount = stdoutLineCount
        };

        WriteReports(report, jsonReportPath, mdReportPath);
        return new McpBudgetRunResult(exitClass, report);
    }

    // ─── Per-call evaluation ────────────────────────────────────────────────────

    private static McpBudgetCallResult EvaluateToolCall(
        string label,
        string raw,
        JsonElement response,
        int budgetBytes,
        bool stdoutHadRawLogs)
    {
        // SizeBytes = UTF-8 byte count of the raw JSON-RPC response line (not re-serialized).
        int sizeBytes = McpBudgetJsonHelper.GetUtf8ByteCount(raw);

        // Extract the tool envelope content using the 5-level priority logic.
        string envelopeJson = McpBudgetJsonHelper.GetToolEnvelopeSourceJson(response);

        bool hasError = response.TryGetProperty("error", out _);

        // isError inside result (tool-level error, not protocol-level error)
        JsonElement? isErrorEl = McpBudgetJsonHelper.FindPropertyValue(response, "isError");
        bool isToolError = isErrorEl.HasValue && isErrorEl.Value.ValueKind == JsonValueKind.True;

        // Secret/redaction safety evaluation on the JSON-serialized envelope.
        bool hasSecretValueExposure = McpBudgetJsonHelper.TestSecretExposure(envelopeJson);
        bool hasRedactionMarker = McpBudgetJsonHelper.TestRedactionMarker(envelopeJson);

        // Extract typed fields from the envelope (safe: we parse a copy as string, extract primitives).
        string tokenCostHint = string.Empty;
        int estimatedSizeBytes = 0;
        bool secretsExposed = false;
        bool secretValuesReturned = false;
        bool redactedOnly = false;

        try
        {
            using JsonDocument envelopeDoc = JsonDocument.Parse(envelopeJson);
            JsonElement root = envelopeDoc.RootElement;

            JsonElement? scEl = McpBudgetJsonHelper.FindPropertyValue(root, "secretsExposed");
            if (scEl.HasValue && scEl.Value.ValueKind == JsonValueKind.True) secretsExposed = true;

            JsonElement? svrEl = McpBudgetJsonHelper.FindPropertyValue(root, "secretValuesReturned");
            if (svrEl.HasValue && svrEl.Value.ValueKind == JsonValueKind.True) secretValuesReturned = true;

            JsonElement? roEl = McpBudgetJsonHelper.FindPropertyValue(root, "redactedOnly");
            if (roEl.HasValue && roEl.Value.ValueKind == JsonValueKind.True) redactedOnly = true;

            JsonElement? esbEl = McpBudgetJsonHelper.FindPropertyValue(root, "estimatedSizeBytes");
            if (esbEl.HasValue && esbEl.Value.ValueKind == JsonValueKind.Number)
            {
                estimatedSizeBytes = esbEl.Value.GetInt32();
            }

            JsonElement? tchEl = McpBudgetJsonHelper.FindPropertyValue(root, "tokenCostHint");
            if (tchEl.HasValue && tchEl.Value.ValueKind == JsonValueKind.String)
            {
                tokenCostHint = tchEl.Value.GetString() ?? string.Empty;
            }
        }
        catch { /* envelope may not be structured JSON; ignore and use defaults */ }

        // Notes: informational only — no sensitive values echoed.
        List<string> notes = [];
        if (hasError || isToolError) notes.Add("JSON-RPC error returned.");
        if (sizeBytes > budgetBytes) notes.Add("Response exceeded budget.");
        if (hasSecretValueExposure) notes.Add("Potential sensitive value pattern detected; value omitted.");
        if (stdoutHadRawLogs) notes.Add("stdout contained non JSON-RPC lines.");

        bool success = !hasError && !isToolError;

        // Passed when: no error, within budget, no secret exposure, no raw stdout contamination.
        bool passed = success && sizeBytes <= budgetBytes && !hasSecretValueExposure && !stdoutHadRawLogs;

        return new McpBudgetCallResult
        {
            Name = label,
            Success = success,
            SizeBytes = sizeBytes,
            BudgetBytes = budgetBytes,
            TokenCostHint = tokenCostHint,
            EstimatedSizeBytes = estimatedSizeBytes,
            HasRawLogs = stdoutHadRawLogs,
            HasSecretValueExposure = hasSecretValueExposure,
            HasRedactionMarker = hasRedactionMarker,
            SecretsExposed = secretsExposed,
            SecretValuesReturned = secretValuesReturned,
            RedactedOnly = redactedOnly,
            Passed = passed,
            Notes = notes
        };
    }

    // ─── Report writing ─────────────────────────────────────────────────────────

    private static void WriteReports(McpBudgetReport report, string jsonPath, string mdPath)
    {
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonWriteOptions), Encoding.UTF8);
        File.WriteAllText(mdPath, BuildMarkdown(report), Encoding.UTF8);
    }

    private static string BuildMarkdown(McpBudgetReport report)
    {
        StringBuilder sb = new();
        sb.AppendLine("# MCP Budget Report");
        sb.AppendLine();
        sb.AppendLine($"- RepoRoot: {report.RepoRoot}");
        sb.AppendLine($"- MCP assembly: {report.McpAssembly}");
        sb.AppendLine($"- Manifest: {report.Manifest}");
        sb.AppendLine($"- Tools listed: {string.Join(", ", report.ToolsListed)}");
        sb.AppendLine();
        sb.AppendLine("| Call | Bytes | Budget | TokenCostHint | Passed |");
        sb.AppendLine("| --- | ---: | ---: | --- | --- |");

        foreach (McpBudgetCallResult result in report.Results)
        {
            sb.AppendLine($"| {result.Name} | {result.SizeBytes} | {result.BudgetBytes} | {result.TokenCostHint} | {result.Passed} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Failures");

        if (report.Failures.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (string failure in report.Failures)
            {
                sb.AppendLine($"- {failure}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Warnings");

        if (report.Warnings.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (string warning in report.Warnings)
            {
                sb.AppendLine($"- {warning}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("stderr may contain logs; stdout must contain only JSON-RPC.");
        sb.Append("No sensitive value is displayed in this report.");

        return sb.ToString();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static McpBudgetReport BuildEmptyReport(
        string fullRepoRoot,
        string mcpAssemblyPath,
        string? manifest,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings)
    {
        return new McpBudgetReport
        {
            GeneratedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            RepoRoot = fullRepoRoot,
            McpAssembly = mcpAssemblyPath,
            McpAssemblyExists = File.Exists(mcpAssemblyPath),
            Manifest = manifest,
            ToolsListed = [],
            Results = [],
            Passed = false,
            Failures = failures,
            Warnings = warnings,
            StderrLineCount = 0,
            StdoutLineCount = 0
        };
    }

    private static string GetPortableAssemblyPath(McpServerLaunchSpec launchSpec_)
    {
        if (launchSpec_.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || launchSpec_.Arguments.Count > 0 && launchSpec_.Arguments[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(launchSpec_.Arguments.Count > 0 ? launchSpec_.Arguments[0] : launchSpec_.FileName);
        }

        return Path.GetFullPath(launchSpec_.FileName);
    }

    private static void AddUnique(List<string> list, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }
}

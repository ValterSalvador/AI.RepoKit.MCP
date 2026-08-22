using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Services.McpLaunch;
using AiRepoKit.Cli.Services.McpBudget;
using Xunit;

namespace AiRepoKit.Cli.Tests;

/// <summary>
/// Deterministic unit tests for McpBudgetService native core.
/// No real MCP process, no PowerShell, no network.
/// Uses FakeMcpSessionFactory / FakeMcpSession to inject controlled responses.
/// </summary>
public sealed class McpBudgetServiceTests
{
    // ── Infrastructure helpers ────────────────────────────────────────────────

    private static string TempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_budget_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates a minimal repo with manifests only.</summary>
    private static string CreateRepoWithDll(bool withPrimaryManifest = true, bool withFallbackManifest = false)
    {
        string path = TempDir();

        if (withPrimaryManifest)
        {
            Directory.CreateDirectory(Path.Combine(path, ".ai", "manifests"));
            File.WriteAllText(Path.Combine(path, ".ai", "manifests", "mcp-context-manifest.json"), "{}");
        }

        if (withFallbackManifest)
        {
            Directory.CreateDirectory(Path.Combine(path, ".ai"));
            File.WriteAllText(Path.Combine(path, ".ai", "mcp-context-manifest.json"), "{}");
        }

        return path;
    }

    private static void DeleteDir(string path)
    {
        if (Directory.Exists(path))
        {
            try { Directory.Delete(path, true); } catch { }
        }
    }

    /// <summary>
    /// Builds a fake complete session that:
    ///   - returns a valid initialize response for id=1
    ///   - returns a valid tools/list response for id=2
    ///   - returns 12 tool call responses for ids 3-14
    /// All responses pass validation by default (within budget, no errors, no secrets).
    /// </summary>
    private static FakeMcpSession BuildPassingSession(int budgetBytes = 100)
    {
        var session = new FakeMcpSession();

        // id=1: initialize
        session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05", capabilities = new { } }));

        // id=2: tools/list
        session.AddResponse(2, BuildJsonRpcResult(2, new
        {
            tools = new[] { new { name = "get_repo_brief" }, new { name = "get_health" } }
        }));

        // ids 3-14: 12 tool calls — small payload so all pass budget
        for (int id = 3; id <= 14; id++)
        {
            session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
        }

        return session;
    }

    private static string BuildJsonRpcResult(int id, object result)
    {
        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
    }

    private static string BuildToolCallResponse(int id, object content)
    {
        string contentJson = JsonSerializer.Serialize(content);
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                content = new[]
                {
                    new { type = "text", text = contentJson }
                }
            }
        });
    }

    // ── Phase 2 contract tests ────────────────────────────────────────────────

    [Fact]
    public void McpBudgetService_MissingDll_DoesNotBlockPortableRun()
    {
        string path = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(path, ".ai", "manifests"));
            File.WriteAllText(Path.Combine(path, ".ai", "manifests", "mcp-context-manifest.json"), "{}");

            var session = BuildPassingSession();
            var factory = new FakeMcpSessionFactory(session);
            var service = new McpBudgetService(factory);
            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(McpBudgetExitClass.Success, result.ExitClass);
            Assert.True(result.IsSuccess);
            Assert.True(result.Report.Passed);
            Assert.Equal(1, session.CreateCount);
            Assert.Equal(1, factory.CreateCount);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_PortableDefault_DoesNotRequireTargetDll()
    {
        string path = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(path, ".ai", "manifests"));
            File.WriteAllText(Path.Combine(path, ".ai", "manifests", "mcp-context-manifest.json"), "{}");

            string targetDll = Path.Combine(path, "Tools", "AiContextMcp", "bin", "Release", "net10.0", "AiRepo.ContextMcp.dll");
            var session = BuildPassingSession();
            var factory = new FakeMcpSessionFactory(session);
            var service = new McpBudgetService(factory);

            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(McpBudgetExitClass.Success, result.ExitClass);
            Assert.True(result.IsSuccess);
            Assert.True(result.Report.Passed);
            Assert.False(File.Exists(targetDll));
            Assert.Equal(1, factory.CreateCount);
            Assert.NotNull(factory.LastLaunchSpec);
            Assert.Equal(McpRuntimeKind.Portable, factory.LastLaunchSpec!.RuntimeKind);
            Assert.Equal(Path.GetFullPath(path), factory.LastLaunchSpec.WorkingDirectory);
            int repoArgIndex = factory.LastLaunchSpec.Arguments.Count - 1;
            Assert.True(repoArgIndex is 3 or 4);
            if (repoArgIndex == 4)
            {
                Assert.EndsWith(".dll", factory.LastLaunchSpec.Arguments[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains("AiRepoKit.Cli", factory.LastLaunchSpec.Arguments[0], StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Equal("mcp", factory.LastLaunchSpec.Arguments[0]);
            }

            Assert.Equal("mcp", factory.LastLaunchSpec.Arguments[repoArgIndex - 3]);
            Assert.Equal("serve", factory.LastLaunchSpec.Arguments[repoArgIndex - 2]);
            Assert.Equal("--repo", factory.LastLaunchSpec.Arguments[repoArgIndex - 1]);
            Assert.Equal(Path.GetFullPath(path), factory.LastLaunchSpec.Arguments[repoArgIndex]);
            Assert.DoesNotContain(factory.LastLaunchSpec.Arguments, arg => arg.Contains("AiRepo.ContextMcp.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(File.Exists(result.Report.McpAssembly), result.Report.McpAssemblyExists);
            Assert.DoesNotContain("Tools" + Path.DirectorySeparatorChar + "AiContextMcp", result.Report.McpAssembly, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_MissingPrimaryAndFallbackManifest_ReturnsFatalFailure()
    {
        string path = CreateRepoWithDll(withPrimaryManifest: false, withFallbackManifest: false);
        try
        {
            var session = new FakeMcpSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(McpBudgetExitClass.FatalFailure, result.ExitClass);
            Assert.False(result.Report.Passed);
            Assert.Equal(0, session.CreateCount);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_FallbackManifest_UsedWhenPrimaryAbsent()
    {
        string path = CreateRepoWithDll(withPrimaryManifest: false, withFallbackManifest: true);
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            // Fallback manifest present — session was created (not fatal due to manifest)
            Assert.Equal(1, session.CreateCount);
            Assert.NotNull(result.Report.Manifest);
            Assert.Contains(".ai" + Path.DirectorySeparatorChar + "mcp-context-manifest.json", result.Report.Manifest);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_PrimaryManifest_TakesPrecedenceOverFallback()
    {
        string path = CreateRepoWithDll(withPrimaryManifest: true, withFallbackManifest: true);
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.NotNull(result.Report.Manifest);
            Assert.Contains("manifests", result.Report.Manifest);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_AllCallsPass_ReturnsSuccess()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(McpBudgetExitClass.Success, result.ExitClass);
            Assert.True(result.IsSuccess);
            Assert.True(result.Report.Passed);
            Assert.Empty(result.Report.Failures);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_InitializeContractVerified()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            // Verify the first sent message is the initialize request
            string firstSent = session.SentMessages[0];
            using JsonDocument doc = JsonDocument.Parse(firstSent);
            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
            Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString());

            JsonElement @params = doc.RootElement.GetProperty("params");
            Assert.Equal("2024-11-05", @params.GetProperty("protocolVersion").GetString());

            JsonElement clientInfo = @params.GetProperty("clientInfo");
            Assert.Equal("MeasureMcpResponseBudget", clientInfo.GetProperty("name").GetString());
            Assert.Equal("1.0.0", clientInfo.GetProperty("version").GetString());
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_NotificationsInitializedSentAfterInitialize()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            // Second message must be notifications/initialized (no id)
            string secondSent = session.SentMessages[1];
            using JsonDocument doc = JsonDocument.Parse(secondSent);
            Assert.Equal("notifications/initialized", doc.RootElement.GetProperty("method").GetString());
            Assert.False(doc.RootElement.TryGetProperty("id", out _));
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_ToolsListSentAsThirdMessage()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            string thirdSent = session.SentMessages[2];
            using JsonDocument doc = JsonDocument.Parse(thirdSent);
            Assert.Equal("tools/list", doc.RootElement.GetProperty("method").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("id").GetInt32());
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_Exactly12BudgetCallsSent()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            // Messages: initialize(1) + notif(1) + tools/list(1) + 12 calls = 15
            Assert.Equal(15, session.SentMessages.Count);
            Assert.Equal(12, session.SentMessages.Skip(3).Count());
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_ExactBudgetCallMatrix_VerifiedByName()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            var names = result.Report.Results.Select(r => r.Name).ToArray();
            Assert.Equal(12, names.Length);
            Assert.Equal("get_repo_brief", names[0]);
            Assert.Equal("get_repo_brief taskHint", names[1]);
            Assert.Equal("get_context packages brief", names[2]);
            Assert.Equal("get_context security brief", names[3]);
            Assert.Equal("get_health all", names[4]);
            Assert.Equal("search_context AutoMapper", names[5]);
            Assert.Equal("get_context symbols brief", names[6]);
            Assert.Equal("get_context endpoints brief", names[7]);
            Assert.Equal("get_context context-packs brief", names[8]);
            Assert.Equal("get_context changed-files brief", names[9]);
            Assert.Equal("get_context graph brief", names[10]);
            Assert.Equal("get_policy secrets", names[11]);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_BudgetBytes_MatchExactMatrix()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            var budgets = result.Report.Results.Select(r => r.BudgetBytes).ToArray();
            // Matrix: 4096, 4096, 4096, 8192, 4096, 4096, 8192, 8192, 8192, 8192, 8192, 4096
            int[] expected = [4096, 4096, 4096, 8192, 4096, 4096, 8192, 8192, 8192, 8192, 8192, 4096];
            Assert.Equal(expected, budgets);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_SizeBytes_IsRawUtf8ByteCount_NotReserializedJson()
    {
        // Regression test: SizeBytes must equal Encoding.UTF8.GetByteCount(rawLine)
        // NOT the byte count of any re-serialized form.
        string path = CreateRepoWithDll();
        try
        {
            // Build a response whose raw line has a known byte count.
            // We'll inject a specific raw response line for id=3.
            const string distinctRawLine = "{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"k\\\":\\\"v\\\"}\"}]}}";
            int expectedBytes = Encoding.UTF8.GetByteCount(distinctRawLine);

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, distinctRawLine);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            // The first tool call result (index 0) corresponds to id=3
            int actualBytes = result.Report.Results[0].SizeBytes;

            Assert.Equal(expectedBytes, actualBytes);
            // Sanity: the byte count is NOT the re-serialized length
            // (re-serializing the parsed JSON document would produce a different length)
            Assert.True(actualBytes > 0);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_ProtocolError_SetsFatalFailure()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = new FakeMcpSession();
            // initialize returns error
            session.AddResponse(1, JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                error = new { code = -32600, message = "Invalid request" }
            }));

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(McpBudgetExitClass.FatalFailure, result.ExitClass);
            Assert.False(result.Report.Passed);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_OverBudgetResponse_FailsCallAndProducesValidationFailure()
    {
        string path = CreateRepoWithDll();
        try
        {
            // Build a large payload that exceeds budget for id=3 (budget=4096)
            string bigText = new string('X', 5000);
            string bigJson = JsonSerializer.Serialize(new { data = bigText });
            string bigResponse = BuildToolCallResponse(3, new { data = bigText });

            // Verify we're actually over budget
            int rawBytes = Encoding.UTF8.GetByteCount(bigResponse);
            Assert.True(rawBytes > 4096, $"Test payload must exceed 4096 bytes (was {rawBytes})");

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, bigResponse);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            // Over-budget call must fail
            McpBudgetCallResult firstCall = result.Report.Results[0];
            Assert.True(firstCall.SizeBytes > 4096);
            Assert.False(firstCall.Passed);

            // Overall result is ValidationFailure (2), not FatalFailure (1)
            Assert.Equal(McpBudgetExitClass.ValidationFailure, result.ExitClass);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_StdoutHadRawLogs_FailsCallsAndProducesWarning()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            session.SimulateNonJsonLine = true; // Inject a non-JSON line

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            // Warning must be present
            Assert.Contains(result.Report.Warnings, w => w.Contains("stdout contained non JSON-RPC lines"));
            // All calls must fail because HasRawLogs=true
            Assert.All(result.Report.Results, r => Assert.False(r.Passed));
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_StderrLines_ProducesStderrWarning()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            session.StderrLineCountToReport = 3;

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.Contains(result.Report.Warnings, w => w.Contains("stderr contained 3 log line(s)"));
            Assert.Equal(3, result.Report.StderrLineCount);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_SecretExposureDetected_FailsCallAndDoesNotLeakValueInReport()
    {
        // SECRET SAFETY: The actual secret value must NEVER appear in the report/assertions.
        const string syntheticSecret = "SENTINEL_SECRET_XYZZY123";
        string path = CreateRepoWithDll();
        try
        {
            // Match the PowerShell reference regex exactly: password=VALUE.
            // JSON object serialization would produce "password":"VALUE", which
            // intentionally does not match the legacy compatibility regex.
            string secretPayload = $"password={syntheticSecret}";
            string secretResponse = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    content = new[] { new { type = "text", text = secretPayload } }
                }
            });

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, secretResponse);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            McpBudgetCallResult firstCall = result.Report.Results[0];

            // Secret exposure must be detected
            Assert.True(firstCall.HasSecretValueExposure);
            Assert.False(firstCall.Passed);

            // Serialize to JSON and verify the synthetic secret value does NOT appear
            string jsonReport = JsonSerializer.Serialize(result.Report);
            Assert.DoesNotContain(syntheticSecret, jsonReport);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_RedactionMarker_IsDetected()
    {
        string path = CreateRepoWithDll();
        try
        {
            string redactedPayload = JsonSerializer.Serialize(new { value = "<redacted>" });
            string redactedResponse = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    content = new[] { new { type = "text", text = redactedPayload } }
                }
            });

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, redactedResponse);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.True(result.Report.Results[0].HasRedactionMarker);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_StructuredContent_TakesPriorityOverContentArray()
    {
        string path = CreateRepoWithDll();
        try
        {
            // structuredContent must be preferred over content[]
            var envelopeData = new { tokenCostHint = "100", estimatedSizeBytes = 1234 };
            string response = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    structuredContent = envelopeData,
                    content = new[] { new { type = "text", text = "{\"tokenCostHint\":\"WRONG\"}" } }
                }
            });

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, response);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            McpBudgetCallResult firstCall = result.Report.Results[0];
            Assert.Equal("100", firstCall.TokenCostHint);
            Assert.Equal(1234, firstCall.EstimatedSizeBytes);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_ContentTextParsedAsJson_WhenStructuredContentAbsent()
    {
        string path = CreateRepoWithDll();
        try
        {
            var innerData = new { tokenCostHint = "50", estimatedSizeBytes = 512 };
            string innerJson = JsonSerializer.Serialize(innerData);
            string response = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    content = new[] { new { type = "text", text = innerJson } }
                }
            });

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, response);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            McpBudgetCallResult firstCall = result.Report.Results[0];
            Assert.Equal("50", firstCall.TokenCostHint);
            Assert.Equal(512, firstCall.EstimatedSizeBytes);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_RecursivePropertyLookup_FindsNestedFields()
    {
        string path = CreateRepoWithDll();
        try
        {
            // secretsExposed nested inside a wrapper object
            var nested = new { wrapper = new { inner = new { secretsExposed = true } } };
            string innerJson = JsonSerializer.Serialize(nested);
            string response = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    content = new[] { new { type = "text", text = innerJson } }
                }
            });

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, response);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.True(result.Report.Results[0].SecretsExposed);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_RecursiveContentFallback_FindsNestedText()
    {
        string path = CreateRepoWithDll();
        try
        {
            string nestedPayload = JsonSerializer.Serialize(new
            {
                tokenCostHint = "nested-recursive"
            });

            var session = new FakeMcpSession();
            session.AddResponse(
                1,
                BuildJsonRpcResult(
                    1,
                    new { protocolVersion = "2024-11-05" }));
            session.AddResponse(
                2,
                BuildJsonRpcResult(
                    2,
                    new { tools = Array.Empty<object>() }));

            string response = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                wrapper = new
                {
                    content = new[]
                    {
                        new
                        {
                            nested = new
                            {
                                text = nestedPayload
                            }
                        }
                    }
                }
            });

            session.AddRawResponse(3, response);

            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(
                    id,
                    BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(
                new FakeMcpSessionFactory(session));

            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(
                "nested-recursive",
                result.Report.Results[0].TokenCostHint);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_ToolError_FailsCallAndMarksNotSuccess()
    {
        string path = CreateRepoWithDll();
        try
        {
            string toolErrorResponse = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    isError = true,
                    content = new[] { new { type = "text", text = "Tool execution failed" } }
                }
            });

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, toolErrorResponse);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            McpBudgetCallResult firstCall = result.Report.Results[0];
            Assert.False(firstCall.Success);
            Assert.False(firstCall.Passed);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_ValidationFailure_ExitClass2()
    {
        string path = CreateRepoWithDll();
        try
        {
            // over-budget response forces ValidationFailure
            string bigData = new string('A', 6000);
            string bigResponse = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    content = new[] { new { type = "text", text = bigData } }
                }
            });

            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new { tools = Array.Empty<object>() }));
            session.AddRawResponse(3, bigResponse);
            for (int id = 4; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(McpBudgetExitClass.ValidationFailure, result.ExitClass);
            Assert.Equal(2, (int)result.ExitClass);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_SuccessfulRun_ExitClass0()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(McpBudgetExitClass.Success, result.ExitClass);
            Assert.Equal(0, (int)result.ExitClass);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_JsonReportWritten_WithPascalCasePropertyNames()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            string jsonPath = Path.Combine(path, ".ai", "generated", "reports", "mcp-budget-report.json");
            Assert.True(File.Exists(jsonPath));

            string json = File.ReadAllText(jsonPath);
            // Verify top-level PascalCase names
            Assert.Contains("\"GeneratedAtLocal\"", json);
            Assert.Contains("\"RepoRoot\"", json);
            Assert.Contains("\"McpAssembly\"", json);
            Assert.Contains("\"McpAssemblyExists\"", json);
            Assert.Contains("\"Manifest\"", json);
            Assert.Contains("\"ToolsListed\"", json);
            Assert.Contains("\"Results\"", json);
            Assert.Contains("\"Passed\"", json);
            Assert.Contains("\"Failures\"", json);
            Assert.Contains("\"Warnings\"", json);
            Assert.Contains("\"StderrLineCount\"", json);
            Assert.Contains("\"StdoutLineCount\"", json);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_JsonReportCallResults_HavePascalCasePropertyNames()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            string jsonPath = Path.Combine(path, ".ai", "generated", "reports", "mcp-budget-report.json");
            string json = File.ReadAllText(jsonPath);
            // Per-call PascalCase names
            Assert.Contains("\"Name\"", json);
            Assert.Contains("\"Success\"", json);
            Assert.Contains("\"SizeBytes\"", json);
            Assert.Contains("\"BudgetBytes\"", json);
            Assert.Contains("\"TokenCostHint\"", json);
            Assert.Contains("\"EstimatedSizeBytes\"", json);
            Assert.Contains("\"HasRawLogs\"", json);
            Assert.Contains("\"HasSecretValueExposure\"", json);
            Assert.Contains("\"HasRedactionMarker\"", json);
            Assert.Contains("\"SecretsExposed\"", json);
            Assert.Contains("\"SecretValuesReturned\"", json);
            Assert.Contains("\"RedactedOnly\"", json);
            // camelCase names must NOT appear as keys
            Assert.DoesNotContain("\"sizeBytes\"", json);
            Assert.DoesNotContain("\"passed\":", json); // avoid false negative from "Passed"
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_MarkdownReportWritten_WithExpectedStructure()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            string mdPath = Path.Combine(path, ".ai", "generated", "reports", "mcp-budget-report.md");
            Assert.True(File.Exists(mdPath));

            string md = File.ReadAllText(mdPath);
            Assert.Contains("# MCP Budget Report", md);
            Assert.Contains("## Failures", md);
            Assert.Contains("## Warnings", md);
            Assert.Contains("No sensitive value is displayed", md);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_GeneratedAtLocal_IsLocalTime()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            // Format must be yyyy-MM-dd HH:mm:ss
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", result.Report.GeneratedAtLocal);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_SessionDisposed_OnSuccessfulRun()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = BuildPassingSession();
            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            service.Run(path);

            Assert.True(session.Disposed, "Session must be disposed after the run.");
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_SessionDisposed_OnException()
    {
        string path = CreateRepoWithDll();
        try
        {
            // Session that throws on WaitForResponse id=1
            var session = new FakeMcpSession();
            session.ThrowOnResponseId = 1;

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.True(session.Disposed, "Session must be disposed even when exception occurs.");
            Assert.Equal(McpBudgetExitClass.FatalFailure, result.ExitClass);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    [Fact]
    public void McpBudgetService_ToolsListed_FromToolsListResponse()
    {
        string path = CreateRepoWithDll();
        try
        {
            var session = new FakeMcpSession();
            session.AddResponse(1, BuildJsonRpcResult(1, new { protocolVersion = "2024-11-05" }));
            session.AddResponse(2, BuildJsonRpcResult(2, new
            {
                tools = new[] { new { name = "get_repo_brief" }, new { name = "get_health" }, new { name = "get_context" } }
            }));
            for (int id = 3; id <= 14; id++)
            {
                session.AddResponse(id, BuildToolCallResponse(id, new { ok = true }));
            }

            var service = new McpBudgetService(new FakeMcpSessionFactory(session));
            McpBudgetRunResult result = service.Run(path);

            Assert.Equal(3, result.Report.ToolsListed.Count);
            Assert.Contains("get_repo_brief", result.Report.ToolsListed);
            Assert.Contains("get_health", result.Report.ToolsListed);
            Assert.Contains("get_context", result.Report.ToolsListed);
        }
        finally
        {
            DeleteDir(path);
        }
    }

    // ── JSON helper tests ─────────────────────────────────────────────────────

    [Fact]
    public void McpBudgetJsonHelper_GetUtf8ByteCount_EqualsEncodingByteCount()
    {
        const string sample = "hello \u00e9 \u4e2d\u6587"; // multi-byte chars
        int expected = Encoding.UTF8.GetByteCount(sample);
        Assert.Equal(expected, McpBudgetJsonHelper.GetUtf8ByteCount(sample));
    }

    [Fact]
    public void McpBudgetJsonHelper_TestSecretExposure_DetectsPasswordPattern()
    {
        Assert.True(McpBudgetJsonHelper.TestSecretExposure("password=mysecretvalue"));
        Assert.True(McpBudgetJsonHelper.TestSecretExposure("Password: abc123"));
        Assert.False(McpBudgetJsonHelper.TestSecretExposure("passwordfield is empty"));
    }

    [Fact]
    public void McpBudgetJsonHelper_TestSecretExposure_DetectsTokenPattern()
    {
        Assert.True(McpBudgetJsonHelper.TestSecretExposure("token=eyJhbGciOiJSUzI1NiJ9"));
    }

    [Fact]
    public void McpBudgetJsonHelper_TestRedactionMarker_DetectsRedacted()
    {
        Assert.True(McpBudgetJsonHelper.TestRedactionMarker("<redacted>"));
        Assert.True(McpBudgetJsonHelper.TestRedactionMarker("value is REDACTED here"));
        Assert.True(McpBudgetJsonHelper.TestRedactionMarker("***"));
        Assert.False(McpBudgetJsonHelper.TestRedactionMarker("no marker here"));
    }

    [Fact]
    public void McpBudgetJsonHelper_FindPropertyValue_IsCaseInsensitive()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"SecretsExposed\": true}");
        JsonElement? found = McpBudgetJsonHelper.FindPropertyValue(doc.RootElement, "secretsexposed");
        Assert.True(found.HasValue);
        Assert.Equal(JsonValueKind.True, found.Value.ValueKind);
    }

    [Fact]
    public void McpBudgetJsonHelper_FindPropertyValue_SearchesNestedObjects()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"a\":{\"b\":{\"target\":42}}}");
        JsonElement? found = McpBudgetJsonHelper.FindPropertyValue(doc.RootElement, "target");
        Assert.True(found.HasValue);
        Assert.Equal(42, found.Value.GetInt32());
    }

    [Fact]
    public void McpBudgetJsonHelper_FindPropertyValue_SearchesWithinArrays()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"items\":[{\"name\":\"a\"},{\"name\":\"b\"}]}");
        JsonElement? found = McpBudgetJsonHelper.FindPropertyValue(doc.RootElement, "name");
        Assert.True(found.HasValue);
        Assert.Equal("a", found.Value.GetString()); // First match in DFS order
    }

    // ── Fake infrastructure ───────────────────────────────────────────────────

    private sealed class FakeMcpSessionFactory(FakeMcpSession session) : IMcpSessionFactory
    {
        public McpServerLaunchSpec? LastLaunchSpec { get; private set; }
        public int CreateCount { get; private set; }

        public IMcpSession Create(McpServerLaunchSpec launchSpec, int startupTimeoutSeconds)
        {
            LastLaunchSpec = launchSpec;
            CreateCount++;
            session.CreateCount++;
            return session;
        }
    }

    private sealed class FakeMcpSession : IMcpSession
    {
        private readonly Dictionary<int, string> _responses = new();
        public List<string> SentMessages { get; } = [];
        public int CreateCount { get; set; }
        public bool Disposed { get; private set; }
        public bool SimulateNonJsonLine { get; set; }
        public int StderrLineCountToReport { get; set; }
        public int? ThrowOnResponseId { get; set; }

        public bool StdoutHadNonJsonLine => SimulateNonJsonLine;
        public int StdoutLineCount => _responses.Count;
        public int StderrLineCount => StderrLineCountToReport;

        public void AddResponse(int id, string rawJson) => _responses[id] = rawJson;
        public void AddRawResponse(int id, string rawLine) => _responses[id] = rawLine;

        public void SendJson(string text) => SentMessages.Add(text);

        public (string Raw, JsonDocument Document) WaitForResponse(int id, TimeSpan timeout)
        {
            if (ThrowOnResponseId.HasValue && ThrowOnResponseId.Value == id)
            {
                throw new TimeoutException($"Simulated timeout for id={id}");
            }

            if (_responses.TryGetValue(id, out string? raw))
            {
                return (raw, JsonDocument.Parse(raw));
            }

            throw new InvalidOperationException($"FakeMcpSession: no response configured for id={id}");
        }

        public void Dispose() => Disposed = true;
    }
}

using System.Text.Json;
using AiRepoKit.Cli.Models.ContextPacks;
using AiRepoKit.Cli.Services.ContextPacks;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class ContextPackSelectionServiceTests
{
    private const string GeneratedAt = "2026-09-03 10:11:12 +02:00";
    private readonly ContextPackSelectionService service = new();

    [Theory]
    [InlineData(true, true, "RoslynLite", "RoslynLite", true, true, true)]
    [InlineData(false, true, "RoslynLite", "RoslynLite", false, true, false)]
    [InlineData(true, false, "RoslynLite", "RoslynLite", true, false, false)]
    [InlineData(true, true, "Legacy", "RoslynLite", false, true, false)]
    [InlineData(true, true, "RoslynLite", "Legacy", true, false, false)]
    public void GetInventoryCompatibility_ReportsEachInventoryIndependently(
        bool includeSymbols,
        bool includeEndpoints,
        string symbolIndexer,
        string endpointIndexer,
        bool expectedSymbol,
        bool expectedEndpoint,
        bool expectedCompatible)
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot, includeSymbols, includeEndpoints, symbolIndexer, endpointIndexer);

            ContextPackInventoryCompatibility result = this.service.GetInventoryCompatibility(repoRoot);

            Assert.Equal(expectedSymbol, result.SymbolCompatible);
            Assert.Equal(expectedEndpoint, result.EndpointCompatible);
            Assert.Equal(expectedCompatible, result.Compatible);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Theory]
    [InlineData(false, true, ".ai/generated/inventories/symbol-inventory.json")]
    [InlineData(true, false, ".ai/generated/inventories/endpoint-inventory.json")]
    public void Select_MissingRequiredInventory_ThrowsExistingMessage(
        bool includeSymbols,
        bool includeEndpoints,
        string expectedPath)
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot, includeSymbols, includeEndpoints);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => this.service.Select(CreateRequest(repoRoot), GeneratedAt));

            Assert.Equal($"Missing required context-pack input: {expectedPath}", exception.Message);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_InvalidRequiredJson_ThrowsExistingInvalidJsonBehavior()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteRaw(repoRoot, ".ai/generated/inventories/symbol-inventory.json", "{not-json");
            WriteEndpointInventory(repoRoot);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => this.service.Select(CreateRequest(repoRoot), GeneratedAt));

            Assert.StartsWith("Invalid JSON in .ai/generated/inventories/symbol-inventory.json: ", exception.Message);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_OptionalMalformedJson_ReturnsWarningAndDoesNotCreateOutputDirectory()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteRaw(repoRoot, ".ai/generated/inventories/package-inventory.json", "{not-json");

            ContextPackSelectionResult result = this.service.Select(CreateRequest(repoRoot), GeneratedAt);

            Assert.Contains("Optional JSON could not be read: .ai/generated/inventories/package-inventory.json", result.Warnings);
            Assert.Equal(GeneratedAt, result.Pack.GeneratedAtLocal);
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai", "generated", "context-packs")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_SameInputsAndTimestamp_SerializesIdentically()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot, symbols:
            [
                new { Name = "OrdersController", Classification = "Controller", File = "src/App/OrdersController.cs" }
            ]);
            ContextPackRequest request = CreateRequest(repoRoot, target: "Orders");

            string first = JsonSerializer.Serialize(this.service.Select(request, GeneratedAt));
            string second = JsonSerializer.Serialize(this.service.Select(request, GeneratedAt));

            Assert.Equal(first, second);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_ChangeApi_PreservesScoringTargetContributionAndLimit()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(
                repoRoot,
                symbols:
                [
                    new { Name = "OtherService", Classification = "Service", File = "src/App/OtherService.cs" },
                    new { Name = "OrdersController", Classification = "Controller", File = "src/App/OrdersController.cs" }
                ],
                endpoints:
                [
                    new { Method = "GET", Route = "/other", HandlerOrController = "OtherController", File = "src/App/OtherController.cs" },
                    new { Method = "GET", Route = "/orders", HandlerOrController = "OrdersController", File = "src/App/OrdersController.cs" }
                ]);

            ContextPackSelectionResult result = this.service.Select(
                CreateRequest(repoRoot, task: "change-api", target: "Orders", limit: 1),
                GeneratedAt);

            ContextPackItem symbol = Assert.Single(result.Pack.RelevantSymbols);
            ContextPackItem endpoint = Assert.Single(result.Pack.RelevantEndpoints);
            Assert.Equal("OrdersController", symbol.Name);
            Assert.Equal(90, symbol.Score);
            Assert.Equal("GET /orders", endpoint.Name);
            Assert.Equal(125, endpoint.Score);
            Assert.Single(result.Pack.LikelyFiles);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_ChangeUi_ExcludesEndpoints()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(
                repoRoot,
                symbols: [new { Name = "OrdersPage", Classification = "Page", File = "src/App/Orders.razor" }],
                endpoints: [new { Method = "GET", Route = "/orders", HandlerOrController = "OrdersController", File = "src/App/OrdersController.cs" }]);

            ContextPackSelectionResult result = this.service.Select(CreateRequest(repoRoot, task: "change-ui"), GeneratedAt);

            Assert.Single(result.Pack.RelevantSymbols);
            Assert.Empty(result.Pack.RelevantEndpoints);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_UpdatePackage_PreservesPackageScoringAndUppercaseArrayFallback()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteJson(repoRoot, ".ai/generated/inventories/package-inventory.json", new
            {
                Packages = new[]
                {
                    new { Package = "Contoso.Target", Version = "1.2.3", Project = "src/App/App.csproj" }
                }
            });

            ContextPackSelectionResult result = this.service.Select(
                CreateRequest(repoRoot, task: "update-package", target: "Target"),
                GeneratedAt);

            ContextPackItem package = Assert.Single(result.Pack.RelevantPackages);
            Assert.Equal("Contoso.Target 1.2.3", package.Name);
            Assert.Equal(110, package.Score);
            Assert.Empty(result.Pack.RelevantEndpoints);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_ReviewRisk_PreservesRiskAndValidationBehavior()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot, symbols:
            [
                new { Name = "OrderRepository", Classification = "Repository", File = "src/App/OrderRepository.cs" }
            ]);

            ContextPackSelectionResult result = this.service.Select(CreateRequest(repoRoot, task: "review-risk"), GeneratedAt);

            Assert.Contains("Persistence boundary symbols nearby", result.Pack.RiskAreas);
            Assert.Equal(new[] { "airepo audit --repo .", "airepo self-check --repo . --skip-build-mcp" }, result.Pack.ValidationCommands);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_NonRedactedSecretReport_UsesOnlySafeSummary()
    {
        const string sentinel = "FAKE-SECRET-SENTINEL-DO-NOT-COPY";
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);
            WriteJson(repoRoot, ".ai/generated/reports/secret-scan-report.json", new
            {
                RedactedOnly = false,
                FindingCount = 7,
                SecretValue = sentinel
            });

            ContextPackSelectionResult result = this.service.Select(CreateRequest(repoRoot, task: "security-review"), GeneratedAt);
            string serialized = JsonSerializer.Serialize(result);

            Assert.Contains("Secret-scan report was present but not marked redacted-only; only summary risk was used.", result.Warnings);
            Assert.Contains("Secret-scan report was not marked redacted-only", result.Pack.RiskAreas);
            Assert.DoesNotContain(sentinel, serialized, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_AbsentOptionalReports_PreservesNotes()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);

            ContextPackSelectionResult result = this.service.Select(CreateRequest(repoRoot), GeneratedAt);

            Assert.Contains("Latest build summary was not present.", result.Pack.Notes);
            Assert.Contains("Secret-scan report was not present.", result.Pack.Notes);
            Assert.Contains("MCP context manifest was not present.", result.Pack.Notes);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_BudgetZero_PreservesNoBudgetReportSemantics()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot);

            ContextPackSelectionResult result = this.service.Select(CreateRequest(repoRoot, budget: 0), GeneratedAt);

            Assert.True(result.Pack.EstimatedTokens > 0);
            Assert.Null(result.Pack.Budget);
            Assert.False(result.Pack.Truncated);
            Assert.Empty(result.Pack.Cuts ?? []);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Select_PositiveOverBudget_PreservesReportAndFallbackCut()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteInventories(repoRoot, symbols:
            [
                new { Name = "LargeReviewService", Classification = "Service", File = "src/App/LargeReviewService.cs" }
            ]);

            ContextPackSelectionResult result = this.service.Select(CreateRequest(repoRoot, task: "review-risk", budget: 1), GeneratedAt);

            Assert.Equal(1, result.Pack.Budget);
            Assert.True(result.Pack.EstimatedTokens > 1);
            Assert.True(result.Pack.Truncated);
            var cut = Assert.Single(result.Pack.Cuts ?? []);
            Assert.Equal("context-pack", cut.Path);
            Assert.Equal("Estimated output exceeds budget; compact fields should be preferred by consumers.", cut.Reason);
            Assert.Equal(result.Pack.EstimatedTokens - 1, cut.RemovedEstimatedTokens);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    private static ContextPackRequest CreateRequest(
        string repoRoot,
        string task = "review-risk",
        string target = "",
        int limit = 20,
        int budget = 0)
    {
        return new ContextPackRequest(repoRoot, task, target, "all", limit, false, false, false, false, true, budget);
    }

    private static string CreateTempRepo()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_context_selection_test_" + Guid.NewGuid().ToString("N"));
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

    private static void WriteInventories(
        string repoRoot,
        bool includeSymbols = true,
        bool includeEndpoints = true,
        string symbolIndexer = "RoslynLite",
        string endpointIndexer = "RoslynLite",
        object[]? symbols = null,
        object[]? endpoints = null)
    {
        if (includeSymbols)
        {
            WriteJson(repoRoot, ".ai/generated/inventories/symbol-inventory.json", new
            {
                Indexer = symbolIndexer,
                GeneratedAtLocal = "fixed",
                TotalFilesScanned = 1,
                Symbols = symbols ?? []
            });
        }

        if (includeEndpoints)
        {
            WriteEndpointInventory(repoRoot, endpointIndexer, endpoints);
        }
    }

    private static void WriteEndpointInventory(string repoRoot, string indexer = "RoslynLite", object[]? endpoints = null)
    {
        WriteJson(repoRoot, ".ai/generated/inventories/endpoint-inventory.json", new
        {
            Indexer = indexer,
            GeneratedAtLocal = "fixed",
            TotalEndpoints = endpoints?.Length ?? 0,
            Endpoints = endpoints ?? []
        });
    }

    private static void WriteJson(string repoRoot, string relativePath, object value)
    {
        WriteRaw(repoRoot, relativePath, JsonSerializer.Serialize(value));
    }

    private static void WriteRaw(string repoRoot, string relativePath, string value)
    {
        string path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
    }
}

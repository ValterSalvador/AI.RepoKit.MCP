using System.Text.Json;
using AiRepoKit.Cli.Models.SpecContexts;
using AiRepoKit.Cli.Services.SpecContexts;
using AiRepoKit.Spec.Context;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class RepositoryEvidenceCollectorTests
{
    private readonly RepositoryEvidenceCollector _collector = new();

    [Fact]
    public void Collect_EmptyRepository_ReturnsFixedOrderedEvidenceAndNoPersistence()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            RepositoryEvidenceCollection result = this.Collect(repoRoot);

            Assert.Equal(
                new[]
                {
                    "build-summary",
                    "changed-files",
                    "code-index-cache",
                    "code-inventory:endpoint",
                    "code-inventory:package",
                    "code-inventory:project",
                    "code-inventory:project-references",
                    "code-inventory:symbol",
                    "context-pack:change-api",
                    "context-pack:review-risk",
                    "context-pack:test-generation",
                    "graph:project",
                    "graph:risk",
                    "impact",
                    "repository-policy",
                    "secret-scan"
                },
                result.Evidence.Select(item_ => item_.EvidenceId));
            RepositoryEvidence symbol = GetEvidence(result, "code-inventory:symbol");
            Assert.Equal(RepositoryEvidenceAvailability.Missing, symbol.Availability);
            Assert.Equal(RepositoryEvidenceFreshness.Unknown, symbol.Freshness);
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai", "generated", "context-packs")));
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai", "generated", "spec-context")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Theory]
    [InlineData("symbol-inventory.json", "code-inventory:symbol")]
    [InlineData("endpoint-inventory.json", "code-inventory:endpoint")]
    public void Collect_MalformedRequiredInventory_IsUnavailableWithoutThrowing(
        string fileName,
        string evidenceId)
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteCompatibleInventories(repoRoot);
            WriteRaw(repoRoot, $".ai/generated/inventories/{fileName}", "{not-json");

            RepositoryEvidence evidence = GetEvidence(this.Collect(repoRoot), evidenceId);

            Assert.Equal(RepositoryEvidenceAvailability.Unavailable, evidence.Availability);
            Assert.Equal(RepositoryEvidenceFreshness.Unknown, evidence.Freshness);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_ParseableIncompatibleSymbolInventory_IsUnavailable()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteJson(repoRoot, ".ai/generated/inventories/symbol-inventory.json", new { Symbols = Array.Empty<object>() });
            WriteEndpointInventory(repoRoot);

            RepositoryEvidence evidence = GetEvidence(this.Collect(repoRoot), "code-inventory:symbol");

            Assert.Equal(RepositoryEvidenceAvailability.Unavailable, evidence.Availability);
            Assert.Equal(RepositoryEvidenceFreshness.Unknown, evidence.Freshness);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_CompatibleInventoriesAndMatchingCache_AreCurrentThenBecomeStale()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteCompatibleInventories(repoRoot);
            string sourcePath = WriteSource(repoRoot, "src/App/Current.cs", "internal class Current { }");
            FileInfo info = new(sourcePath);
            WriteCache(repoRoot, "src/App/Current.cs", info.Length, info.LastWriteTimeUtc.ToString("O"));

            RepositoryEvidenceCollection current = this.Collect(repoRoot);
            AssertStates(current, RepositoryEvidenceFreshness.Current);

            WriteCache(repoRoot, "src/App/Current.cs", info.Length + 1, info.LastWriteTimeUtc.ToString("O"));
            RepositoryEvidenceCollection stale = this.Collect(repoRoot);
            AssertStates(stale, RepositoryEvidenceFreshness.Stale);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_MissingCache_LeavesCompatibleInventoriesAvailableWithUnknownFreshness()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteCompatibleInventories(repoRoot);

            RepositoryEvidenceCollection result = this.Collect(repoRoot);

            Assert.Equal(RepositoryEvidenceAvailability.Missing, GetEvidence(result, "code-index-cache").Availability);
            Assert.Equal(RepositoryEvidenceFreshness.Unknown, GetEvidence(result, "code-index-cache").Freshness);
            Assert.Equal(RepositoryEvidenceAvailability.Available, GetEvidence(result, "code-inventory:symbol").Availability);
            Assert.Equal(RepositoryEvidenceFreshness.Unknown, GetEvidence(result, "code-inventory:symbol").Freshness);
            Assert.Equal(RepositoryEvidenceAvailability.Available, GetEvidence(result, "code-inventory:endpoint").Availability);
            Assert.Equal(RepositoryEvidenceFreshness.Unknown, GetEvidence(result, "code-inventory:endpoint").Freshness);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_ParseableOptionalSources_HaveSpecifiedFreshness()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteJson(repoRoot, ".ai/generated/inventories/package-inventory.json", new { packages = Array.Empty<object>() });
            WriteJson(repoRoot, ".ai/generated/inventories/project-inventory.json", new { projects = Array.Empty<object>() });
            WriteJson(repoRoot, ".ai/generated/inventories/project-references.json", new { references = Array.Empty<object>() });
            WriteJson(repoRoot, ".ai/generated/reports/latest-build-summary.json", new { status = "ok" });

            RepositoryEvidenceCollection result = this.Collect(repoRoot);

            foreach (string id in new[] { "code-inventory:package", "code-inventory:project", "code-inventory:project-references", "build-summary" })
            {
                Assert.Equal(RepositoryEvidenceAvailability.Available, GetEvidence(result, id).Availability);
                Assert.Equal(RepositoryEvidenceFreshness.Unknown, GetEvidence(result, id).Freshness);
            }
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_ManifestAndRedactedSecretReport_CopyOnlySafeMetadata()
    {
        const string sentinel = "P03D_LEAK_PROBE_ALPHA";
        string repoRoot = CreateTempRepo();
        try
        {
            WriteJson(repoRoot, ".ai/manifests/mcp-context-manifest.json", new
            {
                generatedAtLocal = "fixed-policy-time",
                restrictedPaths = new[] { "private" }
            });
            WriteJson(repoRoot, ".ai/generated/reports/secret-scan-report.json", new
            {
                RedactedOnly = true,
                FindingCount = 3,
                GeneratedAtLocal = "fixed-secret-time",
                ProbeValue = sentinel
            });

            RepositoryEvidenceCollection result = this.Collect(repoRoot);
            RepositoryEvidence policy = GetEvidence(result, "repository-policy");
            RepositoryEvidence secretEvidence = GetEvidence(result, "secret-scan");

            Assert.Equal(RepositoryEvidenceAvailability.Available, policy.Availability);
            Assert.Equal(RepositoryEvidenceFreshness.NotApplicable, policy.Freshness);
            Assert.Equal("fixed-policy-time", policy.SourceGeneratedAt);
            Assert.Equal(RepositoryEvidenceAvailability.Available, secretEvidence.Availability);
            Assert.Contains("3", secretEvidence.Detail, StringComparison.Ordinal);
            Assert.Equal("fixed-secret-time", secretEvidence.SourceGeneratedAt);
            Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_NonRedactedSecretReport_IsUnavailableAndNeverLeaksValues()
    {
        const string sentinel = "P03D_LEAK_PROBE_BETA";
        string repoRoot = CreateTempRepo();
        try
        {
            WriteJson(repoRoot, ".ai/generated/reports/secret-scan-report.json", new
            {
                RedactedOnly = false,
                FindingCount = 9,
                ProbeValue = sentinel
            });

            RepositoryEvidenceCollection result = this.Collect(repoRoot);
            RepositoryEvidence secretEvidence = GetEvidence(result, "secret-scan");

            Assert.Equal(RepositoryEvidenceAvailability.Unavailable, secretEvidence.Availability);
            Assert.Equal(RepositoryEvidenceFreshness.Unknown, secretEvidence.Freshness);
            Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(result), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_CompatibleInventories_ProduceContextPackGraphAndImpactCandidates()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteCompatibleInventories(
                repoRoot,
                symbols:
                [
                    new
                    {
                        Name = "OrdersController",
                        Classification = "Controller",
                        Kind = "Class",
                        Visibility = "public",
                        File = "src\\App\\OrdersController.cs"
                    }
                ],
                endpoints:
                [
                    new
                    {
                        Method = "GET",
                        Route = "/orders",
                        HandlerOrController = "OrdersController",
                        File = "src\\App\\OrdersController.cs"
                    }
                ]);
            WriteJson(repoRoot, ".ai/generated/inventories/project-inventory.json", new
            {
                projects = new[] { new { path = "src/App/App.csproj", targetFrameworks = new[] { "net10.0" } } }
            });
            WriteJson(repoRoot, ".ai/generated/inventories/project-references.json", new { references = Array.Empty<object>() });

            RepositoryEvidenceCollection result = this.Collect(repoRoot, "Orders");

            Assert.All(
                new[] { "context-pack:change-api", "context-pack:review-risk", "context-pack:test-generation" },
                id_ => Assert.Equal(RepositoryEvidenceAvailability.Available, GetEvidence(result, id_).Availability));
            Assert.Equal(RepositoryEvidenceAvailability.Available, GetEvidence(result, "graph:project").Availability);
            Assert.Equal(RepositoryEvidenceAvailability.Available, GetEvidence(result, "graph:risk").Availability);
            Assert.Equal(RepositoryEvidenceAvailability.Available, GetEvidence(result, "impact").Availability);
            Assert.Contains(result.ReferenceCandidates, item_ => item_.Kind == "symbol" && item_.Reference == "src/App/OrdersController.cs#OrdersController");
            Assert.Contains(result.ReferenceCandidates, item_ => item_.Kind == "endpoint" && item_.Reference.StartsWith("src/App/OrdersController.cs#", StringComparison.Ordinal));
            Assert.Contains(result.ReferenceCandidates, item_ => item_.Kind == "graph-project" && item_.Reference == "src/App/App.csproj");
            Assert.Contains(result.ReferenceCandidates, item_ => item_.Kind == "graph-risk" && item_.Reference == "src/App/OrdersController.cs");
            Assert.Contains(result.ReferenceCandidates, item_ => item_.Kind == "project" && item_.Reference == "src/App/App.csproj");
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_StaleInventories_PropagateFreshnessToDerivedEvidence()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteCompatibleInventories(repoRoot);
            string sourcePath = WriteSource(repoRoot, "Current.cs", "internal class Current { }");
            FileInfo info = new(sourcePath);
            WriteCache(repoRoot, "Current.cs", info.Length + 1, info.LastWriteTimeUtc.ToString("O"));

            RepositoryEvidenceCollection result = this.Collect(repoRoot);

            Assert.Equal(RepositoryEvidenceFreshness.Stale, GetEvidence(result, "context-pack:change-api").Freshness);
            Assert.Equal(RepositoryEvidenceFreshness.Stale, GetEvidence(result, "graph:risk").Freshness);
            Assert.Equal(RepositoryEvidenceFreshness.Stale, GetEvidence(result, "impact").Freshness);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_UnsafeAndManifestRestrictedCandidates_AreExcluded()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteJson(repoRoot, ".ai/manifests/mcp-context-manifest.json", new
            {
                restrictedPaths = new[] { "src/private", "*.blocked" }
            });
            WriteCompatibleInventories(
                repoRoot,
                symbols:
                [
                    Symbol("Safe", "src\\Feature\\File.cs"),
                    Symbol("Traversal", "../outside.cs"),
                    Symbol("Settings", "src/App/appsettings.Development.json"),
                    Symbol("Private", "src/private/Hidden.cs"),
                    Symbol("Glob", "src/App/secret.blocked")
                ]);

            RepositoryEvidenceCollection result = this.Collect(repoRoot, "Safe Traversal Settings Private Glob");
            string serialized = JsonSerializer.Serialize(result.ReferenceCandidates);

            Assert.Contains("src/Feature/File.cs", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("outside.cs", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("appsettings", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src/private", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret.blocked", serialized, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Collect_RepeatedCallsSerializeIdentically()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            WriteCompatibleInventories(repoRoot, symbols: [Symbol("Orders", "src/App/Orders.cs")]);

            string first = JsonSerializer.Serialize(this.Collect(repoRoot, "Orders"));
            string second = JsonSerializer.Serialize(this.Collect(repoRoot, "Orders"));

            Assert.Equal(first, second);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    private RepositoryEvidenceCollection Collect(
        string repoRoot_,
        string target_ = "")
    {
        return this._collector.Collect(
            new RepositoryEvidenceCollectionRequest(repoRoot_, target_, 20, 1000));
    }

    private static RepositoryEvidence GetEvidence(
        RepositoryEvidenceCollection collection_,
        string evidenceId_)
    {
        return Assert.Single(collection_.Evidence, item_ => item_.EvidenceId == evidenceId_);
    }

    private static void AssertStates(
        RepositoryEvidenceCollection collection_,
        RepositoryEvidenceFreshness expected_)
    {
        Assert.Equal(expected_, GetEvidence(collection_, "code-index-cache").Freshness);
        Assert.Equal(expected_, GetEvidence(collection_, "code-inventory:symbol").Freshness);
        Assert.Equal(expected_, GetEvidence(collection_, "code-inventory:endpoint").Freshness);
    }

    private static object Symbol(
        string name_,
        string file_)
    {
        return new
        {
            Name = name_,
            Classification = "Service",
            Kind = "Class",
            Visibility = "public",
            File = file_
        };
    }

    private static void WriteCompatibleInventories(
        string repoRoot_,
        object[]? symbols = null,
        object[]? endpoints = null)
    {
        WriteJson(repoRoot_, ".ai/generated/inventories/symbol-inventory.json", new
        {
            Indexer = "RoslynLite",
            GeneratedAtLocal = "fixed-symbol-time",
            TotalFilesScanned = 1,
            Symbols = symbols ?? []
        });
        WriteEndpointInventory(repoRoot_, endpoints);
    }

    private static void WriteEndpointInventory(
        string repoRoot_,
        object[]? endpoints = null)
    {
        WriteJson(repoRoot_, ".ai/generated/inventories/endpoint-inventory.json", new
        {
            Indexer = "RoslynLite",
            GeneratedAtLocal = "fixed-endpoint-time",
            TotalEndpoints = endpoints?.Length ?? 0,
            Endpoints = endpoints ?? []
        });
    }

    private static void WriteCache(
        string repoRoot_,
        string file_,
        long sizeBytes_,
        string lastWriteTimeUtc_)
    {
        WriteJson(repoRoot_, ".ai/generated/cache/code-index-cache.json", new
        {
            Files = new[]
            {
                new
                {
                    File = file_,
                    SizeBytes = sizeBytes_,
                    LastWriteTimeUtc = lastWriteTimeUtc_
                }
            }
        });
    }

    private static string WriteSource(
        string repoRoot_,
        string relativePath_,
        string contents_)
    {
        string path = Path.Combine(repoRoot_, relativePath_.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents_);
        return path;
    }

    private static void WriteJson(
        string repoRoot_,
        string relativePath_,
        object value_)
    {
        WriteRaw(repoRoot_, relativePath_, JsonSerializer.Serialize(value_));
    }

    private static void WriteRaw(
        string repoRoot_,
        string relativePath_,
        string contents_)
    {
        string path = Path.Combine(repoRoot_, relativePath_.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents_);
    }

    private static string CreateTempRepo()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "airepo_evidence_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRepo(
        string path_)
    {
        if (Directory.Exists(path_))
        {
            try
            {
                Directory.Delete(path_, true);
            }
            catch
            {
            }
        }
    }
}

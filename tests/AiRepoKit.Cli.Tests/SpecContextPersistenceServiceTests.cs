using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models.SpecContexts;
using AiRepoKit.Cli.Services.ContextBudget;
using AiRepoKit.Cli.Services.SpecContexts;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class SpecContextPersistenceServiceTests
{
    [Fact]
    public void Persist_ValidContext_PersistsToExpectedPathAndReturnsForwardSlashRelativePath()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("p03e-spec");

            string relativePath = service.Persist(repoRoot, context);

            Assert.Equal(".ai/generated/spec-context/p03e-spec.json", relativePath);
            Assert.DoesNotContain("\\", relativePath);

            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "p03e-spec.json");
            Assert.True(File.Exists(fullPath));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_ReturnedPath_UsesForwardSlashes()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("slash-spec");

            string relativePath = service.Persist(repoRoot, context);

            Assert.StartsWith(".ai/generated/spec-context/", relativePath, StringComparison.Ordinal);
            Assert.DoesNotContain("\\", relativePath);
            Assert.Equal(".ai/generated/spec-context/slash-spec.json", relativePath);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_DirectoriesCreatedWhenAbsent()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("p03e-dirs");

            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai")));

            string relativePath = service.Persist(repoRoot, context);

            Assert.True(Directory.Exists(Path.Combine(repoRoot, ".ai")));
            Assert.True(Directory.Exists(Path.Combine(repoRoot, ".ai", "generated")));
            Assert.True(Directory.Exists(Path.Combine(repoRoot, ".ai", "generated", "spec-context")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".ai", "generated", "spec-context", "p03e-dirs.json")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_MissingRepositoryRoot_ThrowsAndDoesNotCreateRoot()
    {
        string nonExistentRoot = Path.Combine(
            Path.GetTempPath(),
            "nonexistent_repo_" + Guid.NewGuid().ToString("N"));

        Assert.False(Directory.Exists(nonExistentRoot));

        SpecContextPersistenceService service = new();
        SpecContext context = CreateValidSpecContext("p03e-root");

        Assert.Throws<DirectoryNotFoundException>(() => service.Persist(nonExistentRoot, context));
        Assert.False(Directory.Exists(nonExistentRoot));
    }

    [Fact]
    public void Persist_NullSpecContext_ThrowsArgumentNullException()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            Assert.Throws<ArgumentNullException>(() => service.Persist(repoRoot, null!));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Persist_BlankRepoRoot_ThrowsArgumentException(string? invalidRoot_)
    {
        SpecContextPersistenceService service = new();
        SpecContext context = CreateValidSpecContext("p03e-blank-root");

        Assert.Throws<ArgumentException>(() => service.Persist(invalidRoot_!, context));
    }

    [Theory]
    [InlineData("INVALID_UPPERCASE")]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("has spaces")]
    [InlineData("con")]
    [InlineData("aux")]
    [InlineData("prn")]
    public void Persist_InvalidSpecId_ThrowsBeforeDirectoryCreation(string invalidSpecId_)
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext(invalidSpecId_);

            Assert.Throws<ArgumentException>(() => service.Persist(repoRoot, context));
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_InvalidSpecContext_ThrowsBeforeDirectoryCreation()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("p03e-invalid-ctx") with
            {
                Budget = 0 // Budget <= 0 is invalid
            };

            Assert.Throws<InvalidOperationException>(() => service.Persist(repoRoot, context));
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_PersistedJsonRoundTripsThroughSpecJsonSerializer()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("roundtrip-spec");

            service.Persist(repoRoot, context);
            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "roundtrip-spec.json");

            string json = File.ReadAllText(fullPath, Encoding.UTF8);
            SpecContext deserialized = SpecJsonSerializer.Deserialize<SpecContext>(json);

            Assert.Equal(context.SpecId, deserialized.SpecId);
            Assert.Equal(context.SchemaId, deserialized.SchemaId);
            Assert.Equal(context.SchemaVersion, deserialized.SchemaVersion);
            Assert.Equal(context.RequirementSetRevision, deserialized.RequirementSetRevision);
            Assert.Equal(context.WorkSpecRevision, deserialized.WorkSpecRevision);
            Assert.Equal(context.Budget, deserialized.Budget);
            Assert.Equal(context.ReferenceLimit, deserialized.ReferenceLimit);
            Assert.Equal(context.EstimatedTokens, deserialized.EstimatedTokens);
            Assert.Equal(context.Evidence.Count, deserialized.Evidence.Count);
            Assert.Equal(context.References.Count, deserialized.References.Count);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_PersistedJsonUsesExistingSpecJsonContract()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("contract-spec");

            service.Persist(repoRoot, context);
            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "contract-spec.json");
            string content = File.ReadAllText(fullPath, Encoding.UTF8);

            // Compact JSON
            Assert.DoesNotContain("\r\n", content);
            Assert.DoesNotContain("\n", content);

            // camelCase property names
            Assert.Contains("\"specId\":", content);
            Assert.Contains("\"schemaVersion\":", content);
            Assert.Contains("\"requirementSetRevision\":", content);
            Assert.Contains("\"workSpecRevision\":", content);

            // String enum representation
            Assert.Contains("\"availability\":\"available\"", content);
            Assert.Contains("\"freshness\":\"unknown\"", content);

            // Deterministic sorted property ordering: "budget" precedes "estimatedTokens"
            int budgetIndex = content.IndexOf("\"budget\":", StringComparison.Ordinal);
            int estTokensIndex = content.IndexOf("\"estimatedTokens\":", StringComparison.Ordinal);
            Assert.True(budgetIndex >= 0 && estTokensIndex > budgetIndex);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_PersistedBytesContainNoUtf8Bom()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("nobom-spec");

            service.Persist(repoRoot, context);
            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "nobom-spec.json");
            byte[] bytes = File.ReadAllBytes(fullPath);

            Assert.True(bytes.Length >= 3);
            bool hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.False(hasBom);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_PersistedPayloadIsDeterministicAcrossRepeatedPersist()
    {
        string repoRoot1 = CreateTempRepo();
        string repoRoot2 = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("determ-spec");

            service.Persist(repoRoot1, context);
            service.Persist(repoRoot2, context);

            byte[] bytes1 = File.ReadAllBytes(Path.Combine(repoRoot1, ".ai", "generated", "spec-context", "determ-spec.json"));
            byte[] bytes2 = File.ReadAllBytes(Path.Combine(repoRoot2, ".ai", "generated", "spec-context", "determ-spec.json"));

            Assert.True(bytes1.SequenceEqual(bytes2));

            // Call again on repo1
            service.Persist(repoRoot1, context);
            byte[] bytes1After = File.ReadAllBytes(Path.Combine(repoRoot1, ".ai", "generated", "spec-context", "determ-spec.json"));
            Assert.True(bytes1.SequenceEqual(bytes1After));
        }
        finally
        {
            DeleteTempRepo(repoRoot1);
            DeleteTempRepo(repoRoot2);
        }
    }

    [Fact]
    public void Persist_DeleteGeneratedJsonAndPersistAgain_ProducesByteIdenticalResult()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("regen-spec");

            service.Persist(repoRoot, context);
            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "regen-spec.json");
            byte[] bytes1 = File.ReadAllBytes(fullPath);

            File.Delete(fullPath);
            Assert.False(File.Exists(fullPath));

            service.Persist(repoRoot, context);
            byte[] bytes2 = File.ReadAllBytes(fullPath);

            Assert.True(bytes1.SequenceEqual(bytes2));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_ExistingDifferentFinalJsonFile_SafelyReplaced()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("replace-spec");

            string targetDir = Path.Combine(repoRoot, ".ai", "generated", "spec-context");
            Directory.CreateDirectory(targetDir);
            string fullPath = Path.Combine(targetDir, "replace-spec.json");
            File.WriteAllText(fullPath, "{\"old\":\"different content\"}", Encoding.UTF8);

            string returned = service.Persist(repoRoot, context);
            Assert.Equal(".ai/generated/spec-context/replace-spec.json", returned);

            string content = File.ReadAllText(fullPath, Encoding.UTF8);
            Assert.DoesNotContain("old", content);
            Assert.Contains("\"specId\":\"replace-spec\"", content);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_ExistingIdenticalFinalJsonFile_Succeeds()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("identical-spec");

            string path1 = service.Persist(repoRoot, context);
            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "identical-spec.json");
            byte[] bytes1 = File.ReadAllBytes(fullPath);

            string path2 = service.Persist(repoRoot, context);
            byte[] bytes2 = File.ReadAllBytes(fullPath);

            Assert.Equal(path1, path2);
            Assert.True(bytes1.SequenceEqual(bytes2));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_AiOccupiedByFile_ThrowsAndFailsSafely()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            File.WriteAllText(Path.Combine(repoRoot, ".ai"), "occupied by file");
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("occupied-ai");

            Assert.Throws<InvalidOperationException>(() => service.Persist(repoRoot, context));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".ai")));
            Assert.Equal("occupied by file", File.ReadAllText(Path.Combine(repoRoot, ".ai")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_AiGeneratedOccupiedByFile_ThrowsAndFailsSafely()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".ai"));
            File.WriteAllText(Path.Combine(repoRoot, ".ai", "generated"), "occupied by file");
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("occupied-gen");

            Assert.Throws<InvalidOperationException>(() => service.Persist(repoRoot, context));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".ai", "generated")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_AiGeneratedSpecContextOccupiedByFile_ThrowsAndFailsSafely()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, ".ai", "generated"));
            File.WriteAllText(Path.Combine(repoRoot, ".ai", "generated", "spec-context"), "occupied by file");
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("occupied-sc");

            Assert.Throws<InvalidOperationException>(() => service.Persist(repoRoot, context));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".ai", "generated", "spec-context")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_FinalOutputOccupiedByDirectory_ThrowsAndFailsSafely()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            string targetDir = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "dir-spec.json");
            Directory.CreateDirectory(targetDir);
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("dir-spec");

            Assert.Throws<InvalidOperationException>(() => service.Persist(repoRoot, context));
            Assert.True(Directory.Exists(targetDir));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_OversizedSerializedContext_RejectedAndNoFinalFileCreated()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            RepositoryEvidence ev = new()
            {
                EvidenceId = "ev-1",
                Source = "source",
                Kind = "kind",
                Reference = "ref",
                Availability = RepositoryEvidenceAvailability.Available,
                Freshness = RepositoryEvidenceFreshness.Unknown
            };

            string largeReason = new('x', 15_000);
            List<SpecContextReference> refs = [];
            for (int i = 0; i < 100; i++)
            {
                refs.Add(new SpecContextReference
                {
                    EvidenceId = "ev-1",
                    Kind = "kind",
                    Reference = $"ref-{i}",
                    Reason = largeReason,
                    Priority = 10
                });
            }

            SpecContext context = new()
            {
                SchemaId = SpecContextSchema.SchemaId,
                SchemaVersion = SpecContextSchema.SchemaVersion,
                SpecId = "oversized-spec",
                RequirementSetRevision = new ArtifactRevision(1),
                WorkSpecRevision = new ArtifactRevision(1),
                Target = "target",
                ReferenceLimit = 100,
                Budget = 10_000_000,
                EstimatedTokens = 500,
                Truncated = false,
                Evidence = [ev],
                References = refs,
                Omissions = []
            };

            Assert.Empty(SpecContextValidator.Validate(context));

            SpecContextPersistenceService service = new();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Persist(repoRoot, context));
            Assert.Contains("exceeds maximum allowed size", ex.Message, StringComparison.Ordinal);

            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_TemporarySiblingNotLeftAfterSuccessfulWrite()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("temp-clean-spec");

            service.Persist(repoRoot, context);
            string tempPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "temp-clean-spec.json.tmp");
            Assert.False(File.Exists(tempPath));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_TemporarySiblingCleanedAfterFailedWrite()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("locked-spec");

            string dir = Path.Combine(repoRoot, ".ai", "generated", "spec-context");
            Directory.CreateDirectory(dir);
            string finalPath = Path.Combine(dir, "locked-spec.json");
            string tempPath = Path.Combine(dir, "locked-spec.json.tmp");

            File.WriteAllText(finalPath, "{\"old\":\"different\"}", Encoding.UTF8);

            File.SetAttributes(finalPath, FileAttributes.ReadOnly);
            try
            {
                using (FileStream fs = new(finalPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.ThrowsAny<Exception>(() => service.Persist(repoRoot, context));
                }
            }
            finally
            {
                File.SetAttributes(finalPath, FileAttributes.Normal);
            }

            Assert.False(File.Exists(tempPath));
            Assert.Equal("{\"old\":\"different\"}", File.ReadAllText(finalPath));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_CanonicalSpecsDirectoryNotCreated()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("no-specs-spec");

            service.Persist(repoRoot, context);

            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai", "specs")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_PreExistingCanonicalFilesRemainByteIdentical()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            string canonicalDir = Path.Combine(repoRoot, ".ai", "specs", "canonical-spec");
            Directory.CreateDirectory(canonicalDir);
            string reqPath = Path.Combine(canonicalDir, "requirements.json");
            string workPath = Path.Combine(canonicalDir, "work-spec.json");

            byte[] reqBytesOriginal = [1, 2, 3, 4, 5];
            byte[] workBytesOriginal = [6, 7, 8, 9, 10];
            File.WriteAllBytes(reqPath, reqBytesOriginal);
            File.WriteAllBytes(workPath, workBytesOriginal);

            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("canonical-spec");

            service.Persist(repoRoot, context);

            byte[] reqBytesAfter = File.ReadAllBytes(reqPath);
            byte[] workBytesAfter = File.ReadAllBytes(workPath);

            Assert.True(reqBytesOriginal.SequenceEqual(reqBytesAfter));
            Assert.True(workBytesOriginal.SequenceEqual(workBytesAfter));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_RequirementSetAndWorkSpecObjectsNotMutated()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            RequirementSet reqSet = new()
            {
                Revision = new ArtifactRevision(3),
                Inputs = [],
                Requirements = []
            };
            WorkSpec workSpec = new()
            {
                Revision = new ArtifactRevision(5),
                RequirementSetRevision = new ArtifactRevision(3),
                Constraints = [],
                AcceptanceCriteria = []
            };

            string reqBefore = JsonSerializer.Serialize(reqSet);
            string workBefore = JsonSerializer.Serialize(workSpec);

            SpecContextBuildRequest request = new(
                repoRoot,
                "no-mutation-spec",
                reqSet,
                workSpec,
                "Orders",
                10,
                1000,
                1000);

            FakeCollector collector = new(new RepositoryEvidenceCollection([], []));
            SpecContextBuilder builder = new(collector, new ContextBudgeter());
            SpecContext context = builder.Build(request);

            SpecContextPersistenceService service = new();
            service.Persist(repoRoot, context);

            Assert.Equal(reqBefore, JsonSerializer.Serialize(reqSet));
            Assert.Equal(workBefore, JsonSerializer.Serialize(workSpec));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_SpecContextBuilderOutput_PersistedWithoutChangingFields()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            RequirementSet reqSet = new()
            {
                Revision = new ArtifactRevision(1),
                Inputs = [],
                Requirements = []
            };
            WorkSpec workSpec = new()
            {
                Revision = new ArtifactRevision(2),
                RequirementSetRevision = new ArtifactRevision(1),
                Constraints = [],
                AcceptanceCriteria = []
            };

            SpecContextBuildRequest request = new(
                repoRoot,
                "builder-out-spec",
                reqSet,
                workSpec,
                "Orders",
                10,
                1000,
                1000);

            FakeCollector collector = new(new RepositoryEvidenceCollection([], []));
            SpecContextBuilder builder = new(collector, new ContextBudgeter());
            SpecContext context = builder.Build(request);

            string contextJsonBefore = SpecJsonSerializer.Serialize(context);

            SpecContextPersistenceService service = new();
            service.Persist(repoRoot, context);

            string contextJsonAfter = SpecJsonSerializer.Serialize(context);
            Assert.Equal(contextJsonBefore, contextJsonAfter);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_RepeatedBuilderOutput_DeletedAndRegeneratedAndPersistedByteIdentically()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            RepositoryEvidence ev = new()
            {
                EvidenceId = "ev-1",
                Source = "src/Orders.cs",
                Kind = "file",
                Reference = "src/Orders.cs",
                Availability = RepositoryEvidenceAvailability.Available,
                Freshness = RepositoryEvidenceFreshness.Current
            };
            SpecContextReference r = new()
            {
                EvidenceId = "ev-1",
                Kind = "file",
                Reference = "src/Orders.cs",
                Reason = "Direct domain reference.",
                Priority = 100
            };
            RepositoryEvidenceCollection collection = new([ev], [r]);

            RequirementSet reqSet = new()
            {
                Revision = new ArtifactRevision(1),
                Inputs = [],
                Requirements = []
            };
            WorkSpec workSpec = new()
            {
                Revision = new ArtifactRevision(1),
                RequirementSetRevision = new ArtifactRevision(1),
                Constraints = [],
                AcceptanceCriteria = []
            };

            SpecContextBuildRequest request = new(
                repoRoot,
                "regen-builder-spec",
                reqSet,
                workSpec,
                "Orders",
                10,
                1000,
                1000);

            SpecContextBuilder builder1 = new(new FakeCollector(collection), new ContextBudgeter());
            SpecContext context1 = builder1.Build(request);

            SpecContextPersistenceService service = new();
            service.Persist(repoRoot, context1);

            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "regen-builder-spec.json");
            byte[] bytes1 = File.ReadAllBytes(fullPath);

            File.Delete(fullPath);
            Assert.False(File.Exists(fullPath));

            SpecContextBuilder builder2 = new(new FakeCollector(collection), new ContextBudgeter());
            SpecContext context2 = builder2.Build(request);

            service.Persist(repoRoot, context2);
            byte[] bytes2 = File.ReadAllBytes(fullPath);

            Assert.True(bytes1.SequenceEqual(bytes2));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_NoWallClockOrGeneratedTimestampAdded()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("clock-free-spec");

            service.Persist(repoRoot, context);

            string fullPath = Path.Combine(repoRoot, ".ai", "generated", "spec-context", "clock-free-spec.json");
            string json = File.ReadAllText(fullPath, Encoding.UTF8);

            Assert.DoesNotContain("timestamp", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("createdAt", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("updatedAt", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("persistedAt", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Persist_ReparsePointOrSymlink_RejectedWhenPrivilegesAvailable()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            string targetDir = Path.Combine(repoRoot, "real-target");
            Directory.CreateDirectory(targetDir);
            string symlinkAi = Path.Combine(repoRoot, ".ai");

            try
            {
                Directory.CreateSymbolicLink(symlinkAi, targetDir);
            }
            catch
            {
                // Symbolic link creation not supported or privileged in this environment.
                return;
            }

            SpecContextPersistenceService service = new();
            SpecContext context = CreateValidSpecContext("symlink-spec");

            Assert.Throws<InvalidOperationException>(() => service.Persist(repoRoot, context));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    private static SpecContext CreateValidSpecContext(
        string specId_ = "valid-spec")
    {
        RepositoryEvidence ev = new()
        {
            EvidenceId = "ev-1",
            Source = "src/Test.cs",
            Kind = "file",
            Reference = "src/Test.cs",
            Availability = RepositoryEvidenceAvailability.Available,
            Freshness = RepositoryEvidenceFreshness.Unknown
        };

        SpecContextReference r = new()
        {
            EvidenceId = "ev-1",
            Kind = "file",
            Reference = "src/Test.cs",
            Reason = "Valid test reference.",
            Priority = 10
        };

        return new SpecContext
        {
            SchemaId = SpecContextSchema.SchemaId,
            SchemaVersion = SpecContextSchema.SchemaVersion,
            SpecId = specId_,
            RequirementSetRevision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Target = "test-target",
            ReferenceLimit = 10,
            Budget = 1000,
            EstimatedTokens = 100,
            Truncated = false,
            Evidence = [ev],
            References = [r],
            Omissions = []
        };
    }

    private static string CreateTempRepo()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "airepo_spec_persist_test_" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeCollector :
        IRepositoryEvidenceCollector
    {
        private readonly RepositoryEvidenceCollection _collection;

        public FakeCollector(
            RepositoryEvidenceCollection collection_)
        {
            this._collection = collection_;
        }

        public int CallCount
        {
            get;
            private set;
        }

        public RepositoryEvidenceCollectionRequest? LastRequest
        {
            get;
            private set;
        }

        public RepositoryEvidenceCollection Collect(
            RepositoryEvidenceCollectionRequest request_)
        {
            this.CallCount++;
            this.LastRequest = request_;
            return this._collection;
        }
    }
}

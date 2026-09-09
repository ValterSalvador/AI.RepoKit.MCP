using System.Text;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecApprovalLedgerStoreTests
{
    [Fact]
    public void Load_NonexistentLedgerReturnsNullAndCreatesNothing()
    {
        using TestApprovalRepository repository =
            new(createRoot: false);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecApprovalLedger? ledger =
            store.Load();

        Assert.Null(ledger);
        Assert.False(Directory.Exists(repository.Root));
    }

    [Fact]
    public void DryRun_FirstAppendPredictsApr001RevisionOneWithoutFilesystemMutation()
    {
        using TestApprovalRepository repository =
            new(createRoot: false);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        RequirementSet requirementSet =
            CreateRequirementSet();

        SpecApprovalLedgerStoreResult result =
            store.Append(
                allocatedId_ =>
                    SpecApprovalBinding.Create(
                        allocatedId_,
                        requirementSet),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.DryRun
                });

        Assert.True(result.Changed);
        Assert.False(result.Applied);
        Assert.Null(result.PreviousRevision);
        Assert.Equal(new ArtifactRevision(1), result.TargetRevision);
        Assert.Equal(SpecWriteMode.DryRun, result.Mode);
        Assert.Equal("APR-001", result.Approval.Id.Value);
        Assert.False(Directory.Exists(repository.Root));
    }

    [Fact]
    public void Apply_FirstAppendCreatesApprovalsJsonWithApr001RevisionOneUtf8NoBom()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        RequirementSet requirementSet =
            CreateRequirementSet();

        SpecApprovalLedgerStoreResult result =
            store.Append(
                allocatedId_ =>
                    SpecApprovalBinding.Create(
                        allocatedId_,
                        requirementSet),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply
                });

        Assert.True(result.Changed);
        Assert.True(result.Applied);
        Assert.Null(result.PreviousRevision);
        Assert.Equal(new ArtifactRevision(1), result.TargetRevision);
        Assert.Equal(SpecWriteMode.Apply, result.Mode);
        Assert.Equal("APR-001", result.Approval.Id.Value);

        string ledgerPath =
            repository.LedgerPath;

        Assert.True(File.Exists(ledgerPath));

        byte[] rawBytes =
            File.ReadAllBytes(ledgerPath);

        // UTF-8 without BOM
        Assert.False(
            rawBytes.Length >= 3 &&
            rawBytes[0] == 0xEF &&
            rawBytes[1] == 0xBB &&
            rawBytes[2] == 0xBF);

        SpecApprovalLedger? reloaded =
            store.Load();

        Assert.NotNull(reloaded);
        Assert.Equal(new ArtifactRevision(1), reloaded.Revision);
        Assert.Single(reloaded.Approvals);
        Assert.Equal("APR-001", reloaded.Approvals[0].Id.Value);
    }

    [Fact]
    public void Load_PersistedLedgerRoundTripsDeterministically()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        store.Append(
            id => SpecApprovalBinding.Create(id, CreateRequirementSet()),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = null
            });

        store.Append(
            id => SpecApprovalBinding.Create(id, CreateWorkSpec()),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        string fileContent =
            File.ReadAllText(repository.LedgerPath, Encoding.UTF8);

        SpecApprovalLedger? loaded =
            store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Approvals.Count);

        string serialized =
            SpecJsonSerializer.Serialize(loaded);

        Assert.Equal(fileContent, serialized);
    }

    [Fact]
    public void Apply_SecondAppendAssignsApr002IncrementedRevisionAndPreservesHistory()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecApprovalLedgerStoreResult first =
            store.Append(
                id => SpecApprovalBinding.Create(id, CreateRequirementSet()),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = null
                });

        SpecApprovalLedgerStoreResult second =
            store.Append(
                id => SpecApprovalBinding.Create(id, CreateWorkSpec()),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.Equal("APR-001", first.Approval.Id.Value);
        Assert.Equal(new ArtifactRevision(1), first.TargetRevision);

        Assert.Equal("APR-002", second.Approval.Id.Value);
        Assert.Equal(new ArtifactRevision(1), second.PreviousRevision);
        Assert.Equal(new ArtifactRevision(2), second.TargetRevision);

        SpecApprovalLedger? loaded =
            store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(new ArtifactRevision(2), loaded.Revision);
        Assert.Equal(2, loaded.Approvals.Count);
        Assert.Equal("APR-001", loaded.Approvals[0].Id.Value);
        Assert.Equal("APR-002", loaded.Approvals[1].Id.Value);
    }

    [Fact]
    public void Append_AllocatesNextIdFromMaximumNumericSuffixWithGaps()
    {
        using TestApprovalRepository repository =
            new();

        // Seed with APR-001 and APR-004
        SpecApprovalLedger seed =
            new()
            {
                SpecId = "spec-sample",
                Revision = new ArtifactRevision(2),
                Approvals =
                [
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-001"),
                        CreateRequirementSet(revision_: 1)),
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-004"),
                        CreateRequirementSet(revision_: 2))
                ]
            };

        repository.WriteLedger(seed);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecApprovalLedgerStoreResult result =
            store.Append(
                id => SpecApprovalBinding.Create(id, CreateWorkSpec()),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(2)
                });

        Assert.Equal("APR-005", result.Approval.Id.Value);
        Assert.Equal(new ArtifactRevision(3), result.TargetRevision);
    }

    [Fact]
    public void Append_SupportsSuffixGrowthBeyondThreeDigits()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedger seed =
            new()
            {
                SpecId = "spec-sample",
                Revision = new ArtifactRevision(1),
                Approvals =
                [
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-999"),
                        CreateRequirementSet())
                ]
            };

        repository.WriteLedger(seed);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecApprovalLedgerStoreResult result =
            store.Append(
                id => SpecApprovalBinding.Create(id, CreateWorkSpec()),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.Equal("APR-1000", result.Approval.Id.Value);
        Assert.Equal(new ArtifactRevision(2), result.TargetRevision);
    }

    [Fact]
    public void Append_ExactSemanticReapprovalIsIdempotentNoOp()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        RequirementSet requirementSet =
            CreateRequirementSet();

        SpecApprovalLedgerStoreResult first =
            store.Append(
                id => SpecApprovalBinding.Create(id, requirementSet),
                new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        Assert.True(first.Changed);
        Assert.True(first.Applied);
        Assert.Equal("APR-001", first.Approval.Id.Value);

        byte[] bytesBefore =
            File.ReadAllBytes(repository.LedgerPath);
        DateTime lastWriteTime =
            File.GetLastWriteTimeUtc(repository.LedgerPath);

        SpecApprovalLedgerStoreResult idempotentResult =
            store.Append(
                id => SpecApprovalBinding.Create(id, requirementSet),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(999) // Should be ignored because idempotent match resolves first
                });

        Assert.False(idempotentResult.Changed);
        Assert.False(idempotentResult.Applied);
        Assert.Equal(new ArtifactRevision(1), idempotentResult.PreviousRevision);
        Assert.Equal(new ArtifactRevision(1), idempotentResult.TargetRevision);
        Assert.Equal("APR-001", idempotentResult.Approval.Id.Value);

        byte[] bytesAfter =
            File.ReadAllBytes(repository.LedgerPath);

        Assert.Equal(bytesBefore, bytesAfter);
        Assert.Equal(lastWriteTime, File.GetLastWriteTimeUtc(repository.LedgerPath));
    }

    [Fact]
    public void Append_IdempotentRetryWithNullExpectedRevisionOnExistingLedgerSucceeds()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        RequirementSet requirementSet =
            CreateRequirementSet();

        store.Append(
            id => SpecApprovalBinding.Create(id, requirementSet),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        // Retry with ExpectedCurrentRevision = null
        SpecApprovalLedgerStoreResult result =
            store.Append(
                id => SpecApprovalBinding.Create(id, requirementSet),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = null
                });

        Assert.False(result.Changed);
        Assert.False(result.Applied);
        Assert.Equal("APR-001", result.Approval.Id.Value);
    }

    [Fact]
    public void Append_NewAppendWithMismatchedExpectedRevisionThrowsRevisionConflict()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        store.Append(
            id => SpecApprovalBinding.Create(id, CreateRequirementSet()),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        id => SpecApprovalBinding.Create(id, CreateWorkSpec()),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply,
                            ExpectedCurrentRevision = new ArtifactRevision(5)
                        }));

        Assert.Equal(SpecPersistenceException.RevisionConflict, exception.ErrorCode);

        SpecApprovalLedger? loaded =
            store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(new ArtifactRevision(1), loaded.Revision);
        Assert.Single(loaded.Approvals);
    }

    [Fact]
    public void Append_SameArtifactKeyWithDifferentSemanticsThrowsValidationFailed()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        RequirementSet set1 =
            CreateRequirementSet(statement_: "Original requirement");
        RequirementSet set2 =
            CreateRequirementSet(statement_: "Modified requirement"); // Same revision 1, but different semantic content

        store.Append(
            id => SpecApprovalBinding.Create(id, set1),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        id => SpecApprovalBinding.Create(id, set2),
                        new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Append_FactoryReturnsWrongAprIdThrowsValidationFailed()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        _ =>
                            SpecApprovalBinding.Create(
                                new StableEntityId("APR-999"),
                                CreateRequirementSet()),
                        new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.ValidationFailed, exception.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void Append_FactoryReturnsNullThrowsValidationFailed()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        _ => null!,
                        new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.ValidationFailed, exception.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void Append_FactoryReturnsInvalidApprovalThrowsValidationFailed()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        allocatedId_ =>
                            new Approval
                            {
                                Id = allocatedId_,
                                ArtifactKind = SpecArtifactKind.RequirementSet,
                                ArtifactIdentity = SpecArtifactIdentity.RequirementSet,
                                ArtifactRevision = new ArtifactRevision(1),
                                CanonicalSemanticRepresentation = string.Empty, // Invalid!
                                SemanticDigest = new string('a', 64)
                            },
                        new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.ValidationFailed, exception.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void Load_RejectsInvalidLedgerSchema()
    {
        using TestApprovalRepository repository =
            new();

        string invalidSchemaJson =
            """
            {
              "schemaId": "invalid.schema",
              "schemaVersion": 1,
              "specId": "spec-sample",
              "artifactIdentity": "approvals",
              "revision": 1,
              "approvals": []
            }
            """;

        repository.WriteRaw(invalidSchemaJson);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Load_RejectsLedgerSpecIdMismatch()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedger ledger =
            new()
            {
                SpecId = "different-spec",
                Revision = new ArtifactRevision(1),
                Approvals = []
            };

        repository.WriteLedger(ledger);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Load_RejectsDuplicateOrConflictingHistory()
    {
        using TestApprovalRepository repository =
            new();

        RequirementSet requirementSet =
            CreateRequirementSet();

        SpecApprovalLedger duplicateLedger =
            new()
            {
                SpecId = "spec-sample",
                Revision = new ArtifactRevision(1),
                Approvals =
                [
                    SpecApprovalBinding.Create(new StableEntityId("APR-001"), requirementSet),
                    SpecApprovalBinding.Create(new StableEntityId("APR-002"), requirementSet)
                ]
            };

        repository.WriteLedger(duplicateLedger);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Load_RejectsInvalidJson()
    {
        using TestApprovalRepository repository =
            new();

        repository.WriteRaw("{ not-valid-json ");

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.InvalidJson, exception.ErrorCode);
    }

    [Fact]
    public void Load_RejectsUnknownJsonField()
    {
        using TestApprovalRepository repository =
            new();

        string unknownFieldJson =
            """
            {
              "schemaId": "https://airepokit.org/schemas/spec-v1.json",
              "schemaVersion": 1,
              "specId": "spec-sample",
              "artifactIdentity": "approvals",
              "revision": 1,
              "approvals": [],
              "unexpectedField": "prohibited"
            }
            """;

        repository.WriteRaw(unknownFieldJson);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.InvalidJson, exception.ErrorCode);
    }

    [Fact]
    public void Load_RejectsInvalidUtf8()
    {
        using TestApprovalRepository repository =
            new();

        repository.WriteBytes([0xFF, 0xFE, 0xFD]);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.InvalidUtf8, exception.ErrorCode);
    }

    [Fact]
    public void Load_AcceptsValidUtf8Bom()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedger ledger =
            new()
            {
                SpecId = "spec-sample",
                Revision = new ArtifactRevision(1),
                Approvals = []
            };

        byte[] jsonBytes =
            Encoding.UTF8.GetBytes(SpecJsonSerializer.Serialize(ledger));

        byte[] bomBytes =
            [0xEF, 0xBB, 0xBF, .. jsonBytes];

        repository.WriteBytes(bomBytes);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecApprovalLedger? loaded =
            store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("spec-sample", loaded.SpecId);
        Assert.Equal(new ArtifactRevision(1), loaded.Revision);
    }

    [Fact]
    public void Load_RejectsOversizedExistingLedger()
    {
        using TestApprovalRepository repository =
            new();

        byte[] oversizedBytes =
            new byte[SpecWorkspace.MaximumArtifactSizeBytes + 10];
        Array.Fill(oversizedBytes, (byte)' ');

        repository.WriteBytes(oversizedBytes);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.ArtifactTooLarge, exception.ErrorCode);
    }

    [Fact]
    public void Append_RejectsOversizedProposedLedgerInDryRunAndApplyWithoutMutation()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        RequirementSet oversizedSet =
            CreateRequirementSet(statement_: new string('z', SpecWorkspace.MaximumArtifactSizeBytes));

        SpecPersistenceException dryRunEx =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        id => SpecApprovalBinding.Create(id, oversizedSet),
                        new SpecStoreOptions { Mode = SpecWriteMode.DryRun }));

        Assert.Equal(SpecPersistenceException.ArtifactTooLarge, dryRunEx.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));

        SpecPersistenceException applyEx =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        id => SpecApprovalBinding.Create(id, oversizedSet),
                        new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.ArtifactTooLarge, applyEx.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void LoadAndAppend_RejectWhenApprovalPathIsOccupiedByDirectory()
    {
        using TestApprovalRepository repository =
            new();

        Directory.CreateDirectory(repository.LedgerPath);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException loadException =
            Assert.Throws<SpecPersistenceException>(
                () => store.Load());

        Assert.Equal(SpecPersistenceException.ReadFailed, loadException.ErrorCode);

        SpecPersistenceException appendException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        id => SpecApprovalBinding.Create(id, CreateRequirementSet()),
                        new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.ReadFailed, appendException.ErrorCode);
    }

    [Fact]
    public void ArtifactPaths_GetApprovalLedgerPath_RejectRepositoryEscape()
    {
        string root =
            Path.Combine(Path.GetTempPath(), "airepokit-escape-" + Guid.NewGuid().ToString("N"));

        SpecArtifactPaths paths =
            new(root, new SpecId("spec-sample"));

        string ledgerPath =
            paths.GetApprovalLedgerPath();

        Assert.StartsWith(root, ledgerPath, StringComparison.Ordinal);
        Assert.EndsWith("approvals.json", ledgerPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactPaths_GetApprovalLedgerPath_RejectExistingSymbolicLinkWhereSupported()
    {
        string repositoryRoot =
            Path.Combine(
                Path.GetTempPath(),
                "airepokit-spec-ledger-link-" + Guid.NewGuid().ToString("N"));
        string artifactDirectory =
            Path.Combine(
                repositoryRoot,
                ".ai",
                "specs",
                "spec-sample");
        string link =
            Path.Combine(
                artifactDirectory,
                "approvals.json");
        string target =
            Path.Combine(
                repositoryRoot,
                "target.json");

        try
        {
            Directory.CreateDirectory(
                artifactDirectory);
            File.WriteAllText(
                target,
                "{}");

            try
            {
                File.CreateSymbolicLink(
                    link,
                    target);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                return;
            }

            SpecArtifactPaths paths =
                new(
                    repositoryRoot,
                    new SpecId(
                        "spec-sample"));

            Assert.Throws<InvalidOperationException>(
                () =>
                    paths.GetApprovalLedgerPath());
        }
        finally
        {
            try
            {
                File.Delete(
                    link);
            }
            catch
            {
            }

            try
            {
                Directory.Delete(
                    repositoryRoot,
                    true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Apply_RejectsWhenRepositoryRootDoesNotExist()
    {
        using TestApprovalRepository repository =
            new(createRoot: false);

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    store.Append(
                        id => SpecApprovalBinding.Create(id, CreateRequirementSet()),
                        new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.WriteFailed, exception.ErrorCode);
        Assert.False(Directory.Exists(repository.Root));
    }

    [Fact]
    public void AtomicWriter_CleansUpTempFilesOnFailure()
    {
        using TestApprovalRepository repository =
            new();

        Directory.CreateDirectory(repository.SpecDirectory);

        Assert.Throws<SpecPersistenceException>(
            () =>
                SpecAtomicFileWriter.Write(
                    repository.LedgerPath,
                    Encoding.UTF8.GetBytes("test"),
                    () => throw new SpecPersistenceException(
                        SpecPersistenceException.WriteFailed,
                        "Injected post-write failure.")));

        Assert.False(File.Exists(repository.LedgerPath));
        Assert.Empty(
            Directory.GetFiles(
                repository.SpecDirectory,
                ".*.tmp",
                SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Append_GenericImplementationPlanApprovalRecordIsPersisted()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        ImplementationPlan plan =
            CreatePlan();

        SpecApprovalLedgerStoreResult result =
            store.Append(
                id => SpecApprovalBinding.Create(id, plan),
                new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        Assert.True(result.Applied);
        Assert.Equal(SpecArtifactKind.ImplementationPlan, result.Approval.ArtifactKind);
        Assert.Equal("APR-001", result.Approval.Id.Value);

        SpecApprovalLedger? loaded =
            store.Load();
        Assert.NotNull(loaded);
        Assert.Single(loaded.Approvals);
        Assert.Equal(SpecArtifactKind.ImplementationPlan, loaded.Approvals[0].ArtifactKind);
    }

    [Fact]
    public async Task Concurrent_ApprovalAppendsSerializeWithoutLostUpdates()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore store =
            repository.CreateStore();

        const int workerCount = 10;
        ManualResetEventSlim startSignal = new(false);
        Task<SpecApprovalLedgerStoreResult>[] tasks = new Task<SpecApprovalLedgerStoreResult>[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            int index = i + 1;
            tasks[i] = Task.Run(() =>
            {
                startSignal.Wait();
                RequirementSet req = CreateRequirementSet(revision_: index, statement_: $"Req statement {index}");

                while (true)
                {
                    SpecApprovalLedger? current = store.Load();
                    ArtifactRevision? expected = current?.Revision;

                    try
                    {
                        return store.Append(
                            id => SpecApprovalBinding.Create(id, req),
                            new SpecStoreOptions
                            {
                                Mode = SpecWriteMode.Apply,
                                ExpectedCurrentRevision = expected
                            });
                    }
                    catch (SpecPersistenceException ex) when (ex.ErrorCode == SpecPersistenceException.RevisionConflict)
                    {
                        // Concurrent writer committed, retry with updated revision
                    }
                }
            });
        }

        startSignal.Set();
        await Task.WhenAll(tasks);

        SpecApprovalLedger? finalLedger =
            store.Load();

        Assert.NotNull(finalLedger);
        Assert.Equal(new ArtifactRevision(workerCount), finalLedger.Revision);
        Assert.Equal(workerCount, finalLedger.Approvals.Count);

        HashSet<string> seenIds = new(StringComparer.Ordinal);
        for (int i = 0; i < workerCount; i++)
        {
            string id = finalLedger.Approvals[i].Id.Value;
            Assert.True(seenIds.Add(id));
            Assert.Equal($"APR-{i + 1:D3}", id);
        }
    }

    [Fact]
    public async Task Concurrent_ApprovalAppendAndWorkspaceStoreCoordinateOnSharedLock()
    {
        using TestApprovalRepository repository =
            new();

        SpecApprovalLedgerStore approvalStore =
            repository.CreateStore();

        SpecWorkspace workspace =
            new(repository.Root, new SpecId("spec-sample"));

        ManualResetEventSlim startSignal = new(false);

        Task<SpecStoreResult> workspaceTask = Task.Run(() =>
        {
            startSignal.Wait();
            return workspace.Store(
                CreateRequirementSet(statement_: "Workspace requirement"),
                new SpecStoreOptions { Mode = SpecWriteMode.Apply });
        });

        Task<SpecApprovalLedgerStoreResult> approvalTask = Task.Run(() =>
        {
            startSignal.Wait();
            return approvalStore.Append(
                id => SpecApprovalBinding.Create(id, CreateWorkSpec()),
                new SpecStoreOptions { Mode = SpecWriteMode.Apply });
        });

        startSignal.Set();
        await Task.WhenAll(workspaceTask, approvalTask);

        SpecStoreResult workspaceResult = await workspaceTask;
        SpecApprovalLedgerStoreResult approvalResult = await approvalTask;

        Assert.True(workspaceResult.Applied);
        Assert.True(approvalResult.Applied);

        Assert.NotNull(workspace.Load().RequirementSet);
        Assert.NotNull(approvalStore.Load());
    }

    private static RequirementSet CreateRequirementSet(
        int revision_ = 1,
        string statement_ = "Requirement statement")
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id = new StableEntityId("INPUT-001"),
                    Text = "Input text"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = statement_,
                    SourceInputIds =
                    [
                        new StableEntityId("INPUT-001")
                    ]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec(
        int revision_ = 1,
        int requirementRevision_ = 1)
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(revision_),
            RequirementSetRevision = new ArtifactRevision(requirementRevision_),
            Constraints =
            [
                new Constraint
                {
                    Id = new StableEntityId("CON-001"),
                    Statement = "Constraint statement",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-001"),
                    Statement = "Criterion statement",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ]
                }
            ]
        };
    }

    private static ImplementationPlan CreatePlan(
        int revision_ = 1,
        int workSpecRevision_ = 1)
    {
        return new ImplementationPlan
        {
            Revision = new ArtifactRevision(revision_),
            WorkSpecRevision = new ArtifactRevision(workSpecRevision_),
            Steps =
            [
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-001"),
                    Statement = "Step statement",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId("AC-001")
                    ]
                }
            ]
        };
    }

    private sealed class TestApprovalRepository : IDisposable
    {
        private readonly SpecArtifactPaths _paths;

        public TestApprovalRepository(bool createRoot = true)
        {
            this.Root = Path.Combine(
                Path.GetTempPath(),
                "airepokit-ledger-test-" + Guid.NewGuid().ToString("N"));

            this._paths = new SpecArtifactPaths(
                this.Root,
                new SpecId("spec-sample"));

            if (createRoot)
            {
                Directory.CreateDirectory(this.Root);
            }
        }

        public string Root { get; }

        public string SpecDirectory =>
            this._paths.SpecDirectory;

        public string LedgerPath =>
            this._paths.GetApprovalLedgerPath();

        public SpecApprovalLedgerStore CreateStore()
        {
            return new SpecApprovalLedgerStore(
                this.Root,
                new SpecId("spec-sample"));
        }

        public void WriteLedger(SpecApprovalLedger ledger_)
        {
            this.WriteRaw(SpecJsonSerializer.Serialize(ledger_));
        }

        public void WriteRaw(string json_)
        {
            this.WriteBytes(Encoding.UTF8.GetBytes(json_));
        }

        public void WriteBytes(byte[] bytes_)
        {
            Directory.CreateDirectory(this.SpecDirectory);
            File.WriteAllBytes(this.LedgerPath, bytes_);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Root))
                {
                    Directory.Delete(this.Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

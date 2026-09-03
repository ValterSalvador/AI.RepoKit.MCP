using System.Text;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecWorkspaceStoreTests
{
    [Fact]
    public void DryRun_NewRequirementSetPlansRevisionOneWithoutMutation()
    {
        using TestRepository repository =
            new(
                createRoot: false);

        SpecStoreResult result =
            repository.CreateWorkspace().Store(
                CreateRequirementSet(
                    revision_: 99),
                DryRun());

        Assert.True(
            result.Changed);
        Assert.False(
            result.Applied);
        Assert.Null(
            result.PreviousRevision);
        Assert.Equal(
            new ArtifactRevision(
                1),
            result.TargetRevision);
        Assert.Equal(
            SpecArtifactKind.RequirementSet,
            result.ArtifactKind);
        Assert.Equal(
            SpecWriteMode.DryRun,
            result.Mode);
        Assert.False(
            Directory.Exists(
                repository.Root));
    }

    [Fact]
    public void Apply_NewArtifactsAssignWorkspaceOwnedRevisionOne()
    {
        using TestRepository repository =
            new();
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        SpecStoreResult requirementResult =
            workspace.Store(
                CreateRequirementSet(
                    revision_: 99),
                Apply());
        SpecStoreResult workSpecResult =
            workspace.Store(
                CreateWorkSpec(
                    revision_: 99),
                Apply());
        SpecStoreResult planResult =
            workspace.Store(
                CreateImplementationPlan(
                    revision_: 99),
                Apply());
        SpecWorkspaceSnapshot snapshot =
            workspace.Load();

        Assert.All(
            new[]
            {
                requirementResult,
                workSpecResult,
                planResult
            },
            result_ =>
            {
                Assert.True(
                    result_.Changed);
                Assert.True(
                    result_.Applied);
                Assert.Equal(
                    new ArtifactRevision(
                        1),
                    result_.TargetRevision);
            });
        Assert.Equal(
            new ArtifactRevision(
                1),
            snapshot.RequirementSet!.Revision);
        Assert.Equal(
            new ArtifactRevision(
                1),
            snapshot.WorkSpec!.Revision);
        Assert.Equal(
            new ArtifactRevision(
                1),
            snapshot.ImplementationPlan!.Revision);
    }

    [Fact]
    public void Apply_SemanticTransitionsIncrementExactlyOnceIncludingReturnToPriorContent()
    {
        using TestRepository repository =
            new();
        SpecWorkspace workspace =
            repository.CreateWorkspace();
        RequirementSet original =
            CreateRequirementSet(
                statement_: "A");

        workspace.Store(
            original,
            Apply());
        SpecStoreResult second =
            workspace.Store(
                CreateRequirementSet(
                    statement_: "B"),
                Apply(
                    1));
        SpecStoreResult third =
            workspace.Store(
                original,
                Apply(
                    2));

        Assert.Equal(
            new ArtifactRevision(
                2),
            second.TargetRevision);
        Assert.Equal(
            new ArtifactRevision(
                3),
            third.TargetRevision);
        Assert.Equal(
            new ArtifactRevision(
                3),
            workspace.Load().RequirementSet!.Revision);
    }

    [Fact]
    public void DryRun_UpdatePlansNextRevisionAndPreservesCanonicalBytes()
    {
        using TestRepository repository =
            CreateRequirementRepository();
        byte[] before =
            repository.Read(
                SpecArtifactKind.RequirementSet);

        SpecStoreResult result =
            repository.CreateWorkspace().Store(
                CreateRequirementSet(
                    statement_: "Changed"),
                DryRun(
                    1));

        Assert.True(
            result.Changed);
        Assert.False(
            result.Applied);
        Assert.Equal(
            new ArtifactRevision(
                2),
            result.TargetRevision);
        Assert.Equal(
            before,
            repository.Read(
                SpecArtifactKind.RequirementSet));
    }

    [Fact]
    public void SemanticNoOpPrecedesRevisionConflictAndPreservesBytesRevisionAndTimestamp()
    {
        using TestRepository repository =
            CreateRequirementRepository();
        string path =
            repository.GetPath(
                SpecArtifactKind.RequirementSet);
        byte[] before =
            File.ReadAllBytes(
                path);
        DateTime timestamp =
            new(
                2020,
                1,
                2,
                3,
                4,
                5,
                DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(
            path,
            timestamp);

        SpecStoreResult result =
            repository.CreateWorkspace().Store(
                CreateRequirementSet(
                    revision_: 44),
                Apply(
                    expectedRevision_: null));

        Assert.False(
            result.Changed);
        Assert.False(
            result.Applied);
        Assert.Equal(
            new ArtifactRevision(
                1),
            result.TargetRevision);
        Assert.Equal(
            before,
            File.ReadAllBytes(
                path));
        Assert.Equal(
            timestamp,
            File.GetLastWriteTimeUtc(
                path));
    }

    [Fact]
    public void SemanticNoOpPreservesNonCanonicalFormattingAndAcceptedBom()
    {
        using TestRepository repository =
            new();
        string canonical =
            SpecJsonSerializer.Serialize(
                CreateRequirementSet());
        byte[] formattedWithBom =
        [
            0xEF,
            0xBB,
            0xBF,
            .. Encoding.UTF8.GetBytes(
                canonical.Replace(
                    ",",
                    ", ",
                    StringComparison.Ordinal))
        ];
        repository.WriteBytes(
            SpecArtifactKind.RequirementSet,
            formattedWithBom);

        SpecStoreResult result =
            repository.CreateWorkspace().Store(
                CreateRequirementSet(
                    revision_: 91),
                Apply(
                    75));

        Assert.False(
            result.Changed);
        Assert.Equal(
            formattedWithBom,
            repository.Read(
                SpecArtifactKind.RequirementSet));
    }

    [Fact]
    public void IdempotentRetryWithPriorRevisionReturnsNoOpAtCommittedRevision()
    {
        using TestRepository repository =
            CreateRequirementRepository();
        SpecWorkspace workspace =
            repository.CreateWorkspace();
        RequirementSet changed =
            CreateRequirementSet(
                statement_: "Changed");

        workspace.Store(
            changed,
            Apply(
                1));
        SpecStoreResult retry =
            workspace.Store(
                changed,
                Apply(
                    1));

        Assert.False(
            retry.Changed);
        Assert.False(
            retry.Applied);
        Assert.Equal(
            new ArtifactRevision(
                2),
            retry.TargetRevision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public void ExistingSemanticChangeRequiresMatchingExpectedRevision(
        int? expectedRevision_)
    {
        using TestRepository repository =
            CreateRequirementRepository();
        byte[] before =
            repository.Read(
                SpecArtifactKind.RequirementSet);

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    repository.CreateWorkspace().Store(
                        CreateRequirementSet(
                            statement_: "Changed"),
                        Apply(
                            expectedRevision_)));

        Assert.Equal(
            SpecPersistenceException.RevisionConflict,
            exception.ErrorCode);
        Assert.Equal(
            before,
            repository.Read(
                SpecArtifactKind.RequirementSet));
    }

    [Fact]
    public void MissingArtifactRejectsNonNullExpectedRevisionWithoutMutation()
    {
        using TestRepository repository =
            new();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    repository.CreateWorkspace().Store(
                        CreateRequirementSet(),
                        Apply(
                            1)));

        Assert.Equal(
            SpecPersistenceException.RevisionConflict,
            exception.ErrorCode);
        Assert.False(
            Directory.Exists(
                Path.Combine(
                    repository.Root,
                    ".ai")));
    }

    [Fact]
    public async Task ConcurrentDifferentWritesFromSameRevisionAllowExactlyOneCommit()
    {
        using TestRepository repository =
            CreateRequirementRepository();
        ManualResetEventSlim start =
            new(
                initialState: false);

        Task<Exception?> first =
            Task.Run(
                () =>
                    AttemptStore(
                        repository.CreateWorkspace(),
                        CreateRequirementSet(
                            statement_: "First"),
                        start));
        Task<Exception?> second =
            Task.Run(
                () =>
                    AttemptStore(
                        repository.CreateWorkspace(),
                        CreateRequirementSet(
                            statement_: "Second"),
                        start));

        start.Set();

        Exception?[] outcomes =
            await Task.WhenAll(
                first,
                second);

        Assert.Single(
            outcomes,
            outcome_ =>
                outcome_ is null);
        SpecPersistenceException conflict =
            Assert.IsType<SpecPersistenceException>(
                Assert.Single(
                    outcomes,
                    outcome_ =>
                        outcome_ is not null));
        Assert.Equal(
            SpecPersistenceException.RevisionConflict,
            conflict.ErrorCode);
        Assert.Equal(
            new ArtifactRevision(
                2),
            repository.CreateWorkspace().Load().RequirementSet!.Revision);
    }

    [Fact]
    public void WorkSpecRequiresCurrentRequirementSetBinding()
    {
        using TestRepository missing =
            new();

        SpecPersistenceException missingException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    missing.CreateWorkspace().Store(
                        CreateWorkSpec(),
                        DryRun()));

        Assert.Equal(
            SpecPersistenceException.MissingDependency,
            missingException.ErrorCode);

        using TestRepository mismatched =
            CreateRequirementRepository();

        SpecPersistenceException validationException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    mismatched.CreateWorkspace().Store(
                        CreateWorkSpec(
                            requirementRevision_: 2),
                        DryRun()));

        Assert.Equal(
            SpecPersistenceException.ValidationFailed,
            validationException.ErrorCode);
        Assert.False(
            File.Exists(
                mismatched.GetPath(
                    SpecArtifactKind.WorkSpec)));
    }

    [Fact]
    public void ImplementationPlanRequiresCurrentNonStaleDependenciesAndBinding()
    {
        using TestRepository missing =
            CreateRequirementRepository();

        SpecPersistenceException missingException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    missing.CreateWorkspace().Store(
                        CreateImplementationPlan(),
                        DryRun()));

        Assert.Equal(
            SpecPersistenceException.MissingDependency,
            missingException.ErrorCode);

        using TestRepository stale =
            CreateCurrentWorkSpecRepository();
        stale.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet(
                revision_: 2));

        SpecPersistenceException staleException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    stale.CreateWorkspace().Store(
                        CreateImplementationPlan(),
                        DryRun()));

        Assert.Equal(
            SpecPersistenceException.StaleDependency,
            staleException.ErrorCode);

        using TestRepository mismatched =
            CreateCurrentWorkSpecRepository();

        SpecPersistenceException validationException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    mismatched.CreateWorkspace().Store(
                        CreateImplementationPlan(
                            workSpecRevision_: 2),
                        DryRun()));

        Assert.Equal(
            SpecPersistenceException.ValidationFailed,
            validationException.ErrorCode);
    }

    [Fact]
    public void UpstreamUpdatesPreserveDownstreamBytesAndLoadThemAsStale()
    {
        using TestRepository repository =
            CreateFullRepository();
        byte[] workSpecBefore =
            repository.Read(
                SpecArtifactKind.WorkSpec);
        byte[] planBefore =
            repository.Read(
                SpecArtifactKind.ImplementationPlan);
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        workspace.Store(
            CreateRequirementSet(
                statement_: "Changed requirement"),
            Apply(
                1));
        SpecWorkspaceSnapshot afterRequirement =
            workspace.Load();

        Assert.Equal(
            workSpecBefore,
            repository.Read(
                SpecArtifactKind.WorkSpec));
        Assert.Equal(
            planBefore,
            repository.Read(
                SpecArtifactKind.ImplementationPlan));
        Assert.True(
            afterRequirement.IsWorkSpecStale);
        Assert.True(
            afterRequirement.IsImplementationPlanStale);
    }

    [Fact]
    public void WorkSpecUpdatePreservesPlanBytesAndMakesPlanStale()
    {
        using TestRepository repository =
            CreateFullRepository();
        byte[] planBefore =
            repository.Read(
                SpecArtifactKind.ImplementationPlan);

        repository.CreateWorkspace().Store(
            CreateWorkSpec(
                constraintStatement_: "Changed constraint"),
            Apply(
                1));
        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.Equal(
            planBefore,
            repository.Read(
                SpecArtifactKind.ImplementationPlan));
        Assert.True(
            snapshot.IsImplementationPlanStale);
    }

    [Fact]
    public void IdenticalStaleWorkSpecReturnsNoOpBeforeCurrentDependencyValidation()
    {
        using TestRepository repository =
            CreateCurrentWorkSpecRepository();
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        workspace.Store(
            CreateRequirementSet(
                statement_: "Changed requirement"),
            Apply(
                1));
        byte[] before =
            repository.Read(
                SpecArtifactKind.WorkSpec);

        SpecStoreResult result =
            workspace.Store(
                CreateWorkSpec(
                    revision_: 99),
                Apply(
                    99));

        Assert.False(
            result.Changed);
        Assert.False(
            result.Applied);
        Assert.Equal(
            new ArtifactRevision(
                1),
            result.TargetRevision);
        Assert.Equal(
            before,
            repository.Read(
                SpecArtifactKind.WorkSpec));
    }

    [Fact]
    public void IdenticalTransitivelyStalePlanReturnsNoOpBeforeStaleDependencyValidation()
    {
        using TestRepository repository =
            CreateFullRepository();
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        workspace.Store(
            CreateRequirementSet(
                statement_: "Changed requirement"),
            Apply(
                1));
        byte[] before =
            repository.Read(
                SpecArtifactKind.ImplementationPlan);

        SpecStoreResult result =
            workspace.Store(
                CreateImplementationPlan(
                    revision_: 99),
                Apply(
                    99));

        Assert.False(
            result.Changed);
        Assert.False(
            result.Applied);
        Assert.Equal(
            new ArtifactRevision(
                1),
            result.TargetRevision);
        Assert.Equal(
            before,
            repository.Read(
                SpecArtifactKind.ImplementationPlan));
    }

    [Fact]
    public void IdenticalRevisionStalePlanReturnsNoOpAfterWorkSpecAdvances()
    {
        using TestRepository repository =
            CreateFullRepository();
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        workspace.Store(
            CreateWorkSpec(
                constraintStatement_: "Changed constraint"),
            Apply(
                1));
        byte[] before =
            repository.Read(
                SpecArtifactKind.ImplementationPlan);

        SpecStoreResult result =
            workspace.Store(
                CreateImplementationPlan(
                    revision_: 99),
                Apply(
                    99));

        Assert.False(
            result.Changed);
        Assert.False(
            result.Applied);
        Assert.Equal(
            new ArtifactRevision(
                1),
            result.TargetRevision);
        Assert.Equal(
            before,
            repository.Read(
                SpecArtifactKind.ImplementationPlan));
    }

    [Fact]
    public void DifferentPlanAgainstStaleWorkSpecStillRejectsStaleDependency()
    {
        using TestRepository repository =
            CreateFullRepository();
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        workspace.Store(
            CreateRequirementSet(
                statement_: "Changed requirement"),
            Apply(
                1));
        byte[] before =
            repository.Read(
                SpecArtifactKind.ImplementationPlan);
        ImplementationPlan original =
            CreateImplementationPlan();
        ImplementationPlan changed =
            original with
            {
                Steps =
                [
                    original.Steps[0] with
                    {
                        Statement =
                            "Changed plan"
                    }
                ]
            };

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    workspace.Store(
                        changed,
                        Apply(
                            1)));

        Assert.Equal(
            SpecPersistenceException.StaleDependency,
            exception.ErrorCode);
        Assert.Equal(
            before,
            repository.Read(
                SpecArtifactKind.ImplementationPlan));
    }

    [Fact]
    public void InvalidCandidateAndCorruptedWorkspaceAreRejectedBeforeMutation()
    {
        using TestRepository invalid =
            new();
        RequirementSet invalidRequirementSet =
            CreateRequirementSet() with
            {
                ArtifactIdentity =
                    "wrong"
            };

        SpecPersistenceException invalidException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    invalid.CreateWorkspace().Store(
                        invalidRequirementSet,
                        Apply()));

        Assert.Equal(
            SpecPersistenceException.ValidationFailed,
            invalidException.ErrorCode);
        Assert.False(
            Directory.Exists(
                Path.Combine(
                    invalid.Root,
                    ".ai")));

        using TestRepository corrupted =
            new();
        corrupted.WriteBytes(
            SpecArtifactKind.RequirementSet,
            Encoding.UTF8.GetBytes(
                "{"));
        byte[] before =
            corrupted.Read(
                SpecArtifactKind.RequirementSet);

        SpecPersistenceException corruptedException =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    corrupted.CreateWorkspace().Store(
                        CreateRequirementSet(),
                        Apply()));

        Assert.Equal(
            SpecPersistenceException.InvalidJson,
            corruptedException.ErrorCode);
        Assert.Equal(
            before,
            corrupted.Read(
                SpecArtifactKind.RequirementSet));
    }

    [Fact]
    public void OversizedOutputRejectsBeforeMutation()
    {
        using TestRepository repository =
            new();
        RequirementSet oversized =
            CreateRequirementSet(
                statement_: new string(
                    'x',
                    SpecWorkspace.MaximumArtifactSizeBytes));

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    repository.CreateWorkspace().Store(
                        oversized,
                        Apply()));

        Assert.Equal(
            SpecPersistenceException.ArtifactTooLarge,
            exception.ErrorCode);
        Assert.False(
            Directory.Exists(
                Path.Combine(
                    repository.Root,
                    ".ai")));
    }

    [Fact]
    public void ApplyWritesUtf8WithoutBomLoadsSuccessfullyAndLeavesNoTempFiles()
    {
        using TestRepository repository =
            new();

        repository.CreateWorkspace().Store(
            CreateRequirementSet(),
            Apply());
        byte[] bytes =
            repository.Read(
                SpecArtifactKind.RequirementSet);

        Assert.False(
            bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF);
        Assert.NotNull(
            repository.CreateWorkspace().Load().RequirementSet);
        Assert.Empty(
            Directory.GetFiles(
                repository.SpecDirectory,
                ".*.tmp",
                SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void AtomicWriter_RevalidationFailurePreservesOldBytesAndRemovesTempFile()
    {
        using TestRepository repository =
            CreateRequirementRepository();
        string path =
            repository.GetPath(
                SpecArtifactKind.RequirementSet);
        byte[] before =
            File.ReadAllBytes(
                path);

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    SpecAtomicFileWriter.Write(
                        path,
                        Encoding.UTF8.GetBytes(
                            "replacement"),
                        () =>
                            throw new SpecPersistenceException(
                                SpecPersistenceException.WriteFailed,
                                "Injected pre-replacement failure.")));

        Assert.Equal(
            SpecPersistenceException.WriteFailed,
            exception.ErrorCode);
        Assert.Equal(
            before,
            File.ReadAllBytes(
                path));
        Assert.Empty(
            Directory.GetFiles(
                repository.SpecDirectory,
                ".*.tmp",
                SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ApplyCreatesOnlyCanonicalDirectoriesAndPreservesUnrelatedFile()
    {
        using TestRepository repository =
            new();
        string unrelated =
            Path.Combine(
                repository.Root,
                "unrelated.txt");
        File.WriteAllText(
            unrelated,
            "preserve");

        repository.CreateWorkspace().Store(
            CreateRequirementSet(),
            Apply());

        Assert.True(
            Directory.Exists(
                repository.SpecDirectory));
        Assert.Equal(
            "preserve",
            File.ReadAllText(
                unrelated));
        Assert.False(
            Directory.Exists(
                Path.Combine(
                    repository.Root,
                    ".ai",
                    "generated")));
    }

    [Fact]
    public void ApplyDoesNotCreateMissingRepositoryRoot()
    {
        using TestRepository repository =
            new(
                createRoot: false);

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    repository.CreateWorkspace().Store(
                        CreateRequirementSet(),
                        Apply()));

        Assert.Equal(
            SpecPersistenceException.WriteFailed,
            exception.ErrorCode);
        Assert.False(
            Directory.Exists(
                repository.Root));
    }

    [Fact]
    public void SemanticUpdateAtMaximumRevisionRejectsWithoutMutation()
    {
        using TestRepository repository =
            new();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet(
                revision_: int.MaxValue));
        byte[] before =
            repository.Read(
                SpecArtifactKind.RequirementSet);

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    repository.CreateWorkspace().Store(
                        CreateRequirementSet(
                            statement_: "Changed"),
                        Apply(
                            int.MaxValue)));

        Assert.Equal(
            SpecPersistenceException.WriteFailed,
            exception.ErrorCode);
        Assert.Equal(
            before,
            repository.Read(
                SpecArtifactKind.RequirementSet));
    }

    [Fact]
    public void ExistingCanonicalLinkIsRejectedBeforeMutationWhereSupported()
    {
        using TestRepository repository =
            new();
        Directory.CreateDirectory(
            repository.SpecDirectory);
        string target =
            Path.Combine(
                repository.Root,
                "target.json");
        File.WriteAllText(
            target,
            SpecJsonSerializer.Serialize(
                CreateRequirementSet()));

        try
        {
            File.CreateSymbolicLink(
                repository.GetPath(
                    SpecArtifactKind.RequirementSet),
                target);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            return;
        }

        string before =
            File.ReadAllText(
                target);

        Assert.Throws<SpecPersistenceException>(
            () =>
                repository.CreateWorkspace().Store(
                    CreateRequirementSet(
                        statement_: "Changed"),
                    Apply(
                        1)));
        Assert.Equal(
            before,
            File.ReadAllText(
                target));
    }

    private static Exception? AttemptStore(
        SpecWorkspace workspace_,
        RequirementSet requirementSet_,
        ManualResetEventSlim start_)
    {
        start_.Wait();

        try
        {
            workspace_.Store(
                requirementSet_,
                Apply(
                    1));

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static SpecStoreOptions DryRun(
        int? expectedRevision_ = null)
    {
        return new SpecStoreOptions
        {
            Mode =
                SpecWriteMode.DryRun,
            ExpectedCurrentRevision =
                expectedRevision_ is null
                    ? null
                    : new ArtifactRevision(
                        expectedRevision_.Value)
        };
    }

    private static SpecStoreOptions Apply(
        int? expectedRevision_ = null)
    {
        return new SpecStoreOptions
        {
            Mode =
                SpecWriteMode.Apply,
            ExpectedCurrentRevision =
                expectedRevision_ is null
                    ? null
                    : new ArtifactRevision(
                        expectedRevision_.Value)
        };
    }

    private static TestRepository CreateRequirementRepository()
    {
        TestRepository repository =
            new();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());

        return repository;
    }

    private static TestRepository CreateCurrentWorkSpecRepository()
    {
        TestRepository repository =
            CreateRequirementRepository();
        repository.Write(
            SpecArtifactKind.WorkSpec,
            CreateWorkSpec());

        return repository;
    }

    private static TestRepository CreateFullRepository()
    {
        TestRepository repository =
            CreateCurrentWorkSpecRepository();
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            CreateImplementationPlan());

        return repository;
    }

    private static RequirementSet CreateRequirementSet(
        int revision_ = 1,
        string statement_ = "Requirement")
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(
                    revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id =
                        new StableEntityId(
                            "INPUT-001"),
                    Text =
                        "Input"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id =
                        new StableEntityId(
                            "REQ-001"),
                    Statement =
                        statement_,
                    SourceInputIds =
                    [
                        new StableEntityId(
                            "INPUT-001")
                    ]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec(
        int revision_ = 1,
        int requirementRevision_ = 1,
        string constraintStatement_ = "Constraint")
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(
                    revision_),
            RequirementSetRevision =
                new ArtifactRevision(
                    requirementRevision_),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId(
                            "CON-001"),
                    Statement =
                        constraintStatement_,
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
                    ]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id =
                        new StableEntityId(
                            "AC-001"),
                    Statement =
                        "Criterion",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
                    ]
                }
            ]
        };
    }

    private static ImplementationPlan CreateImplementationPlan(
        int revision_ = 1,
        int workSpecRevision_ = 1)
    {
        return new ImplementationPlan
        {
            Revision =
                new ArtifactRevision(
                    revision_),
            WorkSpecRevision =
                new ArtifactRevision(
                    workSpecRevision_),
            Steps =
            [
                new PlanStep
                {
                    Id =
                        new StableEntityId(
                            "PLAN-STEP-001"),
                    Statement =
                        "Implement",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId(
                            "AC-001")
                    ]
                }
            ]
        };
    }

    private sealed class TestRepository :
        IDisposable
    {
        private readonly SpecArtifactPaths _paths;

        public TestRepository(
            bool createRoot = true)
        {
            this.Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "airepokit-spec-store-" +
                    Guid.NewGuid().ToString(
                        "N"));
            this._paths =
                new SpecArtifactPaths(
                    this.Root,
                    new SpecId(
                        "spec-1"));

            if (createRoot)
            {
                Directory.CreateDirectory(
                    this.Root);
            }
        }

        public string Root { get; }

        public string SpecDirectory =>
            this._paths.SpecDirectory;

        public SpecWorkspace CreateWorkspace()
        {
            return new SpecWorkspace(
                this.Root,
                new SpecId(
                    "spec-1"));
        }

        public string GetPath(
            SpecArtifactKind artifactKind_)
        {
            return this._paths.GetArtifactPath(
                artifactKind_);
        }

        public byte[] Read(
            SpecArtifactKind artifactKind_)
        {
            return File.ReadAllBytes(
                this.GetPath(
                    artifactKind_));
        }

        public void Write<T>(
            SpecArtifactKind artifactKind_,
            T artifact_)
        {
            this.WriteBytes(
                artifactKind_,
                Encoding.UTF8.GetBytes(
                    SpecJsonSerializer.Serialize(
                        artifact_)));
        }

        public void WriteBytes(
            SpecArtifactKind artifactKind_,
            byte[] bytes_)
        {
            Directory.CreateDirectory(
                this.SpecDirectory);
            File.WriteAllBytes(
                this.GetPath(
                    artifactKind_),
                bytes_);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(
                    this.Root,
                    recursive: true);
            }
            catch
            {
            }
        }
    }
}

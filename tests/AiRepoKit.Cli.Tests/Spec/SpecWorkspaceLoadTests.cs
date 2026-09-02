using System.Text;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecWorkspaceLoadTests
{
    [Fact]
    public void Load_NonexistentWorkspaceReturnsEmptySnapshotAndCreatesNothing()
    {
        using TestRepository repository =
            new(
                createRoot: false);
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        SpecWorkspaceSnapshot snapshot =
            workspace.Load();

        Assert.True(
            snapshot.IsEmpty);
        Assert.Null(
            snapshot.RequirementSet);
        Assert.Null(
            snapshot.WorkSpec);
        Assert.Null(
            snapshot.ImplementationPlan);
        Assert.False(
            Directory.Exists(
                repository.Root));
    }

    [Fact]
    public void Load_ValidRequirementSetOnlyLoadsPartialWorkspace()
    {
        using TestRepository repository =
            new();
        RequirementSet requirementSet =
            CreateRequirementSet();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            requirementSet);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.NotNull(
            snapshot.RequirementSet);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                requirementSet),
            SpecJsonSerializer.Serialize(
                snapshot.RequirementSet));
        Assert.Null(
            snapshot.WorkSpec);
        Assert.Null(
            snapshot.ImplementationPlan);
        Assert.False(
            snapshot.IsEmpty);
        Assert.False(
            snapshot.IsWorkSpecStale);
        Assert.False(
            snapshot.IsImplementationPlanStale);
    }

    [Fact]
    public void Load_ValidRequirementSetAndWorkSpecLoadsPartialWorkspace()
    {
        using TestRepository repository =
            new();
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            requirementSet);
        repository.Write(
            SpecArtifactKind.WorkSpec,
            workSpec);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.NotNull(
            snapshot.RequirementSet);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                requirementSet),
            SpecJsonSerializer.Serialize(
                snapshot.RequirementSet));
        Assert.NotNull(
            snapshot.WorkSpec);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                workSpec),
            SpecJsonSerializer.Serialize(
                snapshot.WorkSpec));
        Assert.Null(
            snapshot.ImplementationPlan);
        Assert.False(
            snapshot.IsWorkSpecStale);
    }

    [Fact]
    public void Load_ValidFullStackLoadsCurrentWorkspace()
    {
        using TestRepository repository =
            CreateFullRepository();

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.NotNull(
            snapshot.RequirementSet);
        Assert.NotNull(
            snapshot.WorkSpec);
        Assert.NotNull(
            snapshot.ImplementationPlan);
        Assert.False(
            snapshot.IsWorkSpecStale);
        Assert.False(
            snapshot.IsImplementationPlanStale);
    }

    [Fact]
    public void Load_WorkSpecWithoutRequirementSetRejectsMissingDependency()
    {
        using TestRepository repository =
            new();
        repository.Write(
            SpecArtifactKind.WorkSpec,
            CreateWorkSpec());

        AssertPersistenceError(
            repository,
            SpecPersistenceException.MissingDependency,
            SpecArtifactKind.WorkSpec);
    }

    [Fact]
    public void Load_ImplementationPlanWithoutWorkSpecRejectsMissingDependency()
    {
        using TestRepository repository =
            new();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            CreateImplementationPlan());

        AssertPersistenceError(
            repository,
            SpecPersistenceException.MissingDependency,
            SpecArtifactKind.ImplementationPlan);
    }

    [Fact]
    public void Load_MalformedJsonRejectsInvalidJson()
    {
        using TestRepository repository =
            new();
        repository.WriteBytes(
            SpecArtifactKind.RequirementSet,
            Encoding.UTF8.GetBytes(
                "{"));

        AssertPersistenceError(
            repository,
            SpecPersistenceException.InvalidJson,
            SpecArtifactKind.RequirementSet);
    }

    [Fact]
    public void Load_UnknownJsonMemberRejectsInvalidJson()
    {
        using TestRepository repository =
            new();
        string json =
            SpecJsonSerializer.Serialize(
                CreateRequirementSet());
        repository.WriteBytes(
            SpecArtifactKind.RequirementSet,
            Encoding.UTF8.GetBytes(
                json.Insert(
                    1,
                    "\"unknown\":true,")));

        AssertPersistenceError(
            repository,
            SpecPersistenceException.InvalidJson,
            SpecArtifactKind.RequirementSet);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_UnsupportedSchemaRejectsValidation(
        bool changeSchemaId_)
    {
        using TestRepository repository =
            new();
        RequirementSet requirementSet =
            changeSchemaId_
                ? CreateRequirementSet() with
                {
                    SchemaId =
                        "unsupported"
                }
                : CreateRequirementSet() with
                {
                    SchemaVersion =
                        SpecSchema.SchemaVersion + 1
                };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            requirementSet);

        SpecPersistenceException exception =
            AssertPersistenceError(
                repository,
                SpecPersistenceException.ValidationFailed,
                SpecArtifactKind.RequirementSet);

        Assert.Contains(
            exception.ValidationErrors,
            error_ =>
                error_.Code ==
                (changeSchemaId_
                    ? SpecValidationErrorCodes.UnsupportedSchemaId
                    : SpecValidationErrorCodes.UnsupportedSchemaVersion));
    }

    [Fact]
    public void Load_InvalidStableEntityIdConverterInputRejectsInvalidJson()
    {
        using TestRepository repository =
            new();
        string json =
            SpecJsonSerializer
                .Serialize(
                    CreateRequirementSet())
                .Replace(
                    "REQ-001",
                    "invalid",
                    StringComparison.Ordinal);
        repository.WriteBytes(
            SpecArtifactKind.RequirementSet,
            Encoding.UTF8.GetBytes(
                json));

        AssertPersistenceError(
            repository,
            SpecPersistenceException.InvalidJson,
            SpecArtifactKind.RequirementSet);
    }

    [Fact]
    public void Load_ArtifactLargerThanBoundRejectsBeforeDeserialization()
    {
        using TestRepository repository =
            new();
        repository.WriteBytes(
            SpecArtifactKind.RequirementSet,
            new byte[SpecWorkspace.MaximumArtifactSizeBytes + 1]);

        AssertPersistenceError(
            repository,
            SpecPersistenceException.ArtifactTooLarge,
            SpecArtifactKind.RequirementSet);
    }

    [Fact]
    public void Load_InvalidUtf8RejectsWithoutReplacementCharacters()
    {
        using TestRepository repository =
            new();
        repository.WriteBytes(
            SpecArtifactKind.RequirementSet,
            [0xC3, 0x28]);

        AssertPersistenceError(
            repository,
            SpecPersistenceException.InvalidUtf8,
            SpecArtifactKind.RequirementSet);
    }

    [Fact]
    public void Load_Utf8BomIsAccepted()
    {
        using TestRepository repository =
            new();
        byte[] json =
            Encoding.UTF8.GetBytes(
                SpecJsonSerializer.Serialize(
                    CreateRequirementSet()));
        repository.WriteBytes(
            SpecArtifactKind.RequirementSet,
            [0xEF, 0xBB, 0xBF, .. json]);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.NotNull(
            snapshot.RequirementSet);
    }

    [Fact]
    public void Load_ExistingArtifactSymbolicLinkRejectsWhereSupported()
    {
        using TestRepository repository =
            new();
        string target =
            Path.Combine(
                repository.Root,
                "target.json");
        File.WriteAllText(
            target,
            SpecJsonSerializer.Serialize(
                CreateRequirementSet()));
        string link =
            repository.GetPath(
                SpecArtifactKind.RequirementSet);
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        try
        {
            File.CreateSymbolicLink(
                link,
                target);
        }
        catch (Exception linkException) when (
            linkException is IOException or
            UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            return;
        }

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                workspace.Load);

        Assert.Equal(
            SpecPersistenceException.ReadFailed,
            exception.ErrorCode);
        Assert.Equal(
            SpecArtifactKind.RequirementSet,
            exception.ArtifactKind);
    }

    [Fact]
    public void Load_WorkSpecRevisionMismatchLoadsAndMarksStale()
    {
        using TestRepository repository =
            new();
        WorkSpec staleWorkSpec =
            CreateWorkSpec() with
            {
                RequirementSetRevision =
                    new ArtifactRevision(
                        1)
            };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            staleWorkSpec);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.NotNull(
            snapshot.WorkSpec);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                staleWorkSpec),
            SpecJsonSerializer.Serialize(
                snapshot.WorkSpec));
        Assert.True(
            snapshot.IsWorkSpecStale);
    }

    [Fact]
    public void Load_ImplementationPlanRevisionMismatchLoadsAndMarksStale()
    {
        using TestRepository repository =
            new();
        ImplementationPlan stalePlan =
            CreateImplementationPlan() with
            {
                WorkSpecRevision =
                    new ArtifactRevision(
                        4)
            };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            CreateWorkSpec());
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            stalePlan);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.NotNull(
            snapshot.ImplementationPlan);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                stalePlan),
            SpecJsonSerializer.Serialize(
                snapshot.ImplementationPlan));
        Assert.True(
            snapshot.IsImplementationPlanStale);
        Assert.False(
            snapshot.IsWorkSpecStale);
    }

    [Fact]
    public void Load_ImplementationPlanBecomesStaleWhenWorkSpecIsStale()
    {
        using TestRepository repository =
            new();
        WorkSpec staleWorkSpec =
            CreateWorkSpec() with
            {
                RequirementSetRevision =
                    new ArtifactRevision(
                        1)
            };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            staleWorkSpec);
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            CreateImplementationPlan());

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.True(
            snapshot.IsWorkSpecStale);
        Assert.True(
            snapshot.IsImplementationPlanStale);
    }

    [Fact]
    public void Load_TransitivelyStalePlanWithDanglingCurrentAcceptanceCriterionRejectsValidation()
    {
        using TestRepository repository =
            new();
        WorkSpec staleWorkSpec =
            CreateWorkSpec() with
            {
                RequirementSetRevision =
                    new ArtifactRevision(
                        1)
            };
        PlanStep step =
            CreateImplementationPlan().Steps[0] with
            {
                AcceptanceCriterionIds =
                [
                    new StableEntityId(
                        "AC-999")
                ]
            };
        ImplementationPlan invalidPlan =
            CreateImplementationPlan() with
            {
                Steps =
                [
                    step
                ]
            };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            staleWorkSpec);
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            invalidPlan);

        SpecPersistenceException exception =
            AssertPersistenceError(
                repository,
                SpecPersistenceException.ValidationFailed,
                SpecArtifactKind.ImplementationPlan);

        Assert.Contains(
            exception.ValidationErrors,
            error_ =>
                error_.Code ==
                    SpecValidationErrorCodes.DanglingReference &&
                error_.TargetEntityId ==
                    "AC-999");
    }

    [Fact]
    public void Load_OlderPlanWithHistoricalAcceptanceCriterionDifferenceLoadsStale()
    {
        using TestRepository repository =
            new();
        WorkSpec currentWorkSpec =
            CreateWorkSpec() with
            {
                Revision =
                    new ArtifactRevision(
                        6),
                AcceptanceCriteria =
                [
                    CreateWorkSpec().AcceptanceCriteria[0] with
                    {
                        Id =
                            new StableEntityId(
                                "AC-002")
                    }
                ]
            };
        ImplementationPlan stalePlan =
            CreateImplementationPlan();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            currentWorkSpec);
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            stalePlan);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.False(
            snapshot.IsWorkSpecStale);
        Assert.True(
            snapshot.IsImplementationPlanStale);
        Assert.NotNull(
            snapshot.ImplementationPlan);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                stalePlan),
            SpecJsonSerializer.Serialize(
                snapshot.ImplementationPlan));
    }

    [Fact]
    public void Load_TransitivelyStalePlanWithHistoricalRequirementDifferenceLoadsStale()
    {
        using TestRepository repository =
            new();
        RequirementSet newerRequirementSet =
            CreateRequirementSet() with
            {
                Revision =
                    new ArtifactRevision(
                        3),
                Requirements =
                [
                    new Requirement
                    {
                        Id =
                            new StableEntityId(
                                "REQ-002"),
                        Statement =
                            "Replacement requirement",
                        SourceInputIds =
                        [
                            new StableEntityId(
                                "INPUT-001")
                        ]
                    }
                ]
            };
        WorkSpec staleWorkSpec =
            CreateWorkSpec();
        ImplementationPlan stalePlan =
            CreateImplementationPlan();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            newerRequirementSet);
        repository.Write(
            SpecArtifactKind.WorkSpec,
            staleWorkSpec);
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            stalePlan);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.True(
            snapshot.IsWorkSpecStale);
        Assert.True(
            snapshot.IsImplementationPlanStale);
        Assert.NotNull(
            snapshot.ImplementationPlan);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                stalePlan),
            SpecJsonSerializer.Serialize(
                snapshot.ImplementationPlan));
    }

    [Fact]
    public void Load_CurrentWorkSpecWithIntrinsicFailureRejectsValidation()
    {
        using TestRepository repository =
            new();
        AcceptanceCriterion duplicate =
            CreateWorkSpec().AcceptanceCriteria[0];
        WorkSpec invalidWorkSpec =
            CreateWorkSpec() with
            {
                AcceptanceCriteria =
                [
                    duplicate,
                    duplicate
                ]
            };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            invalidWorkSpec);

        SpecPersistenceException exception =
            AssertPersistenceError(
                repository,
                SpecPersistenceException.ValidationFailed,
                SpecArtifactKind.WorkSpec);

        Assert.Contains(
            exception.ValidationErrors,
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.DuplicateEntityId);
    }

    [Fact]
    public void Load_CurrentPlanWithIntrinsicFailureRejectsValidation()
    {
        using TestRepository repository =
            new();
        PlanStep duplicate =
            CreateImplementationPlan().Steps[0];
        ImplementationPlan invalidPlan =
            CreateImplementationPlan() with
            {
                Steps =
                [
                    duplicate,
                    duplicate
                ]
            };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            CreateWorkSpec());
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            invalidPlan);

        SpecPersistenceException exception =
            AssertPersistenceError(
                repository,
                SpecPersistenceException.ValidationFailed,
                SpecArtifactKind.ImplementationPlan);

        Assert.Contains(
            exception.ValidationErrors,
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.DuplicateEntityId);
    }

    [Fact]
    public void Load_StaleWorkSpecWithOnlyNewerUpstreamDifferencesLoadsStale()
    {
        using TestRepository repository =
            new();
        RequirementSet newerRequirementSet =
            CreateRequirementSet() with
            {
                Revision =
                    new ArtifactRevision(
                        3),
                Requirements =
                [
                    new Requirement
                    {
                        Id =
                            new StableEntityId(
                                "REQ-002"),
                        Statement =
                            "Replacement requirement",
                        SourceInputIds =
                        [
                            new StableEntityId(
                                "INPUT-001")
                        ]
                    }
                ]
            };
        WorkSpec staleWorkSpec =
            CreateWorkSpec() with
            {
                RequirementSetRevision =
                    new ArtifactRevision(
                        2)
            };
        repository.Write(
            SpecArtifactKind.RequirementSet,
            newerRequirementSet);
        repository.Write(
            SpecArtifactKind.WorkSpec,
            staleWorkSpec);

        SpecWorkspaceSnapshot snapshot =
            repository.CreateWorkspace().Load();

        Assert.True(
            snapshot.IsWorkSpecStale);
        Assert.NotNull(
            snapshot.WorkSpec);
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                staleWorkSpec),
            SpecJsonSerializer.Serialize(
                snapshot.WorkSpec));
    }

    [Fact]
    public void Load_StaleWorkSpecWithIntrinsicFailuresStillRejects()
    {
        using TestRepository repository =
            new();
        WorkSpec baseWorkSpec =
            CreateWorkSpec() with
            {
                RequirementSetRevision =
                    new ArtifactRevision(
                        1)
            };

        WorkSpec[] invalidWorkSpecs =
        [
            baseWorkSpec with
            {
                AcceptanceCriteria =
                [
                    baseWorkSpec.AcceptanceCriteria[0] with
                    {
                        Id =
                            new StableEntityId(
                                "REQ-010")
                    }
                ]
            },
            baseWorkSpec with
            {
                AcceptanceCriteria =
                [
                    baseWorkSpec.AcceptanceCriteria[0],
                    baseWorkSpec.AcceptanceCriteria[0]
                ]
            },
            baseWorkSpec with
            {
                AcceptanceCriteria =
                [
                    baseWorkSpec.AcceptanceCriteria[0] with
                    {
                        RequirementIds =
                        [
                            new StableEntityId(
                                "AC-999")
                        ]
                    }
                ]
            }
        ];

        foreach (WorkSpec invalidWorkSpec in
                 invalidWorkSpecs)
        {
            repository.Write(
                SpecArtifactKind.RequirementSet,
                CreateRequirementSet());
            repository.Write(
                SpecArtifactKind.WorkSpec,
                invalidWorkSpec);

            SpecPersistenceException exception =
                AssertPersistenceError(
                    repository,
                    SpecPersistenceException.ValidationFailed,
                    SpecArtifactKind.WorkSpec);

            Assert.Contains(
                exception.ValidationErrors,
                error_ =>
                    error_.Code is
                        SpecValidationErrorCodes.InvalidEntityKind or
                        SpecValidationErrorCodes.DuplicateEntityId or
                        SpecValidationErrorCodes.InvalidReferenceTargetKind);
        }
    }

    [Fact]
    public void Load_RepeatedCallsAreDeterministicAndCreateNoAdditionalFiles()
    {
        using TestRepository repository =
            CreateFullRepository();
        string[] filesBefore =
            Directory.GetFiles(
                repository.Root,
                "*",
                SearchOption.AllDirectories);
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        SpecWorkspaceSnapshot first =
            workspace.Load();
        SpecWorkspaceSnapshot second =
            workspace.Load();
        string[] filesAfter =
            Directory.GetFiles(
                repository.Root,
                "*",
                SearchOption.AllDirectories);

        Assert.Equal(
            SpecJsonSerializer.Serialize(
                first.RequirementSet!),
            SpecJsonSerializer.Serialize(
                second.RequirementSet!));
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                first.WorkSpec!),
            SpecJsonSerializer.Serialize(
                second.WorkSpec!));
        Assert.Equal(
            SpecJsonSerializer.Serialize(
                first.ImplementationPlan!),
            SpecJsonSerializer.Serialize(
                second.ImplementationPlan!));
        Assert.Equal(
            first.IsWorkSpecStale,
            second.IsWorkSpecStale);
        Assert.Equal(
            first.IsImplementationPlanStale,
            second.IsImplementationPlanStale);
        Assert.Equal(
            filesBefore.OrderBy(
                path_ =>
                    path_,
                StringComparer.Ordinal),
            filesAfter.OrderBy(
                path_ =>
                    path_,
                StringComparer.Ordinal));
    }

    [Fact]
    public void Load_SuccessAndFailureLeaveCanonicalBytesUnchanged()
    {
        using TestRepository repository =
            CreateFullRepository();
        Dictionary<string, byte[]> beforeSuccess =
            repository.ReadCanonicalBytes();

        repository.CreateWorkspace().Load();

        AssertCanonicalBytesEqual(
            beforeSuccess,
            repository.ReadCanonicalBytes());

        string requirementSetPath =
            repository.GetPath(
                SpecArtifactKind.RequirementSet);
        File.WriteAllText(
            requirementSetPath,
            "{");
        Dictionary<string, byte[]> beforeFailure =
            repository.ReadCanonicalBytes();

        AssertPersistenceError(
            repository,
            SpecPersistenceException.InvalidJson,
            SpecArtifactKind.RequirementSet);

        AssertCanonicalBytesEqual(
            beforeFailure,
            repository.ReadCanonicalBytes());
    }

    private static TestRepository CreateFullRepository()
    {
        TestRepository repository =
            new();
        repository.Write(
            SpecArtifactKind.RequirementSet,
            CreateRequirementSet());
        repository.Write(
            SpecArtifactKind.WorkSpec,
            CreateWorkSpec());
        repository.Write(
            SpecArtifactKind.ImplementationPlan,
            CreateImplementationPlan());

        return repository;
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(
                    2),
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
                        "Requirement",
                    SourceInputIds =
                    [
                        new StableEntityId(
                            "INPUT-001")
                    ]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec()
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(
                    5),
            RequirementSetRevision =
                new ArtifactRevision(
                    2),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId(
                            "CON-001"),
                    Statement =
                        "Constraint",
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

    private static ImplementationPlan CreateImplementationPlan()
    {
        return new ImplementationPlan
        {
            Revision =
                new ArtifactRevision(
                    3),
            WorkSpecRevision =
                new ArtifactRevision(
                    5),
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

    private static SpecPersistenceException AssertPersistenceError(
        TestRepository repository_,
        string errorCode_,
        SpecArtifactKind artifactKind_)
    {
        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    repository_.CreateWorkspace().Load());

        Assert.Equal(
            errorCode_,
            exception.ErrorCode);
        Assert.Equal(
            artifactKind_,
            exception.ArtifactKind);

        return exception;
    }

    private static void AssertCanonicalBytesEqual(
        IReadOnlyDictionary<string, byte[]> expected_,
        IReadOnlyDictionary<string, byte[]> actual_)
    {
        Assert.Equal(
            expected_.Keys.OrderBy(
                path_ =>
                    path_,
                StringComparer.Ordinal),
            actual_.Keys.OrderBy(
                path_ =>
                    path_,
                StringComparer.Ordinal));

        foreach ((string path, byte[] bytes) in
                 expected_)
        {
            Assert.Equal(
                bytes,
                actual_[path]);
        }
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
                    "airepokit-spec-workspace-" +
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
                    this._paths.SpecDirectory);
            }
        }

        public string Root { get; }

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

        public Dictionary<string, byte[]> ReadCanonicalBytes()
        {
            return Enum
                .GetValues<SpecArtifactKind>()
                .Select(
                    artifactKind_ =>
                        this.GetPath(
                            artifactKind_))
                .Where(
                    File.Exists)
                .ToDictionary(
                    path_ =>
                        path_,
                    File.ReadAllBytes,
                    StringComparer.Ordinal);
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
                this._paths.SpecDirectory);
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

using System.Text.Json;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Projection;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class ImplementationChecklistProjectorTests
{
    [Fact]
    public void Project_UsesCanonicalMetadataDigestPreservesStepOrderAndSortsReferences()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalPlan(
                repository);

        SpecWorkspaceSnapshot snapshot =
            service.Workspace.Load();

        ImplementationPlan canonicalPlan =
            snapshot.ImplementationPlan!;

        ImplementationChecklistProjection projection =
            ImplementationChecklistProjector.Project(
                new SpecId("spec-sample"),
                snapshot,
                null);

        Assert.Equal(
            "spec-sample",
            projection.SpecId);
        Assert.Equal(
            canonicalPlan.Revision,
            projection.PlanRevision);
        Assert.Equal(
            canonicalPlan.WorkSpecRevision,
            projection.WorkSpecRevision);
        Assert.Equal(
            SpecSemanticDigest.Compute(
                canonicalPlan),
            projection.SemanticDigest);

        Assert.False(
            projection.Stale);
        Assert.Equal(
            SpecApprovalStatus.NotApproved,
            projection.ApprovalStatus);

        Assert.Equal(
            new[]
            {
                "PLAN-STEP-010",
                "PLAN-STEP-002"
            },
            projection
                .Steps
                .Select(
                    step_ =>
                        step_.Id.Value)
                .ToArray());

        Assert.Equal(
            new[]
            {
                "REQ-001",
                "REQ-002"
            },
            projection
                .Steps[0]
                .RequirementIds
                .Select(
                    id_ =>
                        id_.Value)
                .ToArray());

        Assert.Equal(
            new[]
            {
                "AC-001",
                "AC-002"
            },
            projection
                .Steps[0]
                .AcceptanceCriterionIds
                .Select(
                    id_ =>
                        id_.Value)
                .ToArray());
    }

    [Fact]
    public void Project_CurrentApprovedPlan_UsesEvaluatorCurrent()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalPlan(
                repository,
                approveUpstream_: true,
                approvePlan_: true);

        SpecWorkspaceSnapshot snapshot =
            service.Workspace.Load();

        SpecApprovalLedger ledger =
            service.LedgerStore.Load()!;

        ImplementationChecklistProjection projection =
            ImplementationChecklistProjector.Project(
                new SpecId("spec-sample"),
                snapshot,
                ledger);

        SpecArtifactApprovalStatus expected =
            SpecApprovalStatusEvaluator.Evaluate(
                SpecArtifactKind.ImplementationPlan,
                snapshot,
                ledger)!;

        Assert.False(
            projection.Stale);
        Assert.Equal(
            SpecApprovalStatus.Current,
            expected.Status);
        Assert.Equal(
            expected.Status,
            projection.ApprovalStatus);
    }

    [Fact]
    public void Project_StalePreviouslyApprovedPlan_IsRenderableAndUsesEvaluatorStale()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalPlan(
                repository,
                approveUpstream_: true,
                approvePlan_: true);

        MakePlanStale(
            service);

        SpecWorkspaceSnapshot snapshot =
            service.Workspace.Load();

        SpecApprovalLedger ledger =
            service.LedgerStore.Load()!;

        ImplementationChecklistProjection projection =
            ImplementationChecklistProjector.Project(
                new SpecId("spec-sample"),
                snapshot,
                ledger);

        SpecArtifactApprovalStatus expected =
            SpecApprovalStatusEvaluator.Evaluate(
                SpecArtifactKind.ImplementationPlan,
                snapshot,
                ledger)!;

        Assert.True(
            projection.Stale);
        Assert.Equal(
            SpecApprovalStatus.Stale,
            expected.Status);
        Assert.Equal(
            expected.Status,
            projection.ApprovalStatus);

        string markdown =
            ImplementationChecklistProjector.ProjectMarkdown(
                projection);

        Assert.Contains(
            "Status: STALE",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Project_StalePlanWithoutPlanApprovalHistory_DoesNotCoerceStatus()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalPlan(
                repository);

        MakePlanStale(
            service);

        SpecWorkspaceSnapshot snapshot =
            service.Workspace.Load();

        SpecApprovalLedger? ledger =
            service.LedgerStore.Load();

        ImplementationChecklistProjection projection =
            ImplementationChecklistProjector.Project(
                new SpecId("spec-sample"),
                snapshot,
                ledger);

        Assert.True(
            projection.Stale);
        Assert.Equal(
            SpecApprovalStatus.NotApproved,
            projection.ApprovalStatus);

        string markdown =
            ImplementationChecklistProjector.ProjectMarkdown(
                projection);

        Assert.Contains(
            "Status: STALE",
            markdown,
            StringComparison.Ordinal);

        Assert.Contains(
            "Approval Status: NOT_APPROVED",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Project_MissingPlan_ThrowsStableFailure()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalWorkSpecWithoutPlan(
                repository);

        SpecWorkspaceSnapshot snapshot =
            service.Workspace.Load();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    ImplementationChecklistProjector.Project(
                        new SpecId("spec-sample"),
                        snapshot,
                        null));

        Assert.Equal(
            "Cannot project an implementation checklist because no canonical ImplementationPlan exists.",
            exception.Message);
    }

    [Fact]
    public void ProjectJson_IsByteStableAndHasExactAuthorizedShape()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalPlan(
                repository);

        ImplementationChecklistProjection projection =
            ImplementationChecklistProjector.Project(
                new SpecId("spec-sample"),
                service.Workspace.Load(),
                null);

        string first =
            ImplementationChecklistProjector.ProjectJson(
                projection);

        string second =
            ImplementationChecklistProjector.ProjectJson(
                projection);

        Assert.Equal(
            first,
            second);

        using JsonDocument document =
            JsonDocument.Parse(
                first);

        string[] topLevelNames =
            document
                .RootElement
                .EnumerateObject()
                .Select(
                    property_ =>
                        property_.Name)
                .ToArray();

        Assert.Equal(
            new[]
            {
                "approvalStatus",
                "planRevision",
                "semanticDigest",
                "specId",
                "stale",
                "steps",
                "workSpecRevision"
            },
            topLevelNames);

        JsonElement firstStep =
            document
                .RootElement
                .GetProperty(
                    "steps")[0];

        string[] stepNames =
            firstStep
                .EnumerateObject()
                .Select(
                    property_ =>
                        property_.Name)
                .ToArray();

        Assert.Equal(
            new[]
            {
                "acceptanceCriterionIds",
                "id",
                "requirementIds",
                "statement"
            },
            stepNames);

        Assert.Equal(
            "notApproved",
            document
                .RootElement
                .GetProperty(
                    "approvalStatus")
                .GetString());

        Assert.Matches(
            "^[0-9a-f]{64}$",
            document
                .RootElement
                .GetProperty(
                    "semanticDigest")
                .GetString()!);

        string[] forbidden =
        [
            "completed",
            "progress",
            "executed",
            "started",
            "failed",
            "assignee",
            "timestamp",
            "executionStatus",
            "taskStatus",
            "model",
            "agent",
            "prompt"
        ];

        foreach (string name in forbidden)
        {
            Assert.False(
                document
                    .RootElement
                    .TryGetProperty(
                        name,
                        out _));
        }
    }

    [Fact]
    public void ProjectMarkdown_IsByteStablePreservesStepOrderAndSortsReferences()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalPlan(
                repository);

        ImplementationChecklistProjection projection =
            ImplementationChecklistProjector.Project(
                new SpecId("spec-sample"),
                service.Workspace.Load(),
                null);

        string first =
            ImplementationChecklistProjector.ProjectMarkdown(
                projection);

        string second =
            ImplementationChecklistProjector.ProjectMarkdown(
                projection);

        Assert.Equal(
            first,
            second);

        int firstStep =
            first.IndexOf(
                "`PLAN-STEP-010`",
                StringComparison.Ordinal);

        int secondStep =
            first.IndexOf(
                "`PLAN-STEP-002`",
                StringComparison.Ordinal);

        Assert.True(
            firstStep >= 0);
        Assert.True(
            secondStep > firstStep);

        Assert.Contains(
            "Requirements: `REQ-001`, `REQ-002`",
            first,
            StringComparison.Ordinal);

        Assert.Contains(
            "Acceptance Criteria: `AC-001`, `AC-002`",
            first,
            StringComparison.Ordinal);

        Assert.Contains(
            "Semantic Digest: sha256:" +
            projection.SemanticDigest,
            first,
            StringComparison.Ordinal);

        Assert.Contains(
            "Status: CURRENT",
            first,
            StringComparison.Ordinal);

        Assert.Contains(
            "Approval Status: NOT_APPROVED",
            first,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectMarkdown_EscapesTextUsesLfAndExactlyOneFinalLf()
    {
        ImplementationChecklistProjection projection =
            new()
            {
                SpecId =
                    "spec-sample",
                PlanRevision =
                    new ArtifactRevision(1),
                WorkSpecRevision =
                    new ArtifactRevision(1),
                SemanticDigest =
                    new string(
                        'a',
                        64),
                Stale =
                    false,
                ApprovalStatus =
                    SpecApprovalStatus.NotApproved,
                Steps =
                [
                    new ImplementationChecklistStepProjection
                    {
                        Id =
                            new StableEntityId("PLAN-STEP-001"),
                        Statement =
                            "line1\nline2\t\\ & < >",
                        RequirementIds =
                            [],
                        AcceptanceCriterionIds =
                            []
                    }
                ]
            };

        string markdown =
            ImplementationChecklistProjector.ProjectMarkdown(
                projection);

        Assert.DoesNotContain(
            "\r",
            markdown,
            StringComparison.Ordinal);

        Assert.Contains(
            "<code>line1\\nline2\\t\\\\ &amp; &lt; &gt;</code>",
            markdown,
            StringComparison.Ordinal);

        Assert.EndsWith(
            "\n",
            markdown,
            StringComparison.Ordinal);

        Assert.False(
            markdown.EndsWith(
                "\n\n",
                StringComparison.Ordinal));

        string[] lines =
            markdown.Split(
                '\n');

        foreach (string line in lines)
        {
            Assert.Equal(
                line.TrimEnd(),
                line);
        }
    }

    [Fact]
    public void ProjectMarkdown_ZeroReferencesAndZeroSteps_AreExplicit()
    {
        ImplementationChecklistProjection noReferences =
            new()
            {
                SpecId =
                    "spec-sample",
                PlanRevision =
                    new ArtifactRevision(1),
                WorkSpecRevision =
                    new ArtifactRevision(1),
                SemanticDigest =
                    new string(
                        'b',
                        64),
                Stale =
                    false,
                ApprovalStatus =
                    SpecApprovalStatus.NotApproved,
                Steps =
                [
                    new ImplementationChecklistStepProjection
                    {
                        Id =
                            new StableEntityId("PLAN-STEP-001"),
                        Statement =
                            "Statement",
                        RequirementIds =
                            [],
                        AcceptanceCriterionIds =
                            []
                    }
                ]
            };

        string referencesMarkdown =
            ImplementationChecklistProjector.ProjectMarkdown(
                noReferences);

        Assert.Contains(
            "Requirements: _none_",
            referencesMarkdown,
            StringComparison.Ordinal);

        Assert.Contains(
            "Acceptance Criteria: _none_",
            referencesMarkdown,
            StringComparison.Ordinal);

        ImplementationChecklistProjection noSteps =
            noReferences with
            {
                Steps =
                    []
            };

        string noStepsMarkdown =
            ImplementationChecklistProjector.ProjectMarkdown(
                noSteps);

        Assert.Contains(
            "## Steps\n\n_None._\n",
            noStepsMarkdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Project_DoesNotMutateSnapshotPlanLedgerOrFilesystem()
    {
        using TestRepository repository =
            new();

        SpecLifecycleService service =
            CreateCanonicalPlan(
                repository,
                approveUpstream_: true,
                approvePlan_: true);

        SpecWorkspaceSnapshot snapshot =
            service.Workspace.Load();

        ImplementationPlan plan =
            snapshot.ImplementationPlan!;

        SpecApprovalLedger ledger =
            service.LedgerStore.Load()!;

        string[] requirementOrderBefore =
            plan
                .Steps[0]
                .RequirementIds
                .Select(
                    id_ =>
                        id_.Value)
                .ToArray();

        string[] acceptanceOrderBefore =
            plan
                .Steps[0]
                .AcceptanceCriterionIds
                .Select(
                    id_ =>
                        id_.Value)
                .ToArray();

        string[] approvalOrderBefore =
            ledger
                .Approvals
                .Select(
                    approval_ =>
                        approval_.Id.Value)
                .ToArray();

        IReadOnlyDictionary<string, string> filesBefore =
            CaptureFiles(
                repository.Root);

        ImplementationChecklistProjection projection =
            ImplementationChecklistProjector.Project(
                new SpecId("spec-sample"),
                snapshot,
                ledger);

        _ =
            ImplementationChecklistProjector.ProjectJson(
                projection);

        _ =
            ImplementationChecklistProjector.ProjectMarkdown(
                projection);

        Assert.Equal(
            requirementOrderBefore,
            plan
                .Steps[0]
                .RequirementIds
                .Select(
                    id_ =>
                        id_.Value)
                .ToArray());

        Assert.Equal(
            acceptanceOrderBefore,
            plan
                .Steps[0]
                .AcceptanceCriterionIds
                .Select(
                    id_ =>
                        id_.Value)
                .ToArray());

        Assert.Equal(
            approvalOrderBefore,
            ledger
                .Approvals
                .Select(
                    approval_ =>
                        approval_.Id.Value)
                .ToArray());

        Assert.Equal(
            filesBefore,
            CaptureFiles(
                repository.Root));
    }

    private static SpecLifecycleService CreateCanonicalPlan(
        TestRepository repository_,
        bool approveUpstream_ = false,
        bool approvePlan_ = false)
    {
        if (approvePlan_ &&
            !approveUpstream_)
        {
            throw new ArgumentException(
                "Plan approval requires approved upstream artifacts.",
                nameof(approvePlan_));
        }

        SpecLifecycleService service =
            repository_.CreateService();

        service.InitializeRequirementSet(
            CreateRequirementSet(),
            Apply());

        if (approveUpstream_)
        {
            service.ApproveRequirementSet(
                new ArtifactRevision(1),
                Apply());
        }

        service.RefineWorkSpec(
            CreateWorkSpec(),
            Apply());

        if (approveUpstream_)
        {
            service.ApproveWorkSpec(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode =
                        SpecWriteMode.Apply,
                    ExpectedCurrentRevision =
                        new ArtifactRevision(1)
                });
        }

        service.RefineImplementationPlan(
            CreateImplementationPlan(),
            Apply());

        if (approvePlan_)
        {
            service.ApproveImplementationPlan(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode =
                        SpecWriteMode.Apply,
                    ExpectedCurrentRevision =
                        new ArtifactRevision(2)
                });
        }

        return service;
    }

    private static SpecLifecycleService CreateCanonicalWorkSpecWithoutPlan(
        TestRepository repository_)
    {
        SpecLifecycleService service =
            repository_.CreateService();

        service.InitializeRequirementSet(
            CreateRequirementSet(),
            Apply());

        service.RefineWorkSpec(
            CreateWorkSpec(),
            Apply());

        return service;
    }

    private static void MakePlanStale(
        SpecLifecycleService service_)
    {
        service_.RefineWorkSpec(
            CreateWorkSpec(
                revision_: 99,
                criterionOneStatement_:
                    "Changed acceptance criterion"),
            new SpecStoreOptions
            {
                Mode =
                    SpecWriteMode.Apply,
                ExpectedCurrentRevision =
                    new ArtifactRevision(1)
            });
    }

    private static SpecStoreOptions Apply()
    {
        return new SpecStoreOptions
        {
            Mode =
                SpecWriteMode.Apply
        };
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Inputs =
            [
                new RequirementInput
                {
                    Id =
                        new StableEntityId("INPUT-001"),
                    Text =
                        "Input"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id =
                        new StableEntityId("REQ-001"),
                    Statement =
                        "Requirement one",
                    SourceInputIds =
                    [
                        new StableEntityId("INPUT-001")
                    ]
                },
                new Requirement
                {
                    Id =
                        new StableEntityId("REQ-002"),
                    Statement =
                        "Requirement two",
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
        string criterionOneStatement_ = "Criterion one")
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(
                    revision_),
            RequirementSetRevision =
                new ArtifactRevision(1),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId("CON-001"),
                    Statement =
                        "Constraint",
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
                    Id =
                        new StableEntityId("AC-001"),
                    Statement =
                        criterionOneStatement_,
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ]
                },
                new AcceptanceCriterion
                {
                    Id =
                        new StableEntityId("AC-002"),
                    Statement =
                        "Criterion two",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-002")
                    ]
                }
            ]
        };
    }

    private static ImplementationPlan CreateImplementationPlan()
    {
        return new ImplementationPlan
        {
            WorkSpecRevision =
                new ArtifactRevision(1),
            Steps =
            [
                new PlanStep
                {
                    Id =
                        new StableEntityId("PLAN-STEP-010"),
                    Statement =
                        "First canonical step",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-002"),
                        new StableEntityId("REQ-001")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId("AC-002"),
                        new StableEntityId("AC-001")
                    ]
                },
                new PlanStep
                {
                    Id =
                        new StableEntityId("PLAN-STEP-002"),
                    Statement =
                        "Second canonical step",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-002")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId("AC-002")
                    ]
                }
            ]
        };
    }

    private static IReadOnlyDictionary<string, string> CaptureFiles(
        string root_)
    {
        return Directory
            .GetFiles(
                root_,
                "*",
                SearchOption.AllDirectories)
            .OrderBy(
                path_ =>
                    path_,
                StringComparer.Ordinal)
            .ToDictionary(
                path_ =>
                    Path.GetRelativePath(
                        root_,
                        path_),
                path_ =>
                    Convert.ToBase64String(
                        File.ReadAllBytes(
                            path_)),
                StringComparer.Ordinal);
    }

    private sealed class TestRepository :
        IDisposable
    {
        public TestRepository()
        {
            this.Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "airepokit-checklist-projector-test-" +
                    Guid
                        .NewGuid()
                        .ToString(
                            "N"));

            Directory.CreateDirectory(
                this.Root);
        }

        public string Root
        {
            get;
        }

        public SpecLifecycleService CreateService()
        {
            return new SpecLifecycleService(
                this.Root,
                new SpecId(
                    "spec-sample"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(
                        this.Root))
                {
                    Directory.Delete(
                        this.Root,
                        recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
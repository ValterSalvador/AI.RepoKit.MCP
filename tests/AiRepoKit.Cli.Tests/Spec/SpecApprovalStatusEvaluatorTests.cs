using AiRepoKit.Spec;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecApprovalStatusEvaluatorTests
{
    [Fact]
    public void Evaluate_RequirementSetExists_NoHistory_ReturnsNotApproved()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                approvals_: null);

        Assert.Single(statuses);
        SpecArtifactApprovalStatus status =
            statuses[0];
        Assert.Equal(SpecArtifactKind.RequirementSet, status.ArtifactKind);
        Assert.Equal(requirementSet.ArtifactIdentity, status.ArtifactIdentity);
        Assert.Equal(SpecApprovalStatus.NotApproved, status.Status);
    }

    [Fact]
    public void Evaluate_RequirementSetExists_ExactCurrentApproval_ReturnsCurrent()
    {
        RequirementSet requirementSet =
            CreateRequirementSet(revision_: 1);
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet);

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [approval]);

        Assert.Single(statuses);
        SpecArtifactApprovalStatus status =
            statuses[0];
        Assert.Equal(SpecArtifactKind.RequirementSet, status.ArtifactKind);
        Assert.Equal(SpecApprovalStatus.Current, status.Status);
    }

    [Fact]
    public void Evaluate_RequirementSetExists_OldRevisionHistory_ReturnsStale()
    {
        RequirementSet oldRequirementSet =
            CreateRequirementSet(revision_: 1);
        RequirementSet currentRequirementSet =
            CreateRequirementSet(
                revision_: 2,
                statement_: "Updated requirement");
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: currentRequirementSet);

        Approval oldApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                oldRequirementSet);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [oldApproval]);

        Assert.Single(statuses);
        SpecArtifactApprovalStatus status =
            statuses[0];
        Assert.Equal(SpecArtifactKind.RequirementSet, status.ArtifactKind);
        Assert.Equal(SpecApprovalStatus.Stale, status.Status);
    }

    [Fact]
    public void Evaluate_WorkSpecExists_NoHistory_ReturnsNotApproved()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                approvals_: null);

        Assert.Equal(2, statuses.Count);
        Assert.Equal(SpecApprovalStatus.NotApproved, statuses[0].Status);
        Assert.Equal(SpecApprovalStatus.NotApproved, statuses[1].Status);
        Assert.Equal(SpecArtifactKind.WorkSpec, statuses[1].ArtifactKind);
    }

    [Fact]
    public void Evaluate_WorkSpecExists_ExactApproval_RequirementSetCurrent_WorkSpecCurrent_ReturnsCurrent()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec,
                isWorkSpecStale: false);

        Approval reqApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);
        Approval workApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                workSpec);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [reqApproval, workApproval]);

        Assert.Equal(2, statuses.Count);
        Assert.Equal(SpecApprovalStatus.Current, statuses[0].Status);
        Assert.Equal(SpecApprovalStatus.Current, statuses[1].Status);
    }

    [Fact]
    public void Evaluate_WorkSpecExists_ApprovalExists_RequirementSetNotApproved_ReturnsStale()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec);

        Approval workApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                workSpec);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [workApproval]);

        SpecArtifactApprovalStatus workSpecStatus =
            statuses.First(s => s.ArtifactKind == SpecArtifactKind.WorkSpec);
        Assert.Equal(SpecApprovalStatus.Stale, workSpecStatus.Status);
    }

    [Fact]
    public void Evaluate_WorkSpecExists_ApprovalExists_RequirementSetStale_ReturnsStale()
    {
        RequirementSet oldReqSet =
            CreateRequirementSet(revision_: 1);
        RequirementSet currentReqSet =
            CreateRequirementSet(
                revision_: 2,
                statement_: "Updated");
        WorkSpec workSpec =
            CreateWorkSpec();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: currentReqSet,
                workSpec: workSpec);

        Approval oldReqApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                oldReqSet);
        Approval workApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                workSpec);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [oldReqApproval, workApproval]);

        SpecArtifactApprovalStatus reqStatus =
            statuses.First(s => s.ArtifactKind == SpecArtifactKind.RequirementSet);
        SpecArtifactApprovalStatus workSpecStatus =
            statuses.First(s => s.ArtifactKind == SpecArtifactKind.WorkSpec);

        Assert.Equal(SpecApprovalStatus.Stale, reqStatus.Status);
        Assert.Equal(SpecApprovalStatus.Stale, workSpecStatus.Status);
    }

    [Fact]
    public void Evaluate_WorkSpecExists_ApprovalExists_IsWorkSpecStale_ReturnsStale()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec,
                isWorkSpecStale: true);

        Approval reqApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);
        Approval workApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                workSpec);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [reqApproval, workApproval]);

        SpecArtifactApprovalStatus workSpecStatus =
            statuses.First(s => s.ArtifactKind == SpecArtifactKind.WorkSpec);
        Assert.Equal(SpecApprovalStatus.Stale, workSpecStatus.Status);
    }

    [Fact]
    public void Evaluate_ImplementationPlanExists_NoHistory_ReturnsNotApproved()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        ImplementationPlan plan =
            CreateImplementationPlan();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec,
                implementationPlan: plan);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                approvals_: null);

        Assert.Equal(3, statuses.Count);
        Assert.Equal(SpecApprovalStatus.NotApproved, statuses[2].Status);
        Assert.Equal(SpecArtifactKind.ImplementationPlan, statuses[2].ArtifactKind);
    }

    [Fact]
    public void Evaluate_PlanExists_ExactApproval_WorkSpecCurrent_PlanCurrent_ReturnsCurrent()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        ImplementationPlan plan =
            CreateImplementationPlan();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec,
                implementationPlan: plan,
                isWorkSpecStale: false,
                isPlanStale: false);

        Approval reqApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);
        Approval workApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                workSpec);
        Approval planApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-003"),
                plan);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [reqApproval, workApproval, planApproval]);

        Assert.Equal(3, statuses.Count);
        Assert.Equal(SpecApprovalStatus.Current, statuses[0].Status);
        Assert.Equal(SpecApprovalStatus.Current, statuses[1].Status);
        Assert.Equal(SpecApprovalStatus.Current, statuses[2].Status);
    }

    [Fact]
    public void Evaluate_PlanExists_ApprovalExists_WorkSpecNonCurrent_ReturnsStale()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        ImplementationPlan plan =
            CreateImplementationPlan();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec,
                implementationPlan: plan);

        Approval reqApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);
        Approval planApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-003"),
                plan);

        // WorkSpec has no approval (NotApproved) -> Plan must be Stale
        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [reqApproval, planApproval]);

        SpecArtifactApprovalStatus planStatus =
            statuses.First(s => s.ArtifactKind == SpecArtifactKind.ImplementationPlan);
        Assert.Equal(SpecApprovalStatus.Stale, planStatus.Status);
    }

    [Fact]
    public void Evaluate_PlanExists_ApprovalExists_IsImplementationPlanStale_ReturnsStale()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        WorkSpec workSpec =
            CreateWorkSpec();
        ImplementationPlan plan =
            CreateImplementationPlan();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec,
                implementationPlan: plan,
                isWorkSpecStale: false,
                isPlanStale: true);

        Approval reqApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);
        Approval workApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                workSpec);
        Approval planApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-003"),
                plan);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            SpecApprovalStatusEvaluator.Evaluate(
                snapshot,
                [reqApproval, workApproval, planApproval]);

        SpecArtifactApprovalStatus planStatus =
            statuses.First(s => s.ArtifactKind == SpecArtifactKind.ImplementationPlan);
        Assert.Equal(SpecApprovalStatus.Stale, planStatus.Status);
    }

    [Fact]
    public void Evaluate_AbsentCanonicalArtifact_DoesNotFabricateArtifactStatus()
    {
        // Empty snapshot
        SpecWorkspaceSnapshot emptySnapshot =
            CreateSnapshot();
        IReadOnlyList<SpecArtifactApprovalStatus> emptyStatuses =
            SpecApprovalStatusEvaluator.Evaluate(
                emptySnapshot,
                approvals_: null);
        Assert.Empty(emptyStatuses);

        // Only RequirementSet
        RequirementSet requirementSet =
            CreateRequirementSet();
        SpecWorkspaceSnapshot reqOnlySnapshot =
            CreateSnapshot(
                requirementSet: requirementSet);
        IReadOnlyList<SpecArtifactApprovalStatus> reqOnlyStatuses =
            SpecApprovalStatusEvaluator.Evaluate(
                reqOnlySnapshot,
                approvals_: null);
        Assert.Single(reqOnlyStatuses);
        Assert.Equal(SpecArtifactKind.RequirementSet, reqOnlyStatuses[0].ArtifactKind);

        // RequirementSet and WorkSpec (no Plan)
        WorkSpec workSpec =
            CreateWorkSpec();
        SpecWorkspaceSnapshot reqAndWorkSnapshot =
            CreateSnapshot(
                requirementSet: requirementSet,
                workSpec: workSpec);
        IReadOnlyList<SpecArtifactApprovalStatus> reqAndWorkStatuses =
            SpecApprovalStatusEvaluator.Evaluate(
                reqAndWorkSnapshot,
                approvals_: null);
        Assert.Equal(2, reqAndWorkStatuses.Count);
        Assert.DoesNotContain(
            reqAndWorkStatuses,
            s => s.ArtifactKind == SpecArtifactKind.ImplementationPlan);
    }

    [Fact]
    public void Evaluate_WithArtifactKind_ReturnsTargetStatusOrNull()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        SpecWorkspaceSnapshot snapshot =
            CreateSnapshot(
                requirementSet: requirementSet);

        SpecArtifactApprovalStatus? reqStatus =
            SpecApprovalStatusEvaluator.Evaluate(
                SpecArtifactKind.RequirementSet,
                snapshot,
                approvals_: null);

        Assert.NotNull(reqStatus);
        Assert.Equal(SpecApprovalStatus.NotApproved, reqStatus.Status);

        SpecArtifactApprovalStatus? workSpecStatus =
            SpecApprovalStatusEvaluator.Evaluate(
                SpecArtifactKind.WorkSpec,
                snapshot,
                approvals_: null);

        Assert.Null(workSpecStatus);
    }

    [Fact]
    public void Evaluate_NullSnapshot_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                SpecApprovalStatusEvaluator.Evaluate(
                    snapshot_: null!,
                    ledger_: null));
    }

    private static SpecWorkspaceSnapshot CreateSnapshot(
        RequirementSet? requirementSet = null,
        WorkSpec? workSpec = null,
        ImplementationPlan? implementationPlan = null,
        bool isWorkSpecStale = false,
        bool isPlanStale = false)
    {
        return new SpecWorkspaceSnapshot(
            requirementSet,
            workSpec,
            implementationPlan,
            isWorkSpecStale,
            isPlanStale);
    }

    private static RequirementSet CreateRequirementSet(
        int revision_ = 1,
        string statement_ = "Requirement statement")
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id =
                        new StableEntityId("INPUT-001"),
                    Text =
                        "Input text"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id =
                        new StableEntityId("REQ-001"),
                    Statement =
                        statement_,
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
        int requirementSetRevision_ = 1)
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(revision_),
            RequirementSetRevision =
                new ArtifactRevision(requirementSetRevision_),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId("CON-001"),
                    Statement =
                        "Constraint statement",
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
                        "Acceptance criterion statement",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
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
                new ArtifactRevision(revision_),
            WorkSpecRevision =
                new ArtifactRevision(workSpecRevision_),
            Steps =
            [
                new PlanStep
                {
                    Id =
                        new StableEntityId("PLAN-STEP-001"),
                    Statement =
                        "Step statement",
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
}

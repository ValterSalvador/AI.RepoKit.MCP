using AiRepoKit.Spec;
using AiRepoKit.Spec.Diff;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecDiffDownstreamTests
{
    private static readonly SpecId _specId = new("test-spec");

    // 19. Requirement semantic change identifies referencing WorkSpec constraints
    [Fact]
    public void RequirementSemanticChange_IdentifiesReferencingWorkSpecConstraints()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        RequirementSet candidate = rs with
        {
            Requirements =
            [
                rs.Requirements[0] with { Statement = "Changed REQ-001" },
                rs.Requirements[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        SpecReferenceImpact impact = Assert.Single(
            result.ReferenceImpacts,
            r_ => r_.ArtifactKind == SpecArtifactKind.WorkSpec && r_.EntityKind == SpecDiffEntityKind.Constraint);

        Assert.Equal("CON-001", impact.EntityId);
        Assert.Equal(["REQ-001"], impact.ReferencedChangedIds);
    }

    // 20. Requirement semantic change identifies referencing acceptance criteria
    [Fact]
    public void RequirementSemanticChange_IdentifiesReferencingAcceptanceCriteria()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        RequirementSet candidate = rs with
        {
            Requirements =
            [
                rs.Requirements[0] with { Statement = "Changed REQ-001" },
                rs.Requirements[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        SpecReferenceImpact impact = Assert.Single(
            result.ReferenceImpacts,
            r_ => r_.ArtifactKind == SpecArtifactKind.WorkSpec && r_.EntityKind == SpecDiffEntityKind.AcceptanceCriterion);

        Assert.Equal("AC-001", impact.EntityId);
        Assert.Equal(["REQ-001"], impact.ReferencedChangedIds);
    }

    // 21. Requirement semantic change identifies referencing PlanSteps
    [Fact]
    public void RequirementSemanticChange_IdentifiesReferencingPlanSteps()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        RequirementSet candidate = rs with
        {
            Requirements =
            [
                rs.Requirements[0] with { Statement = "Changed REQ-001" },
                rs.Requirements[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        SpecReferenceImpact impact = Assert.Single(
            result.ReferenceImpacts,
            r_ => r_.ArtifactKind == SpecArtifactKind.ImplementationPlan && r_.EntityKind == SpecDiffEntityKind.PlanStep);

        Assert.Equal("PLAN-STEP-001", impact.EntityId);
        Assert.Equal(["REQ-001"], impact.ReferencedChangedIds);
    }

    // 22. AcceptanceCriterion change identifies referencing PlanSteps
    [Fact]
    public void AcceptanceCriterionChange_IdentifiesReferencingPlanSteps()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        WorkSpec candidate = ws with
        {
            AcceptanceCriteria =
            [
                ws.AcceptanceCriteria[0] with { Statement = "Changed AC-001" },
                ws.AcceptanceCriteria[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshot, null, candidate);

        SpecReferenceImpact impact = Assert.Single(
            result.ReferenceImpacts,
            r_ => r_.ArtifactKind == SpecArtifactKind.ImplementationPlan && r_.EntityKind == SpecDiffEntityKind.PlanStep);

        Assert.Equal("PLAN-STEP-001", impact.EntityId);
        Assert.Equal(["AC-001"], impact.ReferencedChangedIds);
    }

    // 23. RequirementSet change reports WorkSpec/Plan stale impact
    [Fact]
    public void RequirementSetChange_ReportsWorkSpecAndPlanStaleImpact()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        SpecApprovalLedger ledger = CreateApprovedLedger(rs, ws, plan);

        RequirementSet candidate = rs with
        {
            Requirements =
            [
                rs.Requirements[0] with { Statement = "Changed" },
                rs.Requirements[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, ledger, candidate);

        SpecApprovalImpact wsImpact = Assert.Single(result.ApprovalImpacts, a_ => a_.ArtifactKind == SpecArtifactKind.WorkSpec);
        SpecApprovalImpact planImpact = Assert.Single(result.ApprovalImpacts, a_ => a_.ArtifactKind == SpecArtifactKind.ImplementationPlan);

        Assert.True(wsImpact.Affected);
        Assert.Equal(SpecApprovalStatus.Current, wsImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, wsImpact.ProposedStatus);

        Assert.True(planImpact.Affected);
        Assert.Equal(SpecApprovalStatus.Current, planImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, planImpact.ProposedStatus);
    }

    // 24. WorkSpec change reports Plan stale impact
    [Fact]
    public void WorkSpecChange_ReportsPlanStaleImpact()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        SpecApprovalLedger ledger = CreateApprovedLedger(rs, ws, plan);

        WorkSpec candidate = ws with
        {
            Constraints =
            [
                ws.Constraints[0] with { Statement = "Changed constraint" },
                ws.Constraints[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshot, ledger, candidate);

        SpecApprovalImpact planImpact = Assert.Single(result.ApprovalImpacts, a_ => a_.ArtifactKind == SpecArtifactKind.ImplementationPlan);
        Assert.True(planImpact.Affected);
        Assert.Equal(SpecApprovalStatus.Current, planImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, planImpact.ProposedStatus);
    }

    // 25. Plan change does not report WorkSpec stale
    [Fact]
    public void PlanChange_DoesNotReportWorkSpecStale()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        SpecApprovalLedger ledger = CreateApprovedLedger(rs, ws, plan);

        ImplementationPlan candidate = plan with
        {
            Steps =
            [
                plan.Steps[0] with { Statement = "Changed step statement" },
                plan.Steps[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeImplementationPlan(_specId, snapshot, ledger, candidate);

        SpecApprovalImpact reqImpact = Assert.Single(result.ApprovalImpacts, a_ => a_.ArtifactKind == SpecArtifactKind.RequirementSet);
        SpecApprovalImpact wsImpact = Assert.Single(result.ApprovalImpacts, a_ => a_.ArtifactKind == SpecArtifactKind.WorkSpec);
        SpecApprovalImpact planImpact = Assert.Single(result.ApprovalImpacts, a_ => a_.ArtifactKind == SpecArtifactKind.ImplementationPlan);

        Assert.False(reqImpact.Affected);
        Assert.Equal(SpecApprovalStatus.Current, reqImpact.ProposedStatus);

        Assert.False(wsImpact.Affected);
        Assert.Equal(SpecApprovalStatus.Current, wsImpact.ProposedStatus);

        Assert.True(planImpact.Affected);
        Assert.Equal(SpecApprovalStatus.Current, planImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, planImpact.ProposedStatus);
    }

    // 26. RequirementSet approved graph: Current -> Stale approval chain
    [Fact]
    public void RequirementSetApprovedGraph_CreatesCurrentToStaleChain()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        SpecApprovalLedger ledger = CreateApprovedLedger(rs, ws, plan);

        RequirementSet candidate = rs with
        {
            Requirements =
            [
                rs.Requirements[0] with { Statement = "Semantic modification" },
                rs.Requirements[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, ledger, candidate);

        Assert.Equal(3, result.ApprovalImpacts.Count);

        SpecApprovalImpact reqImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.RequirementSet);
        Assert.Equal(SpecApprovalStatus.Current, reqImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, reqImpact.ProposedStatus);
        Assert.True(reqImpact.Affected);

        SpecApprovalImpact wsImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.WorkSpec);
        Assert.Equal(SpecApprovalStatus.Current, wsImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, wsImpact.ProposedStatus);
        Assert.True(wsImpact.Affected);

        SpecApprovalImpact planImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.ImplementationPlan);
        Assert.Equal(SpecApprovalStatus.Current, planImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, planImpact.ProposedStatus);
        Assert.True(planImpact.Affected);
    }

    // 27. WorkSpec approved graph: Requirements stays Current; WorkSpec/Plan become Stale
    [Fact]
    public void WorkSpecApprovedGraph_RequirementsStaysCurrent_WorkSpecAndPlanBecomeStale()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        SpecApprovalLedger ledger = CreateApprovedLedger(rs, ws, plan);

        WorkSpec candidate = ws with
        {
            Constraints =
            [
                ws.Constraints[0] with { Statement = "Semantic modification" },
                ws.Constraints[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshot, ledger, candidate);

        SpecApprovalImpact reqImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.RequirementSet);
        Assert.Equal(SpecApprovalStatus.Current, reqImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Current, reqImpact.ProposedStatus);
        Assert.False(reqImpact.Affected);

        SpecApprovalImpact wsImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.WorkSpec);
        Assert.Equal(SpecApprovalStatus.Current, wsImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, wsImpact.ProposedStatus);
        Assert.True(wsImpact.Affected);

        SpecApprovalImpact planImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.ImplementationPlan);
        Assert.Equal(SpecApprovalStatus.Current, planImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, planImpact.ProposedStatus);
        Assert.True(planImpact.Affected);
    }

    // 28. Plan approved graph: upstream remains Current; Plan becomes Stale
    [Fact]
    public void PlanApprovedGraph_UpstreamRemainsCurrent_PlanBecomesStale()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        SpecApprovalLedger ledger = CreateApprovedLedger(rs, ws, plan);

        ImplementationPlan candidate = plan with
        {
            Steps =
            [
                plan.Steps[0] with { Statement = "Semantic modification" },
                plan.Steps[1]
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeImplementationPlan(_specId, snapshot, ledger, candidate);

        SpecApprovalImpact reqImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.RequirementSet);
        Assert.Equal(SpecApprovalStatus.Current, reqImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Current, reqImpact.ProposedStatus);
        Assert.False(reqImpact.Affected);

        SpecApprovalImpact wsImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.WorkSpec);
        Assert.Equal(SpecApprovalStatus.Current, wsImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Current, wsImpact.ProposedStatus);
        Assert.False(wsImpact.Affected);

        SpecApprovalImpact planImpact = result.ApprovalImpacts.Single(a_ => a_.ArtifactKind == SpecArtifactKind.ImplementationPlan);
        Assert.Equal(SpecApprovalStatus.Current, planImpact.CurrentStatus);
        Assert.Equal(SpecApprovalStatus.Stale, planImpact.ProposedStatus);
        Assert.True(planImpact.Affected);
    }

    // 29. No-op preserves approval statuses
    [Fact]
    public void NoOp_PreservesApprovalStatuses()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        SpecApprovalLedger ledger = CreateApprovedLedger(rs, ws, plan);

        RequirementSet candidate = CreateRequirementSetMulti();

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, ledger, candidate);

        Assert.False(result.SemanticChanged);
        Assert.All(result.ApprovalImpacts, a_ =>
        {
            Assert.False(a_.Affected);
            Assert.Equal(a_.CurrentStatus, a_.ProposedStatus);
            Assert.Equal(SpecApprovalStatus.Current, a_.CurrentStatus);
        });
    }

    // 30. SpecContext derived impact for RequirementSet/WorkSpec
    [Fact]
    public void SpecContext_DerivedImpact_ForRequirementSetAndWorkSpec()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();

        RequirementSet candRs = rs with
        {
            Requirements = [rs.Requirements[0] with { Statement = "New statement" }, rs.Requirements[1]]
        };

        SpecWorkspaceSnapshot snapshotRs = SpecWorkspaceValidator.Validate(rs, ws, null);
        SpecDiffResult resultRs = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshotRs, null, candRs);

        SpecDerivedArtifactImpact specContextImpactRs = resultRs.DerivedArtifactImpacts.Single(d_ => d_.ArtifactKind == SpecDerivedArtifactKind.SpecContext);
        Assert.True(specContextImpactRs.Affected);

        WorkSpec candWs = ws with
        {
            Constraints = [ws.Constraints[0] with { Statement = "New constraint" }, ws.Constraints[1]]
        };

        SpecDiffResult resultWs = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshotRs, null, candWs);
        SpecDerivedArtifactImpact specContextImpactWs = resultWs.DerivedArtifactImpacts.Single(d_ => d_.ArtifactKind == SpecDerivedArtifactKind.SpecContext);
        Assert.True(specContextImpactWs.Affected);
    }

    // 31. No SpecContext impact for Plan-only change
    [Fact]
    public void SpecContext_NoImpact_ForPlanOnlyChange()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        ImplementationPlan candPlan = plan with
        {
            Steps = [plan.Steps[0] with { Statement = "New step statement" }, plan.Steps[1]]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeImplementationPlan(_specId, snapshot, null, candPlan);

        SpecDerivedArtifactImpact specContextImpact = result.DerivedArtifactImpacts.Single(d_ => d_.ArtifactKind == SpecDerivedArtifactKind.SpecContext);
        Assert.False(specContextImpact.Affected);
    }

    // 32. Checklist derived impact for Plan change
    [Fact]
    public void Checklist_DerivedImpact_ForPlanChange()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        ImplementationPlan candPlan = plan with
        {
            Steps = [plan.Steps[0] with { Statement = "New step statement" }, plan.Steps[1]]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeImplementationPlan(_specId, snapshot, null, candPlan);

        SpecDerivedArtifactImpact checklistImpact = result.DerivedArtifactImpacts.Single(d_ => d_.ArtifactKind == SpecDerivedArtifactKind.ImplementationChecklist);
        Assert.True(checklistImpact.Affected);
    }

    // 33. Checklist impact when upstream change makes Plan stale
    [Fact]
    public void Checklist_DerivedImpact_WhenUpstreamChangeMakesPlanStale()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        RequirementSet candRs = rs with
        {
            Requirements = [rs.Requirements[0] with { Statement = "New statement" }, rs.Requirements[1]]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, plan);
        Assert.False(snapshot.IsImplementationPlanStale);

        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candRs);

        SpecDerivedArtifactImpact checklistImpact = result.DerivedArtifactImpacts.Single(d_ => d_.ArtifactKind == SpecDerivedArtifactKind.ImplementationChecklist);
        Assert.True(checklistImpact.Affected);
    }

    // 34. No canonical/ledger/generated writes during analyzer operation
    [Fact]
    public void NoWrites_DuringAnalyzerOperation()
    {
        using TestRepo repo = new();
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();

        string specDir = Path.Combine(repo.Root, ".ai", "specs", "test-spec");
        Directory.CreateDirectory(specDir);
        string rsFile = Path.Combine(specDir, "requirements.json");
        string wsFile = Path.Combine(specDir, "work-spec.json");
        string planFile = Path.Combine(specDir, "implementation-plan.json");

        File.WriteAllText(rsFile, SpecJsonSerializer.Serialize(rs));
        File.WriteAllText(wsFile, SpecJsonSerializer.Serialize(ws));
        File.WriteAllText(planFile, SpecJsonSerializer.Serialize(plan));

        DateTime rsTime = File.GetLastWriteTimeUtc(rsFile);
        DateTime wsTime = File.GetLastWriteTimeUtc(wsFile);
        DateTime planTime = File.GetLastWriteTimeUtc(planFile);

        SpecLifecycleService service = new(repo.Root, _specId);
        SpecWorkspaceSnapshot snapshot = service.Workspace.Load();
        SpecApprovalLedger? ledger = service.LedgerStore.Load();

        RequirementSet candidate = rs with
        {
            Requirements = [rs.Requirements[0] with { Statement = "Modified" }, rs.Requirements[1]]
        };

        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, ledger, candidate);
        Assert.True(result.SemanticChanged);

        Assert.Equal(rsTime, File.GetLastWriteTimeUtc(rsFile));
        Assert.Equal(wsTime, File.GetLastWriteTimeUtc(wsFile));
        Assert.Equal(planTime, File.GetLastWriteTimeUtc(planFile));
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".ai", "generated")));
    }

    private static RequirementSet CreateRequirementSetMulti()
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Statement 1", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "Statement 2", SourceInputIds = [new StableEntityId("INPUT-002")] }
            ]
        };
    }

    private static WorkSpec CreateWorkSpecMulti()
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Con 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-002"), Statement = "Con 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "AC 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "AC 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ]
        };
    }

    private static ImplementationPlan CreatePlanMulti()
    {
        return new ImplementationPlan
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Steps =
            [
                new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Step 1", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-002"), Statement = "Step 2", RequirementIds = [new StableEntityId("REQ-002")], AcceptanceCriterionIds = [new StableEntityId("AC-002")] }
            ]
        };
    }

    private static SpecApprovalLedger CreateApprovedLedger(RequirementSet rs_, WorkSpec ws_, ImplementationPlan plan_)
    {
        return new SpecApprovalLedger
        {
            SpecId = _specId.Value,
            Revision = new ArtifactRevision(3),
            Approvals =
            [
                SpecApprovalBinding.Create(new StableEntityId("APR-001"), rs_),
                SpecApprovalBinding.Create(new StableEntityId("APR-002"), ws_),
                SpecApprovalBinding.Create(new StableEntityId("APR-003"), plan_)
            ]
        };
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "airepokit-diff-downstream-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Root);
            Directory.CreateDirectory(Path.Combine(this.Root, ".git"));
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

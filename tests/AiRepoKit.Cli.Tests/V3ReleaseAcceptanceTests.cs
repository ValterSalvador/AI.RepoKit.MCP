using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Commands.Spec;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Verification;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class V3ReleaseAcceptanceTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Spec", "V1");

    [Fact]
    public void SpecSchemaV1_ConstantsAndFixtures_RemainUnchanged()
    {
        string schemaPath = Path.Combine(FixtureRoot, "schema", "spec-ir.schema.json");
        Assert.True(File.Exists(schemaPath), $"Schema file not found at: {schemaPath}");

        using JsonDocument schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        JsonElement root = schemaDoc.RootElement;

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
        Assert.Equal("https://ai.repokit.dev/schemas/spec/v1/spec-ir.schema.json", root.GetProperty("$id").GetString());
        Assert.Equal("AI.RepoKit Spec IR v1", root.GetProperty("title").GetString());

        // Validate that all canonical valid fixtures deserialize successfully
        string validDir = Path.Combine(FixtureRoot, "valid");
        string reqJson = File.ReadAllText(Path.Combine(validDir, "requirements.json"));
        RequirementSet reqSet = SpecJsonSerializer.Deserialize<RequirementSet>(reqJson);
        Assert.NotNull(reqSet);
        Assert.Equal(new ArtifactRevision(3), reqSet.Revision);

        string wsJson = File.ReadAllText(Path.Combine(validDir, "work-spec.json"));
        WorkSpec workSpec = SpecJsonSerializer.Deserialize<WorkSpec>(wsJson);
        Assert.NotNull(workSpec);
        Assert.Equal(new ArtifactRevision(5), workSpec.Revision);

        string planJson = File.ReadAllText(Path.Combine(validDir, "implementation-plan.json"));
        ImplementationPlan plan = SpecJsonSerializer.Deserialize<ImplementationPlan>(planJson);
        Assert.NotNull(plan);
        Assert.Equal(new ArtifactRevision(2), plan.Revision);

        string apprJson = File.ReadAllText(Path.Combine(validDir, "approval.json"));
        Approval approval = SpecJsonSerializer.Deserialize<Approval>(apprJson);
        Assert.NotNull(approval);
        Assert.Equal(new ArtifactRevision(5), approval.ArtifactRevision);

        string evdJson = File.ReadAllText(Path.Combine(validDir, "verification-evidence.json"));
        VerificationEvidence evidence = SpecJsonSerializer.Deserialize<VerificationEvidence>(evdJson);
        Assert.NotNull(evidence);

        string resJson = File.ReadAllText(Path.Combine(validDir, "verification-result.json"));
        VerificationResult result = SpecJsonSerializer.Deserialize<VerificationResult>(resJson);
        Assert.NotNull(result);
    }

    [Fact]
    public void ExistingSchemaV1_SpecWorkspace_LoadsSuccessfully()
    {
        using TestRepo repo = new();
        const string specId = "spec-v1-load";

        string specDir = Path.Combine(repo.Root, ".ai", "specs", specId);
        Directory.CreateDirectory(specDir);

        string validDir = Path.Combine(FixtureRoot, "valid");
        File.Copy(Path.Combine(validDir, "requirements.json"), Path.Combine(specDir, "requirements.json"));
        File.Copy(Path.Combine(validDir, "work-spec.json"), Path.Combine(specDir, "work-spec.json"));
        File.Copy(Path.Combine(validDir, "implementation-plan.json"), Path.Combine(specDir, "implementation-plan.json"));

        SpecWorkspace workspace = new(repo.Root, new SpecId(specId));
        SpecWorkspaceSnapshot snapshot = workspace.Load();

        Assert.False(snapshot.IsEmpty);
        Assert.NotNull(snapshot.RequirementSet);
        Assert.NotNull(snapshot.WorkSpec);
        Assert.NotNull(snapshot.ImplementationPlan);
        Assert.Equal(new ArtifactRevision(3), snapshot.RequirementSet.Revision);
        Assert.Equal(new ArtifactRevision(5), snapshot.WorkSpec.Revision);
        Assert.Equal(new ArtifactRevision(2), snapshot.ImplementationPlan.Revision);
    }

    [Fact]
    public void ApprovalInvalidation_EndToEnd_EvaluatesStaleWhenUpstreamChanges()
    {
        using TestRepo repo = new();
        const string specId = "spec-invalidation-e2e";
        SpecCommand specCommand = new();

        // 1. Init RequirementSet rev 1
        string reqCandidate1 = repo.WriteCandidate("req1.json", CreateRequirementSet(revision_: 1, statement_: "Initial requirement"));
        CommandResult initResult = specCommand.Execute(["init", "--spec-id", specId, "--from", reqCandidate1, "--repo", repo.Root, "--apply"]);
        Assert.True(initResult.Success, initResult.Markdown);

        // 2. Approve RequirementSet rev 1
        CommandResult apprReq = specCommand.Execute(["approve", "--spec-id", specId, "--artifact", "requirements", "--revision", "1", "--repo", repo.Root, "--apply"]);
        Assert.True(apprReq.Success, apprReq.Markdown);

        // 3. Refine WorkSpec rev 1
        string wsCandidate = repo.WriteCandidate("ws1.json", CreateWorkSpec(revision_: 1, reqRev_: 1));
        CommandResult refineWs = specCommand.Execute(["refine", "--spec-id", specId, "--artifact", "work-spec", "--from", wsCandidate, "--repo", repo.Root, "--apply"]);
        Assert.True(refineWs.Success, refineWs.Markdown);

        // 4. Approve WorkSpec rev 1
        CommandResult apprWs = specCommand.Execute(["approve", "--spec-id", specId, "--artifact", "work-spec", "--revision", "1", "--repo", repo.Root, "--apply"]);
        Assert.True(apprWs.Success, apprWs.Markdown);

        // 5. Plan ImplementationPlan rev 1
        string planCandidate = repo.WriteCandidate("plan1.json", CreateImplementationPlan(revision_: 1, wsRev_: 1));
        CommandResult planResult = specCommand.Execute(["plan", "--spec-id", specId, "--from", planCandidate, "--repo", repo.Root, "--apply"]);
        Assert.True(planResult.Success, planResult.Markdown);

        // 6. Approve ImplementationPlan rev 1
        CommandResult apprPlan = specCommand.Execute(["approve", "--spec-id", specId, "--artifact", "implementation-plan", "--revision", "1", "--repo", repo.Root, "--apply"]);
        Assert.True(apprPlan.Success, apprPlan.Markdown);

        // Verify initial state: all Current
        SpecWorkspace workspace = new(repo.Root, new SpecId(specId));
        SpecApprovalLedgerStore ledgerStore = new(repo.Root, new SpecId(specId));
        SpecWorkspaceSnapshot snapshotBefore = workspace.Load();
        SpecApprovalLedger ledgerBefore = ledgerStore.Load()!;
        IReadOnlyList<SpecArtifactApprovalStatus> statusBefore = SpecApprovalStatusEvaluator.Evaluate(snapshotBefore, ledgerBefore);

        Assert.Equal(SpecApprovalStatus.Current, statusBefore.First(s => s.ArtifactKind == SpecArtifactKind.RequirementSet).Status);
        Assert.Equal(SpecApprovalStatus.Current, statusBefore.First(s => s.ArtifactKind == SpecArtifactKind.WorkSpec).Status);
        Assert.Equal(SpecApprovalStatus.Current, statusBefore.First(s => s.ArtifactKind == SpecArtifactKind.ImplementationPlan).Status);

        // 7. Refine RequirementSet to rev 2 (semantic modification)
        string reqCandidate2 = repo.WriteCandidate("req2.json", CreateRequirementSet(revision_: 2, statement_: "Modified requirement causing upstream invalidation"));
        CommandResult refineReq2 = specCommand.Execute(["refine", "--spec-id", specId, "--artifact", "requirements", "--from", reqCandidate2, "--expected-revision", "1", "--repo", repo.Root, "--apply"]);
        Assert.True(refineReq2.Success, refineReq2.Markdown);

        // 8. Re-evaluate approval statuses
        SpecWorkspaceSnapshot snapshotAfter = workspace.Load();
        SpecApprovalLedger ledgerAfter = ledgerStore.Load()!;
        IReadOnlyList<SpecArtifactApprovalStatus> statusAfter = SpecApprovalStatusEvaluator.Evaluate(snapshotAfter, ledgerAfter);

        // RequirementSet rev 2 was previously approved at rev 1 -> Stale
        Assert.Equal(SpecApprovalStatus.Stale, statusAfter.First(s => s.ArtifactKind == SpecArtifactKind.RequirementSet).Status);

        // WorkSpec references RequirementSet rev 1, which is no longer current -> WorkSpec is Stale
        Assert.Equal(SpecApprovalStatus.Stale, statusAfter.First(s => s.ArtifactKind == SpecArtifactKind.WorkSpec).Status);

        // ImplementationPlan references WorkSpec whose approval is now Stale -> ImplementationPlan is Stale
        Assert.Equal(SpecApprovalStatus.Stale, statusAfter.First(s => s.ArtifactKind == SpecArtifactKind.ImplementationPlan).Status);
    }

    [Fact]
    public void SpecVerification_FrozenSemantics_PreservesOutcomes()
    {
        WorkSpec ws = CreateWorkSpec(revision_: 1, reqRev_: 1);
        AcceptanceCriterion ac1 = ws.AcceptanceCriteria[0];

        // Scenario 1: Missing / empty evidence => NOT_VERIFIED (Missing evidence is NEVER PASS)
        SpecVerificationReport reportMissing = SpecVerificationEvaluator.Evaluate(
            specId_: "spec-v1",
            requirementSetRevision_: new ArtifactRevision(1),
            workSpecRevision_: new ArtifactRevision(1),
            implementationPlanRevision_: new ArtifactRevision(1),
            evidence_: [],
            observations_: [],
            workSpec_: ws);

        Assert.Equal(VerificationStatus.NotVerified, reportMissing.OverallStatus);
        Assert.Equal(VerificationStatus.NotVerified, reportMissing.Results[0].Status);
        Assert.NotEqual(VerificationStatus.Pass, reportMissing.Results[0].Status);

        // Scenario 2: Recognized contradictory evidence => FAIL
        VerificationEvidence failEvidence = new()
        {
            Id = new StableEntityId("EVD-001"),
            AcceptanceCriterionIds = [ac1.Id],
            PlanStepIds = [],
            Description = "Unit test failed"
        };
        SpecVerificationEvidenceObservation failObs = new()
        {
            EvidenceId = failEvidence.Id,
            RepositoryEvidenceId = "build-summary",
            Source = "build-summary",
            Kind = "report",
            Reference = ".ai/generated/reports/latest-build-summary.json",
            Availability = RepositoryEvidenceAvailability.Available,
            Freshness = RepositoryEvidenceFreshness.Unknown,
            SourceGeneratedAt = "2026-09-10T10:00:00Z",
            Disposition = SpecVerificationEvidenceDisposition.Contradicting,
            Reason = "Test suite failed with exit code 1"
        };

        SpecVerificationReport reportFail = SpecVerificationEvaluator.Evaluate(
            specId_: "spec-v1",
            requirementSetRevision_: new ArtifactRevision(1),
            workSpecRevision_: new ArtifactRevision(1),
            implementationPlanRevision_: new ArtifactRevision(1),
            evidence_: [failEvidence],
            observations_: [failObs],
            workSpec_: ws);

        Assert.Equal(VerificationStatus.Fail, reportFail.OverallStatus);
        Assert.Equal(VerificationStatus.Fail, reportFail.Results[0].Status);

        // Scenario 3: Recognized supporting evidence with no contradiction => PASS
        VerificationEvidence passEvidence = new()
        {
            Id = new StableEntityId("EVD-002"),
            AcceptanceCriterionIds = [ac1.Id],
            PlanStepIds = [],
            Description = "All acceptance tests passed green"
        };
        SpecVerificationEvidenceObservation passObs = new()
        {
            EvidenceId = passEvidence.Id,
            RepositoryEvidenceId = "build-summary",
            Source = "build-summary",
            Kind = "report",
            Reference = ".ai/generated/reports/latest-build-summary.json",
            Availability = RepositoryEvidenceAvailability.Available,
            Freshness = RepositoryEvidenceFreshness.Unknown,
            SourceGeneratedAt = "2026-09-10T10:00:00Z",
            Disposition = SpecVerificationEvidenceDisposition.Supporting,
            Reason = "Build clean and 100% tests passed"
        };

        SpecVerificationReport reportPass = SpecVerificationEvaluator.Evaluate(
            specId_: "spec-v1",
            requirementSetRevision_: new ArtifactRevision(1),
            workSpecRevision_: new ArtifactRevision(1),
            implementationPlanRevision_: new ArtifactRevision(1),
            evidence_: [passEvidence],
            observations_: [passObs],
            workSpec_: ws);

        Assert.Equal(VerificationStatus.Pass, reportPass.OverallStatus);
        Assert.Equal(VerificationStatus.Pass, reportPass.Results[0].Status);

        // Scenario 4: Inconclusive / unsupported LLM text => NOT_VERIFIED
        VerificationEvidence inconclusiveEvidence = new()
        {
            Id = new StableEntityId("EVD-003"),
            AcceptanceCriterionIds = [ac1.Id],
            PlanStepIds = [],
            Description = "AI Assistant claims: I verified the code works manually."
        };
        SpecVerificationEvidenceObservation inconclusiveObs = new()
        {
            EvidenceId = inconclusiveEvidence.Id,
            RepositoryEvidenceId = "build-summary",
            Source = "build-summary",
            Kind = "report",
            Reference = ".ai/generated/reports/latest-build-summary.json",
            Availability = RepositoryEvidenceAvailability.Available,
            Freshness = RepositoryEvidenceFreshness.Unknown,
            SourceGeneratedAt = "2026-09-10T10:00:00Z",
            Disposition = SpecVerificationEvidenceDisposition.Inconclusive,
            Reason = "Unsupported conversational opinion without repository evidence."
        };

        SpecVerificationReport reportInconclusive = SpecVerificationEvaluator.Evaluate(
            specId_: "spec-v1",
            requirementSetRevision_: new ArtifactRevision(1),
            workSpecRevision_: new ArtifactRevision(1),
            implementationPlanRevision_: new ArtifactRevision(1),
            evidence_: [inconclusiveEvidence],
            observations_: [inconclusiveObs],
            workSpec_: ws);

        Assert.Equal(VerificationStatus.NotVerified, reportInconclusive.OverallStatus);
        Assert.Equal(VerificationStatus.NotVerified, reportInconclusive.Results[0].Status);
        Assert.NotEqual(VerificationStatus.Pass, reportInconclusive.Results[0].Status);
    }

    [Fact]
    public void Documentation_ContainsRequiredSectionsAndInvariants()
    {
        string repoRoot = ResolveRepoRoot(AppContext.BaseDirectory);

        // 1. docs/v3-spec-driven-development.md
        string sddDocPath = Path.Combine(repoRoot, "docs", "v3-spec-driven-development.md");
        Assert.True(File.Exists(sddDocPath), $"Missing SDD doc at: {sddDocPath}");
        string sddContent = File.ReadAllText(sddDocPath);

        // Stable headings
        Assert.Contains("## Canonical vs. Generated State", sddContent);
        Assert.Contains("## Spec Lifecycle", sddContent);
        Assert.Contains("## Revision & Approval Semantics", sddContent);
        Assert.Contains("## Evidence Semantics & Verification", sddContent);
        Assert.Contains("## CLI Reference", sddContent);
        Assert.Contains("## Portable MCP Read-Only Spec Context", sddContent);
        Assert.Contains("## Migration & Upgrade Contract", sddContent);
        Assert.Contains("## V4+ Architectural Boundary", sddContent);

        // Canonical paths and ledger
        Assert.Contains(".ai/specs/<spec-id>/requirements.json", sddContent);
        Assert.Contains(".ai/specs/<spec-id>/work-spec.json", sddContent);
        Assert.Contains(".ai/specs/<spec-id>/implementation-plan.json", sddContent);
        Assert.Contains(".ai/specs/<spec-id>/approvals.json", sddContent);

        // Generated state distinction
        Assert.Contains(".ai/generated/spec-context/<spec-id>.json", sddContent);
        Assert.Contains("ImplementationChecklistProjector", sddContent);

        // Invariants
        Assert.Contains("Missing evidence is NOT a PASS", sddContent);
        Assert.Contains("Unsupported LLM opinion is NOT verification evidence", sddContent);

        // Fixed MCP surface
        Assert.Contains("5 tools, 9 resources, and 17 prompts", sddContent);

        // V4+ non-goals
        Assert.Contains("Task DAG Execution", sddContent);
        Assert.Contains("Agent / Client Materialization", sddContent);
        Assert.Contains("Model Runtime & LLM Invocation", sddContent);
        Assert.Contains("PromptCompiler", sddContent);
        Assert.Contains("Autonomous Repair", sddContent);
        Assert.Contains("Scheduler & Orchestration", sddContent);

        // 2. README.md
        string readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), $"Missing README.md at: {readmePath}");
        string readmeContent = File.ReadAllText(readmePath);

        Assert.Contains("v3.0.0 release readiness", readmeContent);
        Assert.Contains("## Spec-Driven Development (v3.0.0)", readmeContent);
        Assert.Contains("docs/v3-spec-driven-development.md", readmeContent);
        Assert.Contains("airepo spec init", readmeContent);
        Assert.Contains("get_context kind=spec", readmeContent);
        Assert.Contains("get_context kind=spec-context", readmeContent);
        Assert.Contains("get_context kind=verification", readmeContent);

        // 3. CHANGELOG.md
        string changelogPath = Path.Combine(repoRoot, "CHANGELOG.md");
        Assert.True(File.Exists(changelogPath), $"Missing CHANGELOG.md at: {changelogPath}");
        string changelogContent = File.ReadAllText(changelogPath);

        Assert.Contains("## [3.0.0] - 2026-09-14", changelogContent);
        Assert.Contains("### Spec-Driven Development", changelogContent);
        Assert.Contains("## [2.0.0] - 2026-08-24", changelogContent);
    }

    private static RequirementSet CreateRequirementSet(int revision_, string statement_)
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id = new StableEntityId("INPUT-001"),
                    Text = "Input requirement text"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = statement_,
                    SourceInputIds = [new StableEntityId("INPUT-001")]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec(int revision_, int reqRev_)
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(revision_),
            RequirementSetRevision = new ArtifactRevision(reqRev_),
            Constraints =
            [
                new Constraint
                {
                    Id = new StableEntityId("CON-001"),
                    Statement = "Constraint statement",
                    RequirementIds = [new StableEntityId("REQ-001")]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-001"),
                    Statement = "Verified criteria statement",
                    RequirementIds = [new StableEntityId("REQ-001")]
                }
            ]
        };
    }

    private static ImplementationPlan CreateImplementationPlan(int revision_, int wsRev_)
    {
        return new ImplementationPlan
        {
            Revision = new ArtifactRevision(revision_),
            WorkSpecRevision = new ArtifactRevision(wsRev_),
            Steps =
            [
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-001"),
                    Statement = "Implementation plan step statement",
                    RequirementIds = [new StableEntityId("REQ-001")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-001")]
                }
            ]
        };
    }

    private static string ResolveRepoRoot(string baseDir_)
    {
        DirectoryInfo? dir = new(baseDir_);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI.RepoKit.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(baseDir_, "..", "..", "..", ".."));
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "airepokit-v3release-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Root);
            Directory.CreateDirectory(Path.Combine(this.Root, ".git"));
        }

        public string WriteCandidate<T>(string fileName_, T obj_)
        {
            string path = Path.Combine(this.Root, fileName_);
            File.WriteAllText(path, SpecJsonSerializer.Serialize(obj_));
            return path;
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

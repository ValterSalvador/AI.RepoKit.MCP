using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecDiffAcceptanceTests
{
    private static readonly SpecId _specId = new("p06-acceptance-spec");

    [Fact]
    public void P06_IntegratedAcceptanceScenario()
    {
        using TestRepo repo = new();
        SpecCommand cli = new();

        // 1. Build and approve RequirementSet -> WorkSpec -> ImplementationPlan using public CLI
        RequirementSet initialRs = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Initial input 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Initial input 2" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Initial requirement 1", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "Initial requirement 2", SourceInputIds = [new StableEntityId("INPUT-002")] }
            ]
        };
        string rsCandidate = repo.WriteCandidate("rs-init.json", initialRs);

        CommandResult initResult = cli.Execute([
            "init",
            "--spec-id", _specId.Value,
            "--from", rsCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(initResult.Success);

        CommandResult appRsResult = cli.Execute([
            "approve",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(appRsResult.Success);

        WorkSpec initialWs = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Constraint 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-002"), Statement = "Constraint 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "Acceptance criterion 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "Acceptance criterion 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ]
        };
        string wsCandidate = repo.WriteCandidate("ws-init.json", initialWs);

        CommandResult refineWsResult = cli.Execute([
            "refine",
            "--spec-id", _specId.Value,
            "--artifact", "work-spec",
            "--from", wsCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(refineWsResult.Success);

        CommandResult appWsResult = cli.Execute([
            "approve",
            "--spec-id", _specId.Value,
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(appWsResult.Success);

        ImplementationPlan initialPlan = new()
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Steps =
            [
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-001"),
                    Statement = "Step 1",
                    RequirementIds = [new StableEntityId("REQ-001")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-001")]
                },
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-002"),
                    Statement = "Step 2",
                    RequirementIds = [new StableEntityId("REQ-002")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-002")]
                }
            ]
        };
        string planCandidate = repo.WriteCandidate("plan-init.json", initialPlan);

        CommandResult planResult = cli.Execute([
            "plan",
            "--spec-id", _specId.Value,
            "--from", planCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(planResult.Success);

        CommandResult appPlanResult = cli.Execute([
            "approve",
            "--spec-id", _specId.Value,
            "--artifact", "implementation-plan",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(appPlanResult.Success);

        // Snapshot canonical state before running any diffs
        string specDir = Path.Combine(repo.Root, ".ai", "specs", _specId.Value);
        string rsCanonical = File.ReadAllText(Path.Combine(specDir, "requirements.json"));
        string wsCanonical = File.ReadAllText(Path.Combine(specDir, "work-spec.json"));
        string planCanonical = File.ReadAllText(Path.Combine(specDir, "implementation-plan.json"));
        string approvalsCanonical = File.ReadAllText(Path.Combine(specDir, "approvals.json"));

        // Scenario A: RequirementSet semantic candidate change
        RequirementSet rsModCandidate = initialRs with
        {
            Requirements =
            [
                initialRs.Requirements[0] with { Statement = "Modified requirement statement 1" },
                initialRs.Requirements[1]
            ]
        };
        string rsModCandidatePath = repo.WriteCandidate("rs-mod.json", rsModCandidate);

        CommandResult diffRsResult = cli.Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--from", rsModCandidatePath,
            "--repo", repo.Root,
            "--json"
        ]);
        Assert.True(diffRsResult.Success);

        using (JsonDocument docA = JsonDocument.Parse(diffRsResult.Markdown))
        {
            JsonElement rootA = docA.RootElement;
            Assert.True(rootA.GetProperty("semanticChanged").GetBoolean());
            Assert.Equal(2, rootA.GetProperty("proposedRevision").GetInt32());

            // * changed Requirement correctly classified
            JsonElement entityChangesA = rootA.GetProperty("entityChanges");
            JsonElement req1Change = entityChangesA.EnumerateArray().Single(e_ => e_.GetProperty("entityId").GetString() == "REQ-001");
            Assert.Equal("modified", req1Change.GetProperty("changeKind").GetString());

            // * affected WorkSpec elements reported by stable ID
            JsonElement refImpactsA = rootA.GetProperty("referenceImpacts");
            Assert.Contains(refImpactsA.EnumerateArray(), r_ =>
                r_.GetProperty("artifactKind").GetString() == "workSpec" &&
                r_.GetProperty("entityKind").GetString() == "constraint" &&
                r_.GetProperty("entityId").GetString() == "CON-001");
            Assert.Contains(refImpactsA.EnumerateArray(), r_ =>
                r_.GetProperty("artifactKind").GetString() == "workSpec" &&
                r_.GetProperty("entityKind").GetString() == "acceptanceCriterion" &&
                r_.GetProperty("entityId").GetString() == "AC-001");

            // * affected Plan step references reported
            Assert.Contains(refImpactsA.EnumerateArray(), r_ =>
                r_.GetProperty("artifactKind").GetString() == "implementationPlan" &&
                r_.GetProperty("entityKind").GetString() == "planStep" &&
                r_.GetProperty("entityId").GetString() == "PLAN-STEP-001");

            // * RequirementSet/WorkSpec/Plan approval impacts report prospective stale state
            JsonElement appImpactsA = rootA.GetProperty("approvalImpacts");
            foreach (JsonElement app in appImpactsA.EnumerateArray())
            {
                Assert.True(app.GetProperty("affected").GetBoolean());
                Assert.Equal("current", app.GetProperty("currentStatus").GetString());
                Assert.Equal("stale", app.GetProperty("proposedStatus").GetString());
            }

            // * SpecContext impact reported
            JsonElement derivedA = rootA.GetProperty("derivedArtifactImpacts");
            JsonElement specContextA = derivedA.EnumerateArray().Single(d_ => d_.GetProperty("artifactKind").GetString() == "specContext");
            Assert.True(specContextA.GetProperty("affected").GetBoolean());

            // * checklist impact reported
            JsonElement checklistA = derivedA.EnumerateArray().Single(d_ => d_.GetProperty("artifactKind").GetString() == "implementationChecklist");
            Assert.True(checklistA.GetProperty("affected").GetBoolean());
        }

        // * canonical files and approvals remain byte-identical
        Assert.Equal(rsCanonical, File.ReadAllText(Path.Combine(specDir, "requirements.json")));
        Assert.Equal(wsCanonical, File.ReadAllText(Path.Combine(specDir, "work-spec.json")));
        Assert.Equal(planCanonical, File.ReadAllText(Path.Combine(specDir, "implementation-plan.json")));
        Assert.Equal(approvalsCanonical, File.ReadAllText(Path.Combine(specDir, "approvals.json")));

        // Scenario B: WorkSpec candidate change
        WorkSpec wsModCandidate = initialWs with
        {
            AcceptanceCriteria =
            [
                initialWs.AcceptanceCriteria[0] with { Statement = "Modified acceptance criterion statement 1" },
                initialWs.AcceptanceCriteria[1]
            ]
        };
        string wsModCandidatePath = repo.WriteCandidate("ws-mod.json", wsModCandidate);

        CommandResult diffWsResult = cli.Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "work-spec",
            "--from", wsModCandidatePath,
            "--repo", repo.Root,
            "--json"
        ]);
        Assert.True(diffWsResult.Success);

        using (JsonDocument docB = JsonDocument.Parse(diffWsResult.Markdown))
        {
            JsonElement rootB = docB.RootElement;
            Assert.True(rootB.GetProperty("semanticChanged").GetBoolean());

            // * changed acceptance criterion classified
            JsonElement ac1Change = rootB.GetProperty("entityChanges").EnumerateArray().Single(e_ => e_.GetProperty("entityId").GetString() == "AC-001");
            Assert.Equal("modified", ac1Change.GetProperty("changeKind").GetString());

            // * referencing Plan step reported
            JsonElement refImpactsB = rootB.GetProperty("referenceImpacts");
            JsonElement planStepImpact = Assert.Single(refImpactsB.EnumerateArray(), r_ => r_.GetProperty("entityId").GetString() == "PLAN-STEP-001");
            Assert.Equal("implementationPlan", planStepImpact.GetProperty("artifactKind").GetString());

            // * Plan prospective stale state reported & approval effects correct
            JsonElement appImpactsB = rootB.GetProperty("approvalImpacts");
            JsonElement reqAppB = appImpactsB.EnumerateArray().Single(a_ => a_.GetProperty("artifactKind").GetString() == "requirementSet");
            Assert.False(reqAppB.GetProperty("affected").GetBoolean());
            Assert.Equal("current", reqAppB.GetProperty("proposedStatus").GetString());

            JsonElement wsAppB = appImpactsB.EnumerateArray().Single(a_ => a_.GetProperty("artifactKind").GetString() == "workSpec");
            Assert.True(wsAppB.GetProperty("affected").GetBoolean());
            Assert.Equal("stale", wsAppB.GetProperty("proposedStatus").GetString());

            JsonElement planAppB = appImpactsB.EnumerateArray().Single(a_ => a_.GetProperty("artifactKind").GetString() == "implementationPlan");
            Assert.True(planAppB.GetProperty("affected").GetBoolean());
            Assert.Equal("stale", planAppB.GetProperty("proposedStatus").GetString());
        }

        // * canonical files unchanged
        Assert.Equal(rsCanonical, File.ReadAllText(Path.Combine(specDir, "requirements.json")));
        Assert.Equal(wsCanonical, File.ReadAllText(Path.Combine(specDir, "work-spec.json")));
        Assert.Equal(planCanonical, File.ReadAllText(Path.Combine(specDir, "implementation-plan.json")));
        Assert.Equal(approvalsCanonical, File.ReadAllText(Path.Combine(specDir, "approvals.json")));

        // Scenario C: Plan candidate change
        ImplementationPlan planModCandidate = initialPlan with
        {
            Steps =
            [
                initialPlan.Steps[0] with { Statement = "Modified step statement 1" },
                initialPlan.Steps[1]
            ]
        };
        string planModCandidatePath = repo.WriteCandidate("plan-mod.json", planModCandidate);

        CommandResult diffPlanResult = cli.Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "plan",
            "--from", planModCandidatePath,
            "--repo", repo.Root,
            "--json"
        ]);
        Assert.True(diffPlanResult.Success);

        using (JsonDocument docC = JsonDocument.Parse(diffPlanResult.Markdown))
        {
            JsonElement rootC = docC.RootElement;
            Assert.True(rootC.GetProperty("semanticChanged").GetBoolean());

            // * changed PlanStep classified
            JsonElement step1Change = rootC.GetProperty("entityChanges").EnumerateArray().Single(e_ => e_.GetProperty("entityId").GetString() == "PLAN-STEP-001");
            Assert.Equal("modified", step1Change.GetProperty("changeKind").GetString());

            // * Plan approval prospective stale state
            JsonElement appImpactsC = rootC.GetProperty("approvalImpacts");
            JsonElement planAppC = appImpactsC.EnumerateArray().Single(a_ => a_.GetProperty("artifactKind").GetString() == "implementationPlan");
            Assert.True(planAppC.GetProperty("affected").GetBoolean());
            Assert.Equal("stale", planAppC.GetProperty("proposedStatus").GetString());

            // * checklist affected, SpecContext not affected
            JsonElement derivedC = rootC.GetProperty("derivedArtifactImpacts");
            JsonElement specContextC = derivedC.EnumerateArray().Single(d_ => d_.GetProperty("artifactKind").GetString() == "specContext");
            Assert.False(specContextC.GetProperty("affected").GetBoolean());

            JsonElement checklistC = derivedC.EnumerateArray().Single(d_ => d_.GetProperty("artifactKind").GetString() == "implementationChecklist");
            Assert.True(checklistC.GetProperty("affected").GetBoolean());
        }

        // * canonical files unchanged
        Assert.Equal(rsCanonical, File.ReadAllText(Path.Combine(specDir, "requirements.json")));
        Assert.Equal(wsCanonical, File.ReadAllText(Path.Combine(specDir, "work-spec.json")));
        Assert.Equal(planCanonical, File.ReadAllText(Path.Combine(specDir, "implementation-plan.json")));
        Assert.Equal(approvalsCanonical, File.ReadAllText(Path.Combine(specDir, "approvals.json")));

        // Scenario D: Repeat the same diff twice
        CommandResult human1 = cli.Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--from", rsModCandidatePath,
            "--repo", repo.Root
        ]);
        CommandResult human2 = cli.Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--from", rsModCandidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(human1.Success);
        Assert.True(human2.Success);
        Assert.Equal(human1.Markdown, human2.Markdown);

        CommandResult json1 = cli.Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--from", rsModCandidatePath,
            "--repo", repo.Root,
            "--json"
        ]);
        CommandResult json2 = cli.Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--from", rsModCandidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(json1.Success);
        Assert.True(json2.Success);
        Assert.Equal(json1.Markdown, json2.Markdown);
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "airepokit-diff-acceptance-test-" + Guid.NewGuid().ToString("N"));
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

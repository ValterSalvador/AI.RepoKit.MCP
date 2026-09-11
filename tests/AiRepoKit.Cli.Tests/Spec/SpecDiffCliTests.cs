using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Diff;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecDiffCliTests
{
    private static readonly SpecId _specId = new("cli-spec");

    // 35. spec diff routing
    [Fact]
    public void SpecDiff_Routing_ExecutesDiffSubcommand()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success, result.Markdown);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("# Spec Diff: `cli-spec`", result.Markdown);
    }

    // 36. required --spec-id
    [Fact]
    public void SpecDiff_MissingSpecId_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing required option: '--spec-id'", result.Markdown);
    }

    // 37. required --artifact
    [Fact]
    public void SpecDiff_MissingArtifact_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing required option: '--artifact'", result.Markdown);
    }

    // 38. required --from
    [Fact]
    public void SpecDiff_MissingFrom_Rejected()
    {
        using TestRepo repo = new();

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing required option: '--from'", result.Markdown);
    }

    // 39. requirements selector
    [Fact]
    public void SpecDiff_RequirementsSelector_Accepted()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success);
        Assert.Contains("- Artifact: `RequirementSet`", result.Markdown);
    }

    // 40. work-spec selector
    [Fact]
    public void SpecDiff_WorkSpecSelector_Accepted()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSetMulti());

        string candidatePath = repo.WriteCandidate("ws.json", CreateWorkSpecMulti());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "work-spec",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success);
        Assert.Contains("- Artifact: `WorkSpec`", result.Markdown);
    }

    // 41. implementation-plan selector
    [Fact]
    public void SpecDiff_ImplementationPlanSelector_Accepted()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSetMulti());
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpecMulti());

        string candidatePath = repo.WriteCandidate("plan.json", CreatePlanMulti());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "implementation-plan",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success);
        Assert.Contains("- Artifact: `ImplementationPlan`", result.Markdown);
    }

    // 42. plan alias
    [Fact]
    public void SpecDiff_PlanAlias_Accepted()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSetMulti());
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpecMulti());

        string candidatePath = repo.WriteCandidate("plan.json", CreatePlanMulti());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "plan",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success);
        Assert.Contains("- Artifact: `ImplementationPlan`", result.Markdown);
    }

    // 43. invalid selector
    [Fact]
    public void SpecDiff_InvalidSelector_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "invalid-artifact",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Invalid or unsupported artifact selector", result.Markdown);
    }

    // 44. --dry-run rejected
    [Fact]
    public void SpecDiff_DryRun_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--dry-run"
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--dry-run' for 'spec diff'", result.Markdown);
    }

    // 45. --apply rejected
    [Fact]
    public void SpecDiff_Apply_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--apply' for 'spec diff'", result.Markdown);
    }

    // 46. --expected-revision rejected
    [Fact]
    public void SpecDiff_ExpectedRevision_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--expected-revision", "1"
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--expected-revision' for 'spec diff'", result.Markdown);
    }

    // 47. input > 1 MiB rejected using existing reader
    [Fact]
    public void SpecDiff_InputTooLarge_Rejected()
    {
        using TestRepo repo = new();
        string largePath = Path.Combine(repo.Root, "large.json");
        byte[] bytes = new byte[SpecWorkspace.MaximumArtifactSizeBytes + 10];
        File.WriteAllBytes(largePath, bytes);

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", largePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(SpecPersistenceException.ArtifactTooLarge, result.Markdown);
    }

    // 48. invalid UTF-8 rejected
    [Fact]
    public void SpecDiff_InvalidUtf8_Rejected()
    {
        using TestRepo repo = new();
        string badUtf8Path = Path.Combine(repo.Root, "bad.json");
        byte[] bytes = [0xFF, 0xFE, 0xFD];
        File.WriteAllBytes(badUtf8Path, bytes);

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", badUtf8Path,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(SpecPersistenceException.InvalidUtf8, result.Markdown);
    }

    // 49. malformed JSON rejected
    [Fact]
    public void SpecDiff_MalformedJson_Rejected()
    {
        using TestRepo repo = new();
        string malformedPath = Path.Combine(repo.Root, "malformed.json");
        File.WriteAllText(malformedPath, "{ not valid json");

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", malformedPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(SpecPersistenceException.InvalidJson, result.Markdown);
    }

    // 50. invalid candidate validation rejected
    [Fact]
    public void SpecDiff_InvalidCandidateValidation_Rejected()
    {
        using TestRepo repo = new();
        // Candidate with duplicate entity ID
        string invalidCandidatePath = Path.Combine(repo.Root, "invalid.json");
        string json = """
        {
            "schemaVersion": 1,
            "schemaId": "https://airepokit.dev/schemas/v3/spec.json",
            "revision": 1,
            "artifactIdentity": "requirements",
            "inputs": [
                { "id": "INPUT-001", "text": "Input 1" }
            ],
            "requirements": [
                { "id": "REQ-001", "statement": "Statement 1", "sourceInputIds": ["INPUT-001"] },
                { "id": "REQ-001", "statement": "Statement duplicate", "sourceInputIds": ["INPUT-001"] }
            ]
        }
        """;
        File.WriteAllText(invalidCandidatePath, json);

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", invalidCandidatePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(SpecPersistenceException.ValidationFailed, result.Markdown);
        Assert.Contains("Duplicate requirement ID", result.Markdown);
    }

    // 51. deterministic human output
    [Fact]
    public void SpecDiff_DeterministicHumanOutput()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result1 = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        CommandResult result2 = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.Markdown, result2.Markdown);
    }

    // 52. deterministic JSON output
    [Fact]
    public void SpecDiff_DeterministicJsonOutput()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result1 = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        CommandResult result2 = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.Markdown, result2.Markdown);
    }

    // 53. camelCase enum JSON
    [Fact]
    public void SpecDiff_CamelCaseEnumJson()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(result.Success);
        string json = result.Markdown;

        Assert.Contains("\"artifactKind\":\"requirementSet\"", json);
        Assert.Contains("\"changeKind\":\"added\"", json);
        Assert.Contains("\"entityKind\":\"requirementInput\"", json);
        Assert.Contains("\"currentStatus\":\"notApproved\"", json);
        Assert.Contains("\"proposedStatus\":\"notApproved\"", json);
    }

    // 54. semantic no-op human/JSON
    [Fact]
    public void SpecDiff_SemanticNoOp_HumanAndJson()
    {
        using TestRepo repo = new();
        RequirementSet rs = CreateRequirementSet();
        repo.InitializeCanonicalRequirementSet(_specId, rs);
        string candidatePath = repo.WriteCandidate("req.json", rs);

        CommandResult humanResult = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(humanResult.Success);
        Assert.Contains("- Semantic Changed: `false`", humanResult.Markdown);
        Assert.Contains("- Current Revision: `1`", humanResult.Markdown);
        Assert.Contains("- Proposed Revision: `1`", humanResult.Markdown);
        Assert.Contains("[UNCHANGED] `REQ-001` (Requirement)", humanResult.Markdown);

        CommandResult jsonResult = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(jsonResult.Success);
        using JsonDocument doc = JsonDocument.Parse(jsonResult.Markdown);
        Assert.False(doc.RootElement.GetProperty("semanticChanged").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("proposedRevision").GetInt32());
    }

    // 55. changed semantic human/JSON
    [Fact]
    public void SpecDiff_ChangedSemantic_HumanAndJson()
    {
        using TestRepo repo = new();
        RequirementSet rs = CreateRequirementSet();
        repo.InitializeCanonicalRequirementSet(_specId, rs);

        RequirementSet candidate = rs with
        {
            Requirements = [rs.Requirements[0] with { Statement = "Changed statement" }]
        };
        string candidatePath = repo.WriteCandidate("req.json", candidate);

        CommandResult humanResult = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(humanResult.Success);
        Assert.Contains("- Semantic Changed: `true`", humanResult.Markdown);
        Assert.Contains("- Current Revision: `1`", humanResult.Markdown);
        Assert.Contains("- Proposed Revision: `2`", humanResult.Markdown);
        Assert.Contains("[MODIFIED] `REQ-001` (Requirement)", humanResult.Markdown);

        CommandResult jsonResult = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(jsonResult.Success);
        using JsonDocument doc = JsonDocument.Parse(jsonResult.Markdown);
        Assert.True(doc.RootElement.GetProperty("semanticChanged").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("proposedRevision").GetInt32());
    }

    // 56. zero repository mutation after CLI diff
    [Fact]
    public void SpecDiff_ZeroRepositoryMutation_AfterCliDiff()
    {
        using TestRepo repo = new();
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan plan = CreatePlanMulti();
        repo.InitializeCanonicalRequirementSet(_specId, rs);
        repo.InitializeCanonicalWorkSpec(_specId, ws);
        repo.InitializeCanonicalImplementationPlan(_specId, plan);

        string specDir = Path.Combine(repo.Root, ".ai", "specs", "cli-spec");
        string rsPath = Path.Combine(specDir, "requirements.json");
        string wsPath = Path.Combine(specDir, "work-spec.json");
        string planPath = Path.Combine(specDir, "implementation-plan.json");

        string rsBefore = File.ReadAllText(rsPath);
        string wsBefore = File.ReadAllText(wsPath);
        string planBefore = File.ReadAllText(planPath);

        RequirementSet candidate = rs with
        {
            Requirements = [rs.Requirements[0] with { Statement = "Candidate modified" }, rs.Requirements[1]]
        };
        string candidatePath = repo.WriteCandidate("candidate.json", candidate);

        CommandResult result = new SpecCommand().Execute([
            "diff",
            "--spec-id", "cli-spec",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success);
        Assert.Equal(rsBefore, File.ReadAllText(rsPath));
        Assert.Equal(wsBefore, File.ReadAllText(wsPath));
        Assert.Equal(planBefore, File.ReadAllText(planPath));
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".ai", "generated")));
    }

    // 57. legacy top-level airepo plan remains unchanged
    [Fact]
    public void Legacy_TopLevelPlan_RemainsUnchanged()
    {
        BootstrapOptions options = Program.Parse(["plan", "--repo", "."]);
        Assert.Equal("plan", options.Command);
        Assert.Empty(options.UnknownOptions);

        PlanCommand planCommand = new();
        CommandResult result = planCommand.Execute(options);

        Assert.True(result.Success);
        Assert.Contains("Plan", result.Markdown);
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Statement 1", SourceInputIds = [new StableEntityId("INPUT-001")] }
            ]
        };
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

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "airepokit-diff-cli-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Root);
            Directory.CreateDirectory(Path.Combine(this.Root, ".git"));
        }

        public string WriteCandidate<T>(string fileName_, T obj_)
        {
            string path = Path.Combine(this.Root, fileName_);
            File.WriteAllText(path, SpecJsonSerializer.Serialize(obj_));
            return path;
        }

        public void InitializeCanonicalRequirementSet(SpecId specId_, RequirementSet rs_)
        {
            string dir = Path.Combine(this.Root, ".ai", "specs", specId_.Value);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "requirements.json"), SpecJsonSerializer.Serialize(rs_));
        }

        public void InitializeCanonicalWorkSpec(SpecId specId_, WorkSpec ws_)
        {
            string dir = Path.Combine(this.Root, ".ai", "specs", specId_.Value);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "work-spec.json"), SpecJsonSerializer.Serialize(ws_));
        }

        public void InitializeCanonicalImplementationPlan(SpecId specId_, ImplementationPlan plan_)
        {
            string dir = Path.Combine(this.Root, ".ai", "specs", specId_.Value);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "implementation-plan.json"), SpecJsonSerializer.Serialize(plan_));
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

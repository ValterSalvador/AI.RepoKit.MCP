using System.Globalization;
using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecCommandLifecycleTests
{
    [Fact]
    public void Init_DryRun_WritesNothing()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-init-dry",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Mode: `DryRun`", result.Markdown);
        Assert.Contains("Changed: `true`", result.Markdown);
        Assert.Contains("Applied: `false`", result.Markdown);
        Assert.Contains("Target Revision: `1`", result.Markdown);

        string expectedPath = Path.Combine(repo.Root, ".ai", "specs", "spec-init-dry", "requirements.json");
        Assert.False(File.Exists(expectedPath));
    }

    [Fact]
    public void Init_Apply_CreatesRequirementSetRevision1()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-init-apply",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Mode: `Apply`", result.Markdown);
        Assert.Contains("Applied: `true`", result.Markdown);

        string expectedPath = Path.Combine(repo.Root, ".ai", "specs", "spec-init-apply", "requirements.json");
        Assert.True(File.Exists(expectedPath));

        RequirementSet saved = SpecJsonSerializer.Deserialize<RequirementSet>(File.ReadAllText(expectedPath));
        Assert.Equal(1, saved.Revision.Value);
    }

    [Fact]
    public void Init_DuplicateInit_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult first = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-init-dup",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(first.Success);

        CommandResult second = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-init-dup",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.False(second.Success);
        Assert.Equal(1, second.ExitCode);
        Assert.Contains("already exists", second.Markdown);
    }

    [Fact]
    public void Show_IsReadOnly()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult initResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-show-ro",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(initResult.Success);

        string[] filesBefore = Directory.GetFiles(repo.Root, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
        DateTime[] timestampsBefore = filesBefore.Select(File.GetLastWriteTimeUtc).ToArray();

        CommandResult showResult = new SpecCommand().Execute([
            "show",
            "--spec-id", "spec-show-ro",
            "--repo", repo.Root
        ]);

        Assert.True(showResult.Success);
        Assert.Equal(0, showResult.ExitCode);

        string[] filesAfter = Directory.GetFiles(repo.Root, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
        DateTime[] timestampsAfter = filesAfter.Select(File.GetLastWriteTimeUtc).ToArray();

        Assert.Equal(filesBefore, filesAfter);
        Assert.Equal(timestampsBefore, timestampsAfter);
    }

    [Fact]
    public void Show_Selectors_Requirements_WorkSpec_Approvals_All()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-show-sel",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult reqResult = new SpecCommand().Execute([
            "show",
            "--spec-id", "spec-show-sel",
            "--artifact", "requirements",
            "--repo", repo.Root
        ]);
        Assert.True(reqResult.Success);
        Assert.Contains("# Spec Requirements", reqResult.Markdown);
        Assert.Contains("Present: `true`", reqResult.Markdown);

        CommandResult wsResult = new SpecCommand().Execute([
            "show",
            "--spec-id", "spec-show-sel",
            "--artifact", "work-spec",
            "--repo", repo.Root
        ]);
        Assert.True(wsResult.Success);
        Assert.Contains("# Spec Work Spec", wsResult.Markdown);
        Assert.Contains("Present: `false`", wsResult.Markdown);

        CommandResult appResult = new SpecCommand().Execute([
            "show",
            "--spec-id", "spec-show-sel",
            "--artifact", "approvals",
            "--repo", repo.Root
        ]);
        Assert.True(appResult.Success);
        Assert.Contains("# Spec Approvals", appResult.Markdown);
        Assert.Contains("Present: `false`", appResult.Markdown);

        CommandResult allResult = new SpecCommand().Execute([
            "show",
            "--spec-id", "spec-show-sel",
            "--artifact", "all",
            "--repo", repo.Root
        ]);
        Assert.True(allResult.Success);
        Assert.Contains("# Spec: `spec-show-sel`", allResult.Markdown);
        Assert.Contains("Lifecycle State", allResult.Markdown);
    }

    [Fact]
    public void JsonOutput_ParsesAndIsDeterministic()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result1 = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-json-det",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        CommandResult result2 = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-json-det",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(result1.Success);
        Assert.Equal(0, result1.ExitCode);
        Assert.Equal(result1.Markdown, result2.Markdown);

        using JsonDocument doc = JsonDocument.Parse(result1.Markdown);
        JsonElement root = doc.RootElement;
        Assert.Equal("spec-json-det", root.GetProperty("specId").GetString());
        Assert.Equal("requirementSet", root.GetProperty("artifactKind").GetString());
        Assert.Equal("dryRun", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("changed").GetBoolean());
        Assert.False(root.GetProperty("applied").GetBoolean());
        Assert.Equal(1, root.GetProperty("targetRevision").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("currentRevision").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("previousRevision").ValueKind);
    }

    [Fact]
    public void Refine_RequirementSet_RequiresExpectedRevision()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-refine-req",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult result = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-refine-req",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("requires '--expected-revision'", result.Markdown);
    }

    [Fact]
    public void Refine_RequirementSet_SemanticNoOp()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-refine-noop",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult result = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-refine-noop",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Changed: `false`", result.Markdown);
        Assert.Contains("Applied: `false`", result.Markdown);
        Assert.Contains("Target Revision: `1`", result.Markdown);
    }

    [Fact]
    public void Refine_RequirementSet_SemanticChange_IncrementsRevision()
    {
        using TestRepo repo = new();
        string candidatePath1 = repo.WriteCandidate("req1.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-refine-inc",
            "--from", candidatePath1,
            "--repo", repo.Root,
            "--apply"
        ]);

        string candidatePath2 = repo.WriteCandidate("req2.json", CreateRequirementSet(requirementStatement_: "Updated statement"));

        CommandResult result = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-refine-inc",
            "--artifact", "requirements",
            "--from", candidatePath2,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Changed: `true`", result.Markdown);
        Assert.Contains("Applied: `true`", result.Markdown);
        Assert.Contains("Previous Revision: `1`", result.Markdown);
        Assert.Contains("Target Revision: `2`", result.Markdown);
    }

    [Fact]
    public void Refine_FirstWorkSpecCreation_WithoutExpectedRevision()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-first-ws",
            "--from", reqPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        string wsPath = repo.WriteCandidate("ws.json", CreateWorkSpec());

        CommandResult result = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-first-ws",
            "--artifact", "work-spec",
            "--from", wsPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Target Revision: `1`", result.Markdown);

        CommandResult withExpectedResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-first-ws-2",
            "--artifact", "work-spec",
            "--from", wsPath,
            "--expected-revision", "1",
            "--repo", repo.Root
        ]);

        Assert.False(withExpectedResult.Success);
        Assert.Contains("Cannot specify '--expected-revision' when creating the initial WorkSpec", withExpectedResult.Markdown);
    }

    [Fact]
    public void Refine_ExistingWorkSpec_RequiresExpectedRevision()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-exist-ws",
            "--from", reqPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        string wsPath = repo.WriteCandidate("ws.json", CreateWorkSpec());

        _ = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-exist-ws",
            "--artifact", "work-spec",
            "--from", wsPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult missingExpectedResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-exist-ws",
            "--artifact", "work-spec",
            "--from", wsPath,
            "--repo", repo.Root
        ]);

        Assert.False(missingExpectedResult.Success);
        Assert.Contains("Refining existing WorkSpec requires '--expected-revision'", missingExpectedResult.Markdown);
    }

    [Fact]
    public void Refine_WrongExpectedRevision_Rejected()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-wrong-rev",
            "--from", reqPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult result = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-wrong-rev",
            "--artifact", "requirements",
            "--from", reqPath,
            "--expected-revision", "99",
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("revision-conflict", result.Markdown);
    }

    [Fact]
    public void Approve_RequirementSet_DryRunAndApply()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-app-req",
            "--from", reqPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult dryResult = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-app-req",
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root
        ]);

        Assert.True(dryResult.Success);
        Assert.Equal(0, dryResult.ExitCode);
        Assert.Contains("Mode: `DryRun`", dryResult.Markdown);
        Assert.Contains("Applied: `false`", dryResult.Markdown);
        Assert.Contains("Current Approval Status: `NotApproved`", dryResult.Markdown);
        Assert.Contains("Proposed Approval Status: `Current`", dryResult.Markdown);

        string ledgerPath = Path.Combine(repo.Root, ".ai", "specs", "spec-app-req", "approvals.json");
        Assert.False(File.Exists(ledgerPath));

        CommandResult applyResult = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-app-req",
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.True(applyResult.Success);
        Assert.Equal(0, applyResult.ExitCode);
        Assert.Contains("Mode: `Apply`", applyResult.Markdown);
        Assert.Contains("Applied: `true`", applyResult.Markdown);
        Assert.Contains("Current Approval Status: `Current`", applyResult.Markdown);
        Assert.True(File.Exists(ledgerPath));
    }

    [Fact]
    public void Approve_ExactReapproval_IsIdempotent()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-app-idem",
            "--from", reqPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult first = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-app-idem",
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(first.Success);
        Assert.Contains("Changed: `true`", first.Markdown);

        CommandResult second = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-app-idem",
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(second.Success);
        Assert.Contains("Changed: `false`", second.Markdown);
        Assert.Contains("Applied: `false`", second.Markdown);
    }

    [Fact]
    public void Approve_WorkSpec_PrerequisiteCheck_AndSuccess()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());
        string wsPath = repo.WriteCandidate("ws.json", CreateWorkSpec());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-ws-prereq",
            "--from", reqPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        _ = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-ws-prereq",
            "--artifact", "work-spec",
            "--from", wsPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        // WorkSpec approval rejected until RequirementSet CURRENT
        CommandResult failBeforePrereq = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-ws-prereq",
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.False(failBeforePrereq.Success);
        Assert.Equal(1, failBeforePrereq.ExitCode);
        Assert.Contains("approval-prerequisite-failed", failBeforePrereq.Markdown);

        // Approve RequirementSet
        CommandResult approveReq = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-ws-prereq",
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(approveReq.Success);

        // WorkSpec approval succeeds after prerequisite
        CommandResult succeedWs = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-ws-prereq",
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.True(succeedWs.Success);
        Assert.Equal(0, succeedWs.ExitCode);
        Assert.Contains("Changed: `true`", succeedWs.Markdown);
        Assert.Contains("Applied: `true`", succeedWs.Markdown);
    }

    [Fact]
    public void Approve_StaleWorkSpec_Rejected()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());
        string wsPath = repo.WriteCandidate("ws.json", CreateWorkSpec());

        _ = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-ws-stale",
            "--from", reqPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        _ = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-ws-stale",
            "--artifact", "work-spec",
            "--from", wsPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        _ = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-ws-stale",
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        // Refine RequirementSet to revision 2, making WorkSpec stale
        string req2Path = repo.WriteCandidate("req2.json", CreateRequirementSet(requirementStatement_: "Statement v2"));
        _ = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-ws-stale",
            "--artifact", "requirements",
            "--from", req2Path,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        CommandResult staleApprove = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-ws-stale",
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.False(staleApprove.Success);
        Assert.Equal(1, staleApprove.ExitCode);
        Assert.Contains("stale", staleApprove.Markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_Refine_And_Approve_Rejected()
    {
        using TestRepo repo = new();
        string dummyPath = repo.WriteCandidate("dummy.json", CreateRequirementSet());

        CommandResult refinePlan = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-plan-test",
            "--artifact", "implementation-plan",
            "--from", dummyPath,
            "--repo", repo.Root
        ]);
        Assert.False(refinePlan.Success);
        Assert.Contains("Plan refinement is not supported", refinePlan.Markdown);

        CommandResult refinePlanShort = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-plan-test",
            "--artifact", "plan",
            "--from", dummyPath,
            "--repo", repo.Root
        ]);
        Assert.False(refinePlanShort.Success);
        Assert.Contains("Plan refinement is not supported", refinePlanShort.Markdown);

        CommandResult approvePlan = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-plan-test",
            "--artifact", "implementation-plan",
            "--revision", "1",
            "--repo", repo.Root
        ]);
        Assert.False(approvePlan.Success);
        Assert.Contains("Plan approval is not supported", approvePlan.Markdown);

        CommandResult approvePlanShort = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-plan-test",
            "--artifact", "plan",
            "--revision", "1",
            "--repo", repo.Root
        ]);
        Assert.False(approvePlanShort.Success);
        Assert.Contains("Plan approval is not supported", approvePlanShort.Markdown);

        CommandResult specPlan = new SpecCommand().Execute(["plan"]);
        Assert.False(specPlan.Success);
        Assert.Contains("Unsupported Spec subcommand: `plan`", specPlan.Markdown);

        BootstrapOptions topLevelPlan = Program.Parse(["plan"]);
        Assert.Equal("plan", topLevelPlan.Command);
    }

    [Fact]
    public void Malformed_And_Unknown_Candidate_Json_Rejected()
    {
        using TestRepo repo = new();
        string malformedPath = Path.Combine(repo.Root, "malformed.json");
        File.WriteAllText(malformedPath, "{ this is not json");

        CommandResult malformedResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-bad-json",
            "--from", malformedPath,
            "--repo", repo.Root
        ]);

        Assert.False(malformedResult.Success);
        Assert.Contains("invalid-json", malformedResult.Markdown);

        string unknownFieldPath = Path.Combine(repo.Root, "unknown.json");
        File.WriteAllText(unknownFieldPath, """{"unknownProperty": 123}""");

        CommandResult unknownResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-bad-json",
            "--from", unknownFieldPath,
            "--repo", repo.Root
        ]);

        Assert.False(unknownResult.Success);
        Assert.Contains("invalid-json", unknownResult.Markdown);
    }

    [Fact]
    public void Invalid_SpecId_Revision_Options_Rejected()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult uppercaseSpecId = new SpecCommand().Execute([
            "init",
            "--spec-id", "SPEC_UPPER",
            "--from", reqPath,
            "--repo", repo.Root
        ]);
        Assert.False(uppercaseSpecId.Success);
        Assert.Contains("Invalid Spec ID", uppercaseSpecId.Markdown);

        CommandResult nulSpecId = new SpecCommand().Execute([
            "init",
            "--spec-id", "nul",
            "--from", reqPath,
            "--repo", repo.Root
        ]);
        Assert.False(nulSpecId.Success);
        Assert.Contains("Invalid Spec ID", nulSpecId.Markdown);

        CommandResult zeroRevision = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-valid",
            "--artifact", "requirements",
            "--revision", "0",
            "--repo", repo.Root
        ]);
        Assert.False(zeroRevision.Success);
        Assert.Contains("positive integer", zeroRevision.Markdown);

        CommandResult negativeRevision = new SpecCommand().Execute([
            "approve",
            "--spec-id", "spec-valid",
            "--artifact", "requirements",
            "--revision", "-5",
            "--repo", repo.Root
        ]);
        Assert.False(negativeRevision.Success);
        Assert.Contains("positive integer", negativeRevision.Markdown);

        CommandResult unknownOption = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-valid",
            "--from", reqPath,
            "--unknown-opt",
            "--repo", repo.Root
        ]);
        Assert.False(unknownOption.Success);
        Assert.Contains("Unknown option '--unknown-opt'", unknownOption.Markdown);

        CommandResult duplicateOption = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-valid",
            "--spec-id", "spec-valid-2",
            "--from", reqPath,
            "--repo", repo.Root
        ]);
        Assert.False(duplicateOption.Success);
        Assert.Contains("Duplicate option '--spec-id'", duplicateOption.Markdown);
    }

    [Fact]
    public void Both_DryRun_And_Apply_Rejected()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-both-flags",
            "--from", reqPath,
            "--dry-run",
            "--apply",
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Cannot specify both '--dry-run' and '--apply'", result.Markdown);
    }

    [Fact]
    public void Candidate_Size_And_Invalid_Utf8_Boundary()
    {
        using TestRepo repo = new();

        // Size > 1MB
        string hugePath = Path.Combine(repo.Root, "huge.json");
        using (FileStream fs = File.Create(hugePath))
        {
            fs.SetLength(SpecWorkspace.MaximumArtifactSizeBytes + 10);
        }

        CommandResult sizeResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-size-test",
            "--from", hugePath,
            "--repo", repo.Root
        ]);

        Assert.False(sizeResult.Success);
        Assert.Contains("artifact-too-large", sizeResult.Markdown);

        // Invalid UTF-8
        string invalidUtf8Path = Path.Combine(repo.Root, "invalid-utf8.json");
        File.WriteAllBytes(invalidUtf8Path, [0xFF, 0xFE, 0xFD]);

        CommandResult utf8Result = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-utf8-test",
            "--from", invalidUtf8Path,
            "--repo", repo.Root
        ]);

        Assert.False(utf8Result.Success);
        Assert.Contains("invalid-utf8", utf8Result.Markdown);
    }

    [Fact]
    public void No_Mutation_On_Failed_Validation()
    {
        using TestRepo repo = new();

        // Candidate with invalid StableEntityId (contains spaces)
        string invalidContent = """
        {
            "schemaId": "https://airepokit.org/schemas/spec-v1.json",
            "schemaVersion": 1,
            "revision": 1,
            "artifactIdentity": "requirements",
            "inputs": [
                {
                    "id": "INVALID ID WITH SPACES",
                    "text": "some input text"
                }
            ],
            "requirements": []
        }
        """;
        string invalidPath = Path.Combine(repo.Root, "invalid-entity.json");
        File.WriteAllText(invalidPath, invalidContent);

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-validation-fail",
            "--from", invalidPath,
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.False(result.Success);
        string targetPath = Path.Combine(repo.Root, ".ai", "specs", "spec-validation-fail", "requirements.json");
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public void Json_Stdout_Payload_Contains_No_Markdown_Contamination()
    {
        using TestRepo repo = new();
        string reqPath = repo.WriteCandidate("req.json", CreateRequirementSet());

        // Success JSON
        CommandResult successResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-clean-json",
            "--from", reqPath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(successResult.Success);
        string successOutput = successResult.Markdown.Trim();
        Assert.StartsWith("{", successOutput);
        Assert.EndsWith("}", successOutput);
        Assert.DoesNotContain("#", successOutput);
        using (JsonDocument doc = JsonDocument.Parse(successOutput))
        {
            Assert.Equal("spec-clean-json", doc.RootElement.GetProperty("specId").GetString());
        }

        // Failure JSON (e.g. invalid options)
        CommandResult failResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-clean-json",
            "--from", reqPath,
            "--dry-run",
            "--apply",
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.False(failResult.Success);
        string failOutput = failResult.Markdown.Trim();
        Assert.StartsWith("{", failOutput);
        Assert.EndsWith("}", failOutput);
        Assert.DoesNotContain("#", failOutput);
        using (JsonDocument doc = JsonDocument.Parse(failOutput))
        {
            Assert.True(doc.RootElement.TryGetProperty("error", out JsonElement errElement));
            Assert.Contains("Cannot specify both '--dry-run' and '--apply'", errElement.GetString());
        }
    }

    [Fact]
    public void Init_DryRun_Json_CurrentRevisionIsNull_TargetRevisionIsOne()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-init-dry-json",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--dry-run",
            "--json"
        ]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);

        SpecMutationResultDto dto = SpecJsonSerializer.Deserialize<SpecMutationResultDto>(result.Markdown);
        Assert.Null(dto.PreviousRevision);
        Assert.Null(dto.CurrentRevision);
        Assert.Equal(1, dto.TargetRevision.Value);
        Assert.True(dto.Changed);
        Assert.False(dto.Applied);

        using JsonDocument doc = JsonDocument.Parse(result.Markdown);
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("previousRevision").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("currentRevision").ValueKind);
        Assert.Equal(1, root.GetProperty("targetRevision").GetInt32());
    }

    [Fact]
    public void Init_Apply_Json_CurrentRevisionIsOne_TargetRevisionIsOne()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-init-apply-json",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply",
            "--json"
        ]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);

        SpecMutationResultDto dto = SpecJsonSerializer.Deserialize<SpecMutationResultDto>(result.Markdown);
        Assert.Null(dto.PreviousRevision);
        Assert.NotNull(dto.CurrentRevision);
        Assert.Equal(1, dto.CurrentRevision.Value.Value);
        Assert.Equal(1, dto.TargetRevision.Value);
        Assert.True(dto.Changed);
        Assert.True(dto.Applied);

        using JsonDocument doc = JsonDocument.Parse(result.Markdown);
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("previousRevision").ValueKind);
        Assert.Equal(1, root.GetProperty("currentRevision").GetInt32());
        Assert.Equal(1, root.GetProperty("targetRevision").GetInt32());
    }

    [Fact]
    public void Refine_SemanticChange_DryRun_Json_CurrentRemainsPersisted_TargetIsIncremented()
    {
        using TestRepo repo = new();
        string candidatePath1 = repo.WriteCandidate("req1.json", CreateRequirementSet());

        CommandResult initResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-refine-dry-json",
            "--from", candidatePath1,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(initResult.Success);

        string candidatePath2 = repo.WriteCandidate("req2.json", CreateRequirementSet(requirementStatement_: "Semantic change statement"));

        CommandResult refineResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-refine-dry-json",
            "--artifact", "requirements",
            "--from", candidatePath2,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--dry-run",
            "--json"
        ]);

        Assert.True(refineResult.Success);
        Assert.Equal(0, refineResult.ExitCode);

        SpecMutationResultDto dto = SpecJsonSerializer.Deserialize<SpecMutationResultDto>(refineResult.Markdown);
        Assert.NotNull(dto.PreviousRevision);
        Assert.Equal(1, dto.PreviousRevision.Value.Value);
        Assert.NotNull(dto.CurrentRevision);
        Assert.Equal(1, dto.CurrentRevision.Value.Value);
        Assert.Equal(2, dto.TargetRevision.Value);
        Assert.True(dto.Changed);
        Assert.False(dto.Applied);

        using JsonDocument doc = JsonDocument.Parse(refineResult.Markdown);
        JsonElement root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("previousRevision").GetInt32());
        Assert.Equal(1, root.GetProperty("currentRevision").GetInt32());
        Assert.Equal(2, root.GetProperty("targetRevision").GetInt32());
    }

    [Fact]
    public void Refine_SemanticChange_Apply_Json_CurrentBecomesTarget()
    {
        using TestRepo repo = new();
        string candidatePath1 = repo.WriteCandidate("req1.json", CreateRequirementSet());

        CommandResult initResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-refine-apply-json",
            "--from", candidatePath1,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(initResult.Success);

        string candidatePath2 = repo.WriteCandidate("req2.json", CreateRequirementSet(requirementStatement_: "Semantic change statement"));

        CommandResult refineResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-refine-apply-json",
            "--artifact", "requirements",
            "--from", candidatePath2,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply",
            "--json"
        ]);

        Assert.True(refineResult.Success);
        Assert.Equal(0, refineResult.ExitCode);

        SpecMutationResultDto dto = SpecJsonSerializer.Deserialize<SpecMutationResultDto>(refineResult.Markdown);
        Assert.NotNull(dto.PreviousRevision);
        Assert.Equal(1, dto.PreviousRevision.Value.Value);
        Assert.NotNull(dto.CurrentRevision);
        Assert.Equal(2, dto.CurrentRevision.Value.Value);
        Assert.Equal(2, dto.TargetRevision.Value);
        Assert.True(dto.Changed);
        Assert.True(dto.Applied);

        using JsonDocument doc = JsonDocument.Parse(refineResult.Markdown);
        JsonElement root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("previousRevision").GetInt32());
        Assert.Equal(2, root.GetProperty("currentRevision").GetInt32());
        Assert.Equal(2, root.GetProperty("targetRevision").GetInt32());
    }

    [Fact]
    public void Refine_SemanticNoOp_Json_CurrentAndTargetRemainConsistentWithPersistedRevision()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());

        CommandResult initResult = new SpecCommand().Execute([
            "init",
            "--spec-id", "spec-refine-noop-json",
            "--from", candidatePath,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(initResult.Success);

        // Dry-run semantic no-op
        CommandResult dryRunResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-refine-noop-json",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--dry-run",
            "--json"
        ]);

        Assert.True(dryRunResult.Success);
        Assert.Equal(0, dryRunResult.ExitCode);

        SpecMutationResultDto dryRunDto = SpecJsonSerializer.Deserialize<SpecMutationResultDto>(dryRunResult.Markdown);
        Assert.NotNull(dryRunDto.PreviousRevision);
        Assert.Equal(1, dryRunDto.PreviousRevision.Value.Value);
        Assert.NotNull(dryRunDto.CurrentRevision);
        Assert.Equal(1, dryRunDto.CurrentRevision.Value.Value);
        Assert.Equal(1, dryRunDto.TargetRevision.Value);
        Assert.False(dryRunDto.Changed);
        Assert.False(dryRunDto.Applied);

        using (JsonDocument doc = JsonDocument.Parse(dryRunResult.Markdown))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("previousRevision").GetInt32());
            Assert.Equal(1, root.GetProperty("currentRevision").GetInt32());
            Assert.Equal(1, root.GetProperty("targetRevision").GetInt32());
        }

        // Apply semantic no-op
        CommandResult applyResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", "spec-refine-noop-json",
            "--artifact", "requirements",
            "--from", candidatePath,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply",
            "--json"
        ]);

        Assert.True(applyResult.Success);
        Assert.Equal(0, applyResult.ExitCode);

        SpecMutationResultDto applyDto = SpecJsonSerializer.Deserialize<SpecMutationResultDto>(applyResult.Markdown);
        Assert.NotNull(applyDto.PreviousRevision);
        Assert.Equal(1, applyDto.PreviousRevision.Value.Value);
        Assert.NotNull(applyDto.CurrentRevision);
        Assert.Equal(1, applyDto.CurrentRevision.Value.Value);
        Assert.Equal(1, applyDto.TargetRevision.Value);
        Assert.False(applyDto.Changed);
        Assert.False(applyDto.Applied);

        using (JsonDocument doc = JsonDocument.Parse(applyResult.Markdown))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("previousRevision").GetInt32());
            Assert.Equal(1, root.GetProperty("currentRevision").GetInt32());
            Assert.Equal(1, root.GetProperty("targetRevision").GetInt32());
        }
    }

    private static RequirementSet CreateRequirementSet(
        int revision_ = 1,
        string inputStatement_ = "Original requirement",
        string requirementStatement_ = "System must satisfy requirement")
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id = new StableEntityId("INPUT-001"),
                    Text = inputStatement_
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = requirementStatement_,
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
        int requirementSetRevision_ = 1,
        string constraintStatement_ = "Constraint statement",
        string criterionStatement_ = "Acceptance criterion statement")
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(revision_),
            RequirementSetRevision = new ArtifactRevision(requirementSetRevision_),
            Constraints =
            [
                new Constraint
                {
                    Id = new StableEntityId("CON-001"),
                    Statement = constraintStatement_,
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
                    Statement = criterionStatement_,
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ]
                }
            ]
        };
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "airepokit-spec-cli-test-" + Guid.NewGuid().ToString("N"));
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

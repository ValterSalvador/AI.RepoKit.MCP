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

    [Fact]
    public void Plan_InitialCreation_DryRun()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-init-dry";

        PreparePlanPrerequisites(
            repo,
            specId);

        string planPath =
            repo.WriteCandidate(
                "plan-init-dry.json",
                CreateImplementationPlan());

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root
            ]);

        Assert.True(
            result.Success,
            result.Markdown);
        Assert.Equal(
            0,
            result.ExitCode);
        Assert.Contains(
            "# Spec Plan: `spec-plan-init-dry`",
            result.Markdown);
        Assert.Contains(
            "Artifact: `ImplementationPlan`",
            result.Markdown);
        Assert.Contains(
            "Mode: `DryRun`",
            result.Markdown);
        Assert.Contains(
            "Changed: `true`",
            result.Markdown);
        Assert.Contains(
            "Applied: `false`",
            result.Markdown);
        Assert.Contains(
            "Target Revision: `1`",
            result.Markdown);

        Assert.False(
            File.Exists(
                GetPlanPath(
                    repo,
                    specId)));
    }

    [Fact]
    public void Plan_InitialCreation_Apply()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-init-apply";

        PreparePlanPrerequisites(
            repo,
            specId);

        string planPath =
            repo.WriteCandidate(
                "plan-init-apply.json",
                CreateImplementationPlan());

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            result.Success,
            result.Markdown);
        Assert.Equal(
            0,
            result.ExitCode);
        Assert.Contains(
            "Mode: `Apply`",
            result.Markdown);
        Assert.Contains(
            "Changed: `true`",
            result.Markdown);
        Assert.Contains(
            "Applied: `true`",
            result.Markdown);
        Assert.Contains(
            "Target Revision: `1`",
            result.Markdown);

        string canonicalPath =
            GetPlanPath(
                repo,
                specId);

        Assert.True(
            File.Exists(
                canonicalPath));

        ImplementationPlan canonical =
            SpecJsonSerializer.Deserialize<ImplementationPlan>(
                File.ReadAllText(
                    canonicalPath));

        Assert.Equal(
            1,
            canonical.Revision.Value);
    }

    [Fact]
    public void Plan_InitialCreation_WithExpectedRevision_Rejected()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-init-expected";

        PreparePlanPrerequisites(
            repo,
            specId);

        string planPath =
            repo.WriteCandidate(
                "plan-init-expected.json",
                CreateImplementationPlan());

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--expected-revision", "1",
                "--repo", repo.Root
            ]);

        Assert.False(
            result.Success);
        Assert.Equal(
            1,
            result.ExitCode);
        Assert.Contains(
            "revision-conflict",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "must not already exist",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_Refine_DryRun_WithMatchingRevision()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-refine-dry";

        ApplyInitialPlan(
            repo,
            specId,
            "Initial plan statement");

        string changedPath =
            repo.WriteCandidate(
                "plan-refine-dry.json",
                CreateImplementationPlan(
                    stepStatement_:
                        "Changed plan statement"));

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", changedPath,
                "--expected-revision", "1",
                "--repo", repo.Root
            ]);

        Assert.True(
            result.Success,
            result.Markdown);
        Assert.Contains(
            "Mode: `DryRun`",
            result.Markdown);
        Assert.Contains(
            "Previous Revision: `1`",
            result.Markdown);
        Assert.Contains(
            "Target Revision: `2`",
            result.Markdown);
        Assert.Contains(
            "Changed: `true`",
            result.Markdown);
        Assert.Contains(
            "Applied: `false`",
            result.Markdown);

        ImplementationPlan canonical =
            SpecJsonSerializer.Deserialize<ImplementationPlan>(
                File.ReadAllText(
                    GetPlanPath(
                        repo,
                        specId)));

        Assert.Equal(
            1,
            canonical.Revision.Value);
        Assert.Equal(
            "Initial plan statement",
            canonical.Steps[0].Statement);
    }

    [Fact]
    public void Plan_Refine_Apply_WithMatchingRevision()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-refine-apply";

        ApplyInitialPlan(
            repo,
            specId,
            "Initial plan statement");

        string changedPath =
            repo.WriteCandidate(
                "plan-refine-apply.json",
                CreateImplementationPlan(
                    stepStatement_:
                        "Changed plan statement"));

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", changedPath,
                "--expected-revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            result.Success,
            result.Markdown);
        Assert.Contains(
            "Previous Revision: `1`",
            result.Markdown);
        Assert.Contains(
            "Target Revision: `2`",
            result.Markdown);
        Assert.Contains(
            "Applied: `true`",
            result.Markdown);

        ImplementationPlan canonical =
            SpecJsonSerializer.Deserialize<ImplementationPlan>(
                File.ReadAllText(
                    GetPlanPath(
                        repo,
                        specId)));

        Assert.Equal(
            2,
            canonical.Revision.Value);
        Assert.Equal(
            "Changed plan statement",
            canonical.Steps[0].Statement);
    }

    [Fact]
    public void Plan_Refine_WithoutExpectedRevision_Rejected()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-refine-no-expected";

        ApplyInitialPlan(
            repo,
            specId);

        string changedPath =
            repo.WriteCandidate(
                "plan-no-expected.json",
                CreateImplementationPlan(
                    stepStatement_:
                        "Changed"));

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", changedPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            result.Success);
        Assert.Contains(
            "revision-conflict",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "requires the expected current revision",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_Refine_WrongExpectedRevision_Rejected()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-refine-wrong";

        ApplyInitialPlan(
            repo,
            specId);

        string changedPath =
            repo.WriteCandidate(
                "plan-wrong-expected.json",
                CreateImplementationPlan(
                    stepStatement_:
                        "Changed"));

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", changedPath,
                "--expected-revision", "2",
                "--repo", repo.Root
            ]);

        Assert.False(
            result.Success);
        Assert.Contains(
            "revision-conflict",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "does not match current revision '1'",
            result.Markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_SemanticNoOp()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-noop";

        string originalCandidate =
            ApplyInitialPlan(
                repo,
                specId,
                "Stable plan statement");

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", originalCandidate,
                "--expected-revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            result.Success,
            result.Markdown);
        Assert.Contains(
            "Changed: `false`",
            result.Markdown);
        Assert.Contains(
            "Applied: `false`",
            result.Markdown);
        Assert.Contains(
            "Previous Revision: `1`",
            result.Markdown);
        Assert.Contains(
            "Target Revision: `1`",
            result.Markdown);

        ImplementationPlan canonical =
            SpecJsonSerializer.Deserialize<ImplementationPlan>(
                File.ReadAllText(
                    GetPlanPath(
                        repo,
                        specId)));

        Assert.Equal(
            1,
            canonical.Revision.Value);
    }

    [Fact]
    public void Plan_MissingPrerequisites_Rejected()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-missing-deps";

        string planPath =
            repo.WriteCandidate(
                "plan-missing-deps.json",
                CreateImplementationPlan());

        CommandResult missingRequirementSet =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            missingRequirementSet.Success);
        Assert.Contains(
            "missing-dependency",
            missingRequirementSet.Markdown,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "canonical RequirementSet",
            missingRequirementSet.Markdown);

        string requirementPath =
            repo.WriteCandidate(
                "req-missing-workspec.json",
                CreateRequirementSet());

        CommandResult init =
            new SpecCommand().Execute(
            [
                "init",
                "--spec-id", specId,
                "--from", requirementPath,
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            init.Success,
            init.Markdown);

        CommandResult missingWorkSpec =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            missingWorkSpec.Success);
        Assert.Contains(
            "missing-dependency",
            missingWorkSpec.Markdown,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "canonical WorkSpec",
            missingWorkSpec.Markdown);
    }

    [Fact]
    public void Plan_StaleWorkSpec_Rejected()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-stale-ws";

        PreparePlanPrerequisites(
            repo,
            specId);

        string requirementV2Path =
            repo.WriteCandidate(
                "req-stale-v2.json",
                CreateRequirementSet(
                    requirementStatement_:
                        "Updated requirement"));

        CommandResult refineRequirement =
            new SpecCommand().Execute(
            [
                "refine",
                "--spec-id", specId,
                "--artifact", "requirements",
                "--from", requirementV2Path,
                "--expected-revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            refineRequirement.Success,
            refineRequirement.Markdown);

        string planPath =
            repo.WriteCandidate(
                "plan-stale.json",
                CreateImplementationPlan());

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            result.Success);
        Assert.Contains(
            "stale-dependency",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "stale canonical WorkSpec",
            result.Markdown);
    }

    [Fact]
    public void Plan_WorkSpecRevisionMismatch_Rejected()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-ws-mismatch";

        PreparePlanPrerequisites(
            repo,
            specId);

        string planPath =
            repo.WriteCandidate(
                "plan-ws-mismatch.json",
                CreateImplementationPlan(
                    workSpecRevision_:
                        2));

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            result.Success);
        Assert.Contains(
            "validation-failed",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "WorkSpec",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_CandidateValidationFailure_Rejected()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-invalid-refs";

        PreparePlanPrerequisites(
            repo,
            specId);

        string planPath =
            repo.WriteCandidate(
                "plan-invalid-refs.json",
                CreateImplementationPlan(
                    requirementId_:
                        "REQ-999",
                    acceptanceCriterionId_:
                        "AC-999"));

        CommandResult result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            result.Success);
        Assert.Contains(
            "validation-failed",
            result.Markdown,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(
            result.Markdown.Contains(
                "REQ-999",
                StringComparison.Ordinal) ||
            result.Markdown.Contains(
                "AC-999",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_MalformedInput_And_Boundaries()
    {
        using TestRepo repo =
            new();

        string validPath =
            repo.WriteCandidate(
                "plan-boundary-valid.json",
                CreateImplementationPlan());

        CommandResult missingFromOption =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", "spec-plan-boundary",
                "--repo", repo.Root
            ]);

        Assert.False(
            missingFromOption.Success);
        Assert.Contains(
            "Missing required option: '--from'.",
            missingFromOption.Markdown);

        string missingPath =
            Path.Combine(
                repo.Root,
                "does-not-exist.json");

        CommandResult missingFile =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", "spec-plan-boundary",
                "--from", missingPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            missingFile.Success);
        Assert.Contains(
            "read-failed",
            missingFile.Markdown,
            StringComparison.OrdinalIgnoreCase);

        string hugePath =
            Path.Combine(
                repo.Root,
                "plan-huge.json");

        using (
            FileStream stream =
                File.Create(
                    hugePath))
        {
            stream.SetLength(
                SpecWorkspace.MaximumArtifactSizeBytes +
                1);
        }

        CommandResult hugeResult =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", "spec-plan-boundary",
                "--from", hugePath,
                "--repo", repo.Root
            ]);

        Assert.False(
            hugeResult.Success);
        Assert.Contains(
            "artifact-too-large",
            hugeResult.Markdown,
            StringComparison.OrdinalIgnoreCase);

        string invalidUtf8Path =
            Path.Combine(
                repo.Root,
                "plan-invalid-utf8.json");

        File.WriteAllBytes(
            invalidUtf8Path,
            [0xFF, 0xFE, 0xFD]);

        CommandResult utf8Result =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", "spec-plan-boundary",
                "--from", invalidUtf8Path,
                "--repo", repo.Root
            ]);

        Assert.False(
            utf8Result.Success);
        Assert.Contains(
            "invalid-utf8",
            utf8Result.Markdown,
            StringComparison.OrdinalIgnoreCase);

        string malformedPath =
            Path.Combine(
                repo.Root,
                "plan-malformed.json");

        File.WriteAllText(
            malformedPath,
            "{ malformed json");

        CommandResult malformedResult =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", "spec-plan-boundary",
                "--from", malformedPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            malformedResult.Success);
        Assert.Contains(
            "invalid-json",
            malformedResult.Markdown,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(
            File.Exists(
                validPath));
    }

    [Fact]
    public void Plan_ParserAndFlagValidation()
    {
        using TestRepo repo =
            new();

        string planPath =
            repo.WriteCandidate(
                "plan-parser.json",
                CreateImplementationPlan());

        CommandResult bothModes =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", "spec-plan-parser",
                "--from", planPath,
                "--repo", repo.Root,
                "--dry-run",
                "--apply"
            ]);

        Assert.False(
            bothModes.Success);
        Assert.Contains(
            "Cannot specify both '--dry-run' and '--apply'.",
            bothModes.Markdown);

        CommandResult invalidSpecId =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", "INVALID_SPEC",
                "--from", planPath,
                "--repo", repo.Root
            ]);

        Assert.False(
            invalidSpecId.Success);
        Assert.Contains(
            "Invalid Spec ID",
            invalidSpecId.Markdown);

        foreach (string invalidRevision in
                 new[]
                 {
                     "abc",
                     "0",
                     "-1"
                 })
        {
            CommandResult invalidExpected =
                new SpecCommand().Execute(
                [
                    "plan",
                    "--spec-id", "spec-plan-parser",
                    "--from", planPath,
                    "--expected-revision", invalidRevision,
                    "--repo", repo.Root
                ]);

            Assert.False(
                invalidExpected.Success);
            Assert.Contains(
                "Expected revision must be a positive integer.",
                invalidExpected.Markdown);
        }
    }

    [Fact]
    public void Plan_JsonOutput_Stability()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-plan-json";

        PreparePlanPrerequisites(
            repo,
            specId);

        string planPath =
            repo.WriteCandidate(
                "plan-json.json",
                CreateImplementationPlan());

        CommandResult dryRunOne =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root,
                "--json"
            ]);

        CommandResult dryRunTwo =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root,
                "--json"
            ]);

        Assert.True(
            dryRunOne.Success,
            dryRunOne.Markdown);
        Assert.True(
            dryRunTwo.Success,
            dryRunTwo.Markdown);
        Assert.Equal(
            dryRunOne.Markdown,
            dryRunTwo.Markdown);
        Assert.DoesNotContain(
            "#",
            dryRunOne.Markdown,
            StringComparison.Ordinal);

        SpecMutationResultDto dryRunDto =
            SpecJsonSerializer.Deserialize<SpecMutationResultDto>(
                dryRunOne.Markdown);

        Assert.Equal(
            specId,
            dryRunDto.SpecId);
        Assert.Equal(
            SpecArtifactKind.ImplementationPlan,
            dryRunDto.ArtifactKind);
        Assert.Equal(
            SpecWriteMode.DryRun,
            dryRunDto.Mode);
        Assert.True(
            dryRunDto.Changed);
        Assert.False(
            dryRunDto.Applied);
        Assert.Null(
            dryRunDto.PreviousRevision);
        Assert.Null(
            dryRunDto.CurrentRevision);
        Assert.Equal(
            1,
            dryRunDto.TargetRevision.Value);

        using (
            JsonDocument document =
                JsonDocument.Parse(
                    dryRunOne.Markdown))
        {
            Assert.Equal(
                "implementationPlan",
                document
                    .RootElement
                    .GetProperty(
                        "artifactKind")
                    .GetString());
        }

        CommandResult applyResult =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--repo", repo.Root,
                "--apply",
                "--json"
            ]);

        Assert.True(
            applyResult.Success,
            applyResult.Markdown);
        Assert.DoesNotContain(
            "#",
            applyResult.Markdown,
            StringComparison.Ordinal);

        SpecMutationResultDto applyDto =
            SpecJsonSerializer.Deserialize<SpecMutationResultDto>(
                applyResult.Markdown);

        Assert.True(
            applyDto.Applied);
        Assert.NotNull(
            applyDto.CurrentRevision);
        Assert.Equal(
            1,
            applyDto.CurrentRevision.Value.Value);
        Assert.Equal(
            1,
            applyDto.TargetRevision.Value);

        CommandResult failure =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planPath,
                "--expected-revision", "2",
                "--repo", repo.Root,
                "--json"
            ]);

        Assert.False(
            failure.Success);
        Assert.DoesNotContain(
            "#",
            failure.Markdown,
            StringComparison.Ordinal);

        SpecCliErrorDto errorDto =
            SpecJsonSerializer.Deserialize<SpecCliErrorDto>(
                failure.Markdown);

        Assert.Equal(
            "revision-conflict",
            errorDto.ErrorCode);
        Assert.Contains(
            "does not match current revision",
            errorDto.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void PreparePlanPrerequisites(
        TestRepo repo_,
        string specId_)
    {
        string requirementPath =
            repo_.WriteCandidate(
                specId_ + "-requirements.json",
                CreateRequirementSet());

        CommandResult init =
            new SpecCommand().Execute(
            [
                "init",
                "--spec-id", specId_,
                "--from", requirementPath,
                "--repo", repo_.Root,
                "--apply"
            ]);

        Assert.True(
            init.Success,
            init.Markdown);

        string workSpecPath =
            repo_.WriteCandidate(
                specId_ + "-work-spec.json",
                CreateWorkSpec());

        CommandResult workSpec =
            new SpecCommand().Execute(
            [
                "refine",
                "--spec-id", specId_,
                "--artifact", "work-spec",
                "--from", workSpecPath,
                "--repo", repo_.Root,
                "--apply"
            ]);

        Assert.True(
            workSpec.Success,
            workSpec.Markdown);
    }

    private static string ApplyInitialPlan(
        TestRepo repo_,
        string specId_,
        string stepStatement_ = "Initial plan statement")
    {
        PreparePlanPrerequisites(
            repo_,
            specId_);

        string planPath =
            repo_.WriteCandidate(
                specId_ + "-implementation-plan.json",
                CreateImplementationPlan(
                    stepStatement_:
                        stepStatement_));

        CommandResult plan =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId_,
                "--from", planPath,
                "--repo", repo_.Root,
                "--apply"
            ]);

        Assert.True(
            plan.Success,
            plan.Markdown);

        return planPath;
    }

    private static string GetPlanPath(
        TestRepo repo_,
        string specId_)
    {
        return Path.Combine(
            repo_.Root,
            ".ai",
            "specs",
            specId_,
            "implementation-plan.json");
    }

    private static ImplementationPlan CreateImplementationPlan(
        int revision_ = 1,
        int workSpecRevision_ = 1,
        string stepStatement_ = "Implementation plan step",
        string requirementId_ = "REQ-001",
        string acceptanceCriterionId_ = "AC-001")
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
                        stepStatement_,
                    RequirementIds =
                    [
                        new StableEntityId(
                            requirementId_)
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId(
                            acceptanceCriterionId_)
                    ]
                }
            ]
        };
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

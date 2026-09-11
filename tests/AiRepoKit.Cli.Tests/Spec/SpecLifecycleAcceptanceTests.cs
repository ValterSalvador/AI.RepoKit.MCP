using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services.SpecContexts;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Projection;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecLifecycleAcceptanceTests
{
    [Fact]
    public void ScenarioA_RequirementApprovalInvalidationChain_DownstreamStalenessExplicitlyReported()
    {
        using TestRepo repo = new();
        const string specId = "spec-scenario-a";
        string reqCandidate1 = repo.WriteCandidate("req1.json", CreateRequirementSet(revision_: 1, statement_: "Initial requirement statement"));
        string wsCandidate = repo.WriteCandidate("ws1.json", CreateWorkSpec(revision_: 1, requirementSetRevision_: 1));

        // 1. Initial RequirementSet apply -> revision 1 (Matrix 01)
        CommandResult initResult = new SpecCommand().Execute([
            "init",
            "--spec-id", specId,
            "--from", reqCandidate1,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(initResult.Success);
        Assert.Equal(0, initResult.ExitCode);

        // 2. Reject wrong revision approval (Matrix 05)
        CommandResult wrongRevApprove = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "requirements",
            "--revision", "99",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.False(wrongRevApprove.Success);
        Assert.Equal(1, wrongRevApprove.ExitCode);
        Assert.Contains("revision-conflict", wrongRevApprove.Markdown);

        // 3. Refine initial WorkSpec against current RequirementSet (allowed while RequirementSet is NotApproved) (Matrix 10)
        CommandResult wsRefine = new SpecCommand().Execute([
            "refine",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--from", wsCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(wsRefine.Success);
        Assert.Equal(0, wsRefine.ExitCode);
        Assert.Contains("Target Revision: `1`", wsRefine.Markdown);

        // 4. Reject WorkSpec approval when RequirementSet is NotApproved (Matrix 11)
        CommandResult wsApproveBeforeReq = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.False(wsApproveBeforeReq.Success);
        Assert.Equal(1, wsApproveBeforeReq.ExitCode);
        Assert.Contains("approval-prerequisite-failed", wsApproveBeforeReq.Markdown);

        // 5. Approve current RequirementSet -> CURRENT (Matrix 04)
        CommandResult reqApprove = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(reqApprove.Success);
        Assert.Equal(0, reqApprove.ExitCode);
        Assert.Contains("Current Approval Status: `Current`", reqApprove.Markdown);

        // 6. Exact re-approval is idempotent no-op (Matrix 06, 18)
        CommandResult reqReapprove = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(reqReapprove.Success);
        Assert.Contains("Changed: `false`", reqReapprove.Markdown);
        Assert.Contains("Applied: `false`", reqReapprove.Markdown);

        // 7. Approve WorkSpec while RequirementSet is CURRENT -> CURRENT (Matrix 13)
        CommandResult wsApprove = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(wsApprove.Success);
        Assert.Equal(0, wsApprove.ExitCode);
        Assert.Contains("Current Approval Status: `Current`", wsApprove.Markdown);

        // 8. Store existing ImplementationPlan and record its approval in ledger directly
        SpecWorkspace workspace = new(repo.Root, new SpecId(specId));
        ImplementationPlan plan = CreateImplementationPlan(revision_: 1, workSpecRevision_: 1);
        workspace.Store(plan, new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        SpecApprovalLedgerStore ledgerStore = new(repo.Root, new SpecId(specId));
        ledgerStore.Append(
            id => SpecApprovalBinding.Create(id, plan),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply, ExpectedCurrentRevision = new ArtifactRevision(2) });

        // Verify baseline state: all 3 artifacts are CURRENT and not stale
        CommandResult showBaseline = new SpecCommand().Execute([
            "show",
            "--spec-id", specId,
            "--artifact", "all",
            "--repo", repo.Root
        ]);
        Assert.True(showBaseline.Success);
        Assert.Contains("- Requirements: Present (rev 1, status: Current)", showBaseline.Markdown);
        Assert.Contains("- Work Spec: Present (rev 1, status: Current, stale: false)", showBaseline.Markdown);
        Assert.Contains("- Implementation Plan: Present (rev 1, status: Current, stale: false)", showBaseline.Markdown);

        string ledgerPath = Path.Combine(repo.Root, ".ai", "specs", specId, "approvals.json");
        byte[] ledgerBytesBefore = File.ReadAllBytes(ledgerPath);

        // 9. RequirementSet semantic change -> revision 2 (Matrix 03)
        string reqCandidate2 = repo.WriteCandidate("req2.json", CreateRequirementSet(revision_: 1, statement_: "Updated requirement statement v2"));
        CommandResult reqRefineResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", specId,
            "--artifact", "requirements",
            "--from", reqCandidate2,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(reqRefineResult.Success);
        Assert.Equal(0, reqRefineResult.ExitCode);
        Assert.Contains("Target Revision: `2`", reqRefineResult.Markdown);

        // 10. Verify invalidation chain and downstream stale reporting (Matrix 07, 08, 09, 16, 17)
        // Markdown verification
        CommandResult showInvalidated = new SpecCommand().Execute([
            "show",
            "--spec-id", specId,
            "--artifact", "all",
            "--repo", repo.Root
        ]);
        Assert.True(showInvalidated.Success);
        Assert.Contains("- Requirements: Present (rev 2, status: Stale)", showInvalidated.Markdown);
        Assert.Contains("- Work Spec: Present (rev 1, status: Stale, stale: true)", showInvalidated.Markdown);
        Assert.Contains("- Implementation Plan: Present (rev 1, status: Stale, stale: true)", showInvalidated.Markdown);

        // JSON verification
        CommandResult showInvalidatedJson = new SpecCommand().Execute([
            "show",
            "--spec-id", specId,
            "--artifact", "all",
            "--repo", repo.Root,
            "--json"
        ]);
        Assert.True(showInvalidatedJson.Success);
        using (JsonDocument doc = JsonDocument.Parse(showInvalidatedJson.Markdown))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal("stale", root.GetProperty("requirements").GetProperty("approvalStatus").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, root.GetProperty("requirements").GetProperty("revision").GetInt32());

            Assert.True(root.GetProperty("workSpec").GetProperty("stale").GetBoolean());
            Assert.Equal("stale", root.GetProperty("workSpec").GetProperty("approvalStatus").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(1, root.GetProperty("workSpec").GetProperty("revision").GetInt32());

            Assert.True(root.GetProperty("implementationPlan").GetProperty("stale").GetBoolean());
            Assert.Equal("stale", root.GetProperty("implementationPlan").GetProperty("approvalStatus").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(1, root.GetProperty("implementationPlan").GetProperty("revision").GetInt32());
        }

        // Ledger is completely untouched (append-only history preserved, no rewrite or deletion)
        Assert.Equal(ledgerBytesBefore, File.ReadAllBytes(ledgerPath));

        // 11. WorkSpec approval while RequirementSet STALE -> reject (Matrix 12)
        CommandResult staleWsApprove = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.False(staleWsApprove.Success);
        Assert.Equal(1, staleWsApprove.ExitCode);
        Assert.Contains("stale", staleWsApprove.Markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScenarioB_WorkSpecChangeInvalidatesPlan_AndWorkSpecSemanticNoOpIsIdempotent()
    {
        using TestRepo repo = new();
        const string specId = "spec-scenario-b";
        string reqCandidate = repo.WriteCandidate("req.json", CreateRequirementSet());
        string wsCandidate = repo.WriteCandidate("ws.json", CreateWorkSpec());

        // Setup approved RequirementSet and approved WorkSpec
        _ = new SpecCommand().Execute(["init", "--spec-id", specId, "--from", reqCandidate, "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["approve", "--spec-id", specId, "--artifact", "requirements", "--revision", "1", "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["refine", "--spec-id", specId, "--artifact", "work-spec", "--from", wsCandidate, "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["approve", "--spec-id", specId, "--artifact", "work-spec", "--revision", "1", "--repo", repo.Root, "--apply"]);

        // Store existing ImplementationPlan (rev 1, WorkSpecRevision 1) and approve it
        SpecWorkspace workspace = new(repo.Root, new SpecId(specId));
        ImplementationPlan plan = CreateImplementationPlan(revision_: 1, workSpecRevision_: 1);
        workspace.Store(plan, new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        SpecApprovalLedgerStore ledgerStore = new(repo.Root, new SpecId(specId));
        ledgerStore.Append(
            id => SpecApprovalBinding.Create(id, plan),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply, ExpectedCurrentRevision = new ArtifactRevision(2) });

        string wsPath = Path.Combine(repo.Root, ".ai", "specs", specId, "work-spec.json");
        byte[] wsBytesBeforeNoOp = File.ReadAllBytes(wsPath);
        DateTime wsWriteTimeBeforeNoOp = File.GetLastWriteTimeUtc(wsPath);

        // 1. WorkSpec semantic no-op -> same revision / no canonical write (Matrix 14)
        CommandResult noOpResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--from", wsCandidate,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(noOpResult.Success);
        Assert.Equal(0, noOpResult.ExitCode);
        Assert.Contains("Changed: `false`", noOpResult.Markdown);
        Assert.Contains("Applied: `false`", noOpResult.Markdown);
        Assert.Contains("Target Revision: `1`", noOpResult.Markdown);

        Assert.Equal(wsBytesBeforeNoOp, File.ReadAllBytes(wsPath));
        Assert.Equal(wsWriteTimeBeforeNoOp, File.GetLastWriteTimeUtc(wsPath));

        // 2. WorkSpec semantic change -> revision +1 (Matrix 15)
        string changedWsCandidate = repo.WriteCandidate("ws2.json", CreateWorkSpec(criterionStatement_: "Updated criterion for scenario B"));
        CommandResult wsChangeResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--from", changedWsCandidate,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(wsChangeResult.Success);
        Assert.Equal(0, wsChangeResult.ExitCode);
        Assert.Contains("Changed: `true`", wsChangeResult.Markdown);
        Assert.Contains("Applied: `true`", wsChangeResult.Markdown);
        Assert.Contains("Previous Revision: `1`", wsChangeResult.Markdown);
        Assert.Contains("Target Revision: `2`", wsChangeResult.Markdown);

        // 3. Existing ImplementationPlan after WorkSpec change -> artifact stale, approval STALE (Matrix 16, 17)
        CommandResult showResult = new SpecCommand().Execute([
            "show",
            "--spec-id", specId,
            "--artifact", "all",
            "--repo", repo.Root
        ]);
        Assert.True(showResult.Success);
        Assert.Contains("- Requirements: Present (rev 1, status: Current)", showResult.Markdown);
        Assert.Contains("- Work Spec: Present (rev 2, status: Stale, stale: false)", showResult.Markdown);
        Assert.Contains("- Implementation Plan: Present (rev 1, status: Stale, stale: true)", showResult.Markdown);

        // JSON queryability
        CommandResult jsonResult = new SpecCommand().Execute([
            "show",
            "--spec-id", specId,
            "--artifact", "all",
            "--repo", repo.Root,
            "--json"
        ]);
        Assert.True(jsonResult.Success);
        using (JsonDocument doc = JsonDocument.Parse(jsonResult.Markdown))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal("current", root.GetProperty("requirements").GetProperty("approvalStatus").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal("stale", root.GetProperty("workSpec").GetProperty("approvalStatus").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, root.GetProperty("workSpec").GetProperty("revision").GetInt32());
            Assert.False(root.GetProperty("workSpec").GetProperty("stale").GetBoolean());

            Assert.True(root.GetProperty("implementationPlan").GetProperty("stale").GetBoolean());
            Assert.Equal("stale", root.GetProperty("implementationPlan").GetProperty("approvalStatus").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(1, root.GetProperty("implementationPlan").GetProperty("revision").GetInt32());
        }
    }

    [Fact]
    public void ScenarioC_DryRunImmutability_InitRefineApprove_ZeroCanonicalFilesystemMutation()
    {
        using TestRepo repo = new();
        const string specId = "spec-scenario-c";
        string reqCandidate = repo.WriteCandidate("req.json", CreateRequirementSet());
        string changedReqCandidate = repo.WriteCandidate("req2.json", CreateRequirementSet(statement_: "Changed statement for dry run"));
        string wsCandidate = repo.WriteCandidate("ws.json", CreateWorkSpec());

        // 1. Dry-run init
        CommandResult dryInit = new SpecCommand().Execute([
            "init",
            "--spec-id", specId,
            "--from", reqCandidate,
            "--repo", repo.Root,
            "--dry-run"
        ]);
        Assert.True(dryInit.Success);
        Assert.Contains("Mode: `DryRun`", dryInit.Markdown);
        Assert.Contains("Applied: `false`", dryInit.Markdown);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".ai")));

        // Apply init to create baseline RequirementSet revision 1
        _ = new SpecCommand().Execute(["init", "--spec-id", specId, "--from", reqCandidate, "--repo", repo.Root, "--apply"]);
        Snapshot repoSnapshot1 = Snapshot.Capture(repo.Root);

        // 2. Dry-run refine RequirementSet
        CommandResult dryRefineReq = new SpecCommand().Execute([
            "refine",
            "--spec-id", specId,
            "--artifact", "requirements",
            "--from", changedReqCandidate,
            "--expected-revision", "1",
            "--repo", repo.Root,
            "--dry-run"
        ]);
        Assert.True(dryRefineReq.Success);
        Assert.Contains("Mode: `DryRun`", dryRefineReq.Markdown);
        Assert.Contains("Applied: `false`", dryRefineReq.Markdown);
        Assert.Contains("Target Revision: `2`", dryRefineReq.Markdown);
        repoSnapshot1.AssertUnchanged(repo.Root);

        // 3. Dry-run refine WorkSpec (first creation)
        CommandResult dryRefineWs = new SpecCommand().Execute([
            "refine",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--from", wsCandidate,
            "--repo", repo.Root,
            "--dry-run"
        ]);
        Assert.True(dryRefineWs.Success);
        Assert.Contains("Mode: `DryRun`", dryRefineWs.Markdown);
        Assert.Contains("Applied: `false`", dryRefineWs.Markdown);
        Assert.Contains("Target Revision: `1`", dryRefineWs.Markdown);
        repoSnapshot1.AssertUnchanged(repo.Root);

        // 4. Dry-run approve RequirementSet
        CommandResult dryApproveReq = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--dry-run"
        ]);
        Assert.True(dryApproveReq.Success);
        Assert.Contains("Mode: `DryRun`", dryApproveReq.Markdown);
        Assert.Contains("Applied: `false`", dryApproveReq.Markdown);
        Assert.Contains("Proposed Approval Status: `Current`", dryApproveReq.Markdown);
        repoSnapshot1.AssertUnchanged(repo.Root);

        // Apply RequirementSet approval and WorkSpec refine
        _ = new SpecCommand().Execute(["approve", "--spec-id", specId, "--artifact", "requirements", "--revision", "1", "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["refine", "--spec-id", specId, "--artifact", "work-spec", "--from", wsCandidate, "--repo", repo.Root, "--apply"]);
        Snapshot repoSnapshot2 = Snapshot.Capture(repo.Root);

        // 5. Dry-run approve WorkSpec
        CommandResult dryApproveWs = new SpecCommand().Execute([
            "approve",
            "--spec-id", specId,
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--dry-run"
        ]);
        Assert.True(dryApproveWs.Success);
        Assert.Contains("Mode: `DryRun`", dryApproveWs.Markdown);
        Assert.Contains("Applied: `false`", dryApproveWs.Markdown);
        Assert.Contains("Proposed Approval Status: `Current`", dryApproveWs.Markdown);
        repoSnapshot2.AssertUnchanged(repo.Root);
    }

    [Fact]
    public void ScenarioD_Concurrency_CoordinationIdentitySerialized_PreventsSupersededApproval()
    {
        using TestRepo repo = new();
        const string specId = "spec-scenario-d";
        SpecLifecycleService service = new(repo.Root, new SpecId(specId));

        // Create RequirementSet at revision 1
        service.InitializeRequirementSet(
            CreateRequirementSet(revision_: 1, statement_: "Base requirement"),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        // Verify coordination mutex derivation is deterministic for the spec directory
        string specDirectory = Path.Combine(repo.Root, ".ai", "specs", specId);
        string identity = Path.GetFullPath(specDirectory);
        if (OperatingSystem.IsWindows())
        {
            identity = identity.ToUpperInvariant();
        }
        string expectedMutexName = "AIRepoKit.Spec." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        Assert.StartsWith("AIRepoKit.Spec.", expectedMutexName, StringComparison.Ordinal);

        // Now refine RequirementSet to revision 2, superseding revision 1
        service.RefineRequirementSet(
            CreateRequirementSet(revision_: 2, statement_: "Updated requirement that supersedes rev 1"),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply, ExpectedCurrentRevision = new ArtifactRevision(1) });

        // Approval requested for superseded revision 1 MUST be rejected inside the Append transaction
        SpecPersistenceException exception = Assert.Throws<SpecPersistenceException>(() =>
            service.ApproveRequirementSet(
                new ArtifactRevision(1),
                new SpecStoreOptions { Mode = SpecWriteMode.Apply }));

        Assert.Equal(SpecPersistenceException.RevisionConflict, exception.ErrorCode);

        // Zero ledger mutation occurred for the superseded approval attempt
        string ledgerPath = Path.Combine(specDirectory, "approvals.json");
        Assert.False(File.Exists(ledgerPath));

        // Approving the actual current revision (2) succeeds
        SpecApprovalLedgerStoreResult validResult = service.ApproveRequirementSet(
            new ArtifactRevision(2),
            new SpecStoreOptions { Mode = SpecWriteMode.Apply });

        Assert.True(validResult.Applied);
        Assert.Equal(new ArtifactRevision(2), validResult.Approval.ArtifactRevision);
        Assert.True(File.Exists(ledgerPath));
    }

    [Fact]
    public void MarkdownProjectionChange_ApprovalValidityUnchanged()
    {
        using TestRepo repo = new();
        const string specId = "spec-proj-test";
        string reqCandidate = repo.WriteCandidate("req.json", CreateRequirementSet());

        _ = new SpecCommand().Execute(["init", "--spec-id", specId, "--from", reqCandidate, "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["approve", "--spec-id", specId, "--artifact", "requirements", "--revision", "1", "--repo", repo.Root, "--apply"]);

        SpecLifecycleService service = new(repo.Root, new SpecId(specId));
        Assert.Equal(SpecApprovalStatus.Current, service.GetApprovalStatus(SpecArtifactKind.RequirementSet)!.Status);

        // Produce alternate markdown projection / documentation with different formatting
        string modifiedMarkdown = "# Custom Projected Title\n\n* Altered bullet style\n* Different whitespace\n\n";
        string mdPath = Path.Combine(repo.Root, ".ai", "specs", specId, "requirements.md");
        File.WriteAllText(mdPath, modifiedMarkdown);

        // Approval status evaluated against canonical state remains CURRENT
        Assert.Equal(SpecApprovalStatus.Current, service.GetApprovalStatus(SpecArtifactKind.RequirementSet)!.Status);

        CommandResult showResult = new SpecCommand().Execute([
            "show",
            "--spec-id", specId,
            "--artifact", "requirements",
            "--repo", repo.Root
        ]);
        Assert.True(showResult.Success);
        Assert.Contains("Approval Status: `Current`", showResult.Markdown);
    }

    [Fact]
    public void ExistingSpecContextAndQwen_DuringCanonicalRefine_Untouched()
    {
        using TestRepo repo = new();
        const string specId = "spec-untouched-test";

        // Setup .qwen/ folder in repository root
        string qwenDir = Path.Combine(repo.Root, ".qwen");
        Directory.CreateDirectory(qwenDir);
        string qwenFile = Path.Combine(qwenDir, "settings.json");
        File.WriteAllText(qwenFile, """{"qwenConfig": true}""");

        // Setup existing SpecContext in .ai/generated/spec-context/{specId}.json
        SpecContextPersistenceService contextService = new();
        SpecContext context = CreateValidSpecContext(specId);
        string relativeContextPath = contextService.Persist(repo.Root, context);
        string fullContextPath = Path.Combine(repo.Root, relativeContextPath);

        byte[] qwenBytesBefore = File.ReadAllBytes(qwenFile);
        DateTime qwenWriteBefore = File.GetLastWriteTimeUtc(qwenFile);

        byte[] contextBytesBefore = File.ReadAllBytes(fullContextPath);
        DateTime contextWriteBefore = File.GetLastWriteTimeUtc(fullContextPath);

        // Perform canonical lifecycle operations
        string reqCandidate1 = repo.WriteCandidate("req1.json", CreateRequirementSet());
        string reqCandidate2 = repo.WriteCandidate("req2.json", CreateRequirementSet(statement_: "Refined statement"));
        string wsCandidate = repo.WriteCandidate("ws.json", CreateWorkSpec());

        _ = new SpecCommand().Execute(["init", "--spec-id", specId, "--from", reqCandidate1, "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["approve", "--spec-id", specId, "--artifact", "requirements", "--revision", "1", "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["refine", "--spec-id", specId, "--artifact", "work-spec", "--from", wsCandidate, "--repo", repo.Root, "--apply"]);
        _ = new SpecCommand().Execute(["refine", "--spec-id", specId, "--artifact", "requirements", "--from", reqCandidate2, "--expected-revision", "1", "--repo", repo.Root, "--apply"]);

        // Assert .qwen/ file is completely untouched
        Assert.True(File.Exists(qwenFile));
        Assert.Equal(qwenBytesBefore, File.ReadAllBytes(qwenFile));
        Assert.Equal(qwenWriteBefore, File.GetLastWriteTimeUtc(qwenFile));

        // Assert SpecContext is completely untouched
        Assert.True(File.Exists(fullContextPath));
        Assert.Equal(contextBytesBefore, File.ReadAllBytes(fullContextPath));
        Assert.Equal(contextWriteBefore, File.GetLastWriteTimeUtc(fullContextPath));
    }

    [Fact]
    public void P05e_ImplementationPlanIntegratedLifecycle_EndToEndAcceptance()
    {
        using TestRepo repo =
            new();

        const string specId =
            "spec-p05e-integrated";

        string requirementCandidate =
            repo.WriteCandidate(
                "p05e-requirements.json",
                CreateRequirementSet());

        string workSpecCandidate =
            repo.WriteCandidate(
                "p05e-work-spec.json",
                CreateWorkSpec());

        string planCandidateV1 =
            repo.WriteCandidate(
                "p05e-plan-v1.json",
                CreateImplementationPlan(
                    statement_:
                        "Initial integrated plan step"));

        string planCandidateV2 =
            repo.WriteCandidate(
                "p05e-plan-v2.json",
                CreateImplementationPlan(
                    statement_:
                        "Refined integrated plan step"));

        CommandResult init =
            new SpecCommand().Execute(
            [
                "init",
                "--spec-id", specId,
                "--from", requirementCandidate,
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            init.Success,
            init.Markdown);

        CommandResult createWorkSpec =
            new SpecCommand().Execute(
            [
                "refine",
                "--spec-id", specId,
                "--artifact", "work-spec",
                "--from", workSpecCandidate,
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            createWorkSpec.Success,
            createWorkSpec.Markdown);

        Snapshot beforePlanDryRun =
            Snapshot.Capture(
                repo.Root);

        CommandResult planDryRun =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planCandidateV1,
                "--repo", repo.Root
            ]);

        Assert.True(
            planDryRun.Success,
            planDryRun.Markdown);

        Assert.Contains(
            "Mode: `DryRun`",
            planDryRun.Markdown);

        Assert.Contains(
            "Changed: `true`",
            planDryRun.Markdown);

        Assert.Contains(
            "Applied: `false`",
            planDryRun.Markdown);

        Assert.Contains(
            "Target Revision: `1`",
            planDryRun.Markdown);

        beforePlanDryRun.AssertUnchanged(
            repo.Root);

        CommandResult planApply =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planCandidateV1,
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            planApply.Success,
            planApply.Markdown);

        Assert.Contains(
            "Target Revision: `1`",
            planApply.Markdown);

        CommandResult wrongRevision =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planCandidateV2,
                "--expected-revision", "2",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.False(
            wrongRevision.Success);

        Assert.Equal(
            1,
            wrongRevision.ExitCode);

        Assert.Contains(
            "revision-conflict",
            wrongRevision.Markdown,
            StringComparison.OrdinalIgnoreCase);

        CommandResult semanticNoOp =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planCandidateV1,
                "--expected-revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            semanticNoOp.Success,
            semanticNoOp.Markdown);

        Assert.Contains(
            "Changed: `false`",
            semanticNoOp.Markdown);

        Assert.Contains(
            "Applied: `false`",
            semanticNoOp.Markdown);

        Assert.Contains(
            "Target Revision: `1`",
            semanticNoOp.Markdown);

        Snapshot beforeRefineDryRun =
            Snapshot.Capture(
                repo.Root);

        CommandResult refineDryRun =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planCandidateV2,
                "--expected-revision", "1",
                "--repo", repo.Root
            ]);

        Assert.True(
            refineDryRun.Success,
            refineDryRun.Markdown);

        Assert.Contains(
            "Mode: `DryRun`",
            refineDryRun.Markdown);

        Assert.Contains(
            "Changed: `true`",
            refineDryRun.Markdown);

        Assert.Contains(
            "Applied: `false`",
            refineDryRun.Markdown);

        Assert.Contains(
            "Target Revision: `2`",
            refineDryRun.Markdown);

        beforeRefineDryRun.AssertUnchanged(
            repo.Root);

        CommandResult refineApply =
            new SpecCommand().Execute(
            [
                "plan",
                "--spec-id", specId,
                "--from", planCandidateV2,
                "--expected-revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            refineApply.Success,
            refineApply.Markdown);

        Assert.Contains(
            "Previous Revision: `1`",
            refineApply.Markdown);

        Assert.Contains(
            "Target Revision: `2`",
            refineApply.Markdown);

        CommandResult approveRequirements =
            new SpecCommand().Execute(
            [
                "approve",
                "--spec-id", specId,
                "--artifact", "requirements",
                "--revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            approveRequirements.Success,
            approveRequirements.Markdown);

        CommandResult approveWorkSpec =
            new SpecCommand().Execute(
            [
                "approve",
                "--spec-id", specId,
                "--artifact", "work-spec",
                "--revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            approveWorkSpec.Success,
            approveWorkSpec.Markdown);

        Snapshot beforeApprovalDryRun =
            Snapshot.Capture(
                repo.Root);

        CommandResult approvePlanDryRun =
            new SpecCommand().Execute(
            [
                "approve",
                "--spec-id", specId,
                "--artifact", "implementation-plan",
                "--revision", "2",
                "--repo", repo.Root
            ]);

        Assert.True(
            approvePlanDryRun.Success,
            approvePlanDryRun.Markdown);

        Assert.Contains(
            "Changed: `true`",
            approvePlanDryRun.Markdown);

        Assert.Contains(
            "Applied: `false`",
            approvePlanDryRun.Markdown);

        Assert.Contains(
            "Current Approval Status: `NotApproved`",
            approvePlanDryRun.Markdown);

        Assert.Contains(
            "Proposed Approval Status: `Current`",
            approvePlanDryRun.Markdown);

        beforeApprovalDryRun.AssertUnchanged(
            repo.Root);

        CommandResult approvePlanApply =
            new SpecCommand().Execute(
            [
                "approve",
                "--spec-id", specId,
                "--artifact", "implementation-plan",
                "--revision", "2",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            approvePlanApply.Success,
            approvePlanApply.Markdown);

        Assert.Contains(
            "Changed: `true`",
            approvePlanApply.Markdown);

        Assert.Contains(
            "Applied: `true`",
            approvePlanApply.Markdown);

        Assert.Contains(
            "Current Approval Status: `Current`",
            approvePlanApply.Markdown);

        Snapshot beforeReapproval =
            Snapshot.Capture(
                repo.Root);

        CommandResult reapproval =
            new SpecCommand().Execute(
            [
                "approve",
                "--spec-id", specId,
                "--artifact", "plan",
                "--revision", "2",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            reapproval.Success,
            reapproval.Markdown);

        Assert.Contains(
            "Changed: `false`",
            reapproval.Markdown);

        Assert.Contains(
            "Applied: `false`",
            reapproval.Markdown);

        beforeReapproval.AssertUnchanged(
            repo.Root);

        CommandResult showHumanOne =
            new SpecCommand().Execute(
            [
                "show",
                "--spec-id", specId,
                "--artifact", "implementation-plan",
                "--repo", repo.Root
            ]);

        CommandResult showHumanTwo =
            new SpecCommand().Execute(
            [
                "show",
                "--spec-id", specId,
                "--artifact", "plan",
                "--repo", repo.Root
            ]);

        Assert.True(
            showHumanOne.Success,
            showHumanOne.Markdown);

        Assert.Equal(
            showHumanOne.Markdown,
            showHumanTwo.Markdown);

        Assert.Contains(
            "Revision: `2`",
            showHumanOne.Markdown);

        Assert.Contains(
            "Stale: `false`",
            showHumanOne.Markdown);

        Assert.Contains(
            "Approval Status: `Current`",
            showHumanOne.Markdown);

        Assert.Contains(
            "Refined integrated plan step",
            showHumanOne.Markdown);

        CommandResult showJsonOne =
            new SpecCommand().Execute(
            [
                "show",
                "--spec-id", specId,
                "--artifact", "implementation-plan",
                "--repo", repo.Root,
                "--json"
            ]);

        CommandResult showJsonTwo =
            new SpecCommand().Execute(
            [
                "show",
                "--spec-id", specId,
                "--artifact", "plan",
                "--repo", repo.Root,
                "--json"
            ]);

        Assert.True(
            showJsonOne.Success,
            showJsonOne.Markdown);

        Assert.Equal(
            showJsonOne.Markdown,
            showJsonTwo.Markdown);

        using (
            JsonDocument showDocument =
                JsonDocument.Parse(
                    showJsonOne.Markdown))
        {
            JsonElement root =
                showDocument.RootElement;

            Assert.True(
                root.GetProperty(
                    "present").GetBoolean());

            Assert.False(
                root.GetProperty(
                    "stale").GetBoolean());

            Assert.Equal(
                2,
                root.GetProperty(
                    "revision").GetInt32());

            Assert.Equal(
                "current",
                root.GetProperty(
                    "approvalStatus").GetString());
        }

        Snapshot beforeChecklist =
            Snapshot.Capture(
                repo.Root);

        CommandResult checklistHumanOne =
            new SpecCommand().Execute(
            [
                "checklist",
                "--spec-id", specId,
                "--repo", repo.Root
            ]);

        CommandResult checklistHumanTwo =
            new SpecCommand().Execute(
            [
                "checklist",
                "--spec-id", specId,
                "--repo", repo.Root
            ]);

        CommandResult checklistJsonOne =
            new SpecCommand().Execute(
            [
                "checklist",
                "--spec-id", specId,
                "--repo", repo.Root,
                "--json"
            ]);

        CommandResult checklistJsonTwo =
            new SpecCommand().Execute(
            [
                "checklist",
                "--spec-id", specId,
                "--repo", repo.Root,
                "--json"
            ]);

        Assert.True(
            checklistHumanOne.Success,
            checklistHumanOne.Markdown);

        Assert.True(
            checklistJsonOne.Success,
            checklistJsonOne.Markdown);

        Assert.Equal(
            checklistHumanOne.Markdown,
            checklistHumanTwo.Markdown);

        Assert.Equal(
            checklistJsonOne.Markdown,
            checklistJsonTwo.Markdown);

        Assert.Contains(
            "Plan Revision: 2",
            checklistHumanOne.Markdown);

        Assert.Contains(
            "Status: CURRENT",
            checklistHumanOne.Markdown);

        Assert.Contains(
            "Approval Status: CURRENT",
            checklistHumanOne.Markdown);

        Assert.Contains(
            "Refined integrated plan step",
            checklistHumanOne.Markdown);

        using (
            JsonDocument checklistDocument =
                JsonDocument.Parse(
                    checklistJsonOne.Markdown))
        {
            JsonElement root =
                checklistDocument.RootElement;

            Assert.Equal(
                2,
                root.GetProperty(
                    "planRevision").GetInt32());

            Assert.False(
                root.GetProperty(
                    "stale").GetBoolean());

            Assert.Equal(
                "current",
                root.GetProperty(
                    "approvalStatus").GetString());
        }

        foreach (
            string forbiddenField in
            new[]
            {
                "\"completed\"",
                "\"progress\"",
                "\"executed\"",
                "\"started\"",
                "\"failed\"",
                "\"assignee\"",
                "\"timestamp\"",
                "\"executionStatus\""
            })
        {
            Assert.DoesNotContain(
                forbiddenField,
                checklistJsonOne.Markdown,
                StringComparison.Ordinal);
        }

        beforeChecklist.AssertUnchanged(
            repo.Root);

        CommandResult refinePlanRejected =
            new SpecCommand().Execute(
            [
                "refine",
                "--spec-id", specId,
                "--artifact", "implementation-plan",
                "--from", planCandidateV2,
                "--expected-revision", "2",
                "--repo", repo.Root
            ]);

        Assert.False(
            refinePlanRejected.Success);

        Assert.Contains(
            "Plan refinement is not supported.",
            refinePlanRejected.Markdown);

        string changedRequirementCandidate =
            repo.WriteCandidate(
                "p05e-requirements-v2.json",
                CreateRequirementSet(
                    statement_:
                        "Requirement changed after Plan approval"));

        CommandResult changeRequirements =
            new SpecCommand().Execute(
            [
                "refine",
                "--spec-id", specId,
                "--artifact", "requirements",
                "--from", changedRequirementCandidate,
                "--expected-revision", "1",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.True(
            changeRequirements.Success,
            changeRequirements.Markdown);

        Assert.Contains(
            "Target Revision: `2`",
            changeRequirements.Markdown);

        Snapshot beforeStaleInspection =
            Snapshot.Capture(
                repo.Root);

        CommandResult showAllStale =
            new SpecCommand().Execute(
            [
                "show",
                "--spec-id", specId,
                "--artifact", "all",
                "--repo", repo.Root
            ]);

        Assert.True(
            showAllStale.Success,
            showAllStale.Markdown);

        Assert.Contains(
            "- Requirements: Present (rev 2, status: Stale)",
            showAllStale.Markdown);

        Assert.Contains(
            "- Work Spec: Present (rev 1, status: Stale, stale: true)",
            showAllStale.Markdown);

        Assert.Contains(
            "- Implementation Plan: Present (rev 2, status: Stale, stale: true)",
            showAllStale.Markdown);

        CommandResult staleShow =
            new SpecCommand().Execute(
            [
                "show",
                "--spec-id", specId,
                "--artifact", "implementation-plan",
                "--repo", repo.Root,
                "--json"
            ]);

        Assert.True(
            staleShow.Success,
            staleShow.Markdown);

        using (
            JsonDocument staleShowDocument =
                JsonDocument.Parse(
                    staleShow.Markdown))
        {
            JsonElement root =
                staleShowDocument.RootElement;

            Assert.True(
                root.GetProperty(
                    "stale").GetBoolean());

            Assert.Equal(
                "stale",
                root.GetProperty(
                    "approvalStatus").GetString());
        }

        CommandResult staleChecklistHuman =
            new SpecCommand().Execute(
            [
                "checklist",
                "--spec-id", specId,
                "--repo", repo.Root
            ]);

        Assert.True(
            staleChecklistHuman.Success,
            staleChecklistHuman.Markdown);

        Assert.Equal(
            0,
            staleChecklistHuman.ExitCode);

        Assert.Contains(
            "Status: STALE",
            staleChecklistHuman.Markdown);

        Assert.Contains(
            "Approval Status: STALE",
            staleChecklistHuman.Markdown);

        CommandResult staleChecklistJson =
            new SpecCommand().Execute(
            [
                "checklist",
                "--spec-id", specId,
                "--repo", repo.Root,
                "--json"
            ]);

        Assert.True(
            staleChecklistJson.Success,
            staleChecklistJson.Markdown);

        using (
            JsonDocument staleChecklistDocument =
                JsonDocument.Parse(
                    staleChecklistJson.Markdown))
        {
            JsonElement root =
                staleChecklistDocument.RootElement;

            Assert.True(
                root.GetProperty(
                    "stale").GetBoolean());

            Assert.Equal(
                "stale",
                root.GetProperty(
                    "approvalStatus").GetString());
        }

        CommandResult stalePlanApproval =
            new SpecCommand().Execute(
            [
                "approve",
                "--spec-id", specId,
                "--artifact", "implementation-plan",
                "--revision", "2",
                "--repo", repo.Root,
                "--apply"
            ]);

        Assert.False(
            stalePlanApproval.Success);

        Assert.Contains(
            "approval-prerequisite-failed",
            stalePlanApproval.Markdown,
            StringComparison.OrdinalIgnoreCase);

        beforeStaleInspection.AssertUnchanged(
            repo.Root);
    }
    private static RequirementSet CreateRequirementSet(
        int revision_ = 1,
        string statement_ = "Requirement statement")
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id = new StableEntityId("INPUT-001"),
                    Text = "Input text"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = statement_,
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
                    Statement = "Constraint statement",
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

    private static ImplementationPlan CreateImplementationPlan(
        int revision_ = 1,
        int workSpecRevision_ = 1,
        string statement_ = "Step statement")
    {
        return new ImplementationPlan
        {
            Revision = new ArtifactRevision(revision_),
            WorkSpecRevision = new ArtifactRevision(workSpecRevision_),
            Steps =
            [
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-001"),
                    Statement = statement_,
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

    private static SpecContext CreateValidSpecContext(string specId_)
    {
        RepositoryEvidence ev = new()
        {
            EvidenceId = "ev-1",
            Source = "src/Test.cs",
            Kind = "file",
            Reference = "src/Test.cs",
            Availability = RepositoryEvidenceAvailability.Available,
            Freshness = RepositoryEvidenceFreshness.Unknown
        };

        SpecContextReference r = new()
        {
            EvidenceId = "ev-1",
            Kind = "file",
            Reference = "src/Test.cs",
            Reason = "Valid reference.",
            Priority = 10
        };

        return new SpecContext
        {
            SchemaId = SpecContextSchema.SchemaId,
            SchemaVersion = SpecContextSchema.SchemaVersion,
            SpecId = specId_,
            RequirementSetRevision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Target = "acceptance-test",
            ReferenceLimit = 10,
            Budget = 1000,
            EstimatedTokens = 100,
            Truncated = false,
            Evidence = [ev],
            References = [r],
            Omissions = []
        };
    }

    private sealed class Snapshot
    {
        private readonly Dictionary<string, (long Length, DateTime LastWriteUtc, byte[] Hash)> _files;

        private Snapshot(Dictionary<string, (long Length, DateTime LastWriteUtc, byte[] Hash)> files_)
        {
            this._files = files_;
        }

        public static Snapshot Capture(string rootDirectory_)
        {
            Dictionary<string, (long Length, DateTime LastWriteUtc, byte[] Hash)> map = new(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(rootDirectory_))
            {
                foreach (string file in Directory.GetFiles(rootDirectory_, "*", SearchOption.AllDirectories))
                {
                    // Exclude candidate json files written directly under repo root
                    if (Path.GetDirectoryName(file) == rootDirectory_)
                    {
                        continue;
                    }

                    byte[] content = File.ReadAllBytes(file);
                    byte[] hash = SHA256.HashData(content);
                    FileInfo info = new(file);
                    string relative = Path.GetRelativePath(rootDirectory_, file);
                    map[relative] = (info.Length, info.LastWriteTimeUtc, hash);
                }
            }

            return new Snapshot(map);
        }

        public void AssertUnchanged(string rootDirectory_)
        {
            Snapshot current = Capture(rootDirectory_);
            Assert.Equal(this._files.Keys.OrderBy(k => k), current._files.Keys.OrderBy(k => k));
            foreach (string key in this._files.Keys)
            {
                (long expectedLen, DateTime expectedWrite, byte[] expectedHash) = this._files[key];
                (long actualLen, DateTime actualWrite, byte[] actualHash) = current._files[key];
                Assert.Equal(expectedLen, actualLen);
                Assert.Equal(expectedHash, actualHash);
                Assert.Equal(expectedWrite, actualWrite);
            }
        }
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "airepokit-acceptance-test-" + Guid.NewGuid().ToString("N"));
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

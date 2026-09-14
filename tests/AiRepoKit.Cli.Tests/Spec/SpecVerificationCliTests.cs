using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Verification;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecVerificationCliTests
{
    private static readonly SpecId _specId = new("cli-verify-spec");

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
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "Statement 1",
                    SourceInputIds = [new StableEntityId("INPUT-001")]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec()
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints = [],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-001"),
                    Statement = "Criterion 1",
                    RequirementIds = [new StableEntityId("REQ-001")]
                }
            ]
        };
    }

    private static ImplementationPlan CreatePlan()
    {
        return new ImplementationPlan
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
                }
            ]
        };
    }

    private static SpecVerificationRequest CreateVerificationRequest(string repoEvdId_ = "build-summary")
    {
        return new SpecVerificationRequest
        {
            Evidence =
            [
                new SpecVerificationEvidenceBinding
                {
                    Evidence = new VerificationEvidence
                    {
                        Id = new StableEntityId("EVD-001"),
                        Description = "Evidence for AC-001",
                        AcceptanceCriterionIds = [new StableEntityId("AC-001")],
                        PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
                    },
                    RepositoryEvidenceId = repoEvdId_
                }
            ]
        };
    }

    private static void SetupApprovedGraph(TestRepo repo_)
    {
        repo_.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet());
        repo_.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 1);
        repo_.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec());
        repo_.ApproveArtifact(_specId, SpecArtifactKind.WorkSpec, 1);
        repo_.InitializeCanonicalImplementationPlan(_specId, CreatePlan());
        repo_.ApproveArtifact(_specId, SpecArtifactKind.ImplementationPlan, 1);
    }

    // ── CLI Routing and options ──────────────────────────────────────────────

    [Fact]
    public void Route_SpecVerify_ViaProgramAndCommand()
    {
        CommandResult result = Program.RouteSpec(["spec", "verify"]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing required option: '--spec-id'.", result.Markdown);
    }

    [Fact]
    public void Missing_SpecId_Rejected()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("cand.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing required option: '--spec-id'.", result.Markdown);
    }

    [Fact]
    public void Missing_From_Rejected()
    {
        using TestRepo repo = new();

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing required option: '--from'.", result.Markdown);
    }

    [Fact]
    public void Reject_DryRun()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("cand.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", candidatePath,
            "--dry-run",
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--dry-run' for 'spec verify'.", result.Markdown);
    }

    [Fact]
    public void Reject_Apply()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("cand.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", candidatePath,
            "--apply",
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--apply' for 'spec verify'.", result.Markdown);
    }

    [Fact]
    public void Reject_ExpectedRevision()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("cand.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", candidatePath,
            "--expected-revision", "1",
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--expected-revision' for 'spec verify'.", result.Markdown);
    }

    [Fact]
    public void Reject_UnknownOption()
    {
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("cand.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", candidatePath,
            "--some-random-opt",
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option '--some-random-opt' for 'spec verify'.", result.Markdown);
    }

    // ── Input Reader validation tests ────────────────────────────────────────

    [Fact]
    public void Missing_InputFile_Rejected()
    {
        using TestRepo repo = new();

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", Path.Combine(repo.Root, "nonexistent.json"),
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("does not exist", result.Markdown);
    }

    [Fact]
    public void Exceeds1MiB_Rejected()
    {
        using TestRepo repo = new();
        string largePath = Path.Combine(repo.Root, "large.json");
        byte[] largeBytes = new byte[SpecWorkspace.MaximumArtifactSizeBytes + 10];
        Array.Fill(largeBytes, (byte)' ');
        File.WriteAllBytes(largePath, largeBytes);

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", largePath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("exceeds the", result.Markdown);
    }

    [Fact]
    public void InvalidUtf8_Rejected()
    {
        using TestRepo repo = new();
        string invalidUtf8Path = Path.Combine(repo.Root, "invalid-utf8.json");
        File.WriteAllBytes(invalidUtf8Path, [0xFF, 0xFE, 0xFD]);

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", invalidUtf8Path,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not valid UTF-8", result.Markdown);
    }

    [Fact]
    public void MalformedJson_Rejected()
    {
        using TestRepo repo = new();
        string malformedPath = Path.Combine(repo.Root, "malformed.json");
        File.WriteAllText(malformedPath, "{ not valid json");

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", malformedPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not valid spec JSON", result.Markdown);
    }

    [Fact]
    public void UnknownJsonMember_Rejected()
    {
        using TestRepo repo = new();
        string unknownMemberPath = Path.Combine(repo.Root, "unknown-member.json");
        File.WriteAllText(unknownMemberPath, """{"evidence": [], "unexpectedField": "bad"}""");

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", unknownMemberPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not valid spec JSON", result.Markdown);
    }

    // ── Exit semantics & Determinism ─────────────────────────────────────────

    [Fact]
    public void Pass_DomainStatus_Exit0()
    {
        using TestRepo repo = new();
        SetupApprovedGraph(repo);
        repo.WriteReport(".ai/generated/reports/latest-build-summary.json", """{"restoreExitCode": 0, "buildExitCode": 0}""");
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest("build-summary"));

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success, result.Markdown);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("- Overall Status: `PASS`", result.Markdown);
    }

    [Fact]
    public void Fail_DomainStatus_Exit0()
    {
        using TestRepo repo = new();
        SetupApprovedGraph(repo);
        repo.WriteReport(".ai/generated/reports/latest-build-summary.json", """{"restoreExitCode": 1, "buildExitCode": 0}""");
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest("build-summary"));

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success, result.Markdown);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("- Overall Status: `FAIL`", result.Markdown);
    }

    [Fact]
    public void NotVerified_DomainStatus_Exit0()
    {
        using TestRepo repo = new();
        SetupApprovedGraph(repo);
        // No reports exist on disk -> build-summary is missing -> Inconclusive -> NotVerified
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest("build-summary"));

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success, result.Markdown);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("- Overall Status: `NOT_VERIFIED`", result.Markdown);
    }

    [Fact]
    public void Deterministic_HumanOutput()
    {
        using TestRepo repo = new();
        SetupApprovedGraph(repo);
        repo.WriteReport(".ai/generated/reports/latest-build-summary.json", """{"restoreExitCode": 0, "buildExitCode": 0}""");
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest("build-summary"));

        CommandResult result1 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);
        CommandResult result2 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.Equal(result1.Markdown, result2.Markdown);
    }

    [Fact]
    public void Deterministic_JsonOutput_And_CamelCaseEnums()
    {
        using TestRepo repo = new();
        SetupApprovedGraph(repo);
        repo.WriteReport(".ai/generated/reports/latest-build-summary.json", """{"restoreExitCode": 0, "buildExitCode": 0}""");
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest("build-summary"));

        CommandResult result1 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root,
            "--json"
        ]);
        CommandResult result2 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(result1.Success);
        Assert.Equal(result1.Markdown, result2.Markdown);

        // Verify camelCase JSON
        JsonNode? root = JsonNode.Parse(result1.Markdown);
        Assert.NotNull(root);
        Assert.Equal("pass", root["overallStatus"]?.GetValue<string>());
        JsonArray? results = root["results"]?.AsArray();
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("pass", results[0]?["status"]?.GetValue<string>());
        Assert.Equal("AC-001", results[0]?["acceptanceCriterionId"]?.GetValue<string>());
        Assert.Equal("VER-001", results[0]?["id"]?.GetValue<string>());

        JsonArray? observations = root["observations"]?.AsArray();
        Assert.NotNull(observations);
        Assert.Single(observations);
        Assert.Equal("supporting", observations[0]?["disposition"]?.GetValue<string>());
        Assert.Equal("available", observations[0]?["availability"]?.GetValue<string>());
        Assert.Equal("unknown", observations[0]?["freshness"]?.GetValue<string>());
        Assert.Equal("build-summary", observations[0]?["repositoryEvidenceId"]?.GetValue<string>());
    }

    [Fact]
    public void ProvenanceAndFreshness_Rendered()
    {
        using TestRepo repo = new();
        SetupApprovedGraph(repo);
        repo.WriteReport(".ai/generated/reports/latest-build-summary.json", """{"restoreExitCode": 0, "buildExitCode": 0}""");
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest("build-summary"));

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.Contains("- Repository Evidence ID: `build-summary`", result.Markdown);
        Assert.Contains("- Source: `build-summary`", result.Markdown);
        Assert.Contains("- Kind: `report`", result.Markdown);
        Assert.Contains("- Reference: `.ai/generated/reports/latest-build-summary.json`", result.Markdown);
        Assert.Contains("- Availability: `Available`", result.Markdown);
        Assert.Contains("- Freshness: `Unknown`", result.Markdown);
        Assert.Contains("- Disposition: `SUPPORTING`", result.Markdown);
    }

    [Fact]
    public void Zero_Filesystem_Mutation()
    {
        using TestRepo repo = new();
        SetupApprovedGraph(repo);
        repo.WriteReport(".ai/generated/reports/latest-build-summary.json", """{"restoreExitCode": 0, "buildExitCode": 0}""");
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest("build-summary"));

        string[] filesBefore = Directory.GetFiles(repo.Root, "*", SearchOption.AllDirectories)
            .OrderBy(p_ => p_, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, long> sizesBefore = filesBefore.ToDictionary(f_ => f_, f_ => new FileInfo(f_).Length);

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success);

        string[] filesAfter = Directory.GetFiles(repo.Root, "*", SearchOption.AllDirectories)
            .OrderBy(p_ => p_, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, long> sizesAfter = filesAfter.ToDictionary(f_ => f_, f_ => new FileInfo(f_).Length);

        Assert.Equal(filesBefore, filesAfter);
        Assert.Equal(sizesBefore, sizesAfter);
    }

    [Fact]
    public void LegacyPlanAndSpecDiff_Unchanged()
    {
        // Program.Parse("plan") remains top-level plan
        BootstrapOptions options = Program.Parse(["plan"]);
        Assert.Equal("plan", options.Command);

        // Spec diff subcommand still operates
        using TestRepo repo = new();
        string candidatePath = repo.WriteCandidate("req.json", CreateRequirementSet());
        CommandResult diffResult = new SpecCommand().Execute([
            "diff",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--from", candidatePath,
            "--repo", repo.Root
        ]);
        Assert.True(diffResult.Success);
        Assert.Contains("# Spec Diff", diffResult.Markdown);
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "spec-cli-verify-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Root);
            Directory.CreateDirectory(Path.Combine(this.Root, ".git"));
        }

        public string WriteCandidate<T>(string fileName_, T obj_)
        {
            string path = Path.Combine(this.Root, fileName_);
            File.WriteAllText(path, SpecJsonSerializer.Serialize(obj_));
            return path;
        }

        public void WriteReport(string relativePath_, string content_)
        {
            string fullPath = Path.Combine(this.Root, relativePath_.Replace('/', Path.DirectorySeparatorChar));
            string? dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, content_);
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

        public void ApproveArtifact(SpecId specId_, SpecArtifactKind kind_, int revision_)
        {
            string artifactName = kind_ switch
            {
                SpecArtifactKind.RequirementSet => "requirements",
                SpecArtifactKind.WorkSpec => "work-spec",
                SpecArtifactKind.ImplementationPlan => "implementation-plan",
                _ => throw new ArgumentOutOfRangeException(nameof(kind_))
            };

            CommandResult result = new SpecCommand().Execute([
                "approve",
                "--spec-id", specId_.Value,
                "--artifact", artifactName,
                "--revision", revision_.ToString(),
                "--repo", this.Root,
                "--apply"
            ]);

            if (!result.Success)
            {
                throw new InvalidOperationException($"Approve helper failed: {result.Markdown}");
            }
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

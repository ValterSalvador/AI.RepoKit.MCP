using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Verification;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecVerificationPrerequisiteTests
{
    private static readonly SpecId _specId = new("prereq-spec");

    private static RequirementSet CreateRequirementSet(int revision_ = 1)
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(revision_),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "Requirement 1",
                    SourceInputIds = [new StableEntityId("INPUT-001")]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec(int revision_ = 1, int requirementSetRevision_ = 1)
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(revision_),
            RequirementSetRevision = new ArtifactRevision(requirementSetRevision_),
            Constraints = [],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-001"),
                    Statement = "Acceptance criterion 1",
                    RequirementIds = [new StableEntityId("REQ-001")]
                }
            ]
        };
    }

    private static ImplementationPlan CreatePlan(int revision_ = 1, int workSpecRevision_ = 1)
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
                    Statement = "Plan step 1",
                    RequirementIds = [new StableEntityId("REQ-001")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-001")]
                }
            ]
        };
    }

    private static SpecVerificationRequest CreateVerificationRequest()
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
                    RepositoryEvidenceId = "build-summary"
                }
            ]
        };
    }

    [Fact]
    public void Verify_MissingRequirementSet_FailsExit1()
    {
        using TestRepo repo = new();
        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RequirementSet does not exist", result.Markdown);
    }

    [Fact]
    public void Verify_MissingWorkSpec_FailsExit1()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet());
        repo.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 1);

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("WorkSpec does not exist", result.Markdown);
    }

    [Fact]
    public void Verify_MissingImplementationPlan_FailsExit1()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet());
        repo.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 1);
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec());
        repo.ApproveArtifact(_specId, SpecArtifactKind.WorkSpec, 1);

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ImplementationPlan does not exist", result.Markdown);
    }

    [Fact]
    public void Verify_StaleWorkSpec_FailsExit1()
    {
        using TestRepo repo = new();
        // RS is at rev 2, but WS references rev 1 => stale
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet(revision_: 2));
        repo.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 2);
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec(revision_: 1, requirementSetRevision_: 1));
        repo.InitializeCanonicalImplementationPlan(_specId, CreatePlan(revision_: 1, workSpecRevision_: 1));

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("WorkSpec is stale", result.Markdown);
    }

    [Fact]
    public void Verify_StalePlan_FailsExit1()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet(revision_: 1));
        repo.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 1);
        // WS is at rev 2, Plan references rev 1 => stale
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec(revision_: 2, requirementSetRevision_: 1));
        repo.ApproveArtifact(_specId, SpecArtifactKind.WorkSpec, 2);
        repo.InitializeCanonicalImplementationPlan(_specId, CreatePlan(revision_: 1, workSpecRevision_: 1));

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ImplementationPlan is stale", result.Markdown);
    }

    [Fact]
    public void Verify_RequirementsNotApproved_FailsExit1()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet());
        // Do not approve RequirementSet
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec());
        repo.InitializeCanonicalImplementationPlan(_specId, CreatePlan());

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RequirementSet approval status is not Current", result.Markdown);
    }

    [Fact]
    public void Verify_WorkSpecNotApproved_FailsExit1()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet());
        repo.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 1);
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec());
        // Do not approve WorkSpec
        repo.InitializeCanonicalImplementationPlan(_specId, CreatePlan());

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("WorkSpec approval status is not Current", result.Markdown);
    }

    [Fact]
    public void Verify_PlanNotApproved_FailsExit1()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet());
        repo.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 1);
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec());
        repo.ApproveArtifact(_specId, SpecArtifactKind.WorkSpec, 1);
        repo.InitializeCanonicalImplementationPlan(_specId, CreatePlan());
        // Do not approve ImplementationPlan

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ImplementationPlan approval status is not Current", result.Markdown);
    }

    [Fact]
    public void Verify_FullyCurrentApprovedGraph_SucceedsExit0()
    {
        using TestRepo repo = new();
        repo.InitializeCanonicalRequirementSet(_specId, CreateRequirementSet());
        repo.ApproveArtifact(_specId, SpecArtifactKind.RequirementSet, 1);
        repo.InitializeCanonicalWorkSpec(_specId, CreateWorkSpec());
        repo.ApproveArtifact(_specId, SpecArtifactKind.WorkSpec, 1);
        repo.InitializeCanonicalImplementationPlan(_specId, CreatePlan());
        repo.ApproveArtifact(_specId, SpecArtifactKind.ImplementationPlan, 1);

        // Add passing build summary
        repo.WriteReport(".ai/generated/reports/latest-build-summary.json", """{"restoreExitCode": 0, "buildExitCode": 0}""");

        string requestPath = repo.WriteCandidate("req.json", CreateVerificationRequest());

        CommandResult result = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", requestPath,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success, result.Markdown);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("# Spec Verification: `prereq-spec`", result.Markdown);
        Assert.Contains("- Overall Status: `PASS`", result.Markdown);
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "spec-prereq-test-" + Guid.NewGuid().ToString("N"));
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
            // Use existing approve command to record canonical approval
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

using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Verification;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecVerificationAcceptanceTests
{
    private static readonly SpecId _specId = new("acceptance-spec");

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "System must build and be secure." }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "The solution must compile and pass restore.",
                    SourceInputIds = [new StableEntityId("INPUT-001")]
                },
                new Requirement
                {
                    Id = new StableEntityId("REQ-002"),
                    Statement = "The solution must have zero leaked secrets.",
                    SourceInputIds = [new StableEntityId("INPUT-001")]
                },
                new Requirement
                {
                    Id = new StableEntityId("REQ-003"),
                    Statement = "The solution must meet future compliance criteria.",
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
                    Statement = "Clean build and restore reports exit code 0.",
                    RequirementIds = [new StableEntityId("REQ-001")]
                },
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-002"),
                    Statement = "Automated secret scan reports 0 findings.",
                    RequirementIds = [new StableEntityId("REQ-002")]
                },
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-003"),
                    Statement = "Compliance check confirms external standard.",
                    RequirementIds = [new StableEntityId("REQ-003")]
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
                    Statement = "Validate build and restore exit codes.",
                    RequirementIds = [new StableEntityId("REQ-001")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-001")]
                },
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-002"),
                    Statement = "Execute automated secret scan.",
                    RequirementIds = [new StableEntityId("REQ-002")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-002")]
                },
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-003"),
                    Statement = "Execute compliance review.",
                    RequirementIds = [new StableEntityId("REQ-003")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-003")]
                }
            ]
        };
    }

    private static SpecVerificationRequest CreateVerificationRequest()
    {
        // AC-001 bound to build-summary
        // AC-002 bound to secret-scan
        // AC-003 has no usable evidence
        return new SpecVerificationRequest
        {
            Evidence =
            [
                new SpecVerificationEvidenceBinding
                {
                    Evidence = new VerificationEvidence
                    {
                        Id = new StableEntityId("EVD-001"),
                        Description = "Build summary report verification.",
                        AcceptanceCriterionIds = [new StableEntityId("AC-001")],
                        PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
                    },
                    RepositoryEvidenceId = "build-summary"
                },
                new SpecVerificationEvidenceBinding
                {
                    Evidence = new VerificationEvidence
                    {
                        Id = new StableEntityId("EVD-002"),
                        Description = "Secret scan report verification.",
                        AcceptanceCriterionIds = [new StableEntityId("AC-002")],
                        PlanStepIds = [new StableEntityId("PLAN-STEP-002")]
                    },
                    RepositoryEvidenceId = "secret-scan"
                }
            ]
        };
    }

    [Fact]
    public void IntegratedAcceptance_3Criteria_ExpectedStatus_DeterminismAndImmutability()
    {
        using TestRepo repo = new();

        // 1. spec init --apply
        string reqCandidate = repo.WriteCandidate("req.json", CreateRequirementSet());
        CommandResult initResult = new SpecCommand().Execute([
            "init",
            "--spec-id", _specId.Value,
            "--from", reqCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(initResult.Success, initResult.Markdown);

        // 2. spec approve requirements --apply
        CommandResult approveReqResult = new SpecCommand().Execute([
            "approve",
            "--spec-id", _specId.Value,
            "--artifact", "requirements",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(approveReqResult.Success, approveReqResult.Markdown);

        // 3. spec refine work-spec --apply
        string wsCandidate = repo.WriteCandidate("ws.json", CreateWorkSpec());
        CommandResult refineWsResult = new SpecCommand().Execute([
            "refine",
            "--spec-id", _specId.Value,
            "--artifact", "work-spec",
            "--from", wsCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(refineWsResult.Success, refineWsResult.Markdown);

        // 4. spec approve work-spec --apply
        CommandResult approveWsResult = new SpecCommand().Execute([
            "approve",
            "--spec-id", _specId.Value,
            "--artifact", "work-spec",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(approveWsResult.Success, approveWsResult.Markdown);

        // 5. spec plan --apply
        string planCandidate = repo.WriteCandidate("plan.json", CreatePlan());
        CommandResult planResult = new SpecCommand().Execute([
            "plan",
            "--spec-id", _specId.Value,
            "--from", planCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(planResult.Success, planResult.Markdown);

        // 6. spec approve implementation-plan --apply
        CommandResult approvePlanResult = new SpecCommand().Execute([
            "approve",
            "--spec-id", _specId.Value,
            "--artifact", "implementation-plan",
            "--revision", "1",
            "--repo", repo.Root,
            "--apply"
        ]);
        Assert.True(approvePlanResult.Success, approvePlanResult.Markdown);

        // 7. Write repository evidence reports:
        // AC-001: passing build summary (restoreExitCode=0, buildExitCode=0)
        repo.WriteReport(
            ".ai/generated/reports/latest-build-summary.json",
            """{"restoreExitCode": 0, "buildExitCode": 0}""");

        // AC-002: contradicting secret scan (redactedOnly=true, findingCount=1)
        repo.WriteReport(
            ".ai/generated/reports/secret-scan-report.json",
            """{"redactedOnly": true, "findingCount": 1, "findings": [{"ruleId": "TEST-KEY"}]}""");

        // AC-003: no usable evidence

        string verifyReqPath = repo.WriteCandidate("verify-request.json", CreateVerificationRequest());

        // Snapshot repository files before verification
        string[] filesBefore = Directory.GetFiles(repo.Root, "*", SearchOption.AllDirectories)
            .OrderBy(p_ => p_, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, byte[]> hashesBefore = filesBefore.ToDictionary(
            f_ => f_,
            f_ => File.ReadAllBytes(f_));

        // 8. Execute spec verify (Human output)
        CommandResult humanResult1 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", verifyReqPath,
            "--repo", repo.Root
        ]);

        Assert.True(humanResult1.Success, humanResult1.Markdown);
        Assert.Equal(0, humanResult1.ExitCode);

        // Verify human output assertions
        Assert.Contains("# Spec Verification: `acceptance-spec`", humanResult1.Markdown);
        Assert.Contains("- Overall Status: `FAIL`", humanResult1.Markdown);
        Assert.Contains("- `AC-001`: `PASS`", humanResult1.Markdown);
        Assert.Contains("- `AC-002`: `FAIL`", humanResult1.Markdown);
        Assert.Contains("- `AC-003`: `NOT_VERIFIED`", humanResult1.Markdown);

        // Execute spec verify second time to test human determinism
        CommandResult humanResult2 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", verifyReqPath,
            "--repo", repo.Root
        ]);
        Assert.Equal(humanResult1.Markdown, humanResult2.Markdown);

        // 9. Execute spec verify (--json output)
        CommandResult jsonResult1 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", verifyReqPath,
            "--repo", repo.Root,
            "--json"
        ]);

        Assert.True(jsonResult1.Success, jsonResult1.Markdown);
        Assert.Equal(0, jsonResult1.ExitCode);

        // Execute spec verify second time to test JSON determinism
        CommandResult jsonResult2 = new SpecCommand().Execute([
            "verify",
            "--spec-id", _specId.Value,
            "--from", verifyReqPath,
            "--repo", repo.Root,
            "--json"
        ]);
        Assert.Equal(jsonResult1.Markdown, jsonResult2.Markdown);

        // Verify structured JSON fields
        JsonNode? root = JsonNode.Parse(jsonResult1.Markdown);
        Assert.NotNull(root);
        Assert.Equal("acceptance-spec", root["specId"]?.GetValue<string>());
        Assert.Equal(1, root["requirementSetRevision"]?.GetValue<int>());
        Assert.Equal(1, root["workSpecRevision"]?.GetValue<int>());
        Assert.Equal(1, root["implementationPlanRevision"]?.GetValue<int>());
        Assert.Equal("fail", root["overallStatus"]?.GetValue<string>());

        JsonArray? results = root["results"]?.AsArray();
        Assert.NotNull(results);
        Assert.Equal(3, results.Count);

        // Ordinal sorted AC results
        Assert.Equal("AC-001", results[0]?["acceptanceCriterionId"]?.GetValue<string>());
        Assert.Equal("VER-001", results[0]?["id"]?.GetValue<string>());
        Assert.Equal("pass", results[0]?["status"]?.GetValue<string>());

        Assert.Equal("AC-002", results[1]?["acceptanceCriterionId"]?.GetValue<string>());
        Assert.Equal("VER-002", results[1]?["id"]?.GetValue<string>());
        Assert.Equal("fail", results[1]?["status"]?.GetValue<string>());

        Assert.Equal("AC-003", results[2]?["acceptanceCriterionId"]?.GetValue<string>());
        Assert.Equal("VER-003", results[2]?["id"]?.GetValue<string>());
        Assert.Equal("notVerified", results[2]?["status"]?.GetValue<string>());

        // 10. Verify repository immutability: zero filesystem changes
        string[] filesAfter = Directory.GetFiles(repo.Root, "*", SearchOption.AllDirectories)
            .OrderBy(p_ => p_, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, byte[]> hashesAfter = filesAfter.ToDictionary(
            f_ => f_,
            f_ => File.ReadAllBytes(f_));

        Assert.Equal(filesBefore, filesAfter);
        foreach (string file in filesBefore)
        {
            Assert.Equal(hashesBefore[file], hashesAfter[file]);
        }
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "spec-verify-acceptance-test-" + Guid.NewGuid().ToString("N"));
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

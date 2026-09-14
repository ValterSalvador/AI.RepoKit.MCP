using System.Text.Json;
using AiRepoKit.Cli.Services.SpecVerification;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Verification;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecVerificationAdapterTests
{
    private static RepositoryEvidence CreateRepoEvidence(
        string evidenceId_ = "build-summary",
        string source_ = "build-summary",
        string kind_ = "report",
        string reference_ = ".ai/generated/reports/latest-build-summary.json",
        RepositoryEvidenceAvailability availability_ = RepositoryEvidenceAvailability.Available,
        RepositoryEvidenceFreshness freshness_ = RepositoryEvidenceFreshness.Unknown,
        string generatedAt_ = "2026-09-10T12:00:00Z")
    {
        return new RepositoryEvidence
        {
            EvidenceId = evidenceId_,
            Source = source_,
            Kind = kind_,
            Reference = reference_,
            Availability = availability_,
            Freshness = freshness_,
            SourceGeneratedAt = generatedAt_,
            Detail = "Evidence detail."
        };
    }

    // ── BuildSummaryEvidenceAdapter tests ────────────────────────────────────

    [Fact]
    public void BuildSummary_Success_YieldsSupporting()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, """{"restoreExitCode": 0, "buildExitCode": 0}""");

        RepositoryEvidence evd = CreateRepoEvidence(reference_: reportRel);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Supporting, disposition);
        Assert.Contains("restoreExitCode=0", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void BuildSummary_RestoreFailure_YieldsContradicting()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, """{"restoreExitCode": 1, "buildExitCode": 0}""");

        RepositoryEvidence evd = CreateRepoEvidence(reference_: reportRel);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Contradicting, disposition);
        Assert.Contains("non-zero exit code", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void BuildSummary_BuildFailure_YieldsContradicting()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, """{"restoreExitCode": 0, "buildExitCode": 2}""");

        RepositoryEvidence evd = CreateRepoEvidence(reference_: reportRel);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Contradicting, disposition);
    }

    [Fact]
    public void BuildSummary_MissingFile_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        // Do not create file

        RepositoryEvidence evd = CreateRepoEvidence(reference_: reportRel);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
    }

    [Fact]
    public void BuildSummary_MalformedJson_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, "{not valid json}");

        RepositoryEvidence evd = CreateRepoEvidence(reference_: reportRel);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
        Assert.Contains("could not be parsed", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void BuildSummary_MissingExitCodeFields_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, """{"target": "Release"}""");

        RepositoryEvidence evd = CreateRepoEvidence(reference_: reportRel);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
    }

    [Fact]
    public void BuildSummary_AvailabilityNotAvailable_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, """{"restoreExitCode": 0, "buildExitCode": 0}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            reference_: reportRel,
            availability_: RepositoryEvidenceAvailability.Unavailable);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
        Assert.Contains("not available", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void BuildSummary_StaleFreshness_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, """{"restoreExitCode": 0, "buildExitCode": 0}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            reference_: reportRel,
            freshness_: RepositoryEvidenceFreshness.Stale);
        BuildSummaryEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
        Assert.Contains("stale", adapter.GetReason(evd, disposition));
    }

    // ── SecretScanEvidenceAdapter tests ──────────────────────────────────────

    [Fact]
    public void SecretScan_RedactedOnlyWithZeroFindings_YieldsSupporting()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, """{"redactedOnly": true, "findingCount": 0, "findings": []}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Supporting, disposition);
        Assert.Contains("finding count=0", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void SecretScan_RedactedOnlyWithFindingsCount_YieldsContradicting()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, """{"redactedOnly": true, "findingCount": 3}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Contradicting, disposition);
        Assert.Contains("findings detected", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void SecretScan_RedactedOnlyWithFindingsArray_YieldsContradicting()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, """{"redactedOnly": true, "findings": [{"ruleId": "rule-1"}]}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Contradicting, disposition);
    }

    [Fact]
    public void SecretScan_NonRedacted_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, """{"redactedOnly": false, "findingCount": 0}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
        Assert.Contains("not redacted-only", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void SecretScan_MissingRedactedOnlyField_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, """{"findingCount": 0}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
    }

    [Fact]
    public void SecretScan_MissingFile_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
    }

    [Fact]
    public void SecretScan_MalformedJson_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, "invalid-json");

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
    }

    [Fact]
    public void SecretScan_StaleFreshness_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, """{"redactedOnly": true, "findingCount": 0}""");

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel,
            freshness_: RepositoryEvidenceFreshness.Stale);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
        Assert.Contains("stale", adapter.GetReason(evd, disposition));
    }

    [Fact]
    public void SecretScan_NeverExposesSecretContent()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/secret-scan-report.json";
        dir.WriteFile(reportRel, """
        {
            "redactedOnly": true,
            "findingCount": 1,
            "findings": [
                {
                    "ruleId": "AKIA-KEY",
                    "secret": "AKIAIOSFODNN7EXAMPLE"
                }
            ]
        }
        """);

        RepositoryEvidence evd = CreateRepoEvidence(
            evidenceId_: "secret-scan",
            source_: "secret-scan",
            reference_: reportRel);
        SecretScanEvidenceAdapter adapter = new();

        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(evd, dir.Path);
        string reason = adapter.GetReason(evd, disposition);

        Assert.Equal(SpecVerificationEvidenceDisposition.Contradicting, disposition);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", reason);
        Assert.DoesNotContain("AKIA-KEY", reason);
    }

    // ── Generic unsupported evidence tests ───────────────────────────────────

    [Fact]
    public void Binder_GenericUnsupportedEvidence_Available_YieldsInconclusive()
    {
        using TestTempDir dir = new();
        RepositoryEvidence policyEvd = CreateRepoEvidence(
            evidenceId_: "repository-policy",
            source_: "repository-policy",
            kind_: "policy",
            reference_: ".ai/manifests/mcp-context-manifest.json");

        Dictionary<string, RepositoryEvidence> lookup = new()
        {
            ["repository-policy"] = policyEvd
        };

        SpecVerificationEvidenceBinding binding = new()
        {
            Evidence = new VerificationEvidence
            {
                Id = new StableEntityId("EVD-001"),
                Description = "Evidence for AC-001",
                AcceptanceCriterionIds = [new StableEntityId("AC-001")],
                PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
            },
            RepositoryEvidenceId = "repository-policy"
        };

        SpecVerificationEvidenceBinder binder = new();
        IReadOnlyList<SpecVerificationEvidenceObservation> observations =
            binder.Bind([binding], lookup, dir.Path, out IReadOnlyList<string> errors);

        Assert.Empty(errors);
        SpecVerificationEvidenceObservation obs = Assert.Single(observations);
        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, obs.Disposition);
        Assert.Contains("No deterministic adapter", obs.Reason);
    }

    // ── Binder metadata and freshness projection ─────────────────────────────

    [Fact]
    public void Binder_PreservesMetadataAndFreshness()
    {
        using TestTempDir dir = new();
        string reportRel = ".ai/generated/reports/latest-build-summary.json";
        dir.WriteFile(reportRel, """{"restoreExitCode": 0, "buildExitCode": 0}""");

        RepositoryEvidence buildEvd = CreateRepoEvidence(
            evidenceId_: "build-summary",
            source_: "build-summary",
            kind_: "report",
            reference_: reportRel,
            availability_: RepositoryEvidenceAvailability.Available,
            freshness_: RepositoryEvidenceFreshness.Unknown,
            generatedAt_: "2026-09-12T08:00:00Z");

        Dictionary<string, RepositoryEvidence> lookup = new()
        {
            ["build-summary"] = buildEvd
        };

        SpecVerificationEvidenceBinding binding = new()
        {
            Evidence = new VerificationEvidence
            {
                Id = new StableEntityId("EVD-001"),
                Description = "Evidence for AC-001",
                AcceptanceCriterionIds = [new StableEntityId("AC-001")],
                PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
            },
            RepositoryEvidenceId = "build-summary"
        };

        SpecVerificationEvidenceBinder binder = new();
        IReadOnlyList<SpecVerificationEvidenceObservation> observations =
            binder.Bind([binding], lookup, dir.Path, out IReadOnlyList<string> errors);

        Assert.Empty(errors);
        SpecVerificationEvidenceObservation obs = Assert.Single(observations);
        Assert.Equal("EVD-001", obs.EvidenceId.Value);
        Assert.Equal("build-summary", obs.RepositoryEvidenceId);
        Assert.Equal("build-summary", obs.Source);
        Assert.Equal("report", obs.Kind);
        Assert.Equal(reportRel, obs.Reference);
        Assert.Equal(RepositoryEvidenceAvailability.Available, obs.Availability);
        Assert.Equal(RepositoryEvidenceFreshness.Unknown, obs.Freshness);
        Assert.Equal("2026-09-12T08:00:00Z", obs.SourceGeneratedAt);
        Assert.Equal(SpecVerificationEvidenceDisposition.Supporting, obs.Disposition);
    }

    [Fact]
    public void Binder_DuplicateEvidenceId_RejectedWithErrors()
    {
        SpecVerificationEvidenceBinding binding1 = new()
        {
            Evidence = new VerificationEvidence
            {
                Id = new StableEntityId("EVD-001"),
                Description = "Evidence 1",
                AcceptanceCriterionIds = [new StableEntityId("AC-001")],
                PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
            },
            RepositoryEvidenceId = "build-summary"
        };
        SpecVerificationEvidenceBinding binding2 = new()
        {
            Evidence = new VerificationEvidence
            {
                Id = new StableEntityId("EVD-001"),
                Description = "Evidence 2",
                AcceptanceCriterionIds = [new StableEntityId("AC-001")],
                PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
            },
            RepositoryEvidenceId = "build-summary"
        };

        SpecVerificationEvidenceBinder binder = new();
        IReadOnlyList<SpecVerificationEvidenceObservation> observations =
            binder.Bind([binding1, binding2], new Dictionary<string, RepositoryEvidence>(), "C:\\repo", out IReadOnlyList<string> errors);

        Assert.Empty(observations);
        Assert.Single(errors);
        Assert.Contains("Duplicate verification evidence ID 'EVD-001'", errors[0]);
    }

    private sealed class TestTempDir : IDisposable
    {
        public string Path { get; }

        public TestTempDir()
        {
            this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spec-verify-adapter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Path);
        }

        public void WriteFile(string relativePath_, string content_)
        {
            string fullPath = System.IO.Path.Combine(this.Path, relativePath_.Replace('/', System.IO.Path.DirectorySeparatorChar));
            string? dir = System.IO.Path.GetDirectoryName(fullPath);
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
                if (Directory.Exists(this.Path))
                {
                    Directory.Delete(this.Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

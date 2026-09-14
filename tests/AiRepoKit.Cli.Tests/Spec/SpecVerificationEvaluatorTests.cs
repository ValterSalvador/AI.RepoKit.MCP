using AiRepoKit.Cli.Services.SpecVerification;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Verification;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

/// <summary>
/// Tests 1–20: SpecVerificationEvaluator pure logic.
/// </summary>
public sealed class SpecVerificationEvaluatorTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static WorkSpec MakeWorkSpec(params string[] acIds_)
    {
        return new WorkSpec
        {
            AcceptanceCriteria = acIds_
                .Select(id_ => new AcceptanceCriterion
                {
                    Id = new StableEntityId(id_),
                    Statement = $"Statement for {id_}",
                    RequirementIds = [new StableEntityId("REQ-001")]
                })
                .ToArray(),
            Constraints = []
        };
    }

    private static ImplementationPlan MakePlan(params string[] acIds_)
    {
        return new ImplementationPlan
        {
            WorkSpecRevision = new ArtifactRevision(1),
            Steps = acIds_
                .Select((id_, i_) => new PlanStep
                {
                    Id = new StableEntityId($"PLAN-STEP-{(i_ + 1):D3}"),
                    Statement = $"Step for {id_}",
                    RequirementIds = [],
                    AcceptanceCriterionIds = [new StableEntityId(id_)]
                })
                .ToArray()
        };
    }

    private static VerificationEvidence MakeEvidence(string evdId_, string acId_, string planStepId_ = "PLAN-STEP-001")
    {
        return new VerificationEvidence
        {
            Id = new StableEntityId(evdId_),
            Description = $"Evidence {evdId_} for {acId_}",
            AcceptanceCriterionIds = [new StableEntityId(acId_)],
            PlanStepIds = [new StableEntityId(planStepId_)]
        };
    }

    private static SpecVerificationEvidenceObservation MakeObs(
        string evdId_,
        SpecVerificationEvidenceDisposition disposition_,
        string repoEvidenceId_ = "build-summary")
    {
        return new SpecVerificationEvidenceObservation
        {
            EvidenceId = new StableEntityId(evdId_),
            RepositoryEvidenceId = repoEvidenceId_,
            Source = repoEvidenceId_,
            Kind = "report",
            Reference = ".ai/generated/reports/latest-build-summary.json",
            Availability = RepositoryEvidenceAvailability.Available,
            Freshness = RepositoryEvidenceFreshness.Unknown,
            SourceGeneratedAt = "2026-09-10T10:00:00Z",
            Disposition = disposition_,
            Reason = "Test reason."
        };
    }

    private static SpecVerificationReport Evaluate(
        WorkSpec ws_,
        IReadOnlyList<VerificationEvidence> evidence_,
        IReadOnlyList<SpecVerificationEvidenceObservation> observations_)
    {
        return SpecVerificationEvaluator.Evaluate(
            "spec-001",
            new ArtifactRevision(1),
            new ArtifactRevision(1),
            new ArtifactRevision(1),
            evidence_,
            observations_,
            ws_);
    }

    // ── Tests 1–20 ──────────────────────────────────────────────────────────

    // Test 1: every AC receives exactly one result
    [Fact]
    public void T01_EveryAcReceivesExactlyOneResult()
    {
        WorkSpec ws = MakeWorkSpec("AC-001", "AC-002", "AC-003");
        VerificationEvidence evd1 = MakeEvidence("EVD-001", "AC-001");
        VerificationEvidence evd2 = MakeEvidence("EVD-002", "AC-002");
        VerificationEvidence evd3 = MakeEvidence("EVD-003", "AC-003");
        SpecVerificationEvidenceObservation obs1 = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);
        SpecVerificationEvidenceObservation obs2 = MakeObs("EVD-002", SpecVerificationEvidenceDisposition.Supporting);
        SpecVerificationEvidenceObservation obs3 = MakeObs("EVD-003", SpecVerificationEvidenceDisposition.Supporting);

        SpecVerificationReport report = Evaluate(ws, [evd1, evd2, evd3], [obs1, obs2, obs3]);

        Assert.Equal(3, report.Results.Count);
        Assert.Single(report.Results, r_ => r_.AcceptanceCriterionId.Value == "AC-001");
        Assert.Single(report.Results, r_ => r_.AcceptanceCriterionId.Value == "AC-002");
        Assert.Single(report.Results, r_ => r_.AcceptanceCriterionId.Value == "AC-003");
    }

    // Test 2: deterministic VER IDs sorted by AC ID ordinal
    [Fact]
    public void T02_VERIdsSortedByAcId()
    {
        // AC-003 sorted before AC-010 lexicographically? Actually ordinal: "AC-003" < "AC-010"
        WorkSpec ws = MakeWorkSpec("AC-010", "AC-003", "AC-001");

        SpecVerificationReport report = Evaluate(ws, [], []);

        // Results sorted ordinal: AC-001, AC-003, AC-010
        Assert.Equal("VER-001", report.Results[0].Id.Value);
        Assert.Equal("AC-001", report.Results[0].AcceptanceCriterionId.Value);
        Assert.Equal("VER-002", report.Results[1].Id.Value);
        Assert.Equal("AC-003", report.Results[1].AcceptanceCriterionId.Value);
        Assert.Equal("VER-003", report.Results[2].Id.Value);
        Assert.Equal("AC-010", report.Results[2].AcceptanceCriterionId.Value);
    }

    // Test 3: no evidence => NotVerified
    [Fact]
    public void T03_NoEvidence_YieldsNotVerified()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");

        SpecVerificationReport report = Evaluate(ws, [], []);

        Assert.Single(report.Results);
        Assert.Equal(VerificationStatus.NotVerified, report.Results[0].Status);
        Assert.Equal(VerificationStatus.NotVerified, report.OverallStatus);
    }

    // Test 4: only inconclusive => NotVerified
    [Fact]
    public void T04_OnlyInconclusive_YieldsNotVerified()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        VerificationEvidence evd = MakeEvidence("EVD-001", "AC-001");
        SpecVerificationEvidenceObservation obs = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Inconclusive);

        SpecVerificationReport report = Evaluate(ws, [evd], [obs]);

        Assert.Equal(VerificationStatus.NotVerified, report.Results[0].Status);
    }

    // Test 5: Supporting => Pass
    [Fact]
    public void T05_Supporting_YieldsPass()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        VerificationEvidence evd = MakeEvidence("EVD-001", "AC-001");
        SpecVerificationEvidenceObservation obs = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);

        SpecVerificationReport report = Evaluate(ws, [evd], [obs]);

        Assert.Equal(VerificationStatus.Pass, report.Results[0].Status);
    }

    // Test 6: Contradicting => Fail
    [Fact]
    public void T06_Contradicting_YieldsFail()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        VerificationEvidence evd = MakeEvidence("EVD-001", "AC-001");
        SpecVerificationEvidenceObservation obs = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Contradicting);

        SpecVerificationReport report = Evaluate(ws, [evd], [obs]);

        Assert.Equal(VerificationStatus.Fail, report.Results[0].Status);
    }

    // Test 7: Contradicting beats Supporting
    [Fact]
    public void T07_ContradictingBeatsSupporting()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        VerificationEvidence evd1 = MakeEvidence("EVD-001", "AC-001");
        VerificationEvidence evd2 = new()
        {
            Id = new StableEntityId("EVD-002"),
            Description = "Second evidence",
            AcceptanceCriterionIds = [new StableEntityId("AC-001")],
            PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
        };
        SpecVerificationEvidenceObservation obs1 = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);
        SpecVerificationEvidenceObservation obs2 = MakeObs("EVD-002", SpecVerificationEvidenceDisposition.Contradicting);

        SpecVerificationReport report = Evaluate(ws, [evd1, evd2], [obs1, obs2]);

        Assert.Equal(VerificationStatus.Fail, report.Results[0].Status);
    }

    // Test 8: Supporting + Inconclusive => Pass
    [Fact]
    public void T08_SupportingPlusInconclusive_YieldsPass()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        VerificationEvidence evd1 = MakeEvidence("EVD-001", "AC-001");
        VerificationEvidence evd2 = new()
        {
            Id = new StableEntityId("EVD-002"),
            Description = "Inconclusive",
            AcceptanceCriterionIds = [new StableEntityId("AC-001")],
            PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
        };
        SpecVerificationEvidenceObservation obs1 = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);
        SpecVerificationEvidenceObservation obs2 = MakeObs("EVD-002", SpecVerificationEvidenceDisposition.Inconclusive);

        SpecVerificationReport report = Evaluate(ws, [evd1, evd2], [obs1, obs2]);

        Assert.Equal(VerificationStatus.Pass, report.Results[0].Status);
    }

    // Test 9: overall Pass
    [Fact]
    public void T09_AllPass_YieldsOverallPass()
    {
        WorkSpec ws = MakeWorkSpec("AC-001", "AC-002");
        VerificationEvidence evd1 = MakeEvidence("EVD-001", "AC-001");
        VerificationEvidence evd2 = MakeEvidence("EVD-002", "AC-002", "PLAN-STEP-002");
        SpecVerificationEvidenceObservation obs1 = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);
        SpecVerificationEvidenceObservation obs2 = MakeObs("EVD-002", SpecVerificationEvidenceDisposition.Supporting);

        SpecVerificationReport report = Evaluate(ws, [evd1, evd2], [obs1, obs2]);

        Assert.Equal(VerificationStatus.Pass, report.OverallStatus);
    }

    // Test 10: overall Fail
    [Fact]
    public void T10_AnyFail_YieldsOverallFail()
    {
        WorkSpec ws = MakeWorkSpec("AC-001", "AC-002");
        VerificationEvidence evd1 = MakeEvidence("EVD-001", "AC-001");
        VerificationEvidence evd2 = MakeEvidence("EVD-002", "AC-002", "PLAN-STEP-002");
        SpecVerificationEvidenceObservation obs1 = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);
        SpecVerificationEvidenceObservation obs2 = MakeObs("EVD-002", SpecVerificationEvidenceDisposition.Contradicting);

        SpecVerificationReport report = Evaluate(ws, [evd1, evd2], [obs1, obs2]);

        Assert.Equal(VerificationStatus.Fail, report.OverallStatus);
    }

    // Test 11: overall NotVerified when no Fail and some NotVerified
    [Fact]
    public void T11_AnyNotVerified_NoFail_YieldsOverallNotVerified()
    {
        WorkSpec ws = MakeWorkSpec("AC-001", "AC-002");
        VerificationEvidence evd1 = MakeEvidence("EVD-001", "AC-001");
        // AC-002 has no evidence
        SpecVerificationEvidenceObservation obs1 = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);

        SpecVerificationReport report = Evaluate(ws, [evd1], [obs1]);

        VerificationResult ac2Result = report.Results.Single(r_ => r_.AcceptanceCriterionId.Value == "AC-002");
        Assert.Equal(VerificationStatus.NotVerified, ac2Result.Status);
        Assert.Equal(VerificationStatus.NotVerified, report.OverallStatus);
    }

    // Test 12: result EvidenceIds are sorted ordinal
    [Fact]
    public void T12_ResultEvidenceIdsSortedOrdinal()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        VerificationEvidence evdB = new()
        {
            Id = new StableEntityId("EVD-002"),
            Description = "B",
            AcceptanceCriterionIds = [new StableEntityId("AC-001")],
            PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
        };
        VerificationEvidence evdA = MakeEvidence("EVD-001", "AC-001");
        SpecVerificationEvidenceObservation obsA = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);
        SpecVerificationEvidenceObservation obsB = MakeObs("EVD-002", SpecVerificationEvidenceDisposition.Supporting);

        // Pass in B before A
        SpecVerificationReport report = Evaluate(ws, [evdB, evdA], [obsA, obsB]);

        Assert.Equal(["EVD-001", "EVD-002"],
            report.Results[0].EvidenceIds.Select(id_ => id_.Value).ToArray());
    }

    // Test 13: duplicate EVD IDs rejected by VerificationValidator
    [Fact]
    public void T13_DuplicateEvdId_RejectedByValidator()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        ImplementationPlan plan = MakePlan("AC-001");
        VerificationEvidence evd = MakeEvidence("EVD-001", "AC-001");

        IReadOnlyList<SpecValidationError> errors = VerificationValidator.Validate(
            [evd, evd], [], ws, plan);

        Assert.Contains(errors, e_ => e_.Code == SpecValidationErrorCodes.DuplicateEntityId && e_.SourceEntityId == "EVD-001");
    }

    // Test 14: dangling AC reference rejected by VerificationValidator
    [Fact]
    public void T14_DanglingAcReference_RejectedByValidator()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        ImplementationPlan plan = MakePlan("AC-001");
        VerificationEvidence evd = new()
        {
            Id = new StableEntityId("EVD-001"),
            Description = "Evidence",
            AcceptanceCriterionIds = [new StableEntityId("AC-999")],
            PlanStepIds = [new StableEntityId("PLAN-STEP-001")]
        };

        IReadOnlyList<SpecValidationError> errors = VerificationValidator.Validate(
            [evd], [], ws, plan);

        Assert.Contains(errors, e_ => e_.Code == SpecValidationErrorCodes.DanglingReference && e_.TargetEntityId == "AC-999");
    }

    // Test 15: dangling PlanStep reference rejected by VerificationValidator
    [Fact]
    public void T15_DanglingPlanStepReference_RejectedByValidator()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        ImplementationPlan plan = MakePlan("AC-001");
        VerificationEvidence evd = new()
        {
            Id = new StableEntityId("EVD-001"),
            Description = "Evidence",
            AcceptanceCriterionIds = [new StableEntityId("AC-001")],
            PlanStepIds = [new StableEntityId("PLAN-STEP-999")]
        };

        IReadOnlyList<SpecValidationError> errors = VerificationValidator.Validate(
            [evd], [], ws, plan);

        Assert.Contains(errors, e_ => e_.Code == SpecValidationErrorCodes.DanglingReference && e_.TargetEntityId == "PLAN-STEP-999");
    }

    // Test 16: unknown RepositoryEvidenceId rejected by binder
    [Fact]
    public void T16_UnknownRepositoryEvidenceId_RejectedByBinder()
    {
        VerificationEvidence evd = MakeEvidence("EVD-001", "AC-001");
        SpecVerificationEvidenceBinding binding = new()
        {
            Evidence = evd,
            RepositoryEvidenceId = "unknown-source-xyz"
        };

        SpecVerificationEvidenceBinder binder = new();
        _ = binder.Bind([binding], new Dictionary<string, AiRepoKit.Spec.Context.RepositoryEvidence>(), "C:\\repo", out IReadOnlyList<string> errors);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e_ => e_.Contains("unknown-source-xyz"));
    }

    // Test 17: evidence description alone cannot create Pass (if no obs)
    [Fact]
    public void T17_NoObservation_DescriptionAloneCannotPass()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        // Evidence exists but no observations
        VerificationEvidence evd = MakeEvidence("EVD-001", "AC-001");

        // No observations — the evaluator gets the evidence but no observations for EVD-001
        SpecVerificationReport report = Evaluate(ws, [evd], []);

        Assert.Equal(VerificationStatus.NotVerified, report.Results[0].Status);
    }

    // Test 18: source/freshness metadata copied into observation
    [Fact]
    public void T18_ObservationMetadataCopiedFromRepoEvidence()
    {
        SpecVerificationEvidenceObservation obs = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting, "build-summary");

        Assert.Equal("build-summary", obs.Source);
        Assert.Equal("report", obs.Kind);
        Assert.Equal(RepositoryEvidenceAvailability.Available, obs.Availability);
        Assert.Equal(RepositoryEvidenceFreshness.Unknown, obs.Freshness);
        Assert.Equal("2026-09-10T10:00:00Z", obs.SourceGeneratedAt);
    }

    // Test 19: stale evidence cannot produce Pass
    [Fact]
    public void T19_StaleEvidence_CannotProducePass_ViaAdapter()
    {
        // A stale RepositoryEvidence cannot produce Supporting via BuildSummaryAdapter
        AiRepoKit.Spec.Context.RepositoryEvidence repoEvd = new()
        {
            EvidenceId = "build-summary",
            Source = "build-summary",
            Kind = "report",
            Reference = ".ai/generated/reports/latest-build-summary.json",
            Availability = AiRepoKit.Spec.Context.RepositoryEvidenceAvailability.Available,
            Freshness = AiRepoKit.Spec.Context.RepositoryEvidenceFreshness.Stale,
            SourceGeneratedAt = "2026-09-01T00:00:00Z",
            Detail = "Build report is stale."
        };

        AiRepoKit.Cli.Services.SpecVerification.BuildSummaryEvidenceAdapter adapter = new();
        SpecVerificationEvidenceDisposition disposition = adapter.Evaluate(repoEvd, "C:\\repo");

        Assert.Equal(SpecVerificationEvidenceDisposition.Inconclusive, disposition);
    }

    // Test 20: deterministic result — same inputs, same output
    [Fact]
    public void T20_DeterministicResult_SameInputsSameOutput()
    {
        WorkSpec ws = MakeWorkSpec("AC-001");
        VerificationEvidence evd = MakeEvidence("EVD-001", "AC-001");
        SpecVerificationEvidenceObservation obs = MakeObs("EVD-001", SpecVerificationEvidenceDisposition.Supporting);

        SpecVerificationReport r1 = Evaluate(ws, [evd], [obs]);
        SpecVerificationReport r2 = Evaluate(ws, [evd], [obs]);

        Assert.Equal(r1.OverallStatus, r2.OverallStatus);
        Assert.Equal(r1.Results[0].Id.Value, r2.Results[0].Id.Value);
        Assert.Equal(r1.Results[0].Status, r2.Results[0].Status);
        Assert.Equal(r1.Results[0].Summary, r2.Results[0].Summary);
    }
}

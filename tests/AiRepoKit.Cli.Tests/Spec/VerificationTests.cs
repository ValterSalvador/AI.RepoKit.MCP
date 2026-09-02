using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class VerificationTests
{
    [Fact]
    public void VerificationStatus_NotVerifiedIsDistinctFromPass()
    {
        Assert.NotEqual(
            VerificationStatus.NotVerified,
            VerificationStatus.Pass);

        Assert.NotEqual(
            VerificationStatus.NotVerified,
            VerificationStatus.Fail);
    }

    [Fact]
    public void Validator_AcceptsCompleteTraceability()
    {
        IReadOnlyList<SpecValidationError> errors =
            VerificationValidator.Validate(
                CreateEvidence(),
                CreateResults(),
                CreateWorkSpec(),
                CreatePlan());

        Assert.Empty(
            errors);
    }

    [Fact]
    public void Evidence_TracesAcceptanceCriterionAndPlanStep()
    {
        VerificationEvidence evidence =
            Assert.Single(
                CreateEvidence());

        Assert.Equal(
            "AC-001",
            Assert.Single(
                evidence.AcceptanceCriterionIds).Value);

        Assert.Equal(
            "PLAN-STEP-001",
            Assert.Single(
                evidence.PlanStepIds).Value);
    }

    [Fact]
    public void Result_TracesAcceptanceCriterionAndEvidence()
    {
        VerificationResult result =
            Assert.Single(
                CreateResults());

        Assert.Equal(
            "AC-001",
            result.AcceptanceCriterionId.Value);

        Assert.Equal(
            "EVD-001",
            Assert.Single(
                result.EvidenceIds).Value);

        Assert.Equal(
            VerificationStatus.Pass,
            result.Status);
    }

    [Fact]
    public void Validator_RejectsInvalidEvidenceReferences()
    {
        VerificationEvidence evidence =
            new()
            {
                Id =
                    new StableEntityId(
                        "EVD-001"),
                Description =
                    "Invalid evidence",
                AcceptanceCriterionIds =
                [
                    new StableEntityId(
                        "AC-999"),
                    new StableEntityId(
                        "AC-999"),
                    new StableEntityId(
                        "REQ-001")
                ],
                PlanStepIds =
                [
                    new StableEntityId(
                        "PLAN-STEP-999"),
                    new StableEntityId(
                        "PLAN-STEP-999"),
                    new StableEntityId(
                        "AC-001")
                ]
            };

        string[] actual =
            Describe(
                VerificationValidator.Validate(
                    [evidence],
                    [],
                    CreateWorkSpec(),
                    CreatePlan()));

        Assert.Equal(
            [
                "DanglingReference|EVD-001|AC-999",
                "DanglingReference|EVD-001|PLAN-STEP-999",
                "DuplicateReference|EVD-001|AC-999",
                "DuplicateReference|EVD-001|PLAN-STEP-999",
                "InvalidReferenceTargetKind|EVD-001|AC-001",
                "InvalidReferenceTargetKind|EVD-001|REQ-001"
            ],
            actual);
    }

    [Fact]
    public void Validator_RejectsInvalidResultReferences()
    {
        VerificationResult result =
            new()
            {
                Id =
                    new StableEntityId(
                        "VER-001"),
                AcceptanceCriterionId =
                    new StableEntityId(
                        "AC-999"),
                Status =
                    VerificationStatus.NotVerified,
                EvidenceIds =
                [
                    new StableEntityId(
                        "EVD-999"),
                    new StableEntityId(
                        "EVD-999"),
                    new StableEntityId(
                        "AC-001")
                ],
                Summary =
                    "Not verified"
            };

        string[] actual =
            Describe(
                VerificationValidator.Validate(
                    CreateEvidence(),
                    [result],
                    CreateWorkSpec(),
                    CreatePlan()));

        Assert.Equal(
            [
                "DanglingReference|VER-001|AC-999",
                "DanglingReference|VER-001|EVD-999",
                "DuplicateReference|VER-001|EVD-999",
                "InvalidReferenceTargetKind|VER-001|AC-001"
            ],
            actual);
    }

    [Fact]
    public void Validator_RejectsDuplicateEvidenceAndResultIds()
    {
        VerificationEvidence evidence =
            Assert.Single(
                CreateEvidence());

        VerificationResult result =
            Assert.Single(
                CreateResults());

        string[] actual =
            Describe(
                VerificationValidator.Validate(
                    [evidence, evidence],
                    [result, result],
                    CreateWorkSpec(),
                    CreatePlan()));

        Assert.Equal(
            [
                "DuplicateEntityId|EVD-001|",
                "DuplicateEntityId|VER-001|"
            ],
            actual);
    }

    [Fact]
    public void Validator_RejectsInvalidVerificationStatus()
    {
        VerificationResult result =
            Assert.Single(
                CreateResults()) with
            {
                Status =
                    (VerificationStatus)99
            };

        SpecValidationError error =
            Assert.Single(
                VerificationValidator.Validate(
                    CreateEvidence(),
                    [result],
                    CreateWorkSpec(),
                    CreatePlan()));

        Assert.Equal(
            SpecValidationErrorCodes.InvalidVerificationStatus,
            error.Code);
    }

    [Fact]
    public void Approval_ArtifactIdentityMustMatchArtifactKind()
    {
        Approval approval =
            new()
            {
                Id =
                    new StableEntityId(
                        "APR-100"),
                ArtifactKind =
                    SpecArtifactKind.WorkSpec,
                ArtifactIdentity =
                    SpecArtifactIdentity.RequirementSet,
                ArtifactRevision =
                    new ArtifactRevision(
                        1),
                CanonicalSemanticRepresentation =
                    "semantic",
                SemanticDigest =
                    new string(
                        'a',
                        64)
            };

        SpecValidationError error =
            Assert.Single(
                ApprovalValidator.Validate(
                    approval));

        Assert.Equal(
            SpecValidationErrorCodes.ArtifactIdentityMismatch,
            error.Code);
    }

    private static IReadOnlyList<VerificationEvidence> CreateEvidence()
    {
        return
        [
            new VerificationEvidence
            {
                Id =
                    new StableEntityId(
                        "EVD-001"),
                Description =
                    "Focused test evidence",
                AcceptanceCriterionIds =
                [
                    new StableEntityId(
                        "AC-001")
                ],
                PlanStepIds =
                [
                    new StableEntityId(
                        "PLAN-STEP-001")
                ]
            }
        ];
    }

    private static IReadOnlyList<VerificationResult> CreateResults()
    {
        return
        [
            new VerificationResult
            {
                Id =
                    new StableEntityId(
                        "VER-001"),
                AcceptanceCriterionId =
                    new StableEntityId(
                        "AC-001"),
                Status =
                    VerificationStatus.Pass,
                EvidenceIds =
                [
                    new StableEntityId(
                        "EVD-001")
                ],
                Summary =
                    "Acceptance criterion passed."
            }
        ];
    }

    private static WorkSpec CreateWorkSpec()
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(
                    2),
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id =
                        new StableEntityId(
                            "AC-001"),
                    Statement =
                        "Criterion",
                    RequirementIds =
                    []
                }
            ],
            Constraints =
            []
        };
    }

    private static ImplementationPlan CreatePlan()
    {
        return new ImplementationPlan
        {
            WorkSpecRevision =
                new ArtifactRevision(
                    2),
            Steps =
            [
                new PlanStep
                {
                    Id =
                        new StableEntityId(
                            "PLAN-STEP-001"),
                    Statement =
                        "Logical implementation step",
                    RequirementIds =
                    [],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId(
                            "AC-001")
                    ]
                }
            ]
        };
    }

    private static string[] Describe(
        IReadOnlyList<SpecValidationError> errors_)
    {
        return errors_
            .Select(
                error_ =>
                    $"{error_.Code}|{error_.SourceEntityId}|{error_.TargetEntityId}")
            .ToArray();
    }
}

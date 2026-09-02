using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class ApprovalTests
{
    [Fact]
    public void WorkSpecValidator_AcceptsMatchingRequirementSetRevision()
    {
        RequirementSet requirementSet =
            CreateRequirementSet(
                3);

        WorkSpec workSpec =
            CreateWorkSpec(
                3);

        Assert.Empty(
            WorkSpecValidator.Validate(
                workSpec,
                requirementSet));
    }

    [Fact]
    public void WorkSpecValidator_RejectsRequirementSetRevisionMismatch()
    {
        RequirementSet requirementSet =
            CreateRequirementSet(
                3);

        WorkSpec workSpec =
            CreateWorkSpec(
                2);

        SpecValidationError error =
            Assert.Single(
                WorkSpecValidator.Validate(
                    workSpec,
                    requirementSet));

        Assert.Equal(
            SpecValidationErrorCodes.RevisionMismatch,
            error.Code);
    }

    [Fact]
    public void Approval_BindsArtifactRevisionCanonicalRepresentationAndDigest()
    {
        Approval approval =
            CreateValidApproval();

        Assert.Equal(
            SpecArtifactKind.WorkSpec,
            approval.ArtifactKind);

        Assert.Equal(
            new ArtifactRevision(
                5),
            approval.ArtifactRevision);

        Assert.Equal(
            SpecSchema.CanonicalizationId,
            approval.CanonicalizationId);

        Assert.Equal(
            SpecSchema.CanonicalizationVersion,
            approval.CanonicalizationVersion);

        Assert.Equal(
            SpecSchema.DigestAlgorithm,
            approval.DigestAlgorithm);

        Assert.Equal(
            "work-spec-semantic-v1",
            approval.CanonicalSemanticRepresentation);

        Assert.Equal(
            new string(
                'a',
                64),
            approval.SemanticDigest);
    }

    [Fact]
    public void ApprovalValidator_AcceptsValidBinding()
    {
        Assert.Empty(
            ApprovalValidator.Validate(
                CreateValidApproval()));
    }

    [Fact]
    public void ApprovalValidator_RejectsWrongIdentityKind()
    {
        Approval approval =
            CreateValidApproval() with
            {
                Id =
                    new StableEntityId(
                        "REQ-001")
            };

        SpecValidationError error =
            Assert.Single(
                ApprovalValidator.Validate(
                    approval));

        Assert.Equal(
            SpecValidationErrorCodes.InvalidEntityKind,
            error.Code);
    }

    [Fact]
    public void ApprovalValidator_RejectsInvalidRevision()
    {
        Approval approval =
            CreateValidApproval() with
            {
                ArtifactRevision =
                    default
            };

        SpecValidationError error =
            Assert.Single(
                ApprovalValidator.Validate(
                    approval));

        Assert.Equal(
            SpecValidationErrorCodes.InvalidRevision,
            error.Code);
    }

    [Fact]
    public void ApprovalValidator_RejectsUnsupportedCanonicalization()
    {
        Approval approval =
            CreateValidApproval() with
            {
                CanonicalizationId =
                    "ai.repokit.spec.semantic.other",
                CanonicalizationVersion =
                    2
            };

        string[] codes =
            ApprovalValidator
                .Validate(
                    approval)
                .Select(
                    error_ =>
                        error_.Code)
                .ToArray();

        Assert.Equal(
            [
                SpecValidationErrorCodes.UnsupportedCanonicalizationId,
                SpecValidationErrorCodes.UnsupportedCanonicalizationVersion
            ],
            codes);
    }

    [Fact]
    public void ApprovalValidator_RejectsMissingCanonicalRepresentation()
    {
        Approval approval =
            CreateValidApproval() with
            {
                CanonicalSemanticRepresentation =
                    string.Empty
            };

        SpecValidationError error =
            Assert.Single(
                ApprovalValidator.Validate(
                    approval));

        Assert.Equal(
            SpecValidationErrorCodes.MissingCanonicalRepresentation,
            error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void ApprovalValidator_RejectsInvalidDigest(
        string digest_)
    {
        Approval approval =
            CreateValidApproval() with
            {
                SemanticDigest =
                    digest_
            };

        SpecValidationError error =
            Assert.Single(
                ApprovalValidator.Validate(
                    approval));

        Assert.Equal(
            SpecValidationErrorCodes.InvalidSemanticDigest,
            error.Code);
    }

    [Fact]
    public void ApprovalValidator_RejectsUnsupportedDigestAlgorithm()
    {
        Approval approval =
            CreateValidApproval() with
            {
                DigestAlgorithm =
                    "sha512"
            };

        SpecValidationError error =
            Assert.Single(
                ApprovalValidator.Validate(
                    approval));

        Assert.Equal(
            SpecValidationErrorCodes.UnsupportedDigestAlgorithm,
            error.Code);
    }

    private static Approval CreateValidApproval()
    {
        return new Approval
        {
            Id =
                new StableEntityId(
                    "APR-001"),
            ArtifactKind =
                SpecArtifactKind.WorkSpec,
            ArtifactIdentity =
                SpecArtifactIdentity.WorkSpec,
            ArtifactRevision =
                new ArtifactRevision(
                    5),
            CanonicalSemanticRepresentation =
                "work-spec-semantic-v1",
            SemanticDigest =
                new string(
                    'a',
                    64)
        };
    }

    private static RequirementSet CreateRequirementSet(
        int revision_)
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(
                    revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id =
                        new StableEntityId(
                            "INPUT-001"),
                    Text =
                        "Input"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id =
                        new StableEntityId(
                            "REQ-001"),
                    Statement =
                        "Requirement",
                    SourceInputIds =
                    [
                        new StableEntityId(
                            "INPUT-001")
                    ]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec(
        int requirementSetRevision_)
    {
        return new WorkSpec
        {
            RequirementSetRevision =
                new ArtifactRevision(
                    requirementSetRevision_),
            Constraints =
            [],
            AcceptanceCriteria =
            []
        };
    }
}

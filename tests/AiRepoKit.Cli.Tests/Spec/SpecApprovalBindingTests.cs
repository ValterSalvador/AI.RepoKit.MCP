using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecApprovalBindingTests
{
    [Fact]
    public void JsonWhitespaceAndPropertyOrderDoNotChangeSemanticDigest()
    {
        string compact =
            """
            {"artifactIdentity":"requirements","inputs":[{"id":"INPUT-001","text":"Input"}],"requirements":[{"id":"REQ-001","sourceInputIds":["INPUT-001"],"statement":"Requirement"}],"revision":1,"schemaId":"ai.repokit.spec","schemaVersion":1}
            """;

        string reformatted =
            """
            {
              "schemaVersion": 1,
              "requirements": [
                {
                  "statement": "Requirement",
                  "sourceInputIds": [
                    "INPUT-001"
                  ],
                  "id": "REQ-001"
                }
              ],
              "schemaId": "ai.repokit.spec",
              "revision": 1,
              "inputs": [
                {
                  "text": "Input",
                  "id": "INPUT-001"
                }
              ],
              "artifactIdentity": "requirements"
            }
            """;

        RequirementSet first =
            SpecJsonSerializer.Deserialize<RequirementSet>(
                compact);

        RequirementSet second =
            SpecJsonSerializer.Deserialize<RequirementSet>(
                reformatted);

        Assert.NotEqual(
            compact,
            reformatted);

        Assert.Equal(
            SpecSemanticCanonicalizer.Canonicalize(
                first),
            SpecSemanticCanonicalizer.Canonicalize(
                second));

        Assert.Equal(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                second));
    }

    [Fact]
    public void MarkdownProjectionFormattingDoesNotChangeSemanticDigest()
    {
        string firstMarkdown =
            "# Requirement\n\n- REQ-001: Requirement";

        string secondMarkdown =
            "# Requirement\n\n\n* **REQ-001** — Requirement\n";

        RequirementSet requirementSet =
            CreateRequirementSet();

        string firstDigest =
            SpecSemanticDigest.Compute(
                requirementSet);

        string secondDigest =
            SpecSemanticDigest.Compute(
                requirementSet);

        Assert.NotEqual(
            firstMarkdown,
            secondMarkdown);

        Assert.Equal(
            firstDigest,
            secondDigest);
    }

    [Fact]
    public void ApprovalFactory_BindsExactRequirementSetContract()
    {
        RequirementSet requirementSet =
            CreateRequirementSet() with
            {
                Revision =
                    new ArtifactRevision(
                        7)
            };

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-001"),
                requirementSet);

        Assert.Equal(
            SpecArtifactKind.RequirementSet,
            approval.ArtifactKind);

        Assert.Equal(
            SpecArtifactIdentity.RequirementSet,
            approval.ArtifactIdentity);

        Assert.Equal(
            new ArtifactRevision(
                7),
            approval.ArtifactRevision);

        Assert.Equal(
            SpecSemanticCanonicalizer.Canonicalize(
                requirementSet),
            approval.CanonicalSemanticRepresentation);

        Assert.Equal(
            SpecSemanticDigest.Compute(
                requirementSet),
            approval.SemanticDigest);

        Assert.Empty(
            ApprovalBindingValidator.Validate(
                approval,
                requirementSet));
    }

    [Fact]
    public void ApprovalFactory_BindsExactWorkSpecContract()
    {
        WorkSpec workSpec =
            CreateWorkSpec();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-002"),
                workSpec);

        Assert.Equal(
            SpecArtifactKind.WorkSpec,
            approval.ArtifactKind);

        Assert.Equal(
            workSpec.Revision,
            approval.ArtifactRevision);

        Assert.Empty(
            ApprovalBindingValidator.Validate(
                approval,
                workSpec));
    }

    [Fact]
    public void ApprovalFactory_BindsExactImplementationPlanContract()
    {
        ImplementationPlan plan =
            CreatePlan();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-003"),
                plan);

        Assert.Equal(
            SpecArtifactKind.ImplementationPlan,
            approval.ArtifactKind);

        Assert.Equal(
            plan.Revision,
            approval.ArtifactRevision);

        Assert.Empty(
            ApprovalBindingValidator.Validate(
                approval,
                plan));
    }

    [Fact]
    public void SameSemanticsDifferentArtifactRevisionKeepsDigestButChangesApprovalRevision()
    {
        WorkSpec first =
            CreateWorkSpec() with
            {
                Revision =
                    new ArtifactRevision(
                        5)
            };

        WorkSpec second =
            first with
            {
                Revision =
                    new ArtifactRevision(
                        6)
            };

        Approval firstApproval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-010"),
                first);

        Approval secondApproval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-011"),
                second);

        Assert.Equal(
            firstApproval.CanonicalSemanticRepresentation,
            secondApproval.CanonicalSemanticRepresentation);

        Assert.Equal(
            firstApproval.SemanticDigest,
            secondApproval.SemanticDigest);

        Assert.NotEqual(
            firstApproval.ArtifactRevision,
            secondApproval.ArtifactRevision);
    }

    [Fact]
    public void ApprovalBindingValidator_RejectsRevisionMismatch()
    {
        WorkSpec workSpec =
            CreateWorkSpec();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-020"),
                workSpec) with
            {
                ArtifactRevision =
                    new ArtifactRevision(
                        99)
            };

        Assert.Contains(
            ApprovalBindingValidator.Validate(
                approval,
                workSpec),
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.RevisionMismatch);
    }

    [Fact]
    public void ApprovalBindingValidator_RejectsCanonicalRepresentationMismatch()
    {
        WorkSpec workSpec =
            CreateWorkSpec();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-021"),
                workSpec) with
            {
                CanonicalSemanticRepresentation =
                    "different-semantic-representation"
            };

        Assert.Contains(
            ApprovalBindingValidator.Validate(
                approval,
                workSpec),
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.CanonicalRepresentationMismatch);
    }

    [Fact]
    public void ApprovalBindingValidator_RejectsSemanticDigestMismatch()
    {
        WorkSpec workSpec =
            CreateWorkSpec();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId(
                    "APR-022"),
                workSpec) with
            {
                SemanticDigest =
                    new string(
                        'a',
                        64)
            };

        Assert.Contains(
            ApprovalBindingValidator.Validate(
                approval,
                workSpec),
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.SemanticDigestMismatch);
    }

    [Fact]
    public void CoreValidatorsRejectWrongArtifactIdentity()
    {
        RequirementSet requirementSet =
            CreateRequirementSet() with
            {
                ArtifactIdentity =
                    "wrong-requirements"
            };

        WorkSpec workSpec =
            CreateWorkSpec() with
            {
                ArtifactIdentity =
                    "wrong-work-spec"
            };

        ImplementationPlan plan =
            CreatePlan() with
            {
                ArtifactIdentity =
                    "wrong-plan"
            };

        Assert.Contains(
            RequirementSetValidator.Validate(
                requirementSet),
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.ArtifactIdentityMismatch);

        Assert.Contains(
            WorkSpecValidator.Validate(
                workSpec,
                CreateRequirementSet()),
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.ArtifactIdentityMismatch);

        Assert.Contains(
            ImplementationPlanValidator.Validate(
                plan,
                CreateWorkSpec(),
                CreateRequirementSet()),
            error_ =>
                error_.Code ==
                SpecValidationErrorCodes.ArtifactIdentityMismatch);
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(
                    3),
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

    private static WorkSpec CreateWorkSpec()
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(
                    5),
            RequirementSetRevision =
                new ArtifactRevision(
                    3),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId(
                            "CON-001"),
                    Statement =
                        "Constraint",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
                    ]
                }
            ],
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
                    [
                        new StableEntityId(
                            "REQ-001")
                    ]
                }
            ]
        };
    }

    private static ImplementationPlan CreatePlan()
    {
        return new ImplementationPlan
        {
            Revision =
                new ArtifactRevision(
                    2),
            WorkSpecRevision =
                new ArtifactRevision(
                    5),
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
                    [
                        new StableEntityId(
                            "REQ-001")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId(
                            "AC-001")
                    ]
                }
            ]
        };
    }
}

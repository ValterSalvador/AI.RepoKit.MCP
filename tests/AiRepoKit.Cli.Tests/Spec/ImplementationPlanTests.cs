using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class ImplementationPlanTests
{
    [Fact]
    public void ArtifactRevision_RejectsNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new ArtifactRevision(
                    0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new ArtifactRevision(
                    -1));
    }

    [Fact]
    public void ArtifactRevision_UsesValueEquality()
    {
        Assert.Equal(
            new ArtifactRevision(
                2),
            new ArtifactRevision(
                2));

        Assert.NotEqual(
            new ArtifactRevision(
                1),
            new ArtifactRevision(
                2));
    }

    [Fact]
    public void ImplementationPlan_PreservesLogicalStepOrder()
    {
        ImplementationPlan plan =
            CreatePlan();

        Assert.Equal(
            [
                "PLAN-STEP-001",
                "PLAN-STEP-002"
            ],
            plan.Steps
                .Select(
                    step_ =>
                        step_.Id.Value)
                .ToArray());
    }

    [Fact]
    public void Validator_AcceptsValidPlanTraceability()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();

        WorkSpec workSpec =
            CreateWorkSpec();

        IReadOnlyList<SpecValidationError> errors =
            ImplementationPlanValidator.Validate(
                CreatePlan(),
                workSpec,
                requirementSet);

        Assert.Empty(
            errors);
    }

    [Fact]
    public void Validator_RejectsWorkSpecRevisionMismatch()
    {
        ImplementationPlan plan =
            CreatePlan() with
            {
                WorkSpecRevision =
                    new ArtifactRevision(
                        2)
            };

        SpecValidationError error =
            Assert.Single(
                ImplementationPlanValidator.Validate(
                    plan,
                    CreateWorkSpec(),
                    CreateRequirementSet()));

        Assert.Equal(
            SpecValidationErrorCodes.RevisionMismatch,
            error.Code);
    }

    [Fact]
    public void Validator_RejectsInvalidPlanReferencesDeterministically()
    {
        ImplementationPlan plan =
            new()
            {
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
                            "Invalid references",
                        RequirementIds =
                        [
                            new StableEntityId(
                                "REQ-999"),
                            new StableEntityId(
                                "REQ-999"),
                            new StableEntityId(
                                "AC-001")
                        ],
                        AcceptanceCriterionIds =
                        [
                            new StableEntityId(
                                "AC-999"),
                            new StableEntityId(
                                "AC-999"),
                            new StableEntityId(
                                "REQ-001")
                        ]
                    },
                    new PlanStep
                    {
                        Id =
                            new StableEntityId(
                                "PLAN-STEP-001"),
                        Statement =
                            "Duplicate step",
                        RequirementIds =
                        [],
                        AcceptanceCriterionIds =
                        []
                    }
                ]
            };

        string[] actual =
            ImplementationPlanValidator
                .Validate(
                    plan,
                    CreateWorkSpec(),
                    CreateRequirementSet())
                .Select(
                    error_ =>
                        $"{error_.Code}|{error_.SourceEntityId}|{error_.TargetEntityId}")
                .ToArray();

        Assert.Equal(
            [
                "DanglingReference|PLAN-STEP-001|AC-999",
                "DanglingReference|PLAN-STEP-001|REQ-999",
                "DuplicateEntityId|PLAN-STEP-001|",
                "DuplicateReference|PLAN-STEP-001|AC-999",
                "DuplicateReference|PLAN-STEP-001|REQ-999",
                "InvalidReferenceTargetKind|PLAN-STEP-001|AC-001",
                "InvalidReferenceTargetKind|PLAN-STEP-001|REQ-001"
            ],
            actual);
    }

    [Fact]
    public void Validator_RejectsWrongStepEntityKind()
    {
        ImplementationPlan plan =
            new()
            {
                WorkSpecRevision =
                    new ArtifactRevision(
                        5),
                Steps =
                [
                    new PlanStep
                    {
                        Id =
                            new StableEntityId(
                                "REQ-010"),
                        Statement =
                            "Wrong kind",
                        RequirementIds =
                        [],
                        AcceptanceCriterionIds =
                        []
                    }
                ]
            };

        SpecValidationError error =
            Assert.Single(
                ImplementationPlanValidator.Validate(
                    plan,
                    CreateWorkSpec(),
                    CreateRequirementSet()));

        Assert.Equal(
            SpecValidationErrorCodes.InvalidEntityKind,
            error.Code);

        Assert.Equal(
            "REQ-010",
            error.SourceEntityId);
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
                        "Requirement one",
                    SourceInputIds =
                    [
                        new StableEntityId(
                            "INPUT-001")
                    ]
                },
                new Requirement
                {
                    Id =
                        new StableEntityId(
                            "REQ-002"),
                    Statement =
                        "Requirement two",
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
            [],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id =
                        new StableEntityId(
                            "AC-001"),
                    Statement =
                        "Criterion one",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
                    ]
                },
                new AcceptanceCriterion
                {
                    Id =
                        new StableEntityId(
                            "AC-002"),
                    Statement =
                        "Criterion two",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-002")
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
                        "Implement requirement one.",
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
                },
                new PlanStep
                {
                    Id =
                        new StableEntityId(
                            "PLAN-STEP-002"),
                    Statement =
                        "Implement requirement two.",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-002")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId(
                            "AC-002")
                    ]
                }
            ]
        };
    }
}

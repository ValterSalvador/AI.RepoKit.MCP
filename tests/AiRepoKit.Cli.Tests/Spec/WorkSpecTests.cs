using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class WorkSpecTests
{
    [Fact]
    public void WorkSpec_IsDistinctFromRequirementSet()
    {
        Assert.NotEqual(
            typeof(RequirementSet),
            typeof(WorkSpec));

        Assert.Equal(
            SpecArtifactKind.RequirementSet,
            SpecArtifactKind.RequirementSet);

        Assert.NotEqual(
            SpecArtifactKind.RequirementSet,
            SpecArtifactKind.WorkSpec);
    }

    [Fact]
    public void WorkSpec_UsesExplicitSchemaDefaults()
    {
        WorkSpec workSpec =
            CreateValidWorkSpec();

        Assert.Equal(
            SpecSchema.SchemaId,
            workSpec.SchemaId);

        Assert.Equal(
            SpecSchema.SchemaVersion,
            workSpec.SchemaVersion);
    }

    [Fact]
    public void Validator_AcceptsValidRequirementTraceability()
    {
        IReadOnlyList<SpecValidationError> errors =
            WorkSpecValidator.Validate(
                CreateValidWorkSpec(),
                CreateRequirementSet());

        Assert.Empty(
            errors);
    }

    [Fact]
    public void Validator_RejectsUnsupportedSchema()
    {
        WorkSpec workSpec =
            CreateValidWorkSpec() with
            {
                SchemaId =
                    "ai.repokit.spec.other",
                SchemaVersion =
                    2
            };

        string[] actualCodes =
            WorkSpecValidator
                .Validate(
                    workSpec,
                    CreateRequirementSet())
                .Select(
                    error_ =>
                        error_.Code)
                .ToArray();

        Assert.Equal(
            [
                SpecValidationErrorCodes.UnsupportedSchemaId,
                SpecValidationErrorCodes.UnsupportedSchemaVersion
            ],
            actualCodes);
    }

    [Fact]
    public void Validator_RejectsDuplicateDanglingAndWrongKindReferences()
    {
        WorkSpec workSpec =
            new()
            {
                Constraints =
                [
                    new Constraint
                    {
                        Id =
                            new StableEntityId(
                                "CON-001"),
                        Statement =
                            "Constraint one",
                        RequirementIds =
                        [
                            new StableEntityId(
                                "REQ-999"),
                            new StableEntityId(
                                "REQ-999"),
                            new StableEntityId(
                                "AC-200")
                        ]
                    },
                    new Constraint
                    {
                        Id =
                            new StableEntityId(
                                "CON-001"),
                        Statement =
                            "Duplicate constraint",
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
                                "CON-010"),
                        Statement =
                            "Wrong criterion kind",
                        RequirementIds =
                        [
                            new StableEntityId(
                                "REQ-998"),
                            new StableEntityId(
                                "INPUT-001")
                        ]
                    }
                ]
            };

        string[] actual =
            DescribeErrors(
                WorkSpecValidator.Validate(
                    workSpec,
                    CreateRequirementSet()));

        Assert.Equal(
            [
                "DanglingReference|CON-001|REQ-999",
                "DanglingReference|CON-010|REQ-998",
                "DuplicateEntityId|CON-001|",
                "DuplicateReference|CON-001|REQ-999",
                "InvalidEntityKind|CON-010|",
                "InvalidReferenceTargetKind|CON-001|AC-200",
                "InvalidReferenceTargetKind|CON-010|INPUT-001"
            ],
            actual);
    }

    [Fact]
    public void Validator_RejectsDuplicateAcceptanceCriterionIds()
    {
        WorkSpec workSpec =
            new()
            {
                Constraints =
                [],
                AcceptanceCriteria =
                [
                    CreateAcceptanceCriterion(
                        "AC-001"),
                    CreateAcceptanceCriterion(
                        "AC-001")
                ]
            };

        SpecValidationError error =
            Assert.Single(
                WorkSpecValidator.Validate(
                    workSpec,
                    CreateRequirementSet()));

        Assert.Equal(
            SpecValidationErrorCodes.DuplicateEntityId,
            error.Code);

        Assert.Equal(
            "AC-001",
            error.SourceEntityId);
    }

    [Fact]
    public void Validator_ErrorOrderingIsIndependentOfCollectionOrder()
    {
        WorkSpec first =
            CreateInvalidOrderingFixture(
                reverse_: false);

        WorkSpec second =
            CreateInvalidOrderingFixture(
                reverse_: true);

        string[] firstErrors =
            DescribeErrors(
                WorkSpecValidator.Validate(
                    first,
                    CreateRequirementSet()));

        string[] secondErrors =
            DescribeErrors(
                WorkSpecValidator.Validate(
                    second,
                    CreateRequirementSet()));

        Assert.Equal(
            firstErrors,
            secondErrors);
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
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
                        "First requirement",
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
                        "Second requirement",
                    SourceInputIds =
                    [
                        new StableEntityId(
                            "INPUT-001")
                    ]
                }
            ]
        };
    }

    private static WorkSpec CreateValidWorkSpec()
    {
        return new WorkSpec
        {
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId(
                            "CON-001"),
                    Statement =
                        "The implementation must preserve deterministic behavior.",
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
                        "Requirement references validate without errors.",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001"),
                        new StableEntityId(
                            "REQ-002")
                    ]
                }
            ]
        };
    }

    private static AcceptanceCriterion CreateAcceptanceCriterion(
        string id_)
    {
        return new AcceptanceCriterion
        {
            Id =
                new StableEntityId(
                    id_),
            Statement =
                "Criterion",
            RequirementIds =
            [
                new StableEntityId(
                    "REQ-001")
            ]
        };
    }

    private static WorkSpec CreateInvalidOrderingFixture(
        bool reverse_)
    {
        Constraint firstConstraint =
            new()
            {
                Id =
                    new StableEntityId(
                        "CON-002"),
                Statement =
                    "Second",
                RequirementIds =
                [
                    new StableEntityId(
                        "REQ-998")
                ]
            };

        Constraint secondConstraint =
            new()
            {
                Id =
                    new StableEntityId(
                        "CON-001"),
                Statement =
                    "First",
                RequirementIds =
                [
                    new StableEntityId(
                        "REQ-999")
                ]
            };

        AcceptanceCriterion firstCriterion =
            new()
            {
                Id =
                    new StableEntityId(
                        "AC-002"),
                Statement =
                    "Second",
                RequirementIds =
                [
                    new StableEntityId(
                        "REQ-997")
                ]
            };

        AcceptanceCriterion secondCriterion =
            new()
            {
                Id =
                    new StableEntityId(
                        "AC-001"),
                Statement =
                    "First",
                RequirementIds =
                [
                    new StableEntityId(
                        "REQ-996")
                ]
            };

        return new WorkSpec
        {
            Constraints =
                reverse_
                    ? [firstConstraint, secondConstraint]
                    : [secondConstraint, firstConstraint],
            AcceptanceCriteria =
                reverse_
                    ? [firstCriterion, secondCriterion]
                    : [secondCriterion, firstCriterion]
        };
    }

    private static string[] DescribeErrors(
        IReadOnlyList<SpecValidationError> errors_)
    {
        return errors_
            .Select(
                error_ =>
                    $"{error_.Code}|{error_.SourceEntityId}|{error_.TargetEntityId}")
            .ToArray();
    }
}

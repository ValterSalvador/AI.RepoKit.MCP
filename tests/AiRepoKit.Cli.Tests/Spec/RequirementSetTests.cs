using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class RequirementSetTests
{
    [Fact]
    public void RequirementSet_UsesExplicitSchemaDefaults()
    {
        RequirementSet requirementSet =
            CreateValidRequirementSet();

        Assert.Equal(
            SpecSchema.SchemaId,
            requirementSet.SchemaId);

        Assert.Equal(
            SpecSchema.SchemaVersion,
            requirementSet.SchemaVersion);

        Assert.Single(
            requirementSet.Inputs);

        Assert.Single(
            requirementSet.Requirements);
    }

    [Fact]
    public void Validator_AcceptsValidRequirementTraceability()
    {
        RequirementSet requirementSet =
            CreateValidRequirementSet();

        IReadOnlyList<SpecValidationError> errors =
            RequirementSetValidator.Validate(
                requirementSet);

        Assert.Empty(
            errors);
    }

    [Fact]
    public void Validator_RejectsUnsupportedSchema()
    {
        RequirementSet requirementSet =
            CreateValidRequirementSet() with
            {
                SchemaId =
                    "ai.repokit.spec.other",
                SchemaVersion =
                    2
            };

        string[] actualCodes =
            RequirementSetValidator
                .Validate(
                    requirementSet)
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
    public void Validator_RejectsDuplicateAndDanglingReferences()
    {
        RequirementSet requirementSet =
            new()
            {
                Inputs =
                [
                    new RequirementInput
                    {
                        Id =
                            new StableEntityId(
                                "INPUT-001"),
                        Text =
                            "First input"
                    },
                    new RequirementInput
                    {
                        Id =
                            new StableEntityId(
                                "INPUT-001"),
                        Text =
                            "Duplicate input"
                    },
                    new RequirementInput
                    {
                        Id =
                            new StableEntityId(
                                "REQ-010"),
                        Text =
                            "Wrong entity kind"
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
                            "Requirement statement",
                        SourceInputIds =
                        [
                            new StableEntityId(
                                "INPUT-999"),
                            new StableEntityId(
                                "INPUT-999"),
                            new StableEntityId(
                                "REQ-200")
                        ]
                    }
                ]
            };

        string[] actual =
            RequirementSetValidator
                .Validate(
                    requirementSet)
                .Select(
                    error_ =>
                        $"{error_.Code}|{error_.SourceEntityId}|{error_.TargetEntityId}")
                .ToArray();

        Assert.Equal(
            [
                "DanglingReference|REQ-001|INPUT-999",
                "DuplicateEntityId|INPUT-001|",
                "DuplicateReference|REQ-001|INPUT-999",
                "InvalidEntityKind|REQ-010|",
                "InvalidReferenceTargetKind|REQ-001|REQ-200"
            ],
            actual);
    }

    [Fact]
    public void Validator_RejectsDuplicateRequirementIds()
    {
        RequirementSet requirementSet =
            new()
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
                    CreateRequirement(
                        "REQ-001",
                        "First"),
                    CreateRequirement(
                        "REQ-001",
                        "Second")
                ]
            };

        SpecValidationError error =
            Assert.Single(
                RequirementSetValidator.Validate(
                    requirementSet));

        Assert.Equal(
            SpecValidationErrorCodes.DuplicateEntityId,
            error.Code);

        Assert.Equal(
            "REQ-001",
            error.SourceEntityId);
    }

    [Fact]
    public void Validator_ErrorOrderingIsIndependentOfCollectionOrder()
    {
        RequirementSet first =
            CreateInvalidOrderingFixture(
                reverse_: false);

        RequirementSet second =
            CreateInvalidOrderingFixture(
                reverse_: true);

        string[] firstErrors =
            DescribeErrors(
                RequirementSetValidator.Validate(
                    first));

        string[] secondErrors =
            DescribeErrors(
                RequirementSetValidator.Validate(
                    second));

        Assert.Equal(
            firstErrors,
            secondErrors);
    }

    private static RequirementSet CreateValidRequirementSet()
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
                        "The repository needs a stable requirement contract."
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
                        "The requirement contract must use stable identities.",
                    SourceInputIds =
                    [
                        new StableEntityId(
                            "INPUT-001")
                    ]
                }
            ]
        };
    }

    private static Requirement CreateRequirement(
        string id_,
        string statement_)
    {
        return new Requirement
        {
            Id =
                new StableEntityId(
                    id_),
            Statement =
                statement_,
            SourceInputIds =
            [
                new StableEntityId(
                    "INPUT-001")
            ]
        };
    }

    private static RequirementSet CreateInvalidOrderingFixture(
        bool reverse_)
    {
        RequirementInput firstInput =
            new()
            {
                Id =
                    new StableEntityId(
                        "INPUT-002"),
                Text =
                    "Second"
            };

        RequirementInput secondInput =
            new()
            {
                Id =
                    new StableEntityId(
                        "INPUT-001"),
                Text =
                    "First"
            };

        Requirement firstRequirement =
            new()
            {
                Id =
                    new StableEntityId(
                        "REQ-002"),
                Statement =
                    "Second requirement",
                SourceInputIds =
                [
                    new StableEntityId(
                        "INPUT-999")
                ]
            };

        Requirement secondRequirement =
            new()
            {
                Id =
                    new StableEntityId(
                        "REQ-001"),
                Statement =
                    "First requirement",
                SourceInputIds =
                [
                    new StableEntityId(
                        "INPUT-998")
                ]
            };

        return new RequirementSet
        {
            Inputs =
                reverse_
                    ? [firstInput, secondInput]
                    : [secondInput, firstInput],
            Requirements =
                reverse_
                    ? [firstRequirement, secondRequirement]
                    : [secondRequirement, firstRequirement]
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

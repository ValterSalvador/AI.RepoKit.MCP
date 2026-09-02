using System.Text.RegularExpressions;
using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecSemanticDigestTests
{
    [Fact]
    public void RequirementSet_CanonicalRepresentationIsFrozen()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();

        string canonical =
            SpecSemanticCanonicalizer.Canonicalize(
                requirementSet);

        Assert.Equal(
            "{\"canonicalizationId\":\"ai.repokit.spec.semantic\",\"canonicalizationVersion\":1,\"artifactKind\":\"requirementSet\",\"artifactIdentity\":\"requirements\",\"schemaId\":\"ai.repokit.spec\",\"schemaVersion\":1,\"inputs\":[{\"id\":\"INPUT-001\",\"text\":\"Input one\"},{\"id\":\"INPUT-002\",\"text\":\"Input two\"}],\"requirements\":[{\"id\":\"REQ-001\",\"statement\":\"Requirement one\",\"sourceInputIds\":[\"INPUT-001\",\"INPUT-002\"]},{\"id\":\"REQ-002\",\"statement\":\"Requirement two\",\"sourceInputIds\":[\"INPUT-002\"]}]}",
            canonical);
    }

    [Fact]
    public void RequirementSet_UnorderedCollectionsDoNotChangeDigest()
    {
        RequirementSet first =
            CreateRequirementSet();

        RequirementSet second =
            first with
            {
                Inputs =
                [
                    first.Inputs[1],
                    first.Inputs[0]
                ],
                Requirements =
                [
                    first.Requirements[1] with
                    {
                        SourceInputIds =
                        [
                            new StableEntityId(
                                "INPUT-001"),
                            new StableEntityId(
                                "INPUT-002")
                        ]
                    },
                    first.Requirements[0]
                ]
            };

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
    public void WorkSpec_UnorderedCollectionsDoNotChangeDigest()
    {
        WorkSpec first =
            CreateWorkSpec();

        WorkSpec second =
            first with
            {
                Constraints =
                [
                    first.Constraints[1] with
                    {
                        RequirementIds =
                        [
                            new StableEntityId(
                                "REQ-001"),
                            new StableEntityId(
                                "REQ-002")
                        ]
                    },
                    first.Constraints[0]
                ],
                AcceptanceCriteria =
                [
                    first.AcceptanceCriteria[1] with
                    {
                        RequirementIds =
                        [
                            new StableEntityId(
                                "REQ-001"),
                            new StableEntityId(
                                "REQ-002")
                        ]
                    },
                    first.AcceptanceCriteria[0]
                ]
            };

        Assert.Equal(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                second));
    }

    [Fact]
    public void ImplementationPlan_ReferenceOrderDoesNotChangeDigest()
    {
        ImplementationPlan first =
            CreatePlan();

        ImplementationPlan second =
            first with
            {
                Steps =
                [
                    first.Steps[0] with
                    {
                        RequirementIds =
                        [
                            new StableEntityId(
                                "REQ-002"),
                            new StableEntityId(
                                "REQ-001")
                        ],
                        AcceptanceCriterionIds =
                        [
                            new StableEntityId(
                                "AC-002"),
                            new StableEntityId(
                                "AC-001")
                        ]
                    },
                    first.Steps[1]
                ]
            };

        Assert.Equal(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                second));
    }

    [Fact]
    public void ImplementationPlan_StepOrderIsSemantic()
    {
        ImplementationPlan first =
            CreatePlan();

        ImplementationPlan reordered =
            first with
            {
                Steps =
                [
                    first.Steps[1],
                    first.Steps[0]
                ]
            };

        Assert.NotEqual(
            SpecSemanticCanonicalizer.Canonicalize(
                first),
            SpecSemanticCanonicalizer.Canonicalize(
                reordered));

        Assert.NotEqual(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                reordered));
    }

    [Fact]
    public void RequirementSet_OwnRevisionDoesNotChangeDigest()
    {
        RequirementSet first =
            CreateRequirementSet();

        RequirementSet second =
            first with
            {
                Revision =
                    new ArtifactRevision(
                        999)
            };

        Assert.Equal(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                second));
    }

    [Fact]
    public void WorkSpec_OwnRevisionDoesNotChangeDigest()
    {
        WorkSpec first =
            CreateWorkSpec();

        WorkSpec second =
            first with
            {
                Revision =
                    new ArtifactRevision(
                        999)
            };

        Assert.Equal(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                second));
    }

    [Fact]
    public void ImplementationPlan_OwnRevisionDoesNotChangeDigest()
    {
        ImplementationPlan first =
            CreatePlan();

        ImplementationPlan second =
            first with
            {
                Revision =
                    new ArtifactRevision(
                        999)
            };

        Assert.Equal(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                second));
    }

    [Fact]
    public void CrossArtifactRevisionBindingIsSemantic()
    {
        WorkSpec first =
            CreateWorkSpec();

        WorkSpec changedBinding =
            first with
            {
                RequirementSetRevision =
                    new ArtifactRevision(
                        4)
            };

        Assert.NotEqual(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                changedBinding));

        ImplementationPlan plan =
            CreatePlan();

        ImplementationPlan changedPlanBinding =
            plan with
            {
                WorkSpecRevision =
                    new ArtifactRevision(
                        6)
            };

        Assert.NotEqual(
            SpecSemanticDigest.Compute(
                plan),
            SpecSemanticDigest.Compute(
                changedPlanBinding));
    }

    [Fact]
    public void SemanticStringWhitespaceChangesDigest()
    {
        RequirementSet first =
            CreateRequirementSet();

        RequirementSet second =
            first with
            {
                Requirements =
                [
                    first.Requirements[0] with
                    {
                        Statement =
                            "Requirement  one"
                    },
                    first.Requirements[1]
                ]
            };

        Assert.NotEqual(
            SpecSemanticDigest.Compute(
                first),
            SpecSemanticDigest.Compute(
                second));
    }

    [Fact]
    public void DigestIsDeterministicLowercaseSha256()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();

        string first =
            SpecSemanticDigest.Compute(
                requirementSet);

        string second =
            SpecSemanticDigest.Compute(
                requirementSet);

        Assert.Equal(
            first,
            second);

        Assert.Matches(
            new Regex(
                "^[0-9a-f]{64}$",
                RegexOptions.CultureInvariant),
            first);
    }

    [Fact]
    public void CanonicalRepresentationExcludesOwnArtifactRevision()
    {
        string canonical =
            SpecSemanticCanonicalizer.Canonicalize(
                CreateRequirementSet());

        Assert.DoesNotContain(
            "\"revision\"",
            canonical,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedSchemaVersionCannotBeCanonicalized()
    {
        RequirementSet requirementSet =
            CreateRequirementSet() with
            {
                SchemaVersion =
                    2
            };

        Assert.Throws<ArgumentException>(
            () =>
                SpecSemanticCanonicalizer.Canonicalize(
                    requirementSet));
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
                            "INPUT-002"),
                    Text =
                        "Input two"
                },
                new RequirementInput
                {
                    Id =
                        new StableEntityId(
                            "INPUT-001"),
                    Text =
                        "Input one"
                }
            ],
            Requirements =
            [
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
                            "INPUT-002")
                    ]
                },
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
                            "INPUT-002"),
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
                            "CON-002"),
                    Statement =
                        "Constraint two",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-002")
                    ]
                },
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
                            "REQ-002"),
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
                            "AC-002"),
                    Statement =
                        "Criterion two",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-002")
                    ]
                },
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
                            "REQ-002"),
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
                        "First logical step",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001"),
                        new StableEntityId(
                            "REQ-002")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId(
                            "AC-001"),
                        new StableEntityId(
                            "AC-002")
                    ]
                },
                new PlanStep
                {
                    Id =
                        new StableEntityId(
                            "PLAN-STEP-002"),
                    Statement =
                        "Second logical step",
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

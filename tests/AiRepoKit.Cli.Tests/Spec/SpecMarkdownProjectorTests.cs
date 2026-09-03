using System.Globalization;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Projection;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecMarkdownProjectorTests
{
    [Fact]
    public void RequirementSet_ProjectsExactDeterministicMarkdown()
    {
        RequirementSet artifact =
            CreateRequirementSet();

        string digest =
            SpecSemanticDigest.Compute(
                artifact);

        string expected =
            $$"""
            # Requirement Set

            > Derived projection only. Canonical state is the corresponding JSON artifact under `.ai/specs/<spec-id>/`.

            Artifact identity: `requirements`
            Schema ID: `ai.repokit.spec`
            Schema version: `1`
            Revision: `3`
            Semantic digest: `sha256:{{digest}}`

            ## Inputs

            ### `INPUT-001`

            Text: <code>Input one</code>

            ### `INPUT-002`

            Text: <code>Input two</code>

            ## Requirements

            ### `REQ-001`

            Statement: <code>Requirement one</code>

            Source inputs:

            - `INPUT-001`
            - `INPUT-002`

            ### `REQ-002`

            Statement: <code>Requirement two</code>

            Source inputs: _none_
            """ +
            "\n";

        Assert.Equal(
            expected,
            SpecMarkdownProjector.Project(
                artifact));
    }

    [Fact]
    public void WorkSpec_ProjectsExactDeterministicMarkdown()
    {
        WorkSpec artifact =
            CreateWorkSpec();

        string digest =
            SpecSemanticDigest.Compute(
                artifact);

        string expected =
            $$"""
            # Work Spec

            > Derived projection only. Canonical state is the corresponding JSON artifact under `.ai/specs/<spec-id>/`.

            Artifact identity: `work-spec`
            Schema ID: `ai.repokit.spec`
            Schema version: `1`
            Revision: `5`
            RequirementSet revision: `3`
            Semantic digest: `sha256:{{digest}}`

            ## Constraints

            ### `CON-001`

            Statement: <code>Constraint one</code>

            Requirements:

            - `REQ-001`
            - `REQ-002`

            ### `CON-002`

            Statement: <code>Constraint two</code>

            Requirements: _none_

            ## Acceptance Criteria

            ### `AC-001`

            Statement: <code>Criterion one</code>

            Requirements:

            - `REQ-001`
            - `REQ-002`

            ### `AC-002`

            Statement: <code>Criterion two</code>

            Requirements: _none_
            """ +
            "\n";

        Assert.Equal(
            expected,
            SpecMarkdownProjector.Project(
                artifact));
    }

    [Fact]
    public void ImplementationPlan_ProjectsExactMarkdownAndPreservesStepOrder()
    {
        ImplementationPlan artifact =
            CreateImplementationPlan();

        string digest =
            SpecSemanticDigest.Compute(
                artifact);

        string expected =
            $$"""
            # Implementation Plan

            > Derived projection only. Canonical state is the corresponding JSON artifact under `.ai/specs/<spec-id>/`.

            Artifact identity: `implementation-plan`
            Schema ID: `ai.repokit.spec`
            Schema version: `1`
            Revision: `2`
            WorkSpec revision: `5`
            Semantic digest: `sha256:{{digest}}`

            ## Steps

            ### 1. `PLAN-STEP-002`

            Statement: <code>First logical step</code>

            Requirements:

            - `REQ-001`
            - `REQ-002`

            Acceptance criteria:

            - `AC-001`
            - `AC-002`

            ### 2. `PLAN-STEP-001`

            Statement: <code>Second logical step</code>

            Requirements: _none_

            Acceptance criteria: _none_
            """ +
            "\n";

        Assert.Equal(
            expected,
            SpecMarkdownProjector.Project(
                artifact));
    }

    [Fact]
    public void SemanticSets_ProjectIdenticallyWhenReordered()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();
        RequirementSet reorderedRequirementSet =
            requirementSet with
            {
                Inputs = requirementSet.Inputs.Reverse().ToArray(),
                Requirements =
                    requirementSet.Requirements.Reverse().Select(
                        requirement_ =>
                            requirement_ with
                            {
                                SourceInputIds =
                                    requirement_.SourceInputIds.Reverse().ToArray()
                            }).ToArray()
            };

        WorkSpec workSpec =
            CreateWorkSpec();
        WorkSpec reorderedWorkSpec =
            workSpec with
            {
                Constraints =
                    workSpec.Constraints.Reverse().Select(
                        constraint_ =>
                            constraint_ with
                            {
                                RequirementIds =
                                    constraint_.RequirementIds.Reverse().ToArray()
                            }).ToArray(),
                AcceptanceCriteria =
                    workSpec.AcceptanceCriteria.Reverse().Select(
                        criterion_ =>
                            criterion_ with
                            {
                                RequirementIds =
                                    criterion_.RequirementIds.Reverse().ToArray()
                            }).ToArray()
            };

        Assert.Equal(
            SpecMarkdownProjector.Project(
                requirementSet),
            SpecMarkdownProjector.Project(
                reorderedRequirementSet));
        Assert.Equal(
            SpecMarkdownProjector.Project(
                workSpec),
            SpecMarkdownProjector.Project(
                reorderedWorkSpec));
    }

    [Fact]
    public void ImplementationPlan_OnlyReferenceSetsAreOrderInsensitive()
    {
        ImplementationPlan artifact =
            CreateImplementationPlan();
        ImplementationPlan reorderedReferences =
            artifact with
            {
                Steps =
                    artifact.Steps.Select(
                        step_ =>
                            step_ with
                            {
                                RequirementIds =
                                    step_.RequirementIds.Reverse().ToArray(),
                                AcceptanceCriterionIds =
                                    step_.AcceptanceCriterionIds.Reverse().ToArray()
                            }).ToArray()
            };
        ImplementationPlan reorderedSteps =
            artifact with
            {
                Steps =
                    artifact.Steps.Reverse().ToArray()
            };

        Assert.Equal(
            SpecMarkdownProjector.Project(
                artifact),
            SpecMarkdownProjector.Project(
                reorderedReferences));
        Assert.NotEqual(
            SpecMarkdownProjector.Project(
                artifact),
            SpecMarkdownProjector.Project(
                reorderedSteps));
    }

    [Fact]
    public void Projection_IsRepeatableLfOnlyAndHasOneTrailingLf()
    {
        RequirementSet artifact =
            CreateRequirementSet();

        string first =
            SpecMarkdownProjector.Project(
                artifact);
        string second =
            SpecMarkdownProjector.Project(
                artifact);

        Assert.Equal(
            first,
            second);
        Assert.DoesNotContain(
            '\r',
            first);
        Assert.EndsWith(
            "\n",
            first,
            StringComparison.Ordinal);
        Assert.False(
            first.EndsWith(
                "\n\n",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Projection_IsCultureInvariant()
    {
        ImplementationPlan artifact =
            CreateImplementationPlan();
        CultureInfo originalCulture =
            CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture =
            CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture =
                CultureInfo.GetCultureInfo(
                    "de-DE");
            CultureInfo.CurrentUICulture =
                CultureInfo.GetCultureInfo(
                    "de-DE");
            string german =
                SpecMarkdownProjector.Project(
                    artifact);

            CultureInfo.CurrentCulture =
                CultureInfo.GetCultureInfo(
                    "ar-EG");
            CultureInfo.CurrentUICulture =
                CultureInfo.GetCultureInfo(
                    "ar-EG");
            string arabic =
                SpecMarkdownProjector.Project(
                    artifact);

            Assert.Equal(
                german,
                arabic);
        }
        finally
        {
            CultureInfo.CurrentCulture =
                originalCulture;
            CultureInfo.CurrentUICulture =
                originalUiCulture;
        }
    }

    [Fact]
    public void ArbitraryText_IsVisiblyEscapedOnOnePhysicalLine()
    {
        char backslash =
            (char)92;
        string inputText =
            string.Concat(
                "slash",
                backslash,
                "\r\nline\n\t<tag>&`*_",
                '\u0001',
                '\u007f',
                " café 漢字");

        RequirementSet artifact =
            CreateRequirementSet() with
            {
                Inputs =
                [
                    new RequirementInput
                    {
                        Id = Id("INPUT-001"),
                        Text = inputText
                    }
                ],
                Requirements = []
            };

        string projection =
            SpecMarkdownProjector.Project(
                artifact);
        string visibleBackslash =
            backslash.ToString();
        string expected =
            string.Concat(
                "<code>slash",
                visibleBackslash,
                visibleBackslash,
                visibleBackslash,
                "r",
                visibleBackslash,
                "n",
                "line",
                visibleBackslash,
                "n",
                visibleBackslash,
                "t",
                "&lt;tag&gt;&amp;`*_",
                visibleBackslash,
                "u0001",
                visibleBackslash,
                "u007F café 漢字</code>");

        Assert.Contains(
            expected,
            projection,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            '\r',
            projection);
    }

    [Fact]
    public void OwnRevision_ChangesMetadataButNotSemanticDigest()
    {
        RequirementSet first =
            CreateRequirementSet() with
            {
                Revision = new ArtifactRevision(1)
            };
        RequirementSet second =
            first with
            {
                Revision = new ArtifactRevision(99)
            };

        string firstProjection =
            SpecMarkdownProjector.Project(
                first);
        string secondProjection =
            SpecMarkdownProjector.Project(
                second);

        Assert.NotEqual(
            firstProjection,
            secondProjection);
        Assert.Contains(
            "Revision: `1`",
            firstProjection,
            StringComparison.Ordinal);
        Assert.Contains(
            "Revision: `99`",
            secondProjection,
            StringComparison.Ordinal);
        Assert.Equal(
            GetMetadataLine(
                firstProjection,
                "Semantic digest:"),
            GetMetadataLine(
                secondProjection,
                "Semantic digest:"));
    }

    [Fact]
    public void DependencyRevisions_ChangeMetadataAndSemanticDigest()
    {
        WorkSpec workSpec =
            CreateWorkSpec();
        WorkSpec changedWorkSpec =
            workSpec with
            {
                RequirementSetRevision = new ArtifactRevision(4)
            };
        ImplementationPlan plan =
            CreateImplementationPlan();
        ImplementationPlan changedPlan =
            plan with
            {
                WorkSpecRevision = new ArtifactRevision(6)
            };

        AssertDependencyChange(
            SpecMarkdownProjector.Project(workSpec),
            SpecMarkdownProjector.Project(changedWorkSpec),
            "RequirementSet revision: `3`",
            "RequirementSet revision: `4`");
        AssertDependencyChange(
            SpecMarkdownProjector.Project(plan),
            SpecMarkdownProjector.Project(changedPlan),
            "WorkSpec revision: `5`",
            "WorkSpec revision: `6`");
    }

    [Fact]
    public void EmptyCollections_RenderExplicitMarkers()
    {
        RequirementSet requirementSet =
            CreateRequirementSet() with
            {
                Inputs = [],
                Requirements = []
            };
        WorkSpec workSpec =
            CreateWorkSpec() with
            {
                Constraints = [],
                AcceptanceCriteria = []
            };
        ImplementationPlan plan =
            CreateImplementationPlan() with
            {
                Steps = []
            };

        Assert.Contains(
            "## Inputs\n\n_None._\n",
            SpecMarkdownProjector.Project(requirementSet),
            StringComparison.Ordinal);
        Assert.Contains(
            "## Requirements\n\n_None._\n",
            SpecMarkdownProjector.Project(requirementSet),
            StringComparison.Ordinal);
        Assert.Contains(
            "## Constraints\n\n_None._\n",
            SpecMarkdownProjector.Project(workSpec),
            StringComparison.Ordinal);
        Assert.Contains(
            "## Acceptance Criteria\n\n_None._\n",
            SpecMarkdownProjector.Project(workSpec),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "## Steps\n\n_None._\n",
            SpecMarkdownProjector.Project(plan),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_DoesNotMutateSemanticState()
    {
        WorkSpec artifact =
            CreateWorkSpec();
        string before =
            SpecSemanticDigest.Compute(
                artifact);

        _ = SpecMarkdownProjector.Project(
            artifact);

        Assert.Equal(
            before,
            SpecSemanticDigest.Compute(
                artifact));
    }

    [Fact]
    public void NullArtifacts_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => SpecMarkdownProjector.Project((RequirementSet)null!));
        Assert.Throws<ArgumentNullException>(
            () => SpecMarkdownProjector.Project((WorkSpec)null!));
        Assert.Throws<ArgumentNullException>(
            () => SpecMarkdownProjector.Project((ImplementationPlan)null!));
    }

    private static void AssertDependencyChange(
        string first_,
        string second_,
        string firstRevisionLine_,
        string secondRevisionLine_)
    {
        Assert.Contains(firstRevisionLine_, first_, StringComparison.Ordinal);
        Assert.Contains(secondRevisionLine_, second_, StringComparison.Ordinal);
        Assert.NotEqual(
            GetMetadataLine(first_, "Semantic digest:"),
            GetMetadataLine(second_, "Semantic digest:"));
    }

    private static string GetMetadataLine(
        string projection_,
        string prefix_)
    {
        return projection_.Split('\n').Single(
            line_ => line_.StartsWith(prefix_, StringComparison.Ordinal));
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(3),
            Inputs =
            [
                new RequirementInput { Id = Id("INPUT-002"), Text = "Input two" },
                new RequirementInput { Id = Id("INPUT-001"), Text = "Input one" }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = Id("REQ-002"),
                    Statement = "Requirement two",
                    SourceInputIds = []
                },
                new Requirement
                {
                    Id = Id("REQ-001"),
                    Statement = "Requirement one",
                    SourceInputIds = [Id("INPUT-002"), Id("INPUT-001")]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec()
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(5),
            RequirementSetRevision = new ArtifactRevision(3),
            Constraints =
            [
                new Constraint
                {
                    Id = Id("CON-002"),
                    Statement = "Constraint two",
                    RequirementIds = []
                },
                new Constraint
                {
                    Id = Id("CON-001"),
                    Statement = "Constraint one",
                    RequirementIds = [Id("REQ-002"), Id("REQ-001")]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id = Id("AC-002"),
                    Statement = "Criterion two",
                    RequirementIds = []
                },
                new AcceptanceCriterion
                {
                    Id = Id("AC-001"),
                    Statement = "Criterion one",
                    RequirementIds = [Id("REQ-002"), Id("REQ-001")]
                }
            ]
        };
    }

    private static ImplementationPlan CreateImplementationPlan()
    {
        return new ImplementationPlan
        {
            Revision = new ArtifactRevision(2),
            WorkSpecRevision = new ArtifactRevision(5),
            Steps =
            [
                new PlanStep
                {
                    Id = Id("PLAN-STEP-002"),
                    Statement = "First logical step",
                    RequirementIds = [Id("REQ-002"), Id("REQ-001")],
                    AcceptanceCriterionIds = [Id("AC-002"), Id("AC-001")]
                },
                new PlanStep
                {
                    Id = Id("PLAN-STEP-001"),
                    Statement = "Second logical step",
                    RequirementIds = [],
                    AcceptanceCriterionIds = []
                }
            ]
        };
    }

    private static StableEntityId Id(
        string value_)
    {
        return new StableEntityId(
            value_);
    }
}

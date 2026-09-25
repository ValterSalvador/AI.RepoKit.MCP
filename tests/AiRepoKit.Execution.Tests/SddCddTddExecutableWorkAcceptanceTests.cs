using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Execution.Tests;

public sealed class SddCddTddExecutableWorkAcceptanceTests
{
    [Fact]
    public void CompleteRequirementToTestChain_IsAccepted()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                fixture.ImplementationPlan,
                fixture.ExecutableWork);

        Assert.Empty(
            errors);
    }

    [Fact]
    public void WorkSpecRequirementLinkBreak_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        AcceptanceCriterion brokenCriterion =
            fixture.WorkSpec.AcceptanceCriteria[0] with
            {
                RequirementIds =
                [
                    Id("REQ-999")
                ]
            };

        WorkSpec brokenWorkSpec =
            fixture.WorkSpec with
            {
                AcceptanceCriteria =
                [
                    brokenCriterion
                ]
            };

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                brokenWorkSpec,
                fixture.ImplementationPlan,
                fixture.ExecutableWork);

        Assert.Contains(
            "work-spec:DanglingReference",
            errors);

        Assert.Contains(
            "requirement-overlap:PLAN-STEP-001|AC-001",
            errors);
    }

    [Fact]
    public void PlanRequirementLinkBreak_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        PlanStep brokenStep =
            fixture.ImplementationPlan.Steps[0] with
            {
                RequirementIds =
                [
                    Id("REQ-999")
                ]
            };

        ImplementationPlan brokenPlan =
            fixture.ImplementationPlan with
            {
                Steps =
                [
                    brokenStep
                ]
            };

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                brokenPlan,
                fixture.ExecutableWork);

        Assert.Contains(
            "implementation-plan:DanglingReference",
            errors);
    }

    [Fact]
    public void PlanAcceptanceCriterionLinkBreak_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        PlanStep brokenStep =
            fixture.ImplementationPlan.Steps[0] with
            {
                AcceptanceCriterionIds =
                [
                    Id("AC-999")
                ]
            };

        ImplementationPlan brokenPlan =
            fixture.ImplementationPlan with
            {
                Steps =
                [
                    brokenStep
                ]
            };

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                brokenPlan,
                fixture.ExecutableWork);

        Assert.Contains(
            "implementation-plan:DanglingReference",
            errors);
    }

    [Fact]
    public void TaskSourcePlanStepBreak_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        ExecutableWork brokenWork =
            new(
                fixture.ImplementationPlan.Revision.Value,
                [
                    new ExecutableTask(
                        "task-001",
                        "PLAN-STEP-999",
                        "Implement the requirement.")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                fixture.ExecutableWork.ValidationRequirements);

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                fixture.ImplementationPlan,
                brokenWork);

        Assert.Contains(
            "task-source-plan-step:task-001",
            errors);
    }

    [Fact]
    public void ValidationTaskLinkBreak_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(
                        fixture.ImplementationPlan.Revision.Value,
                        fixture.ExecutableWork.Tasks,
                        Array.Empty<ExecutableTaskDependency>(),
                        [
                            new ValidationRequirement(
                                "validation-001",
                                "task-999",
                                "AC-001",
                                ValidationStrategy.Test,
                                "Verify the requirement.")
                        ]));

        Assert.Equal(
            "validationRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void ValidationAcceptanceCriterionLinkBreak_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        ExecutableWork brokenWork =
            new(
                fixture.ImplementationPlan.Revision.Value,
                fixture.ExecutableWork.Tasks,
                Array.Empty<ExecutableTaskDependency>(),
                [
                    new ValidationRequirement(
                        "validation-001",
                        "task-001",
                        "AC-999",
                        ValidationStrategy.Test,
                        "Verify the requirement.")
                ]);

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                fixture.ImplementationPlan,
                brokenWork);

        Assert.Contains(
            "validation-criterion:validation-001",
            errors);
    }

    [Fact]
    public void NonTestStrategy_DoesNotSatisfyTddCoverage()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        ExecutableWork brokenWork =
            new(
                fixture.ImplementationPlan.Revision.Value,
                fixture.ExecutableWork.Tasks,
                Array.Empty<ExecutableTaskDependency>(),
                [
                    new ValidationRequirement(
                        "validation-001",
                        "task-001",
                        "AC-001",
                        ValidationStrategy.Build,
                        "Build-only validation.")
                ]);

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                fixture.ImplementationPlan,
                brokenWork);

        Assert.Contains(
            "test-coverage:PLAN-STEP-001|AC-001",
            errors);
    }

    [Fact]
    public void ImplementationPlanRevisionMismatch_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        ExecutableWork brokenWork =
            new(
                fixture.ImplementationPlan.Revision.Value + 1,
                fixture.ExecutableWork.Tasks,
                Array.Empty<ExecutableTaskDependency>(),
                fixture.ExecutableWork.ValidationRequirements);

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                fixture.ImplementationPlan,
                brokenWork);

        Assert.Contains(
            "plan-revision",
            errors);
    }

    [Fact]
    public void OrdinalCaseMismatch_IsRejected()
    {
        TraceabilityFixture fixture =
            CreateValidFixture();

        ExecutableWork brokenWork =
            new(
                fixture.ImplementationPlan.Revision.Value,
                [
                    new ExecutableTask(
                        "task-001",
                        "plan-step-001",
                        "Implement the requirement.")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                fixture.ExecutableWork.ValidationRequirements);

        string[] errors =
            ValidateTraceability(
                fixture.RequirementSet,
                fixture.WorkSpec,
                fixture.ImplementationPlan,
                brokenWork);

        Assert.Contains(
            "task-source-plan-step:task-001",
            errors);

        Assert.Contains(
            "test-coverage:PLAN-STEP-001|AC-001",
            errors);
    }

    private static string[] ValidateTraceability(
        RequirementSet requirementSet_,
        WorkSpec workSpec_,
        ImplementationPlan implementationPlan_,
        ExecutableWork executableWork_)
    {
        List<string> errors =
            [];

        AppendSpecErrors(
            errors,
            "requirement-set",
            RequirementSetValidator.Validate(
                requirementSet_));

        AppendSpecErrors(
            errors,
            "work-spec",
            WorkSpecValidator.Validate(
                workSpec_,
                requirementSet_));

        AppendSpecErrors(
            errors,
            "implementation-plan",
            ImplementationPlanValidator.Validate(
                implementationPlan_,
                workSpec_,
                requirementSet_));

        if (
            executableWork_.SourceImplementationPlanRevision !=
            implementationPlan_.Revision.Value
        )
        {
            errors.Add(
                "plan-revision");
        }

        Dictionary<string, AcceptanceCriterion> criteria =
            workSpec_
                .AcceptanceCriteria
                .ToDictionary(
                    criterion_ =>
                        criterion_.Id.Value,
                    StringComparer.Ordinal);

        Dictionary<string, PlanStep> steps =
            implementationPlan_
                .Steps
                .ToDictionary(
                    step_ =>
                        step_.Id.Value,
                    StringComparer.Ordinal);

        Dictionary<string, ExecutableTask> tasks =
            executableWork_
                .Tasks
                .ToDictionary(
                    task_ =>
                        task_.Id,
                    StringComparer.Ordinal);

        foreach (ExecutableTask task in
                 executableWork_.Tasks)
        {
            if (!steps.ContainsKey(
                    task.SourcePlanStepId))
            {
                errors.Add(
                    $"task-source-plan-step:{task.Id}");
            }
        }

        foreach (ValidationRequirement validation in
                 executableWork_.ValidationRequirements)
        {
            if (validation.Strategy !=
                ValidationStrategy.Test)
            {
                continue;
            }

            if (!tasks.TryGetValue(
                    validation.TaskId,
                    out ExecutableTask? task))
            {
                errors.Add(
                    $"validation-task:{validation.Id}");

                continue;
            }

            if (!steps.TryGetValue(
                    task.SourcePlanStepId,
                    out PlanStep? step))
            {
                errors.Add(
                    $"validation-step:{validation.Id}");

                continue;
            }

            bool criterionLinked =
                step
                    .AcceptanceCriterionIds
                    .Any(
                        criterionId_ =>
                            string.Equals(
                                criterionId_.Value,
                                validation.SourceAcceptanceCriterionId,
                                StringComparison.Ordinal));

            if (!criterionLinked)
            {
                errors.Add(
                    $"validation-criterion:{validation.Id}");
            }
        }

        foreach (PlanStep step in
                 implementationPlan_.Steps)
        {
            HashSet<string> stepRequirements =
                step
                    .RequirementIds
                    .Select(
                        requirementId_ =>
                            requirementId_.Value)
                    .ToHashSet(
                        StringComparer.Ordinal);

            foreach (
                StableEntityId criterionId in
                step.AcceptanceCriterionIds)
            {
                if (
                    criteria.TryGetValue(
                        criterionId.Value,
                        out AcceptanceCriterion? criterion)
                )
                {
                    bool overlaps =
                        criterion
                            .RequirementIds
                            .Any(
                                requirementId_ =>
                                    stepRequirements.Contains(
                                        requirementId_.Value));

                    if (!overlaps)
                    {
                        errors.Add(
                            $"requirement-overlap:{step.Id.Value}|{criterionId.Value}");
                    }
                }

                bool covered =
                    executableWork_
                        .ValidationRequirements
                        .Where(
                            validation_ =>
                                validation_.Strategy ==
                                ValidationStrategy.Test)
                        .Any(
                            validation_ =>
                            {
                                if (
                                    !string.Equals(
                                        validation_.SourceAcceptanceCriterionId,
                                        criterionId.Value,
                                        StringComparison.Ordinal)
                                )
                                {
                                    return false;
                                }

                                if (
                                    !tasks.TryGetValue(
                                        validation_.TaskId,
                                        out ExecutableTask? task)
                                )
                                {
                                    return false;
                                }

                                return string.Equals(
                                    task.SourcePlanStepId,
                                    step.Id.Value,
                                    StringComparison.Ordinal);
                            });

                if (!covered)
                {
                    errors.Add(
                        $"test-coverage:{step.Id.Value}|{criterionId.Value}");
                }
            }
        }

        return errors
            .Distinct(
                StringComparer.Ordinal)
            .OrderBy(
                error_ =>
                    error_,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static void AppendSpecErrors(
        ICollection<string> errors_,
        string stage_,
        IReadOnlyList<SpecValidationError> specErrors_)
    {
        foreach (SpecValidationError error in
                 specErrors_)
        {
            errors_.Add(
                $"{stage_}:{error.Code}");
        }
    }

    private static TraceabilityFixture CreateValidFixture()
    {
        RequirementSet requirementSet =
            new()
            {
                Revision =
                    new ArtifactRevision(
                        3),
                Inputs =
                [
                    new RequirementInput
                    {
                        Id =
                            Id("INPUT-001"),
                        Text =
                            "The repository must preserve deterministic traceability."
                    }
                ],
                Requirements =
                [
                    new Requirement
                    {
                        Id =
                            Id("REQ-001"),
                        Statement =
                            "Executable work must remain traceable to its acceptance test.",
                        SourceInputIds =
                        [
                            Id("INPUT-001")
                        ]
                    }
                ]
            };

        WorkSpec workSpec =
            new()
            {
                Revision =
                    new ArtifactRevision(
                        5),
                RequirementSetRevision =
                    requirementSet.Revision,
                Constraints =
                [],
                AcceptanceCriteria =
                [
                    new AcceptanceCriterion
                    {
                        Id =
                            Id("AC-001"),
                        Statement =
                            "The executable task is covered by a deterministic test validation.",
                        RequirementIds =
                        [
                            Id("REQ-001")
                        ]
                    }
                ]
            };

        ImplementationPlan implementationPlan =
            new()
            {
                Revision =
                    new ArtifactRevision(
                        7),
                WorkSpecRevision =
                    workSpec.Revision,
                Steps =
                [
                    new PlanStep
                    {
                        Id =
                            Id("PLAN-STEP-001"),
                        Statement =
                            "Implement the deterministic executable-work acceptance.",
                        RequirementIds =
                        [
                            Id("REQ-001")
                        ],
                        AcceptanceCriterionIds =
                        [
                            Id("AC-001")
                        ]
                    }
                ]
            };

        ExecutableWork executableWork =
            new(
                implementationPlan.Revision.Value,
                [
                    new ExecutableTask(
                        "task-001",
                        "PLAN-STEP-001",
                        "Implement the requirement.")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                [
                    new ValidationRequirement(
                        "validation-001",
                        "task-001",
                        "AC-001",
                        ValidationStrategy.Test,
                        "Verify the requirement.")
                ]);

        return new TraceabilityFixture(
            requirementSet,
            workSpec,
            implementationPlan,
            executableWork);
    }

    private static StableEntityId Id(
        string value_)
    {
        return new StableEntityId(
            value_);
    }

    private sealed record TraceabilityFixture(
        RequirementSet RequirementSet,
        WorkSpec WorkSpec,
        ImplementationPlan ImplementationPlan,
        ExecutableWork ExecutableWork);
}

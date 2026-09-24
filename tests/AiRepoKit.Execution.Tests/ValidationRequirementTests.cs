namespace AiRepoKit.Execution.Tests;

using AiRepoKit.Execution;
using Xunit;

public sealed class ValidationRequirementTests
{
    [Fact]
    public void ValidationStrategy_HasExactlyFrozenDefinedValues()
    {
        ValidationStrategy[] values =
            Enum.GetValues<ValidationStrategy>();

        Assert.Equal(
            [
                ValidationStrategy.Build,
                ValidationStrategy.Test,
                ValidationStrategy.Policy
            ],
            values);

        Assert.Equal(
            [1, 2, 3],
            values
                .Select(
                    value_ =>
                        (int) value_)
                .ToArray());
    }

    [Fact]
    public void ValidationStrategy_ZeroIsUndefined()
    {
        Assert.False(
            Enum.IsDefined(
                typeof(ValidationStrategy),
                0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(100)]
    public void ValidationRequirement_RejectsUndefinedStrategy(
        int rawStrategy_)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new ValidationRequirement(
                        "validation-1",
                        "task-1",
                        "criterion-1",
                        (ValidationStrategy) rawStrategy_,
                        "statement"));

        Assert.Equal(
            "strategy_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ValidationRequirement_RejectsInvalidId(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ValidationRequirement(
                        value_!,
                        "task-1",
                        "criterion-1",
                        ValidationStrategy.Build,
                        "statement"));

        Assert.Equal(
            "id_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ValidationRequirement_RejectsInvalidTaskId(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ValidationRequirement(
                        "validation-1",
                        value_!,
                        "criterion-1",
                        ValidationStrategy.Test,
                        "statement"));

        Assert.Equal(
            "taskId_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ValidationRequirement_RejectsInvalidSourceAcceptanceCriterionId(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ValidationRequirement(
                        "validation-1",
                        "task-1",
                        value_!,
                        ValidationStrategy.Policy,
                        "statement"));

        Assert.Equal(
            "sourceAcceptanceCriterionId_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ValidationRequirement_RejectsInvalidStatement(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ValidationRequirement(
                        "validation-1",
                        "task-1",
                        "criterion-1",
                        ValidationStrategy.Build,
                        value_!));

        Assert.Equal(
            "statement_",
            exception.ParamName);
    }

    [Fact]
    public void ValidationRequirement_PreservesAllStringFieldsExactly()
    {
        ValidationRequirement requirement =
            new(
                "  validation-ID  ",
                "  task-ID  ",
                "  arbitrary-criterion-provenance  ",
                ValidationStrategy.Policy,
                "  exact statement text  ");

        Assert.Equal(
            "  validation-ID  ",
            requirement.Id);

        Assert.Equal(
            "  task-ID  ",
            requirement.TaskId);

        Assert.Equal(
            "  arbitrary-criterion-provenance  ",
            requirement.SourceAcceptanceCriterionId);

        Assert.Equal(
            ValidationStrategy.Policy,
            requirement.Strategy);

        Assert.Equal(
            "  exact statement text  ",
            requirement.Statement);
    }

    [Fact]
    public void SourceAcceptanceCriterionId_DoesNotRequireAcPrefix()
    {
        ValidationRequirement requirement =
            new(
                "validation-1",
                "task-1",
                "opaque-source-id",
                ValidationStrategy.Test,
                "statement");

        Assert.Equal(
            "opaque-source-id",
            requirement.SourceAcceptanceCriterionId);
    }

    [Fact]
    public void TwoArgumentWorkConstructor_ProducesEmptyValidationRequirements()
    {
        ExecutableWork work =
            new(
                1,
                [Task("task-1")]);

        Assert.Empty(
            work.ValidationRequirements);
    }

    [Fact]
    public void ThreeArgumentWorkConstructor_ProducesEmptyValidationRequirements()
    {
        ExecutableTaskDependency dependency =
            new(
                "task-2",
                "task-1");

        ExecutableWork work =
            new(
                1,
                [
                    Task("task-1"),
                    Task("task-2")
                ],
                [dependency]);

        Assert.Single(
            work.Dependencies);

        Assert.Empty(
            work.ValidationRequirements);
    }

    [Fact]
    public void FourArgumentWorkConstructor_RejectsNullValidationRequirementCollection()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-1")],
                        Array.Empty<ExecutableTaskDependency>(),
                        null!));

        Assert.Equal(
            "validationRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void FourArgumentWorkConstructor_RejectsNullValidationRequirementElement()
    {
        ValidationRequirement?[] requirements =
        [
            Requirement(
                "validation-1",
                "task-1",
                ValidationStrategy.Build),
            null
        ];

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-1")],
                        Array.Empty<ExecutableTaskDependency>(),
                        requirements!));

        Assert.Equal(
            "validationRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void Work_TakesDefensiveValidationRequirementSnapshot()
    {
        ValidationRequirement requirement =
            Requirement(
                "validation-1",
                "task-1",
                ValidationStrategy.Test);

        List<ValidationRequirement> requirements =
        [
            requirement
        ];

        ExecutableWork work =
            new(
                1,
                [Task("task-1")],
                Array.Empty<ExecutableTaskDependency>(),
                requirements);

        requirements.Clear();

        Assert.Single(
            work.ValidationRequirements);

        Assert.Same(
            requirement,
            work.ValidationRequirements[0]);
    }

    [Fact]
    public void Work_ExposesReadOnlyValidationRequirementCollection()
    {
        ValidationRequirement requirement =
            Requirement(
                "validation-1",
                "task-1",
                ValidationStrategy.Policy);

        ExecutableWork work =
            new(
                1,
                [Task("task-1")],
                Array.Empty<ExecutableTaskDependency>(),
                [requirement]);

        IList<ValidationRequirement> list =
            Assert.IsAssignableFrom<IList<ValidationRequirement>>(
                work.ValidationRequirements);

        Assert.True(
            list.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                list.Clear());

        Assert.Throws<NotSupportedException>(
            () =>
                list.Add(
                    Requirement(
                        "validation-2",
                        "task-1",
                        ValidationStrategy.Build)));
    }

    [Fact]
    public void Work_PreservesValidationRequirementOrderExactly()
    {
        ValidationRequirement first =
            Requirement(
                "validation-1",
                "task-1",
                ValidationStrategy.Build);

        ValidationRequirement second =
            Requirement(
                "validation-2",
                "task-1",
                ValidationStrategy.Test);

        ExecutableWork work =
            new(
                1,
                [Task("task-1")],
                Array.Empty<ExecutableTaskDependency>(),
                [
                    second,
                    first
                ]);

        Assert.Same(
            second,
            work.ValidationRequirements[0]);

        Assert.Same(
            first,
            work.ValidationRequirements[1]);
    }

    [Fact]
    public void Work_RejectsDuplicateValidationRequirementId()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [Task("task-1")],
                    Array.Empty<ExecutableTaskDependency>(),
                    [
                        Requirement(
                            "validation-1",
                            "task-1",
                            ValidationStrategy.Build),
                        Requirement(
                            "validation-1",
                            "task-1",
                            ValidationStrategy.Test)
                    ]));
    }

    [Fact]
    public void Work_AcceptsCaseDistinctValidationRequirementIds()
    {
        ExecutableWork work =
            new(
                1,
                [Task("task-1")],
                Array.Empty<ExecutableTaskDependency>(),
                [
                    Requirement(
                        "VALIDATION-1",
                        "task-1",
                        ValidationStrategy.Build),
                    Requirement(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Test)
                ]);

        Assert.Equal(
            2,
            work.ValidationRequirements.Count);
    }

    [Fact]
    public void Work_RejectsMissingValidationTaskEndpoint()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-1")],
                        Array.Empty<ExecutableTaskDependency>(),
                        [
                            Requirement(
                                "validation-1",
                                "missing-task",
                                ValidationStrategy.Build)
                        ]));

        Assert.Equal(
            "validationRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void ValidationTaskEndpoint_UsesOrdinalCaseSensitiveIdentity()
    {
        ExecutableWork work =
            new(
                1,
                [
                    Task("task-1"),
                    Task("TASK-1")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                [
                    Requirement(
                        "validation-1",
                        "TASK-1",
                        ValidationStrategy.Build)
                ]);

        Assert.Equal(
            "TASK-1",
            work.ValidationRequirements[0].TaskId);
    }

    [Fact]
    public void ValidationTaskEndpoint_DoesNotResolveThroughSourcePlanStepId()
    {
        ExecutableTask task =
            new(
                "task-1",
                "source-step-1",
                "instruction");

        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [task],
                    Array.Empty<ExecutableTaskDependency>(),
                    [
                        Requirement(
                            "validation-1",
                            "source-step-1",
                            ValidationStrategy.Build)
                    ]));
    }

    [Fact]
    public void Work_AcceptsMultipleRequirementsForSameTask()
    {
        ExecutableWork work =
            new(
                1,
                [Task("task-1")],
                Array.Empty<ExecutableTaskDependency>(),
                [
                    Requirement(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Build),
                    Requirement(
                        "validation-2",
                        "task-1",
                        ValidationStrategy.Test),
                    Requirement(
                        "validation-3",
                        "task-1",
                        ValidationStrategy.Policy)
                ]);

        Assert.Equal(
            3,
            work.ValidationRequirements.Count);
    }

    [Fact]
    public void Work_AcceptsSharedAcceptanceCriterionProvenance()
    {
        string sourceCriterion =
            "shared-criterion";

        ExecutableWork work =
            new(
                1,
                [Task("task-1")],
                Array.Empty<ExecutableTaskDependency>(),
                [
                    new ValidationRequirement(
                        "validation-1",
                        "task-1",
                        sourceCriterion,
                        ValidationStrategy.Build,
                        "build statement"),
                    new ValidationRequirement(
                        "validation-2",
                        "task-1",
                        sourceCriterion,
                        ValidationStrategy.Test,
                        "test statement")
                ]);

        Assert.All(
            work.ValidationRequirements,
            requirement_ =>
                Assert.Equal(
                    sourceCriterion,
                    requirement_.SourceAcceptanceCriterionId));
    }

    [Theory]
    [InlineData(ValidationStrategy.Build)]
    [InlineData(ValidationStrategy.Test)]
    [InlineData(ValidationStrategy.Policy)]
    public void Work_AcceptsEveryFrozenValidationStrategy(
        ValidationStrategy strategy_)
    {
        ExecutableWork work =
            new(
                1,
                [Task("task-1")],
                Array.Empty<ExecutableTaskDependency>(),
                [
                    Requirement(
                        "validation-1",
                        "task-1",
                        strategy_)
                ]);

        Assert.Equal(
            strategy_,
            work.ValidationRequirements[0].Strategy);
    }

    [Fact]
    public void ValidationRequirements_DoNotReorderTasksOrDependencies()
    {
        ExecutableTask taskA =
            Task("task-a");

        ExecutableTask taskB =
            Task("task-b");

        ExecutableTask taskC =
            Task("task-c");

        ExecutableTaskDependency firstDependency =
            new(
                "task-c",
                "task-a");

        ExecutableTaskDependency secondDependency =
            new(
                "task-c",
                "task-b");

        ExecutableWork work =
            new(
                1,
                [
                    taskC,
                    taskA,
                    taskB
                ],
                [
                    secondDependency,
                    firstDependency
                ],
                [
                    Requirement(
                        "validation-1",
                        "task-c",
                        ValidationStrategy.Test)
                ]);

        Assert.Same(
            taskC,
            work.Tasks[0]);

        Assert.Same(
            taskA,
            work.Tasks[1]);

        Assert.Same(
            taskB,
            work.Tasks[2]);

        Assert.Same(
            secondDependency,
            work.Dependencies[0]);

        Assert.Same(
            firstDependency,
            work.Dependencies[1]);
    }

    [Fact]
    public void Equality_IncludesValidationRequirementSequence()
    {
        ExecutableWork left =
            WorkWithRequirements(
                [
                    Requirement(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Build),
                    Requirement(
                        "validation-2",
                        "task-1",
                        ValidationStrategy.Test)
                ]);

        ExecutableWork right =
            WorkWithRequirements(
                [
                    Requirement(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Build),
                    Requirement(
                        "validation-2",
                        "task-1",
                        ValidationStrategy.Test)
                ]);

        Assert.Equal(
            left,
            right);

        Assert.Equal(
            left.GetHashCode(),
            right.GetHashCode());
    }

    [Fact]
    public void Equality_PreservesValidationRequirementOrder()
    {
        ValidationRequirement first =
            Requirement(
                "validation-1",
                "task-1",
                ValidationStrategy.Build);

        ValidationRequirement second =
            Requirement(
                "validation-2",
                "task-1",
                ValidationStrategy.Test);

        ExecutableWork left =
            WorkWithRequirements(
                [
                    first,
                    second
                ]);

        ExecutableWork right =
            WorkWithRequirements(
                [
                    second,
                    first
                ]);

        Assert.NotEqual(
            left,
            right);
    }

    private static ExecutableTask Task(
        string id_)
    {
        return new ExecutableTask(
            id_,
            $"source-{id_}",
            $"instruction-{id_}");
    }

    private static ValidationRequirement Requirement(
        string id_,
        string taskId_,
        ValidationStrategy strategy_)
    {
        return new ValidationRequirement(
            id_,
            taskId_,
            "source-criterion",
            strategy_,
            $"statement-{id_}");
    }

    private static ExecutableWork WorkWithRequirements(
        IReadOnlyList<ValidationRequirement> requirements_)
    {
        return new ExecutableWork(
            1,
            [Task("task-1")],
            Array.Empty<ExecutableTaskDependency>(),
            requirements_);
    }
}
namespace AiRepoKit.WorkflowExecution.Tests;

using System.Collections;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Xunit;

public sealed class WorkflowValidationEngineTests
{
    private static readonly ExecutionEnvironment _environment =
        new(
            AppContext.BaseDirectory);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidationExecutionResult_PreservesValues(bool passed_)
    {
        ValidationExecutionResult result =
            new(
                passed_,
                "objective evidence");

        Assert.Equal(passed_, result.Passed);
        Assert.Equal("objective evidence", result.Evidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ValidationExecutionResult_RejectsInvalidEvidence(string? evidence_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new ValidationExecutionResult(
                    true,
                    evidence_!));
    }

    [Fact]
    public void Constructor_RejectsNullCollection()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new WorkflowValidationEngine(
                    null!));
    }

    [Fact]
    public void Constructor_AllowsEmptyCollection()
    {
        Assert.NotNull(
            new WorkflowValidationEngine(
                Array.Empty<IValidationExecutor>()));
    }

    [Fact]
    public void Constructor_RejectsNullElement()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowValidationEngine(
                    new IValidationExecutor[]
                    {
                        null!
                    }));
    }

    [Theory]
    [InlineData((ValidationStrategy) 0)]
    [InlineData((ValidationStrategy) 4)]
    [InlineData((ValidationStrategy)(-1))]
    [InlineData((ValidationStrategy) 100)]
    [InlineData((ValidationStrategy) 2147483647)]
    public void Constructor_RejectsUndefinedStrategy(ValidationStrategy strategy_)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new WorkflowValidationEngine(
                    new[]
                    {
                        new FakeValidator(strategy_)
                    }));
    }

    [Theory]
    [InlineData(ValidationStrategy.Build)]
    [InlineData(ValidationStrategy.Test)]
    [InlineData(ValidationStrategy.Policy)]
    public void Constructor_RejectsDuplicateStrategy(ValidationStrategy strategy_)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowValidationEngine(
                    new IValidationExecutor[]
                    {
                        new FakeValidator(strategy_),
                        new FakeValidator(strategy_)
                    }));
    }

    [Fact]
    public void Constructor_EnumeratesCollectionExactlyOnce()
    {
        SingleEnumerationEnumerable validators =
            new(
                new FakeValidator(
                    ValidationStrategy.Build));

        _ =
            new WorkflowValidationEngine(
                validators);

        Assert.Equal(
            1,
            validators.EnumerationCount);
    }

    [Fact]
    public async Task Constructor_SnapshotsStrategyAtConstruction()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        validator.CurrentStrategy =
            ValidationStrategy.Test;

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(
            1,
            validator.InvocationCount);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Step(result.State, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_RejectsNullState()
    {
        ExecutableWork work = Work();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () =>
                EmptyEngine().ValidateAsync(
                    null!,
                    work,
                    _environment,
                    "task-a"));
    }

    [Fact]
    public async Task ValidateAsync_RejectsNullWork()
    {
        ExecutableWork work = Work();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () =>
                EmptyEngine().ValidateAsync(
                    AwaitingState(work, "task-a"),
                    null!,
                    _environment,
                    "task-a"));
    }

    [Fact]
    public async Task ValidateAsync_RejectsNullEnvironment()
    {
        ExecutableWork work = Work();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () =>
                EmptyEngine().ValidateAsync(
                    AwaitingState(work, "task-a"),
                    work,
                    null!,
                    "task-a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task ValidateAsync_RejectsInvalidTaskId(string? taskId_)
    {
        ExecutableWork work = Work();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () =>
                EmptyEngine().ValidateAsync(
                    AwaitingState(work, "task-a"),
                    work,
                    _environment,
                    taskId_!));
    }

    [Theory]
    [InlineData(WorkflowStatus.Created)]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Cancelled)]
    public async Task ValidateAsync_RequiresRunningWorkflow(WorkflowStatus status_)
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.ValidateAsync(
                    StateWithWorkflowStatus(
                        work,
                        status_),
                    work,
                    _environment,
                    "task-a"));

        Assert.Equal(0, validator.InvocationCount);
    }

    [Theory]
    [InlineData(WorkflowStepStatus.Pending)]
    [InlineData(WorkflowStepStatus.Running)]
    [InlineData(WorkflowStepStatus.Blocked)]
    [InlineData(WorkflowStepStatus.Failed)]
    [InlineData(WorkflowStepStatus.Completed)]
    [InlineData(WorkflowStepStatus.Cancelled)]
    public async Task ValidateAsync_RequiresAwaitingValidationStep(WorkflowStepStatus status_)
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.ValidateAsync(
                    StateWithStepStatus(
                        work,
                        status_),
                    work,
                    _environment,
                    "task-a"));

        Assert.Equal(0, validator.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_RequiresMatchingRevision()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork stateWork =
            Work(
                1,
                ["task-a"]);

        ExecutableWork validationWork =
            Work(
                2,
                ["task-a"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.ValidateAsync(
                    AwaitingState(
                        stateWork,
                        "task-a"),
                    validationWork,
                    _environment,
                    "task-a"));

        Assert.Equal(0, validator.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_RejectsTaskMissingFromWorkflow()
    {
        ExecutableWork stateWork =
            Work(
                1,
                ["task-a"]);

        ExecutableWork validationWork =
            Work(
                1,
                ["task-b"]);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                EmptyEngine().ValidateAsync(
                    AwaitingState(
                        stateWork,
                        "task-a"),
                    validationWork,
                    _environment,
                    "task-b"));
    }

    [Fact]
    public async Task ValidateAsync_RejectsTaskMissingFromExecutableWork()
    {
        ExecutableWork stateWork =
            Work(
                1,
                ["task-a"]);

        ExecutableWork validationWork =
            Work(
                1,
                ["task-b"]);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                EmptyEngine().ValidateAsync(
                    AwaitingState(
                        stateWork,
                        "task-a"),
                    validationWork,
                    _environment,
                    "task-a"));
    }

    [Fact]
    public async Task ValidateAsync_TaskComparisonIsOrdinal()
    {
        ExecutableWork work = Work();

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                EmptyEngine().ValidateAsync(
                    AwaitingState(
                        work,
                        "task-a"),
                    work,
                    _environment,
                    "TASK-A"));
    }

    [Fact]
    public async Task ValidateAsync_ZeroRequirementsCompletesWithEmptyRegistry()
    {
        ExecutableWork work = Work();

        WorkflowState input =
            AwaitingState(
                work,
                "task-a");

        WorkflowValidationResult result =
            await EmptyEngine().ValidateAsync(
                input,
                work,
                _environment,
                "task-a");

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Step(result.State, "task-a").Status);

        Assert.Empty(result.Evidence);

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            Step(input, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_FiltersRequirementsByTaskOrdinally()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                1,
                ["task-a", "task-b"],
                Requirement(
                    "validation-b",
                    ValidationStrategy.Build,
                    "task-b"));

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(0, validator.InvocationCount);
        Assert.Empty(result.Evidence);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Step(result.State, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_OrdersRequirementsByIdOrdinal()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ValidationRequirement z =
            Requirement(
                "z",
                ValidationStrategy.Build);

        ValidationRequirement a =
            Requirement(
                "A",
                ValidationStrategy.Build);

        ValidationRequirement m =
            Requirement(
                "m",
                ValidationStrategy.Build);

        ExecutableWork work =
            Work(
                z,
                m,
                a);

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(
            ["A", "m", "z"],
            validator.Requirements
                .Select(
                    requirement_ =>
                        requirement_.Id)
                .ToArray());

        Assert.Equal(
            ["A", "m", "z"],
            result.Evidence
                .Select(
                    evidence_ =>
                        evidence_.Requirement.Id)
                .ToArray());
    }

    [Theory]
    [InlineData(ValidationStrategy.Build)]
    [InlineData(ValidationStrategy.Test)]
    [InlineData(ValidationStrategy.Policy)]
    public async Task ValidateAsync_DispatchesExactStrategy(ValidationStrategy strategy_)
    {
        FakeValidator build =
            new(
                ValidationStrategy.Build);

        FakeValidator test =
            new(
                ValidationStrategy.Test);

        FakeValidator policy =
            new(
                ValidationStrategy.Policy);

        FakeValidator[] validators =
        [
            build,
            test,
            policy
        ];

        WorkflowValidationEngine engine =
            new(
                validators);

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    strategy_));

        _ =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        foreach (FakeValidator validator in validators)
        {
            Assert.Equal(
                validator.CurrentStrategy == strategy_
                    ? 1
                    : 0,
                validator.InvocationCount);
        }
    }

    [Theory]
    [InlineData(ValidationStrategy.Build)]
    [InlineData(ValidationStrategy.Test)]
    [InlineData(ValidationStrategy.Policy)]
    public async Task ValidateAsync_MissingValidatorRejectsBeforeInvocation(ValidationStrategy strategy_)
    {
        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    strategy_));

        WorkflowState input =
            AwaitingState(
                work,
                "task-a");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                EmptyEngine().ValidateAsync(
                    input,
                    work,
                    _environment,
                    "task-a"));

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            Step(input, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_PreflightsAllStrategiesBeforeFirstInvocation()
    {
        FakeValidator build =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    build
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "a",
                    ValidationStrategy.Build),
                Requirement(
                    "b",
                    ValidationStrategy.Test));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.ValidateAsync(
                    AwaitingState(
                        work,
                        "task-a"),
                    work,
                    _environment,
                    "task-a"));

        Assert.Equal(0, build.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_MapsExactRequirementEnvironmentAndToken()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ValidationRequirement requirement =
            Requirement(
                "validation-a",
                ValidationStrategy.Build);

        ExecutableWork work =
            Work(
                requirement);

        using CancellationTokenSource source =
            new();

        _ =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a",
                source.Token);

        Assert.Same(
            requirement,
            Assert.Single(
                validator.Requirements));

        Assert.Same(
            _environment,
            validator.LastEnvironment);

        Assert.Equal(
            source.Token,
            validator.LastCancellationToken);
    }

    [Fact]
    public async Task ValidateAsync_InvokesValidatorsSequentially()
    {
        int active = 0;
        int maximumActive = 0;

        FakeValidator validator =
            new(
                ValidationStrategy.Build,
                async (_, _, _) =>
                {
                    active++;
                    maximumActive = Math.Max(maximumActive, active);

                    await Task.Yield();

                    active--;

                    return new ValidationExecutionResult(
                        true,
                        "ok");
                });

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement("a", ValidationStrategy.Build),
                Requirement("b", ValidationStrategy.Build),
                Requirement("c", ValidationStrategy.Build));

        _ =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(1, maximumActive);
        Assert.Equal(3, validator.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_InvokesEachRequirementExactlyOnce()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement("a", ValidationStrategy.Build),
                Requirement("b", ValidationStrategy.Build),
                Requirement("c", ValidationStrategy.Build),
                Requirement("d", ValidationStrategy.Build));

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(4, validator.InvocationCount);
        Assert.Equal(4, result.Evidence.Count);
    }

    [Fact]
    public async Task ValidateAsync_AllPassTransitionsToCompleted()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement("a", ValidationStrategy.Build),
                Requirement("b", ValidationStrategy.Build));

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Step(result.State, "task-a").Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ValidateAsync_FailedResultTransitionsToFailedWithoutShortCircuit(int failedInvocation_)
    {
        int invocation = 0;

        FakeValidator validator =
            new(
                ValidationStrategy.Build,
                (_, _, _) =>
                {
                    invocation++;

                    return Task.FromResult(
                        new ValidationExecutionResult(
                            invocation != failedInvocation_,
                            $"evidence-{invocation}"));
                });

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement("a", ValidationStrategy.Build),
                Requirement("b", ValidationStrategy.Build),
                Requirement("c", ValidationStrategy.Build));

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(3, validator.InvocationCount);
        Assert.Equal(3, result.Evidence.Count);

        Assert.Equal(
            WorkflowStepStatus.Failed,
            Step(result.State, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_EvidenceContainsExactRequirementAndResultObjects()
    {
        ValidationExecutionResult exactResult =
            new(
                true,
                "exact");

        FakeValidator validator =
            new(
                ValidationStrategy.Build,
                (_, _, _) =>
                    Task.FromResult(
                        exactResult));

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ValidationRequirement requirement =
            Requirement(
                "validation-a",
                ValidationStrategy.Build);

        ExecutableWork work =
            Work(
                requirement);

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        WorkflowValidationEvidence evidence =
            Assert.Single(
                result.Evidence);

        Assert.Same(
            requirement,
            evidence.Requirement);

        Assert.Same(
            exactResult,
            evidence.ValidationResult);
    }

    [Fact]
    public async Task ValidateAsync_EvidenceCollectionIsReadOnly()
    {
        WorkflowValidationEngine engine =
            new(
                new IValidationExecutor[]
                {
                    new FakeValidator(
                        ValidationStrategy.Build)
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        ICollection<WorkflowValidationEvidence> collection =
            Assert.IsAssignableFrom<ICollection<WorkflowValidationEvidence>>(
                result.Evidence);

        Assert.True(
            collection.IsReadOnly);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WorkflowStatusRemainsRunning(bool passed_)
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build,
                (_, _, _) =>
                    Task.FromResult(
                        new ValidationExecutionResult(
                            passed_,
                            "objective")));

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        Assert.Equal(
            WorkflowStatus.Running,
            result.State.Status);

        Assert.Equal(
            passed_
                ? WorkflowStepStatus.Completed
                : WorkflowStepStatus.Failed,
            Step(result.State, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotMutateInputState()
    {
        WorkflowValidationEngine engine =
            new(
                new IValidationExecutor[]
                {
                    new FakeValidator(
                        ValidationStrategy.Build)
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        WorkflowState input =
            AwaitingState(
                work,
                "task-a");

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                input,
                work,
                _environment,
                "task-a");

        Assert.NotSame(input, result.State);

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            Step(input, "task-a").Status);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Step(result.State, "task-a").Status);
    }

    [Theory]
    [InlineData(ValidationStrategy.Build)]
    [InlineData(ValidationStrategy.Test)]
    [InlineData(ValidationStrategy.Policy)]
    public async Task ValidateAsync_RejectsNullValidationResultWithoutTransition(ValidationStrategy strategy_)
    {
        FakeValidator validator =
            new(
                strategy_,
                (_, _, _) =>
                    Task.FromResult<ValidationExecutionResult>(
                        null!));

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    strategy_));

        WorkflowState input =
            AwaitingState(
                work,
                "task-a");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.ValidateAsync(
                    input,
                    work,
                    _environment,
                    "task-a"));

        Assert.Equal(1, validator.InvocationCount);

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            Step(input, "task-a").Status);
    }

    [Theory]
    [InlineData(ValidationStrategy.Build)]
    [InlineData(ValidationStrategy.Test)]
    [InlineData(ValidationStrategy.Policy)]
    public async Task ValidateAsync_PropagatesValidatorExceptionWithoutTransition(ValidationStrategy strategy_)
    {
        InvalidOperationException expected =
            new(
                "validator-failure");

        FakeValidator validator =
            new(
                strategy_,
                (_, _, _) =>
                    Task.FromException<ValidationExecutionResult>(
                        expected));

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    strategy_));

        WorkflowState input =
            AwaitingState(
                work,
                "task-a");

        InvalidOperationException actual =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    engine.ValidateAsync(
                        input,
                        work,
                        _environment,
                        "task-a"));

        Assert.Same(expected, actual);
        Assert.Equal(1, validator.InvocationCount);

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            Step(input, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_PreCancelledCallerTokenPreventsInvocation()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build);

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        WorkflowState input =
            AwaitingState(
                work,
                "task-a");

        using CancellationTokenSource source =
            new();

        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                engine.ValidateAsync(
                    input,
                    work,
                    _environment,
                    "task-a",
                    source.Token));

        Assert.Equal(0, validator.InvocationCount);

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            Step(input, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_CancellationStopsRemainingInvocationsWithoutTransition()
    {
        using CancellationTokenSource source =
            new();

        int calls = 0;

        FakeValidator validator =
            new(
                ValidationStrategy.Build,
                (_, _, _) =>
                {
                    calls++;

                    if (calls == 1)
                    {
                        source.Cancel();
                    }

                    return Task.FromResult(
                        new ValidationExecutionResult(
                            true,
                            $"result-{calls}"));
                });

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement("a", ValidationStrategy.Build),
                Requirement("b", ValidationStrategy.Build));

        WorkflowState input =
            AwaitingState(
                work,
                "task-a");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                engine.ValidateAsync(
                    input,
                    work,
                    _environment,
                    "task-a",
                    source.Token));

        Assert.Equal(1, validator.InvocationCount);

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            Step(input, "task-a").Status);
    }

    [Fact]
    public async Task ValidateAsync_UnrelatedOperationCanceledExceptionPropagates()
    {
        OperationCanceledException expected =
            new(
                "validator-cancelled");

        FakeValidator validator =
            new(
                ValidationStrategy.Build,
                (_, _, _) =>
                    Task.FromException<ValidationExecutionResult>(
                        expected));

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        OperationCanceledException actual =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () =>
                    engine.ValidateAsync(
                        AwaitingState(
                            work,
                            "task-a"),
                        work,
                        _environment,
                        "task-a"));

        Assert.Same(expected, actual);
        Assert.Equal(1, validator.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotRetryValidatorException()
    {
        FakeValidator validator =
            new(
                ValidationStrategy.Build,
                (_, _, _) =>
                    Task.FromException<ValidationExecutionResult>(
                        new InvalidOperationException(
                            "failure")));

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.ValidateAsync(
                    AwaitingState(
                        work,
                        "task-a"),
                    work,
                    _environment,
                    "task-a"));

        Assert.Equal(1, validator.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotFallbackToDifferentStrategy()
    {
        FakeValidator build =
            new(
                ValidationStrategy.Build,
                (_, _, _) =>
                    Task.FromException<ValidationExecutionResult>(
                        new InvalidOperationException(
                            "build-failure")));

        FakeValidator test =
            new(
                ValidationStrategy.Test);

        FakeValidator policy =
            new(
                ValidationStrategy.Policy);

        WorkflowValidationEngine engine =
            new(
                new IValidationExecutor[]
                {
                    build,
                    test,
                    policy
                });

        ExecutableWork work =
            Work(
                Requirement(
                    "validation-a",
                    ValidationStrategy.Build));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                engine.ValidateAsync(
                    AwaitingState(
                        work,
                        "task-a"),
                    work,
                    _environment,
                    "task-a"));

        Assert.Equal(1, build.InvocationCount);
        Assert.Equal(0, test.InvocationCount);
        Assert.Equal(0, policy.InvocationCount);
    }

    [Fact]
    public async Task ValidateAsync_FailedResultIsReturnedAsObjectiveEvidence()
    {
        ValidationExecutionResult failed =
            new(
                false,
                "tests failed");

        FakeValidator validator =
            new(
                ValidationStrategy.Test,
                (_, _, _) =>
                    Task.FromResult(
                        failed));

        WorkflowValidationEngine engine =
            new(
                new[]
                {
                    validator
                });

        ValidationRequirement requirement =
            Requirement(
                "validation-a",
                ValidationStrategy.Test);

        ExecutableWork work =
            Work(
                requirement);

        WorkflowValidationResult result =
            await engine.ValidateAsync(
                AwaitingState(
                    work,
                    "task-a"),
                work,
                _environment,
                "task-a");

        WorkflowValidationEvidence evidence =
            Assert.Single(
                result.Evidence);

        Assert.Same(failed, evidence.ValidationResult);
        Assert.False(evidence.ValidationResult.Passed);
        Assert.Equal("tests failed", evidence.ValidationResult.Evidence);

        Assert.Equal(
            WorkflowStepStatus.Failed,
            Step(result.State, "task-a").Status);
    }

    private static WorkflowValidationEngine EmptyEngine()
    {
        return new WorkflowValidationEngine(
            Array.Empty<IValidationExecutor>());
    }

    private static ValidationRequirement Requirement(
        string id_,
        ValidationStrategy strategy_,
        string taskId_ = "task-a")
    {
        return new ValidationRequirement(
            id_,
            taskId_,
            $"acceptance-{id_}",
            strategy_,
            $"Validate {id_}");
    }

    private static ExecutableWork Work(
        params ValidationRequirement[] requirements_)
    {
        return Work(
            1,
            ["task-a"],
            requirements_);
    }

    private static ExecutableWork Work(
        int revision_,
        string[] taskIds_,
        params ValidationRequirement[] requirements_)
    {
        ExecutableTask[] tasks =
            taskIds_
                .Select(
                    taskId_ =>
                        new ExecutableTask(
                            taskId_,
                            $"plan-{taskId_}",
                            $"Execute {taskId_}."))
                .ToArray();

        return new ExecutableWork(
            revision_,
            tasks,
            Array.Empty<ExecutableTaskDependency>(),
            requirements_);
    }

    private static WorkflowState AwaitingState(
        ExecutableWork work_,
        string taskId_)
    {
        WorkflowState state =
            WorkflowStateMachine.Create(
                work_);

        state =
            WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Running);

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                taskId_,
                WorkflowStepStatus.Running);

        return WorkflowStateMachine.TransitionStep(
            state,
            taskId_,
            WorkflowStepStatus.AwaitingValidation);
    }

    private static WorkflowState StateWithStepStatus(
        ExecutableWork work_,
        WorkflowStepStatus status_)
    {
        WorkflowState state =
            WorkflowStateMachine.Create(
                work_);

        state =
            WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Running);

        if (status_ == WorkflowStepStatus.Pending)
        {
            return state;
        }

        if (status_ == WorkflowStepStatus.Cancelled)
        {
            return WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Cancelled);
        }

        state =
            WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Running);

        if (status_ == WorkflowStepStatus.Running)
        {
            return state;
        }

        if (status_ == WorkflowStepStatus.AwaitingValidation)
        {
            return WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.AwaitingValidation);
        }

        if (status_ == WorkflowStepStatus.Blocked)
        {
            return WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Blocked);
        }

        if (status_ == WorkflowStepStatus.Failed)
        {
            return WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Failed);
        }

        if (status_ == WorkflowStepStatus.Completed)
        {
            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.AwaitingValidation);

            return WorkflowStateMachine.TransitionStep(
                state,
                "task-a",
                WorkflowStepStatus.Completed);
        }

        throw new ArgumentOutOfRangeException(
            nameof(status_),
            status_,
            null);
    }

    private static WorkflowState StateWithWorkflowStatus(
        ExecutableWork work_,
        WorkflowStatus status_)
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                work_);

        if (status_ == WorkflowStatus.Created)
        {
            return created;
        }

        WorkflowState state =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        if (status_ == WorkflowStatus.Cancelled)
        {
            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Cancelled);
        }

        if (status_ == WorkflowStatus.Completed)
        {
            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Running);

            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.AwaitingValidation);

            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Completed);

            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Completed);
        }

        if (status_ == WorkflowStatus.Failed)
        {
            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Running);

            state =
                WorkflowStateMachine.TransitionStep(
                    state,
                    "task-a",
                    WorkflowStepStatus.Failed);

            return WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Failed);
        }

        throw new ArgumentOutOfRangeException(
            nameof(status_),
            status_,
            null);
    }

    private static WorkflowStepState Step(
        WorkflowState state_,
        string taskId_)
    {
        return Assert.Single(
            state_.Steps.Where(
                step_ =>
                    string.Equals(
                        step_.TaskId,
                        taskId_,
                        StringComparison.Ordinal)));
    }

    private sealed class FakeValidator :
        IValidationExecutor
    {
        private readonly Func<
            ValidationRequirement,
            ExecutionEnvironment,
            CancellationToken,
            Task<ValidationExecutionResult>> _handler;

        public ValidationStrategy CurrentStrategy
        {
            get;
            set;
        }

        public ValidationStrategy Strategy
        {
            get
            {
                return this.CurrentStrategy;
            }
        }

        public int InvocationCount
        {
            get;
            private set;
        }

        public List<ValidationRequirement> Requirements
        {
            get;
        } =
            new();

        public ExecutionEnvironment? LastEnvironment
        {
            get;
            private set;
        }

        public CancellationToken LastCancellationToken
        {
            get;
            private set;
        }

        public FakeValidator(
            ValidationStrategy strategy_,
            Func<
                ValidationRequirement,
                ExecutionEnvironment,
                CancellationToken,
                Task<ValidationExecutionResult>>? handler_ = null)
        {
            this.CurrentStrategy =
                strategy_;

            this._handler =
                handler_ ??
                (static (_, _, _) =>
                    Task.FromResult(
                        new ValidationExecutionResult(
                            true,
                            "ok")));
        }

        public async Task<ValidationExecutionResult> ValidateAsync(
            ValidationRequirement requirement_,
            ExecutionEnvironment environment_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;

            this.Requirements.Add(
                requirement_);

            this.LastEnvironment =
                environment_;

            this.LastCancellationToken =
                cancellationToken_;

            return await this
                ._handler(
                    requirement_,
                    environment_,
                    cancellationToken_)
                .ConfigureAwait(false);
        }
    }

    private sealed class SingleEnumerationEnumerable :
        IEnumerable<IValidationExecutor>
    {
        private readonly IValidationExecutor[] _items;

        public int EnumerationCount
        {
            get;
            private set;
        }

        public SingleEnumerationEnumerable(
            params IValidationExecutor[] items_)
        {
            this._items =
                items_;
        }

        public IEnumerator<IValidationExecutor> GetEnumerator()
        {
            this.EnumerationCount++;

            if (this.EnumerationCount > 1)
            {
                throw new InvalidOperationException(
                    "Validator collection was enumerated more than once.");
            }

            return (
                (IEnumerable<IValidationExecutor>) this._items
            ).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}

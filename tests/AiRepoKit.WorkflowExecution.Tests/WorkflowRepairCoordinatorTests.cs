namespace AiRepoKit.WorkflowExecution.Tests;

using System.Collections;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Xunit;

public sealed class WorkflowRepairCoordinatorTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    [InlineData(1, 6)]
    [InlineData(1, 7)]
    [InlineData(1, 8)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    [InlineData(2, 5)]
    [InlineData(2, 6)]
    [InlineData(2, 7)]
    [InlineData(2, 8)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    [InlineData(3, 5)]
    [InlineData(3, 6)]
    [InlineData(3, 7)]
    [InlineData(3, 8)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    [InlineData(4, 3)]
    [InlineData(4, 4)]
    [InlineData(4, 5)]
    [InlineData(4, 6)]
    [InlineData(4, 7)]
    [InlineData(4, 8)]
    [InlineData(5, 1)]
    [InlineData(5, 2)]
    [InlineData(5, 3)]
    [InlineData(5, 4)]
    [InlineData(5, 5)]
    [InlineData(5, 6)]
    [InlineData(5, 7)]
    [InlineData(5, 8)]
    [InlineData(6, 1)]
    [InlineData(6, 2)]
    [InlineData(6, 3)]
    [InlineData(6, 4)]
    [InlineData(6, 5)]
    [InlineData(6, 6)]
    [InlineData(6, 7)]
    [InlineData(6, 8)]
    [InlineData(7, 1)]
    [InlineData(7, 2)]
    [InlineData(7, 3)]
    [InlineData(7, 4)]
    [InlineData(7, 5)]
    [InlineData(7, 6)]
    [InlineData(7, 7)]
    [InlineData(7, 8)]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(8, 3)]
    [InlineData(8, 4)]
    [InlineData(8, 5)]
    [InlineData(8, 6)]
    [InlineData(8, 7)]
    [InlineData(8, 8)]
    [InlineData(9, 1)]
    [InlineData(9, 2)]
    [InlineData(9, 3)]
    [InlineData(9, 4)]
    [InlineData(9, 5)]
    [InlineData(9, 6)]
    [InlineData(9, 7)]
    [InlineData(9, 8)]
    [InlineData(10, 1)]
    [InlineData(10, 2)]
    [InlineData(10, 3)]
    [InlineData(10, 4)]
    [InlineData(10, 5)]
    [InlineData(10, 6)]
    [InlineData(10, 7)]
    [InlineData(10, 8)]
    public void Policy_ValidValuesArePreserved(
        int maxAttemptsPerProvider_,
        int maxTotalAttempts_)
    {
        WorkflowRepairPolicy policy =
            new(
                maxAttemptsPerProvider_,
                maxTotalAttempts_);

        Assert.Equal(
            maxAttemptsPerProvider_,
            policy.MaxAttemptsPerProvider);

        Assert.Equal(
            maxTotalAttempts_,
            policy.MaxTotalAttempts);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Policy_RejectsInvalidMaxAttemptsPerProvider(
        int value_)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new WorkflowRepairPolicy(
                        value_,
                        1));

        Assert.Equal(
            "maxAttemptsPerProvider_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Policy_RejectsInvalidMaxTotalAttempts(
        int value_)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new WorkflowRepairPolicy(
                        1,
                        value_));

        Assert.Equal(
            "maxTotalAttempts_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullExecutorCollection()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new WorkflowRepairCoordinator(
                    null!,
                    Array.Empty<IValidationExecutor>(),
                    Policy()));
    }

    [Fact]
    public void Constructor_RejectsEmptyExecutorCollection()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowRepairCoordinator(
                    Array.Empty<IAgentExecutor>(),
                    Array.Empty<IValidationExecutor>(),
                    Policy()));
    }

    [Fact]
    public void Constructor_RejectsNullExecutorElement()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowRepairCoordinator(
                    [
                        Agent("a"),
                        null!
                    ],
                    Array.Empty<IValidationExecutor>(),
                    Policy()));
    }

    [Fact]
    public void Constructor_RejectsNullProviderId()
    {
        FakeAgent agent =
            new(
                null,
                AgentCapabilitySet.Empty);

        Assert.Throws<InvalidOperationException>(
            () =>
                new WorkflowRepairCoordinator(
                    [agent],
                    Array.Empty<IValidationExecutor>(),
                    Policy()));
    }

    [Fact]
    public void Constructor_RejectsNullCapabilities()
    {
        FakeAgent agent =
            new(
                new AgentProviderId("a"),
                null);

        Assert.Throws<InvalidOperationException>(
            () =>
                new WorkflowRepairCoordinator(
                    [agent],
                    Array.Empty<IValidationExecutor>(),
                    Policy()));
    }

    [Fact]
    public void Constructor_RejectsDuplicateProviderIdsOrdinally()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowRepairCoordinator(
                    [
                        Agent("a"),
                        Agent("a")
                    ],
                    Array.Empty<IValidationExecutor>(),
                    Policy()));
    }

    [Fact]
    public void Constructor_EnumeratesAgentCollectionOnce()
    {
        SingleEnumerationEnumerable<IAgentExecutor> executors =
            new(
                Agent("a"),
                Agent("b"));

        _ =
            new WorkflowRepairCoordinator(
                executors,
                Array.Empty<IValidationExecutor>(),
                Policy());

        Assert.Equal(
            1,
            executors.EnumerationCount);
    }

    [Fact]
    public void Constructor_RejectsNullValidatorCollection()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new WorkflowRepairCoordinator(
                    [Agent("a")],
                    null!,
                    Policy()));
    }

    [Fact]
    public void Constructor_AllowsEmptyValidatorCollection()
    {
        _ =
            new WorkflowRepairCoordinator(
                [Agent("a")],
                Array.Empty<IValidationExecutor>(),
                Policy());
    }

    [Fact]
    public void Constructor_RejectsNullValidatorElement()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowRepairCoordinator(
                    [Agent("a")],
                    [
                        null!
                    ],
                    Policy()));
    }

    [Fact]
    public void Constructor_RejectsUndefinedValidatorStrategy()
    {
        FakeValidator validator =
            new(
                (ValidationStrategy)(-1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new WorkflowRepairCoordinator(
                    [Agent("a")],
                    [validator],
                    Policy()));
    }

    [Fact]
    public void Constructor_RejectsDuplicateValidatorStrategy()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new WorkflowRepairCoordinator(
                    [Agent("a")],
                    [
                        Validator(ValidationStrategy.Build),
                        Validator(ValidationStrategy.Build)
                    ],
                    Policy()));
    }

    [Fact]
    public void Constructor_EnumeratesValidatorCollectionOnce()
    {
        SingleEnumerationEnumerable<IValidationExecutor> validators =
            new(
                Validator(ValidationStrategy.Build),
                Validator(ValidationStrategy.Test));

        _ =
            new WorkflowRepairCoordinator(
                [Agent("a")],
                validators,
                Policy());

        Assert.Equal(
            1,
            validators.EnumerationCount);
    }

    [Fact]
    public void Constructor_RejectsNullPolicy()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new WorkflowRepairCoordinator(
                    [Agent("a")],
                    Array.Empty<IValidationExecutor>(),
                    null!));
    }

    [Fact]
    public async Task Constructor_SnapshotsProviderAndCapabilities()
    {
        FakeAgent agent =
            Agent(
                "a",
                Caps("repo.write"));

        WorkflowRepairCoordinator coordinator =
            new(
                [agent],
                Array.Empty<IValidationExecutor>(),
                Policy());

        agent.CurrentProviderId =
            new AgentProviderId("z");

        agent.CurrentCapabilities =
            AgentCapabilitySet.Empty;

        ExecutableWork work =
            Work(
                agentCapabilities_: Caps("repo.write"));

        WorkflowRepairResult result =
            await coordinator.ExecuteAsync(
                Running(work),
                work,
                Envelope(work));

        Assert.Equal(
            "a",
            Assert.Single(result.Attempts).ProviderId.Value);

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Constructor_SnapshotsValidatorStrategy()
    {
        ValidationExecutionResult expected =
            new(
                true,
                "ok");

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    _,
                    _,
                    _,
                    _) =>
                        Task.FromResult(expected));

        WorkflowRepairCoordinator coordinator =
            new(
                [Agent("a")],
                [validator],
                Policy());

        validator.CurrentStrategy =
            ValidationStrategy.Test;

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        WorkflowRepairResult result =
            await coordinator.ExecuteAsync(
                Running(work),
                work,
                Envelope(work));

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));

        Assert.Equal(
            1,
            validator.InvocationCount);
    }

    [Fact]
    public async Task Execute_RejectsNullState()
    {
        ExecutableWork work =
            Work();

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [Agent("a")]);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () =>
                coordinator.ExecuteAsync(
                    null!,
                    work,
                    Envelope(work)));
    }

    [Fact]
    public async Task Execute_RejectsNullWork()
    {
        ExecutableWork work =
            Work();

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [Agent("a")]);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(work),
                    null!,
                    Envelope(work)));
    }

    [Fact]
    public async Task Execute_RejectsNullEnvelope()
    {
        ExecutableWork work =
            Work();

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [Agent("a")]);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(work),
                    work,
                    null!));
    }

    [Fact]
    public async Task Execute_RequiresRunningWorkflow()
    {
        ExecutableWork work =
            Work();

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                coordinator.ExecuteAsync(
                    WorkflowStateMachine.Create(work),
                    work,
                    Envelope(work)));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_RequiresRunningTargetStep()
    {
        ExecutableWork work =
            Work();

        WorkflowState state =
            WorkflowStateMachine.TransitionWorkflow(
                WorkflowStateMachine.Create(work),
                WorkflowStatus.Running);

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                coordinator.ExecuteAsync(
                    state,
                    work,
                    Envelope(work)));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_RequiresMatchingRevision()
    {
        ExecutableWork stateWork =
            Work(
                revision_: 1);

        ExecutableWork work =
            Work(
                revision_: 2);

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(stateWork),
                    work,
                    Envelope(work)));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_RequiresTaskInWorkflowState()
    {
        ExecutableWork stateWork =
            Work(
                taskId_: "other");

        ExecutableWork work =
            Work();

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(stateWork),
                    work,
                    Envelope(work)));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_RequiresTaskInExecutableWork()
    {
        ExecutableWork stateWork =
            Work();

        ExecutableWork otherWork =
            Work(
                taskId_: "other");

        ExecutionEnvelope envelope =
            Envelope(
                stateWork);

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(stateWork),
                    otherWork,
                    envelope));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_RejectsNonNullInputSession()
    {
        ExecutableWork work =
            Work();

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(
                        work,
                        session_:
                            new AgentSessionReference(
                                "session"))));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_PrefightsMissingValidatorBeforeAgentInvocation()
    {
        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work)));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_PrefightsNoEligibleAgent()
    {
        ExecutableWork work =
            Work(
                agentCapabilities_: Caps("repo.write"));

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work)));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_HonorsPreCancelledCallerToken()
    {
        ExecutableWork work =
            Work();

        FakeAgent agent =
            Agent("a");

        WorkflowRepairCoordinator coordinator =
            Coordinator(
                [agent]);

        using CancellationTokenSource source =
            new();

        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                coordinator.ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work),
                    source.Token));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_SelectsEligibleProviderByOrdinalId()
    {
        FakeAgent b =
            Agent("b");

        FakeAgent a =
            Agent("a");

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [b, a])
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            "a",
            Assert.Single(result.Attempts).ProviderId.Value);

        Assert.Equal(
            1,
            a.InvocationCount);

        Assert.Equal(
            0,
            b.InvocationCount);
    }

    [Fact]
    public async Task Execute_FiltersByAgentRequiredCapabilities()
    {
        FakeAgent a =
            Agent("a");

        FakeAgent b =
            Agent(
                "b",
                Caps("repo.write"));

        ExecutableWork work =
            Work(
                agentCapabilities_: Caps("repo.write"));

        WorkflowRepairResult result =
            await Coordinator(
                    [a, b])
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            "b",
            Assert.Single(result.Attempts).ProviderId.Value);

        Assert.Equal(
            0,
            a.InvocationCount);

        Assert.Equal(
            1,
            b.InvocationCount);
    }

    [Fact]
    public async Task Execute_DoesNotUseModelCapabilitiesForProviderSelection()
    {
        FakeAgent a =
            Agent("a");

        ExecutableWork work =
            Work(
                modelCapabilities_: Caps("model.special"));

        ExecutionEnvelope envelope =
            Envelope(work);

        Assert.Equal(
            1,
            envelope.ModelRequiredCapabilities.Count);

        Assert.Equal(
            0,
            envelope.AgentRequiredCapabilities.Count);

        WorkflowRepairResult result =
            await Coordinator(
                    [a])
                .ExecuteAsync(
                    Running(work),
                    work,
                    envelope);

        Assert.Equal(
            "a",
            Assert.Single(result.Attempts).ProviderId.Value);
    }

    [Fact]
    public async Task Execute_RetriesSameProviderBeforeFallback()
    {
        FakeAgent a =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Failed(
                                "failed")));

        FakeAgent b =
            Agent("b");

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [b, a],
                    policy_:
                        new WorkflowRepairPolicy(
                            2,
                            3))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            [
                "a",
                "a",
                "b"
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.ProviderId.Value)
                .ToArray());

        Assert.Equal(
            [
                1,
                2,
                1
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.ProviderAttemptNumber)
                .ToArray());

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));

        Assert.False(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_MaxTotalAttemptsBoundsBeforeFallback()
    {
        FakeAgent a =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Failed()));

        FakeAgent b =
            Agent("b");

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [a, b],
                    policy_:
                        new WorkflowRepairPolicy(
                            5,
                            2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            2,
            result.Attempts.Count);

        Assert.All(
            result.Attempts,
            attempt_ =>
                Assert.Equal(
                    "a",
                    attempt_.ProviderId.Value));

        Assert.Equal(
            0,
            b.InvocationCount);

        Assert.True(
            result.BudgetExhausted);

        Assert.Equal(
            WorkflowStepStatus.Failed,
            Status(result.State));
    }

    [Fact]
    public async Task Execute_DoesNotWrapProvidersAfterFallback()
    {
        FakeAgent a =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Failed()));

        FakeAgent b =
            Agent(
                "b",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Failed()));

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [a, b],
                    policy_:
                        new WorkflowRepairPolicy(
                            1,
                            10))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            2,
            result.Attempts.Count);

        Assert.Equal(
            [
                "a",
                "b"
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.ProviderId.Value)
                .ToArray());

        Assert.True(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_FailedAgentExhaustsBudget()
    {
        AgentExecutionResult failed =
            AgentExecutionResult.Failed(
                "failed");

        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            failed));

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    policy_:
                        new WorkflowRepairPolicy(
                            2,
                            2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            2,
            result.Attempts.Count);

        Assert.All(
            result.Attempts,
            attempt_ =>
                Assert.Same(
                    failed,
                    attempt_.AgentResult));

        Assert.True(
            result.BudgetExhausted);

        Assert.Equal(
            WorkflowStepStatus.Failed,
            Status(result.State));
    }

    [Fact]
    public async Task Execute_BlockedDoesNotRetry()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Blocked(
                                "blocked")));

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    policy_:
                        new WorkflowRepairPolicy(
                            5,
                            5))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Single(
            result.Attempts);

        Assert.Equal(
            WorkflowStepStatus.Blocked,
            Status(result.State));

        Assert.False(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_NeedsInputDoesNotRetry()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.NeedsInput(
                                "input")));

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    policy_:
                        new WorkflowRepairPolicy(
                            5,
                            5))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Single(
            result.Attempts);

        Assert.Equal(
            WorkflowStepStatus.Blocked,
            Status(result.State));

        Assert.False(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_AgentExceptionDoesNotRetry()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromException<AgentExecutionResult>(
                            new InvalidOperationException(
                                "boom")));

        ExecutableWork work =
            Work();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                Coordinator(
                        [agent],
                        policy_:
                            new WorkflowRepairPolicy(
                                5,
                                5))
                    .ExecuteAsync(
                        Running(work),
                        work,
                        Envelope(work)));

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_AgentTimeoutDoesNotRetry()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_:
                    async (
                        _,
                        _,
                        cancellationToken_) =>
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken_);

                        return AgentExecutionResult.Completed();
                    });

        ExecutableWork work =
            Work();

        await Assert.ThrowsAsync<TimeoutException>(
            () =>
                Coordinator(
                        [agent],
                        policy_:
                            new WorkflowRepairPolicy(
                                5,
                                5))
                    .ExecuteAsync(
                        Running(work),
                        work,
                        Envelope(
                            work,
                            timeout_:
                                TimeSpan.FromMilliseconds(
                                    20))));

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_NullAgentResultDoesNotRetry()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult<AgentExecutionResult>(
                            null!));

        ExecutableWork work =
            Work();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                Coordinator(
                        [agent],
                        policy_:
                            new WorkflowRepairPolicy(
                                5,
                                5))
                    .ExecuteAsync(
                        Running(work),
                        work,
                        Envelope(work)));

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_CompletedAgentWithNoRequirementsCompletes()
    {
        AgentExecutionResult completed =
            AgentExecutionResult.Completed(
                "done");

        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            completed));

        ExecutableWork work =
            Work();

        ExecutionEnvelope envelope =
            Envelope(work);

        WorkflowState inputState =
            Running(work);

        WorkflowRepairResult result =
            await Coordinator(
                    [agent])
                .ExecuteAsync(
                    inputState,
                    work,
                    envelope);

        WorkflowRepairAttempt attempt =
            Assert.Single(
                result.Attempts);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));

        Assert.Equal(
            WorkflowStatus.Running,
            result.State.Status);

        Assert.Equal(
            WorkflowStepStatus.Running,
            Status(inputState));

        Assert.Same(
            envelope.Prompt,
            attempt.Prompt);

        Assert.Same(
            completed,
            attempt.AgentResult);

        Assert.Empty(
            attempt.ValidationEvidence);

        Assert.False(
            attempt.SemanticRepair);

        Assert.False(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_ValidationPassCompletesWithExactEvidence()
    {
        ValidationExecutionResult validationResult =
            new(
                true,
                "build ok");

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    _,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            validationResult));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [Agent("a")],
                    [validator])
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        WorkflowRepairAttempt attempt =
            Assert.Single(
                result.Attempts);

        WorkflowValidationEvidence evidence =
            Assert.Single(
                attempt.ValidationEvidence);

        Assert.Same(
            validationResult,
            evidence.ValidationResult);

        Assert.Equal(
            "v1",
            evidence.Requirement.Id);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));

        Assert.False(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_ValidationFailureDrivesSemanticRepair()
    {
        FakeAgent agent =
            Agent("a");

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    invocation_,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? new ValidationExecutionResult(
                                    false,
                                    "compile failed")
                                : new ValidationExecutionResult(
                                    true,
                                    "compile ok")));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build,
                        statement_: "must build")
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    [validator],
                    new WorkflowRepairPolicy(
                        2,
                        2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(
                        work,
                        content_: "original"));

        Assert.Equal(
            2,
            result.Attempts.Count);

        Assert.False(
            result.Attempts[0].SemanticRepair);

        Assert.True(
            result.Attempts[1].SemanticRepair);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));

        Assert.False(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_RepairPromptFormatIsExact()
    {
        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    invocation_,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? new ValidationExecutionResult(
                                    false,
                                    "compile failed")
                                : new ValidationExecutionResult(
                                    true,
                                    "ok")));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build,
                        sourceAcceptanceCriterionId_: "ac1",
                        statement_: "must build")
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [Agent("a")],
                    [validator],
                    new WorkflowRepairPolicy(
                        2,
                        2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(
                        work,
                        content_: "original"));

        Assert.Equal(
            "original\n\n[AIRepoKit.SemanticRepair/v1]\n" +
            "nextAttempt=2\n" +
            "failedValidationCount=1\n" +
            "validation[0].id=v1\n" +
            "validation[0].sourceAcceptanceCriterionId=ac1\n" +
            "validation[0].strategy=Build\n" +
            "validation[0].statement=must build\n" +
            "validation[0].evidence=compile failed",
            result.Attempts[1].Prompt.Content);
    }

    [Fact]
    public async Task Execute_RepairPromptUsesOnlyFailedEvidenceInOrdinalOrder()
    {
        FakeValidator build =
            Validator(
                ValidationStrategy.Build,
                result_:
                    new ValidationExecutionResult(
                        false,
                        "build-fail"));

        FakeValidator test =
            Validator(
                ValidationStrategy.Test,
                result_:
                    new ValidationExecutionResult(
                        true,
                        "test-pass"));

        FakeValidator policy =
            Validator(
                ValidationStrategy.Policy,
                result_:
                    new ValidationExecutionResult(
                        false,
                        "policy-fail"));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "c-build",
                        ValidationStrategy.Build,
                        statement_: "build"),
                    Requirement(
                        "b-test",
                        ValidationStrategy.Test,
                        statement_: "test"),
                    Requirement(
                        "a-policy",
                        ValidationStrategy.Policy,
                        statement_: "policy")
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [Agent("a")],
                    [build, test, policy],
                    new WorkflowRepairPolicy(
                        2,
                        2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        string repair =
            result.Attempts[1].Prompt.Content;

        int policyIndex =
            repair.IndexOf(
                "validation[0].id=a-policy",
                StringComparison.Ordinal);

        int buildIndex =
            repair.IndexOf(
                "validation[1].id=c-build",
                StringComparison.Ordinal);

        Assert.True(
            policyIndex >= 0);

        Assert.True(
            buildIndex > policyIndex);

        Assert.DoesNotContain(
            "b-test",
            repair,
            StringComparison.Ordinal);

        Assert.Contains(
            "failedValidationCount=2",
            repair,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_RepairPromptNeverNestsPreviousRepairPrompt()
    {
        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    invocation_,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ switch
                            {
                                1 =>
                                    new ValidationExecutionResult(
                                        false,
                                        "first-failure"),
                                2 =>
                                    new ValidationExecutionResult(
                                        false,
                                        "second-failure"),
                                _ =>
                                    new ValidationExecutionResult(
                                        true,
                                        "ok")
                            }));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [Agent("a")],
                    [validator],
                    new WorkflowRepairPolicy(
                        3,
                        3))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(
                        work,
                        content_: "original"));

        Assert.Equal(
            3,
            result.Attempts.Count);

        string third =
            result.Attempts[2].Prompt.Content;

        Assert.Equal(
            1,
            CountOccurrences(
                third,
                "[AIRepoKit.SemanticRepair/v1]"));

        Assert.StartsWith(
            "original\n\n[AIRepoKit.SemanticRepair/v1]",
            third,
            StringComparison.Ordinal);

        Assert.Contains(
            "second-failure",
            third,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "first-failure",
            third,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_FailedAgentRetryKeepsCurrentPromptUnchanged()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    invocation_,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? AgentExecutionResult.Failed()
                                : AgentExecutionResult.Completed()));

        ExecutableWork work =
            Work();

        ExecutionEnvelope envelope =
            Envelope(
                work,
                content_: "original");

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    policy_:
                        new WorkflowRepairPolicy(
                            2,
                            2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    envelope);

        Assert.Equal(
            2,
            result.Attempts.Count);

        Assert.Same(
            envelope.Prompt,
            result.Attempts[0].Prompt);

        Assert.Same(
            envelope.Prompt,
            result.Attempts[1].Prompt);

        Assert.False(
            result.Attempts[0].SemanticRepair);

        Assert.False(
            result.Attempts[1].SemanticRepair);
    }

    [Fact]
    public async Task Execute_AgentFailureAfterRepairKeepsRepairPromptUnchanged()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    invocation_,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ switch
                            {
                                1 =>
                                    AgentExecutionResult.Completed(),
                                2 =>
                                    AgentExecutionResult.Failed(),
                                _ =>
                                    AgentExecutionResult.Completed()
                            }));

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    invocation_,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? new ValidationExecutionResult(
                                    false,
                                    "repair")
                                : new ValidationExecutionResult(
                                    true,
                                    "ok")));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    [validator],
                    new WorkflowRepairPolicy(
                        3,
                        3))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            3,
            result.Attempts.Count);

        Assert.Same(
            result.Attempts[1].Prompt,
            result.Attempts[2].Prompt);

        Assert.True(
            result.Attempts[1].SemanticRepair);

        Assert.True(
            result.Attempts[2].SemanticRepair);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));
    }

    [Fact]
    public async Task Execute_ValidationFailureFallsBackAfterProviderBudget()
    {
        FakeAgent a =
            Agent("a");

        FakeAgent b =
            Agent("b");

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    invocation_,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? new ValidationExecutionResult(
                                    false,
                                    "bad")
                                : new ValidationExecutionResult(
                                    true,
                                    "ok")));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [b, a],
                    [validator],
                    new WorkflowRepairPolicy(
                        1,
                        2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            [
                "a",
                "b"
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.ProviderId.Value)
                .ToArray());

        Assert.True(
            result.Attempts[1].SemanticRepair);

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));
    }

    [Fact]
    public async Task Execute_ValidationFailureCanExhaustBudget()
    {
        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                result_:
                    new ValidationExecutionResult(
                        false,
                        "bad"));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [Agent("a")],
                    [validator],
                    new WorkflowRepairPolicy(
                        1,
                        1))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Single(
            result.Attempts);

        Assert.True(
            result.BudgetExhausted);

        Assert.Equal(
            WorkflowStepStatus.Failed,
            Status(result.State));
    }

    [Fact]
    public async Task Execute_ValidatorExceptionDoesNotRetryAgent()
    {
        FakeAgent agent =
            Agent("a");

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    _,
                    _,
                    _,
                    _) =>
                        Task.FromException<ValidationExecutionResult>(
                            new InvalidOperationException(
                                "validator")));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                Coordinator(
                        [agent],
                        [validator],
                        new WorkflowRepairPolicy(
                            5,
                            5))
                    .ExecuteAsync(
                        Running(work),
                        work,
                        Envelope(work)));

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Execute_PreservesPermissionEnvironmentStructuredOutputAndTimeout()
    {
        ExecutionEnvironment environment =
            new(
                Path.GetFullPath("."));

        StructuredOutputContract structuredOutput =
            new(
                "{}");

        FakeAgent agent =
            Agent("a");

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    invocation_,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? new ValidationExecutionResult(
                                    false,
                                    "repair")
                                : new ValidationExecutionResult(
                                    true,
                                    "ok")));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        TimeSpan timeout =
            TimeSpan.FromSeconds(
                30);

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    [validator],
                    new WorkflowRepairPolicy(
                        2,
                        2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(
                        work,
                        permission_: ExecutionPermission.WorkspaceWrite,
                        environment_: environment,
                        structuredOutput_: structuredOutput,
                        timeout_: timeout));

        Assert.Equal(
            2,
            agent.Requests.Count);

        foreach (AgentExecutionRequest request in agent.Requests)
        {
            Assert.Equal(
                ExecutionPermission.WorkspaceWrite,
                request.Permission);

            Assert.Same(
                environment,
                request.Environment);

            Assert.Same(
                structuredOutput,
                request.StructuredOutput);

            Assert.Null(
                request.SessionReference);
        }

        Assert.Equal(
            WorkflowStepStatus.Completed,
            Status(result.State));
    }

    [Fact]
    public async Task Execute_DoesNotMigrateReturnedSessionAcrossRepair()
    {
        AgentSessionReference returnedSession =
            new(
                "provider-session");

        FakeAgent a =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Completed(
                                sessionReference_:
                                    returnedSession)));

        FakeAgent b =
            Agent("b");

        FakeValidator validator =
            Validator(
                ValidationStrategy.Build,
                handler_: (
                    invocation_,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? new ValidationExecutionResult(
                                    false,
                                    "bad")
                                : new ValidationExecutionResult(
                                    true,
                                    "ok")));

        ExecutableWork work =
            Work(
                validationRequirements_:
                [
                    Requirement(
                        "v1",
                        ValidationStrategy.Build)
                ]);

        WorkflowRepairResult result =
            await Coordinator(
                    [a, b],
                    [validator],
                    new WorkflowRepairPolicy(
                        1,
                        2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            2,
            result.Attempts.Count);

        Assert.Single(
            b.Requests);

        Assert.Null(
            b.Requests[0].SessionReference);
    }

    [Fact]
    public async Task Execute_AttemptHistoryIsOrderedAndReadOnly()
    {
        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    invocation_,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ < 3
                                ? AgentExecutionResult.Failed()
                                : AgentExecutionResult.Completed()));

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    policy_:
                        new WorkflowRepairPolicy(
                            3,
                            3))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            [
                1,
                2,
                3
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.AttemptNumber)
                .ToArray());

        Assert.Equal(
            [
                1,
                2,
                3
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.ProviderAttemptNumber)
                .ToArray());

        Assert.IsAssignableFrom<
            IReadOnlyList<WorkflowRepairAttempt>>(
                result.Attempts);

        Assert.False(
            result.BudgetExhausted);
    }

    [Fact]
    public async Task Execute_AttemptStoresExactAgentResultObject()
    {
        AgentExecutionResult exact =
            AgentExecutionResult.Completed(
                "exact");

        FakeAgent agent =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            exact));

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [agent])
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Same(
            exact,
            Assert.Single(result.Attempts).AgentResult);
    }

    [Fact]
    public async Task Execute_AttemptNumbersResetPerProvider()
    {
        FakeAgent a =
            Agent(
                "a",
                handler_: (
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Failed()));

        FakeAgent b =
            Agent(
                "b",
                handler_: (
                    invocation_,
                    _,
                    _) =>
                        Task.FromResult(
                            invocation_ == 1
                                ? AgentExecutionResult.Failed()
                                : AgentExecutionResult.Completed()));

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [a, b],
                    policy_:
                        new WorkflowRepairPolicy(
                            2,
                            4))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            [
                1,
                2,
                1,
                2
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.ProviderAttemptNumber)
                .ToArray());

        Assert.Equal(
            [
                "a",
                "a",
                "b",
                "b"
            ],
            result
                .Attempts
                .Select(
                    attempt_ =>
                        attempt_.ProviderId.Value)
                .ToArray());
    }

    [Fact]
    public async Task Execute_AttemptsAreSequential()
    {
        int active =
            0;

        int maximumActive =
            0;

        FakeAgent agent =
            Agent(
                "a",
                handler_:
                    async (
                        invocation_,
                        _,
                        _) =>
                    {
                        active++;

                        maximumActive =
                            Math.Max(
                                maximumActive,
                                active);

                        await Task.Yield();

                        active--;

                        return invocation_ == 1
                            ? AgentExecutionResult.Failed()
                            : AgentExecutionResult.Completed();
                    });

        ExecutableWork work =
            Work();

        WorkflowRepairResult result =
            await Coordinator(
                    [agent],
                    policy_:
                        new WorkflowRepairPolicy(
                            2,
                            2))
                .ExecuteAsync(
                    Running(work),
                    work,
                    Envelope(work));

        Assert.Equal(
            1,
            maximumActive);

        Assert.Equal(
            2,
            result.Attempts.Count);
    }

    private static WorkflowRepairPolicy Policy()
    {
        return new WorkflowRepairPolicy(
            1,
            1);
    }

    private static WorkflowRepairCoordinator Coordinator(
        IReadOnlyList<IAgentExecutor> agents_,
        IReadOnlyList<IValidationExecutor>? validators_ = null,
        WorkflowRepairPolicy? policy_ = null)
    {
        return new WorkflowRepairCoordinator(
            agents_,
            validators_ ??
                Array.Empty<IValidationExecutor>(),
            policy_ ??
                Policy());
    }

    private static FakeAgent Agent(
        string providerId_,
        AgentCapabilitySet? capabilities_ = null,
        Func<
            int,
            AgentExecutionRequest,
            CancellationToken,
            Task<AgentExecutionResult>>? handler_ = null)
    {
        return new FakeAgent(
            new AgentProviderId(
                providerId_),
            capabilities_ ??
                AgentCapabilitySet.Empty,
            handler_);
    }

    private static FakeValidator Validator(
        ValidationStrategy strategy_,
        ValidationExecutionResult? result_ = null,
        Func<
            int,
            ValidationRequirement,
            ExecutionEnvironment,
            CancellationToken,
            Task<ValidationExecutionResult>>? handler_ = null)
    {
        return new FakeValidator(
            strategy_,
            handler_ ??
                ((
                    _,
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            result_ ??
                            new ValidationExecutionResult(
                                true,
                                "ok"))));
    }

    private static AgentCapabilitySet Caps(
        params string[] values_)
    {
        return new AgentCapabilitySet(
            values_.Select(
                value_ =>
                    new AgentCapability(
                        value_)));
    }

    private static ValidationRequirement Requirement(
        string id_,
        ValidationStrategy strategy_,
        string sourceAcceptanceCriterionId_ = "ac1",
        string statement_ = "must pass",
        string taskId_ = "task")
    {
        return new ValidationRequirement(
            id_,
            taskId_,
            sourceAcceptanceCriterionId_,
            strategy_,
            statement_);
    }

    private static ExecutableWork Work(
        int revision_ = 1,
        string taskId_ = "task",
        IReadOnlyList<ValidationRequirement>? validationRequirements_ = null,
        AgentCapabilitySet? agentCapabilities_ = null,
        AgentCapabilitySet? modelCapabilities_ = null)
    {
        IReadOnlyList<AgentRequirement> agentRequirements =
            agentCapabilities_ is null
                ? Array.Empty<AgentRequirement>()
                : [
                    new AgentRequirement(
                        taskId_,
                        agentCapabilities_)
                ];

        IReadOnlyList<ModelRequirement> modelRequirements =
            modelCapabilities_ is null
                ? Array.Empty<ModelRequirement>()
                : [
                    new ModelRequirement(
                        taskId_,
                        modelCapabilities_)
                ];

        return new ExecutableWork(
            revision_,
            [
                new ExecutableTask(
                    taskId_,
                    "step-1",
                    "do work")
            ],
            Array.Empty<ExecutableTaskDependency>(),
            validationRequirements_ ??
                Array.Empty<ValidationRequirement>(),
            modelRequirements,
            agentRequirements);
    }

    private static WorkflowState Running(
        ExecutableWork work_)
    {
        WorkflowState state =
            WorkflowStateMachine.Create(
                work_);

        state =
            WorkflowStateMachine.TransitionWorkflow(
                state,
                WorkflowStatus.Running);

        return WorkflowStateMachine.TransitionStep(
            state,
            work_.Tasks[0].Id,
            WorkflowStepStatus.Running);
    }

    private static ExecutionEnvelope Envelope(
        ExecutableWork work_,
        string content_ = "original",
        ExecutionPermission permission_ = ExecutionPermission.ReadOnly,
        ExecutionEnvironment? environment_ = null,
        AgentSessionReference? session_ = null,
        StructuredOutputContract? structuredOutput_ = null,
        TimeSpan? timeout_ = null)
    {
        CompiledPrompt prompt =
            new(
                work_.Tasks[0].Id,
                content_);

        return ExecutionEnvelope.Create(
            work_,
            prompt,
            permission_,
            environment_ ??
                new ExecutionEnvironment(
                    Path.GetFullPath(".")),
            session_,
            structuredOutput_,
            timeout_);
    }

    private static WorkflowStepStatus Status(
        WorkflowState state_,
        string taskId_ = "task")
    {
        return Assert
            .Single(
                state_
                    .Steps
                    .Where(
                        step_ =>
                            string.Equals(
                                step_.TaskId,
                                taskId_,
                                StringComparison.Ordinal)))
            .Status;
    }

    private static int CountOccurrences(
        string value_,
        string needle_)
    {
        return value_.Split(
                needle_,
                StringSplitOptions.None)
            .Length -
            1;
    }

    private sealed class FakeAgent :
        IAgentExecutor
    {
        private readonly Func<
            int,
            AgentExecutionRequest,
            CancellationToken,
            Task<AgentExecutionResult>> _handler;

        public AgentProviderId? CurrentProviderId
        {
            get;
            set;
        }

        public AgentCapabilitySet? CurrentCapabilities
        {
            get;
            set;
        }

        public AgentProviderId ProviderId =>
            this.CurrentProviderId!;

        public AgentCapabilitySet Capabilities =>
            this.CurrentCapabilities!;

        public int InvocationCount
        {
            get;
            private set;
        }

        public List<AgentExecutionRequest> Requests
        {
            get;
        } =
            [];

        public FakeAgent(
            AgentProviderId? providerId_,
            AgentCapabilitySet? capabilities_,
            Func<
                int,
                AgentExecutionRequest,
                CancellationToken,
                Task<AgentExecutionResult>>? handler_ = null)
        {
            this.CurrentProviderId =
                providerId_;
            this.CurrentCapabilities =
                capabilities_;

            this._handler =
                handler_ ??
                ((
                    _,
                    _,
                    _) =>
                        Task.FromResult(
                            AgentExecutionResult.Completed()));
        }

        public async Task<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;

            this.Requests.Add(
                request_);

            return await this
                ._handler(
                    this.InvocationCount,
                    request_,
                    cancellationToken_)
                .ConfigureAwait(false);
        }
    }

    private sealed class FakeValidator :
        IValidationExecutor
    {
        private readonly Func<
            int,
            ValidationRequirement,
            ExecutionEnvironment,
            CancellationToken,
            Task<ValidationExecutionResult>> _handler;

        public ValidationStrategy CurrentStrategy
        {
            get;
            set;
        }

        public ValidationStrategy Strategy =>
            this.CurrentStrategy;

        public int InvocationCount
        {
            get;
            private set;
        }

        public List<ValidationRequirement> Requirements
        {
            get;
        } =
            [];

        public FakeValidator(
            ValidationStrategy strategy_,
            Func<
                int,
                ValidationRequirement,
                ExecutionEnvironment,
                CancellationToken,
                Task<ValidationExecutionResult>>? handler_ = null)
        {
            this.CurrentStrategy =
                strategy_;

            this._handler =
                handler_ ??
                ((
                    _,
                    _,
                    _,
                    _) =>
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

            return await this
                ._handler(
                    this.InvocationCount,
                    requirement_,
                    environment_,
                    cancellationToken_)
                .ConfigureAwait(false);
        }
    }

    private sealed class SingleEnumerationEnumerable<T> :
        IEnumerable<T>
    {
        private readonly T[] _items;

        public int EnumerationCount
        {
            get;
            private set;
        }

        public SingleEnumerationEnumerable(
            params T[] items_)
        {
            this._items =
                items_;
        }

        public IEnumerator<T> GetEnumerator()
        {
            this.EnumerationCount++;

            if (this.EnumerationCount > 1)
            {
                throw new InvalidOperationException(
                    "Collection was enumerated more than once.");
            }

            return (
                (IEnumerable<T>) this._items
            ).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}

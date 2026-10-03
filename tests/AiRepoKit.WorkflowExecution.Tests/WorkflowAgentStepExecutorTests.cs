namespace AiRepoKit.WorkflowExecution.Tests;

using System.Collections;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Xunit;

public sealed class WorkflowAgentStepExecutorTests
{
    [Fact]
    public void Constructor_RejectsNullCollection()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new WorkflowAgentStepExecutor(
                        null!));

        Assert.Equal(
            "executors_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsEmptyCollection()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new WorkflowAgentStepExecutor(
                        Array.Empty<IAgentExecutor>()));

        Assert.Equal(
            "executors_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullElement()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new WorkflowAgentStepExecutor(
                        [
                            Agent(
                                "a"),
                            null!
                        ]));

        Assert.Equal(
            "executors_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullProviderId()
    {
        FakeAgent agent =
            new(
                providerId_: (AgentProviderId?) null,
                capabilities_: AgentCapabilitySet.Empty);

        Assert.Throws<InvalidOperationException>(
            () =>
                new WorkflowAgentStepExecutor(
                    [
                        agent
                    ]));
    }

    [Fact]
    public void Constructor_RejectsNullCapabilities()
    {
        FakeAgent agent =
            new(
                providerId_: new AgentProviderId(
                    "a"),
                capabilities_: null);

        Assert.Throws<InvalidOperationException>(
            () =>
                new WorkflowAgentStepExecutor(
                    [
                        agent
                    ]));
    }

    [Fact]
    public void Constructor_RejectsDuplicateProviderIdsOrdinally()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new WorkflowAgentStepExecutor(
                        [
                            Agent(
                                "provider-a"),
                            Agent(
                                "provider-a")
                        ]));

        Assert.Equal(
            "executors_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_EnumeratesExecutorCollectionExactlyOnce()
    {
        SingleEnumerationEnumerable source =
            new(
                Agent(
                    "a"),
                Agent(
                    "b"));

        _ =
            new WorkflowAgentStepExecutor(
                source);

        Assert.Equal(
            1,
            source.EnumerationCount);
    }

    [Fact]
    public async Task Constructor_SnapshotsProviderAndCapabilities()
    {
        FakeAgent mutable =
            Agent(
                "b",
                "cap.a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    mutable
                ]);

        mutable.CurrentProviderId =
            new AgentProviderId(
                "z");

        mutable.CurrentCapabilities =
            AgentCapabilitySet.Empty;

        WorkflowAgentStepResult result =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a",
                    agentCapabilities_: Capabilities(
                        "cap.a")));

        Assert.Equal(
            "b",
            result.ProviderId.Value);

        Assert.Equal(
            1,
            mutable.InvocationCount);
    }

    [Theory]
    [InlineData("b", "a", "a")]
    [InlineData("a", "b", "a")]
    [InlineData("c", "b", "b")]
    [InlineData("a2", "a1", "a1")]
    [InlineData("a1", "a2", "a1")]
    [InlineData("beta", "alpha", "alpha")]
    [InlineData("agent-b", "agent-a", "agent-a")]
    [InlineData("provider.2", "provider.1", "provider.1")]
    [InlineData("x9", "x1", "x1")]
    [InlineData("z", "m", "m")]
    [InlineData("p-b", "p-a", "p-a")]
    [InlineData("a9", "a0", "a0")]
    public async Task Selection_UsesProviderIdOrdinalOrder(
        string firstProvider_,
        string secondProvider_,
        string expectedProvider_)
    {
        FakeAgent first =
            Agent(
                firstProvider_);

        FakeAgent second =
            Agent(
                secondProvider_);

        WorkflowAgentStepExecutor executor =
            new(
                [
                    first,
                    second
                ]);

        WorkflowAgentStepResult result =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a"));

        Assert.Equal(
            expectedProvider_,
            result.ProviderId.Value);

        Assert.Equal(
            1,
            first.InvocationCount +
            second.InvocationCount);
    }

    [Theory]
    [InlineData("cap.a", "cap.a", "cap.b", "a")]
    [InlineData("cap.b", "cap.a", "cap.b", "b")]
    [InlineData("cap.a,cap.b", "cap.a", "cap.a,cap.b", "b")]
    [InlineData("cap.a,cap.b", "cap.a,cap.b", "cap.a,cap.b,cap.c", "a")]
    [InlineData("", "", "", "a")]
    [InlineData("cap.c", "cap.a,cap.c", "cap.c", "a")]
    [InlineData("cap.c", "cap.a", "cap.b,cap.c", "b")]
    [InlineData("cap.a,cap.c", "cap.a,cap.c", "cap.a,cap.b,cap.c", "a")]
    [InlineData("cap.b,cap.c", "cap.a,cap.b", "cap.b,cap.c", "b")]
    [InlineData("cap.a", "cap.a,cap.z", "cap.a,cap.b", "a")]
    public async Task Selection_FiltersByRequiredAgentCapabilities(
        string requiredCsv_,
        string firstCsv_,
        string secondCsv_,
        string expectedProvider_)
    {
        FakeAgent first =
            new(
                "a",
                CapabilitiesFromCsv(
                    firstCsv_));

        FakeAgent second =
            new(
                "b",
                CapabilitiesFromCsv(
                    secondCsv_));

        WorkflowAgentStepExecutor executor =
            new(
                [
                    first,
                    second
                ]);

        WorkflowAgentStepResult result =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a",
                    agentCapabilities_: CapabilitiesFromCsv(
                        requiredCsv_)));

        Assert.Equal(
            expectedProvider_,
            result.ProviderId.Value);
    }

    [Fact]
    public async Task Selection_UsesAgentRequirementsInsteadOfModelRequirements()
    {
        FakeAgent modelOnly =
            Agent(
                "a",
                "model.required");

        FakeAgent agentCapable =
            Agent(
                "b",
                "agent.required");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    modelOnly,
                    agentCapable
                ]);

        WorkflowAgentStepResult result =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a",
                    agentCapabilities_: Capabilities(
                        "agent.required"),
                    modelCapabilities_: Capabilities(
                        "model.required")));

        Assert.Equal(
            "b",
            result.ProviderId.Value);
    }

    [Fact]
    public async Task Selection_ModelRequirementsDoNotRestrictEmptyAgentRequirements()
    {
        FakeAgent first =
            Agent(
                "a");

        FakeAgent second =
            Agent(
                "b");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    second,
                    first
                ]);

        WorkflowAgentStepResult result =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a",
                    modelCapabilities_: Capabilities(
                        "model.required")));

        Assert.Equal(
            "a",
            result.ProviderId.Value);
    }

    [Fact]
    public async Task Selection_NoEligibleProviderFailsBeforeInvocation()
    {
        FakeAgent first =
            Agent(
                "a",
                "cap.a");

        FakeAgent second =
            Agent(
                "b",
                "cap.b");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    first,
                    second
                ]);

        WorkflowState state =
            RunningState(
                "task-a");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                executor.ExecuteAsync(
                    state,
                    Envelope(
                        "task-a",
                        agentCapabilities_: Capabilities(
                            "cap.missing"))));

        Assert.Equal(
            0,
            first.InvocationCount);

        Assert.Equal(
            0,
            second.InvocationCount);

        Assert.Equal(
            WorkflowStepStatus.Running,
            state.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNullWorkflowState()
    {
        WorkflowAgentStepExecutor executor =
            Executor();

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () =>
                    executor.ExecuteAsync(
                        null!,
                        Envelope(
                            "task-a")));

        Assert.Equal(
            "state_",
            exception.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNullExecutionEnvelope()
    {
        WorkflowAgentStepExecutor executor =
            Executor();

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () =>
                    executor.ExecuteAsync(
                        RunningState(
                            "task-a"),
                        null!));

        Assert.Equal(
            "envelope_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(WorkflowStatus.Created)]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Cancelled)]
    public async Task ExecuteAsync_RejectsNonRunningWorkflowBeforeInvocation(
        WorkflowStatus workflowStatus_)
    {
        FakeAgent agent =
            Agent(
                "a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                executor.ExecuteAsync(
                    StateWithWorkflowStatus(
                        workflowStatus_),
                    Envelope(
                        "task-a")));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Theory]
    [InlineData(WorkflowStepStatus.Pending)]
    [InlineData(WorkflowStepStatus.AwaitingValidation)]
    [InlineData(WorkflowStepStatus.Blocked)]
    [InlineData(WorkflowStepStatus.Failed)]
    [InlineData(WorkflowStepStatus.Completed)]
    [InlineData(WorkflowStepStatus.Cancelled)]
    public async Task ExecuteAsync_RejectsNonRunningTargetStepBeforeInvocation(
        WorkflowStepStatus stepStatus_)
    {
        FakeAgent agent =
            Agent(
                "a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                executor.ExecuteAsync(
                    StateWithStepStatus(
                        stepStatus_),
                    Envelope(
                        "task-a")));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_TaskLookupUsesOrdinalIdentity()
    {
        FakeAgent agent =
            Agent(
                "a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                executor.ExecuteAsync(
                    RunningState(
                        "task-a"),
                    Envelope(
                        "TASK-A")));

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_MapsEnvelopeToAgentExecutionRequest()
    {
        AgentSessionReference session =
            new(
                "session-a");

        StructuredOutputContract structuredOutput =
            new(
                """{"type":"object"}""");

        ExecutionEnvironment environment =
            new(
                Path.GetFullPath(
                    "."));

        FakeAgent agent =
            Agent(
                "a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        _ =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a",
                    permission_: ExecutionPermission.WorkspaceWrite,
                    environment_: environment,
                    session_: session,
                    structuredOutput_: structuredOutput));

        AgentExecutionRequest request =
            Assert.IsType<AgentExecutionRequest>(
                agent.LastRequest);

        Assert.Equal(
            "compiled instruction task-a",
            request.Instruction);

        Assert.Equal(
            ExecutionPermission.WorkspaceWrite,
            request.Permission);

        Assert.Same(
            environment,
            request.Environment);

        Assert.Same(
            session,
            request.SessionReference);

        Assert.Same(
            structuredOutput,
            request.StructuredOutput);
    }

    [Fact]
    public async Task CompletedResult_TransitionsToAwaitingValidation()
    {
        WorkflowAgentStepResult result =
            await Executor(
                    AgentExecutionResult.Completed(
                        "done"))
                .ExecuteAsync(
                    RunningState(
                        "task-a"),
                    Envelope(
                        "task-a"));

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            result.State.Steps[0].Status);

        Assert.NotEqual(
            WorkflowStepStatus.Completed,
            result.State.Steps[0].Status);
    }

    [Fact]
    public async Task BlockedResult_TransitionsToBlocked()
    {
        WorkflowAgentStepResult result =
            await Executor(
                    AgentExecutionResult.Blocked(
                        "blocked"))
                .ExecuteAsync(
                    RunningState(
                        "task-a"),
                    Envelope(
                        "task-a"));

        Assert.Equal(
            WorkflowStepStatus.Blocked,
            result.State.Steps[0].Status);
    }

    [Fact]
    public async Task NeedsInputResult_TransitionsToBlocked()
    {
        WorkflowAgentStepResult result =
            await Executor(
                    AgentExecutionResult.NeedsInput(
                        "need-input"))
                .ExecuteAsync(
                    RunningState(
                        "task-a"),
                    Envelope(
                        "task-a"));

        Assert.Equal(
            WorkflowStepStatus.Blocked,
            result.State.Steps[0].Status);
    }

    [Fact]
    public async Task FailedResult_TransitionsToFailed()
    {
        WorkflowAgentStepResult result =
            await Executor(
                    AgentExecutionResult.Failed(
                        "failed"))
                .ExecuteAsync(
                    RunningState(
                        "task-a"),
                    Envelope(
                        "task-a"));

        Assert.Equal(
            WorkflowStepStatus.Failed,
            result.State.Steps[0].Status);
    }

    [Fact]
    public async Task AgentCompletion_DoesNotCompleteWorkflowStep()
    {
        WorkflowState input =
            RunningState(
                "task-a");

        WorkflowAgentStepResult result =
            await Executor(
                    AgentExecutionResult.Completed())
                .ExecuteAsync(
                    input,
                    Envelope(
                        "task-a"));

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            result.State.Steps[0].Status);

        Assert.DoesNotContain(
            result.State.Steps,
            step_ =>
                step_.Status ==
                WorkflowStepStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotMutateInputWorkflowState()
    {
        WorkflowState input =
            RunningState(
                "task-a");

        WorkflowAgentStepResult result =
            await Executor()
                .ExecuteAsync(
                    input,
                    Envelope(
                        "task-a"));

        Assert.Equal(
            WorkflowStepStatus.Running,
            input.Steps[0].Status);

        Assert.Equal(
            WorkflowStepStatus.AwaitingValidation,
            result.State.Steps[0].Status);

        Assert.NotSame(
            input,
            result.State);
    }

    [Fact]
    public async Task Result_PreservesSelectedProviderAndExactAgentResult()
    {
        AgentExecutionResult agentResult =
            AgentExecutionResult.Completed(
                "output");

        FakeAgent agent =
            new(
                "provider-a",
                AgentCapabilitySet.Empty,
                (_, _) =>
                    Task.FromResult(
                        agentResult));

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        WorkflowAgentStepResult result =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a"));

        Assert.Equal(
            "provider-a",
            result.ProviderId.Value);

        Assert.Same(
            agentResult,
            result.AgentResult);
    }

    [Theory]
    [InlineData(AgentExecutionStatus.Completed)]
    [InlineData(AgentExecutionStatus.Blocked)]
    [InlineData(AgentExecutionStatus.NeedsInput)]
    [InlineData(AgentExecutionStatus.Failed)]
    public async Task ExecuteAsync_InvokesOnlySelectedAgentExactlyOnce(
        AgentExecutionStatus status_)
    {
        FakeAgent selected =
            new(
                "a",
                AgentCapabilitySet.Empty,
                (_, _) =>
                    Task.FromResult(
                        Result(
                            status_)));

        FakeAgent later =
            Agent(
                "b");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    later,
                    selected
                ]);

        _ =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a"));

        Assert.Equal(
            1,
            selected.InvocationCount);

        Assert.Equal(
            0,
            later.InvocationCount);
    }

    [Fact]
    public async Task AgentException_IsPropagatedWithoutWorkflowTransition()
    {
        InvalidOperationException failure =
            new(
                "agent-failure");

        FakeAgent agent =
            new(
                "a",
                AgentCapabilitySet.Empty,
                (_, _) =>
                    Task.FromException<AgentExecutionResult>(
                        failure));

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        WorkflowState state =
            RunningState(
                "task-a");

        InvalidOperationException observed =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    executor.ExecuteAsync(
                        state,
                        Envelope(
                            "task-a")));

        Assert.Same(
            failure,
            observed);

        Assert.Equal(
            WorkflowStepStatus.Running,
            state.Steps[0].Status);

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task NullAgentResult_IsRejectedWithoutWorkflowTransition()
    {
        FakeAgent agent =
            new(
                "a",
                AgentCapabilitySet.Empty,
                (_, _) =>
                    Task.FromResult<AgentExecutionResult>(
                        null!));

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        WorkflowState state =
            RunningState(
                "task-a");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                executor.ExecuteAsync(
                    state,
                    Envelope(
                        "task-a")));

        Assert.Equal(
            WorkflowStepStatus.Running,
            state.Steps[0].Status);

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task CallerCancellation_PrecedesAgentInvocation()
    {
        FakeAgent agent =
            Agent(
                "a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        using CancellationTokenSource source =
            new();

        source.Cancel();

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () =>
                    executor.ExecuteAsync(
                        RunningState(
                            "task-a"),
                        Envelope(
                            "task-a"),
                        source.Token));

        Assert.Equal(
            source.Token,
            exception.CancellationToken);

        Assert.Equal(
            0,
            agent.InvocationCount);
    }

    [Fact]
    public async Task Timeout_ProducesTimeoutExceptionWithoutRetry()
    {
        FakeAgent agent =
            new(
                "a",
                AgentCapabilitySet.Empty,
                async (_, cancellationToken_) =>
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken_);

                    return AgentExecutionResult.Completed();
                });

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        WorkflowState state =
            RunningState(
                "task-a");

        await Assert.ThrowsAsync<TimeoutException>(
            () =>
                executor.ExecuteAsync(
                    state,
                    Envelope(
                        "task-a",
                        timeout_: TimeSpan.FromMilliseconds(
                            100))));

        Assert.Equal(
            1,
            agent.InvocationCount);

        Assert.Equal(
            WorkflowStepStatus.Running,
            state.Steps[0].Status);
    }

    [Fact]
    public async Task UnrelatedOperationCanceledException_IsPropagated()
    {
        OperationCanceledException failure =
            new(
                "unrelated");

        FakeAgent agent =
            new(
                "a",
                AgentCapabilitySet.Empty,
                (_, _) =>
                    Task.FromException<AgentExecutionResult>(
                        failure));

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        OperationCanceledException observed =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () =>
                    executor.ExecuteAsync(
                        RunningState(
                            "task-a"),
                        Envelope(
                            "task-a",
                            timeout_: TimeSpan.FromSeconds(
                                5))));

        Assert.Same(
            failure,
            observed);

        Assert.Equal(
            1,
            agent.InvocationCount);
    }

    [Fact]
    public async Task FailedSelectedProvider_DoesNotFallback()
    {
        FakeAgent selected =
            new(
                "a",
                AgentCapabilitySet.Empty,
                (_, _) =>
                    Task.FromResult(
                        AgentExecutionResult.Failed(
                            "failed")));

        FakeAgent fallback =
            Agent(
                "b");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    fallback,
                    selected
                ]);

        WorkflowAgentStepResult result =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a"));

        Assert.Equal(
            WorkflowStepStatus.Failed,
            result.State.Steps[0].Status);

        Assert.Equal(
            1,
            selected.InvocationCount);

        Assert.Equal(
            0,
            fallback.InvocationCount);
    }

    [Fact]
    public async Task ThrowingSelectedProvider_DoesNotFallback()
    {
        FakeAgent selected =
            new(
                "a",
                AgentCapabilitySet.Empty,
                (_, _) =>
                    Task.FromException<AgentExecutionResult>(
                        new InvalidOperationException(
                            "selected-failure")));

        FakeAgent fallback =
            Agent(
                "b");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    fallback,
                    selected
                ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                executor.ExecuteAsync(
                    RunningState(
                        "task-a"),
                    Envelope(
                        "task-a")));

        Assert.Equal(
            1,
            selected.InvocationCount);

        Assert.Equal(
            0,
            fallback.InvocationCount);
    }

    [Fact]
    public async Task NullTimeout_PassesCallerCancellationTokenDirectly()
    {
        FakeAgent agent =
            Agent(
                "a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        using CancellationTokenSource source =
            new();

        _ =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a"),
                source.Token);

        Assert.Equal(
            source.Token,
            agent.LastCancellationToken);
    }

    [Fact]
    public async Task NonNullTimeout_PassesLinkedCancellationToken()
    {
        FakeAgent agent =
            Agent(
                "a");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    agent
                ]);

        using CancellationTokenSource source =
            new();

        _ =
            await executor.ExecuteAsync(
                RunningState(
                    "task-a"),
                Envelope(
                    "task-a",
                    timeout_: TimeSpan.FromSeconds(
                        5)),
                source.Token);

        Assert.NotEqual(
            source.Token,
            agent.LastCancellationToken);

        Assert.True(
            agent.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task RepeatedCalls_WithSameInputsUseSameProviderAndTransition()
    {
        FakeAgent first =
            Agent(
                "a");

        FakeAgent second =
            Agent(
                "b");

        WorkflowAgentStepExecutor executor =
            new(
                [
                    second,
                    first
                ]);

        WorkflowState state =
            RunningState(
                "task-a");

        ExecutionEnvelope envelope =
            Envelope(
                "task-a");

        WorkflowAgentStepResult firstResult =
            await executor.ExecuteAsync(
                state,
                envelope);

        WorkflowAgentStepResult secondResult =
            await executor.ExecuteAsync(
                state,
                envelope);

        Assert.Equal(
            "a",
            firstResult.ProviderId.Value);

        Assert.Equal(
            firstResult.ProviderId,
            secondResult.ProviderId);

        Assert.Equal(
            firstResult.State,
            secondResult.State);

        Assert.Equal(
            2,
            first.InvocationCount);

        Assert.Equal(
            0,
            second.InvocationCount);
    }

    private static WorkflowAgentStepExecutor Executor(
        AgentExecutionResult? result_ = null)
    {
        AgentExecutionResult result =
            result_ ??
            AgentExecutionResult.Completed();

        return new WorkflowAgentStepExecutor(
            [
                new FakeAgent(
                    "a",
                    AgentCapabilitySet.Empty,
                    (_, _) =>
                        Task.FromResult(
                            result))
            ]);
    }

    private static FakeAgent Agent(
        string providerId_,
        params string[] capabilities_)
    {
        return new FakeAgent(
            providerId_,
            Capabilities(
                capabilities_));
    }

    private static AgentCapabilitySet Capabilities(
        params string[] values_)
    {
        if (values_.Length == 0)
        {
            return AgentCapabilitySet.Empty;
        }

        return new AgentCapabilitySet(
            values_.Select(
                value_ =>
                    new AgentCapability(
                        value_)));
    }

    private static AgentCapabilitySet CapabilitiesFromCsv(
        string csv_)
    {
        if (string.IsNullOrEmpty(
                csv_))
        {
            return AgentCapabilitySet.Empty;
        }

        return Capabilities(
            csv_.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
    }

    private static ExecutableWork Work(
        string taskId_,
        AgentCapabilitySet? agentCapabilities_ = null,
        AgentCapabilitySet? modelCapabilities_ = null)
    {
        ModelRequirement[] modelRequirements =
            modelCapabilities_ is null
                ? []
                :
                [
                    new ModelRequirement(
                        taskId_,
                        modelCapabilities_)
                ];

        AgentRequirement[] agentRequirements =
            agentCapabilities_ is null
                ? []
                :
                [
                    new AgentRequirement(
                        taskId_,
                        agentCapabilities_)
                ];

        return new ExecutableWork(
            1,
            [
                new ExecutableTask(
                    taskId_,
                    $"source-{taskId_}",
                    $"instruction-{taskId_}")
            ],
            Array.Empty<ExecutableTaskDependency>(),
            Array.Empty<ValidationRequirement>(),
            modelRequirements,
            agentRequirements);
    }

    private static ExecutionEnvelope Envelope(
        string taskId_,
        AgentCapabilitySet? agentCapabilities_ = null,
        AgentCapabilitySet? modelCapabilities_ = null,
        ExecutionPermission permission_ = ExecutionPermission.ReadOnly,
        ExecutionEnvironment? environment_ = null,
        AgentSessionReference? session_ = null,
        StructuredOutputContract? structuredOutput_ = null,
        TimeSpan? timeout_ = null)
    {
        ExecutionEnvironment environment =
            environment_ ??
            new ExecutionEnvironment(
                Path.GetFullPath(
                    "."));

        ExecutableWork work =
            Work(
                taskId_,
                agentCapabilities_,
                modelCapabilities_);

        return ExecutionEnvelope.Create(
            work,
            new CompiledPrompt(
                taskId_,
                $"compiled instruction {taskId_}"),
            permission_,
            environment,
            session_,
            structuredOutput_,
            timeout_);
    }

    private static WorkflowState RunningState(
        string taskId_)
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    taskId_));

        WorkflowState runningWorkflow =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        return WorkflowStateMachine.TransitionStep(
            runningWorkflow,
            taskId_,
            WorkflowStepStatus.Running);
    }

    private static WorkflowState StateWithStepStatus(
        WorkflowStepStatus status_)
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    "task-a"));

        WorkflowState state =
            WorkflowStateMachine.TransitionWorkflow(
                created,
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
        WorkflowStatus status_)
    {
        WorkflowState created =
            WorkflowStateMachine.Create(
                Work(
                    "task-a"));

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

    private static AgentExecutionResult Result(
        AgentExecutionStatus status_)
    {
        return status_ switch
        {
            AgentExecutionStatus.Completed =>
                AgentExecutionResult.Completed(),

            AgentExecutionStatus.Blocked =>
                AgentExecutionResult.Blocked(),

            AgentExecutionStatus.NeedsInput =>
                AgentExecutionResult.NeedsInput(),

            AgentExecutionStatus.Failed =>
                AgentExecutionResult.Failed(),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(status_),
                    status_,
                    null)
        };
    }

    private sealed class FakeAgent :
        IAgentExecutor
    {
        private readonly Func<
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

        public AgentProviderId ProviderId
        {
            get
            {
                return this.CurrentProviderId!;
            }
        }

        public AgentCapabilitySet Capabilities
        {
            get
            {
                return this.CurrentCapabilities!;
            }
        }

        public int InvocationCount
        {
            get;
            private set;
        }

        public AgentExecutionRequest? LastRequest
        {
            get;
            private set;
        }

        public CancellationToken LastCancellationToken
        {
            get;
            private set;
        }

        public FakeAgent(
            string providerId_,
            AgentCapabilitySet capabilities_,
            Func<
                AgentExecutionRequest,
                CancellationToken,
                Task<AgentExecutionResult>>? handler_ = null)
            : this(
                new AgentProviderId(
                    providerId_),
                capabilities_,
                handler_)
        {
        }

        public FakeAgent(
            AgentProviderId? providerId_,
            AgentCapabilitySet? capabilities_,
            Func<
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
                (static (_, _) =>
                    Task.FromResult(
                        AgentExecutionResult.Completed()));
        }

        public async Task<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;
            this.LastRequest =
                request_;
            this.LastCancellationToken =
                cancellationToken_;

            return await this
                ._handler(
                    request_,
                    cancellationToken_)
                .ConfigureAwait(false);
        }
    }

    private sealed class SingleEnumerationEnumerable :
        IEnumerable<IAgentExecutor>
    {
        private readonly IAgentExecutor[] _items;

        public int EnumerationCount
        {
            get;
            private set;
        }

        public SingleEnumerationEnumerable(
            params IAgentExecutor[] items_)
        {
            this._items =
                items_;
        }

        public IEnumerator<IAgentExecutor> GetEnumerator()
        {
            this.EnumerationCount++;

            if (this.EnumerationCount > 1)
            {
                throw new InvalidOperationException(
                    "Executor collection was enumerated more than once.");
            }

            return (
                (IEnumerable<IAgentExecutor>) this._items
            ).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}

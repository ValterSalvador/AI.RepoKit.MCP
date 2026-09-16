using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentExecutorContractTests
{
    private sealed class FakeAgentExecutor :
        IAgentExecutor
    {
        public AgentProviderId ProviderId
        {
            get;
        }

        public AgentCapabilitySet Capabilities
        {
            get;
        }

        private readonly Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> _handler;

        public FakeAgentExecutor(
            AgentProviderId providerId_,
            Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> handler_,
            AgentCapabilitySet? capabilities_ = null)
        {
            this.ProviderId =
                providerId_;
            this.Capabilities =
                capabilities_ ??
                AgentCapabilitySet.Empty;
            this._handler =
                handler_;
        }

        public Task<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            return this._handler(
                request_,
                cancellationToken_);
        }
    }

    [Fact]
    public void FakeExecutor_ExposesProviderId()
    {
        AgentProviderId expectedProvider =
            new("antigravity");

        IAgentExecutor executor =
            new FakeAgentExecutor(
                expectedProvider,
                (request_, cancellationToken_) =>
                    Task.FromResult(
                        AgentExecutionResult.Completed("ok")));

        Assert.Equal(
            expectedProvider,
            executor.ProviderId);

        Assert.Equal(
            "antigravity",
            executor.ProviderId.Value);
    }

    [Fact]
    public async Task FakeExecutor_CancellationPropagatesAsOperationCanceledException()
    {
        using CancellationTokenSource cts =
            new();
        cts.Cancel();

        IAgentExecutor executor =
            new FakeAgentExecutor(
                new AgentProviderId("fake-provider"),
                (request_, cancellationToken_) =>
                {
                    cancellationToken_.ThrowIfCancellationRequested();

                    return Task.FromResult(
                        AgentExecutionResult.Completed("ok"));
                });

        AgentExecutionRequest request =
            new("Perform action");

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () =>
                    executor.ExecuteAsync(
                        request,
                        cts.Token));

        Assert.NotNull(
            exception);
    }

    [Fact]
    public async Task FakeExecutor_CancellationIsNotAgentExecutionStatusFailed()
    {
        using CancellationTokenSource cts =
            new();
        cts.Cancel();

        bool failedStatusReturned =
            false;

        IAgentExecutor executor =
            new FakeAgentExecutor(
                new AgentProviderId("fake-provider"),
                (request_, cancellationToken_) =>
                {
                    if (cancellationToken_.IsCancellationRequested)
                    {
                        cancellationToken_.ThrowIfCancellationRequested();
                    }

                    return Task.FromResult(
                        AgentExecutionResult.Completed("ok"));
                });

        AgentExecutionRequest request =
            new("Perform action");

        try
        {
            AgentExecutionResult result =
                await executor.ExecuteAsync(
                    request,
                    cts.Token);

            if (result.Status == AgentExecutionStatus.Failed)
            {
                failedStatusReturned =
                    true;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected contract behavior: cancellation throws OperationCanceledException
        }

        Assert.False(
            failedStatusReturned,
            "Cancellation must propagate as OperationCanceledException and must NOT return AgentExecutionStatus.Failed.");
    }

    [Fact]
    public async Task CompletedStatus_DoesNotImplyDeterministicWorkflowVerification()
    {
        // Architectural doctrine:
        // Orchestrator = deterministic
        // Agents = probabilistic
        // AgentCompletion != StepCompletion
        // AgentExecutionStatus.Completed indicates the bounded agent turn finished, NOT that workflow succeeded.

        IAgentExecutor executor =
            new FakeAgentExecutor(
                new AgentProviderId("codex"),
                (request_, cancellationToken_) =>
                    Task.FromResult(
                        AgentExecutionResult.Completed(
                            outputText_: "Code changes written.",
                            sessionReference_: new AgentSessionReference("session-turn-1"))));

        AgentExecutionRequest request =
            new("Fix compiler error in project.");

        AgentExecutionResult result =
            await executor.ExecuteAsync(
                request);

        Assert.Equal(
            AgentExecutionStatus.Completed,
            result.Status);

        // Verification of repository facts (build pass, test pass, verification status)
        // is external and deterministic; the result does not contain verification evidence or workflow status.
        Assert.Null(
            typeof(AgentExecutionResult).GetProperty("VerificationStatus"));
        Assert.Null(
            typeof(AgentExecutionResult).GetProperty("IsStepCompleted"));
        Assert.Null(
            typeof(AgentExecutionResult).GetProperty("Evidence"));
    }

    [Fact]
    public void FakeExecutor_ExposesCapabilities()
    {
        AgentCapabilitySet expectedCapabilities =
            new([new AgentCapability("text"), new AgentCapability("tools")]);

        IAgentExecutor executor =
            new FakeAgentExecutor(
                new AgentProviderId("antigravity"),
                (request_, cancellationToken_) =>
                    Task.FromResult(
                        AgentExecutionResult.Completed("ok")),
                expectedCapabilities);

        Assert.Equal(
            expectedCapabilities,
            executor.Capabilities);

        Assert.True(
            executor.Capabilities.Supports(new AgentCapability("text")));
        Assert.True(
            executor.Capabilities.Supports(new AgentCapability("tools")));
        Assert.False(
            executor.Capabilities.Supports(new AgentCapability("session.resume")));
    }

    [Fact]
    public void FakeExecutor_DefaultsToEmptyCapabilitiesWhenNoneProvided()
    {
        IAgentExecutor executor =
            new FakeAgentExecutor(
                new AgentProviderId("antigravity"),
                (request_, cancellationToken_) =>
                    Task.FromResult(
                        AgentExecutionResult.Completed("ok")));

        Assert.NotNull(
            executor.Capabilities);
        Assert.Empty(
            executor.Capabilities);
        Assert.Equal(
            AgentCapabilitySet.Empty,
            executor.Capabilities);
    }

    [Fact]
    public void IAgentExecutor_CapabilitiesProperty_IsSynchronousMetadata()
    {
        PropertyInfo? capabilitiesProperty =
            typeof(IAgentExecutor).GetProperty("Capabilities");

        Assert.NotNull(
            capabilitiesProperty);

        Assert.Equal(
            typeof(AgentCapabilitySet),
            capabilitiesProperty.PropertyType);

        Assert.True(
            capabilitiesProperty.CanRead);
        Assert.False(
            capabilitiesProperty.CanWrite);

        MethodInfo[] methods =
            typeof(IAgentExecutor).GetMethods();

        foreach (MethodInfo method in methods)
        {
            Assert.DoesNotContain(
                "Discover",
                method.Name,
                StringComparison.OrdinalIgnoreCase);

            if (method.Name.Contains("Capability", StringComparison.OrdinalIgnoreCase))
            {
                Assert.False(
                    typeof(Task).IsAssignableFrom(method.ReturnType),
                    $"Capability member '{method.Name}' must be synchronous.");
            }
        }
    }
}

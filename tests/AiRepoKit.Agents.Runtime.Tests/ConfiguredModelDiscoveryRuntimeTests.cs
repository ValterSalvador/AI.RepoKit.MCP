namespace AiRepoKit.Agents.Runtime.Tests;

using System.Collections;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ConfiguredModelDiscoveryRuntimeTests
{
    private static readonly AgentCapabilitySet _defaultCapabilities = AgentCapabilitySet.Empty;

    private static ModelRuntimeRegistration CreateRegistration(
        string providerId_,
        string modelId_,
        Func<CancellationToken, ValueTask<ModelHealthStatus>>? healthProbe_ = null,
        IModelExecutionRuntime? runtime_ = null)
    {
        return new ModelRuntimeRegistration(
            new AgentProviderId(providerId_),
            modelId_,
            _defaultCapabilities,
            runtime_ ?? new TestModelExecutionRuntime(),
            healthProbe_);
    }

    [Fact]
    public void Constructor_NullRegistrations_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ConfiguredModelDiscoveryRuntime(null!));
    }

    [Fact]
    public void Constructor_EmptyRegistrations_Accepted()
    {
        ConfiguredModelDiscoveryRuntime runtime = new([]);
        Assert.Empty(runtime.Discover());
    }

    [Fact]
    public void Constructor_NullElementInRegistrations_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new ConfiguredModelDiscoveryRuntime([null!]));
    }

    [Fact]
    public void Constructor_EnumeratesInputExactlyOnce()
    {
        ModelRuntimeRegistration reg = CreateRegistration("provider-a", "model-1");
        CountingEnumerable<ModelRuntimeRegistration> countingEnumerable = new([reg]);

        ConfiguredModelDiscoveryRuntime runtime = new(countingEnumerable);

        Assert.Equal(1, countingEnumerable.EnumerationCount);
        Assert.Single(runtime.Discover());
    }

    [Fact]
    public void Constructor_DuplicateProviderIdAndModelId_ThrowsArgumentException()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider-a", "model-1");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider-a", "model-1");

        Assert.Throws<ArgumentException>(
            () => new ConfiguredModelDiscoveryRuntime([reg1, reg2]));
    }

    [Fact]
    public void Constructor_DuplicateAcrossDifferentProviderIdInstancesWithSameValue_ThrowsArgumentException()
    {
        AgentProviderId provider1 = new("provider-a");
        AgentProviderId provider2 = new("provider-a");

        Assert.NotSame(provider1, provider2);
        Assert.Equal(provider1.Value, provider2.Value);

        ModelRuntimeRegistration reg1 = new(
            provider1,
            "model-1",
            _defaultCapabilities,
            new TestModelExecutionRuntime());

        ModelRuntimeRegistration reg2 = new(
            provider2,
            "model-1",
            _defaultCapabilities,
            new TestModelExecutionRuntime());

        Assert.Throws<ArgumentException>(
            () => new ConfiguredModelDiscoveryRuntime([reg1, reg2]));
    }

    [Fact]
    public void Constructor_SameModelIdUnderDifferentProviders_Allowed()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider-a", "model-common");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider-b", "model-common");

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2]);

        Assert.Equal(2, runtime.Discover().Count);
    }

    [Fact]
    public void Constructor_DifferentModelIdsUnderSameProvider_Allowed()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider-a", "model-1");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider-a", "model-2");

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2]);

        Assert.Equal(2, runtime.Discover().Count);
    }

    [Fact]
    public void Constructor_CaseDistinctModelIds_AreDistinctAndAllowed()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider-a", "gpt-4");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider-a", "GPT-4");

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2]);

        Assert.Equal(2, runtime.Discover().Count);
    }

    [Fact]
    public void Discover_EmptyInventory_ReturnsEmptyList()
    {
        ConfiguredModelDiscoveryRuntime runtime = new([]);

        IReadOnlyList<ModelRuntimeRegistration> discovered = runtime.Discover();

        Assert.NotNull(discovered);
        Assert.Empty(discovered);
    }

    [Fact]
    public void Discover_SortsByProviderIdValueOrdinalThenModelIdOrdinal()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider-b", "model-z");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider-b", "model-a");
        ModelRuntimeRegistration reg3 = CreateRegistration("provider-a", "model-2");
        ModelRuntimeRegistration reg4 = CreateRegistration("provider-a", "model-1");

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2, reg3, reg4]);
        IReadOnlyList<ModelRuntimeRegistration> discovered = runtime.Discover();

        Assert.Equal(4, discovered.Count);
        Assert.Same(reg4, discovered[0]); // provider-a, model-1
        Assert.Same(reg3, discovered[1]); // provider-a, model-2
        Assert.Same(reg2, discovered[2]); // provider-b, model-a
        Assert.Same(reg1, discovered[3]); // provider-b, model-z
    }

    [Fact]
    public void Discover_RetainsExactRegistrationReferences()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider-a", "model-1");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider-b", "model-2");

        ConfiguredModelDiscoveryRuntime runtime = new([reg2, reg1]);
        IReadOnlyList<ModelRuntimeRegistration> discovered = runtime.Discover();

        Assert.Same(reg1, discovered[0]);
        Assert.Same(reg2, discovered[1]);
    }

    [Fact]
    public void Discover_RepeatedCalls_ReturnSameCachedInstance()
    {
        ModelRuntimeRegistration reg = CreateRegistration("provider-a", "model-1");
        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        IReadOnlyList<ModelRuntimeRegistration> first = runtime.Discover();
        IReadOnlyList<ModelRuntimeRegistration> second = runtime.Discover();

        Assert.Same(first, second);
    }

    [Fact]
    public void Discover_ReturnedCollection_CannotBeMutated()
    {
        ModelRuntimeRegistration reg = CreateRegistration("provider-a", "model-1");
        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        IReadOnlyList<ModelRuntimeRegistration> discovered = runtime.Discover();

        Assert.False(discovered is List<ModelRuntimeRegistration>);
        Assert.False(discovered is ModelRuntimeRegistration[]);

        IList<ModelRuntimeRegistration> list = Assert.IsAssignableFrom<IList<ModelRuntimeRegistration>>(discovered);
        Assert.True(list.IsReadOnly);

        ModelRuntimeRegistration dummy = CreateRegistration("provider-b", "model-2");
        Assert.Throws<NotSupportedException>(() => list.Add(dummy));
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }

    [Fact]
    public void Discover_InvokesZeroHealthCallbacks()
    {
        int probeCalls = 0;
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ =>
            {
                probeCalls++;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        _ = runtime.Discover();

        Assert.Equal(0, probeCalls);
    }

    [Fact]
    public void Discover_InvokesZeroModelRuntimeExecutions()
    {
        TestModelExecutionRuntime testRuntime = new();
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            runtime_: testRuntime);

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        _ = runtime.Discover();

        Assert.Equal(0, testRuntime.InvocationCount);
    }

    [Fact]
    public void Discover_DoesNotDependOnInputOrdering()
    {
        ModelRuntimeRegistration regA = CreateRegistration("provider-a", "model-1");
        ModelRuntimeRegistration regB = CreateRegistration("provider-b", "model-2");

        ConfiguredModelDiscoveryRuntime runtime1 = new([regA, regB]);
        ConfiguredModelDiscoveryRuntime runtime2 = new([regB, regA]);

        IReadOnlyList<ModelRuntimeRegistration> list1 = runtime1.Discover();
        IReadOnlyList<ModelRuntimeRegistration> list2 = runtime2.Discover();

        Assert.Equal(list1.Count, list2.Count);
        for (int i = 0; i < list1.Count; i++)
        {
            Assert.Same(list1[i], list2[i]);
        }
    }

    [Fact]
    public async Task CheckHealthAsync_EmptyInventory_ReturnsEmptyList()
    {
        ConfiguredModelDiscoveryRuntime runtime = new([]);

        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.NotNull(snapshots);
        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task CheckHealthAsync_NoProbeRegistration_ReturnsUnknown()
    {
        ModelRuntimeRegistration reg = CreateRegistration("provider-a", "model-1", healthProbe_: null);
        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.Single(snapshots);
        Assert.Equal("provider-a", snapshots[0].ProviderId.Value);
        Assert.Equal("model-1", snapshots[0].ModelId);
        Assert.Equal(ModelHealthStatus.Unknown, snapshots[0].Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ExplicitHealthy_ReturnsHealthy()
    {
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => ValueTask.FromResult(ModelHealthStatus.Healthy));

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.Single(snapshots);
        Assert.Equal(ModelHealthStatus.Healthy, snapshots[0].Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ExplicitUnhealthy_ReturnsUnhealthy()
    {
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => ValueTask.FromResult(ModelHealthStatus.Unhealthy));

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.Single(snapshots);
        Assert.Equal(ModelHealthStatus.Unhealthy, snapshots[0].Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ExplicitUnknown_ReturnsUnknown()
    {
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => ValueTask.FromResult(ModelHealthStatus.Unknown));

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.Single(snapshots);
        Assert.Equal(ModelHealthStatus.Unknown, snapshots[0].Status);
    }

    [Fact]
    public async Task CheckHealthAsync_CallbackReceivesExactCallerCancellationToken()
    {
        using CancellationTokenSource cts = new();
        CancellationToken expectedToken = cts.Token;
        CancellationToken receivedToken = default;

        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            ct =>
            {
                receivedToken = ct;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        await runtime.CheckHealthAsync(expectedToken);

        Assert.Equal(expectedToken, receivedToken);
    }

    [Fact]
    public async Task CheckHealthAsync_NonCancellationCallbackException_ReturnsUnhealthy()
    {
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => throw new InvalidOperationException("Probe failed unexpectedly."));

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.Single(snapshots);
        Assert.Equal(ModelHealthStatus.Unhealthy, snapshots[0].Status);
    }

    [Fact]
    public async Task CheckHealthAsync_CallbackCreatedOperationCanceledExceptionWhenCallerTokenNotCanceled_ReturnsUnhealthy()
    {
        using CancellationTokenSource cts = new();

        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => throw new OperationCanceledException("Internal callback cancellation token expired."));

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync(cts.Token);

        Assert.Single(snapshots);
        Assert.Equal(ModelHealthStatus.Unhealthy, snapshots[0].Status);
    }

    [Fact]
    public async Task CheckHealthAsync_CallerTokenPreCanceled_ThrowsOperationCanceledExceptionAndInvokesZeroProbes()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        int probeCalls = 0;
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ =>
            {
                probeCalls++;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.CheckHealthAsync(cts.Token));

        Assert.Equal(0, probeCalls);
    }

    [Fact]
    public async Task CheckHealthAsync_CallerCancellationDuringProbe_PropagatesOperationCanceledException()
    {
        using CancellationTokenSource cts = new();

        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            ct =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.CheckHealthAsync(cts.Token));
    }

    [Fact]
    public async Task CheckHealthAsync_CallerCancellationPreventsLaterProbesFromStarting()
    {
        using CancellationTokenSource cts = new();

        int probe1Calls = 0;
        int probe2Calls = 0;

        ModelRuntimeRegistration reg1 = CreateRegistration(
            "provider-a",
            "model-1",
            ct =>
            {
                probe1Calls++;
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ModelRuntimeRegistration reg2 = CreateRegistration(
            "provider-a",
            "model-2",
            _ =>
            {
                probe2Calls++;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.CheckHealthAsync(cts.Token));

        Assert.Equal(1, probe1Calls);
        Assert.Equal(0, probe2Calls);
    }

    [Fact]
    public async Task CheckHealthAsync_ProbesExecuteSequentially()
    {
        TaskCompletionSource probe1StartedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ModelHealthStatus> probe1CompleteTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        bool probe1Active = false;
        bool probe2Started = false;
        bool overlapDetected = false;

        ModelRuntimeRegistration reg1 = CreateRegistration(
            "provider-a",
            "model-1",
            async _ =>
            {
                probe1Active = true;
                probe1StartedTcs.SetResult();
                ModelHealthStatus status = await probe1CompleteTcs.Task;
                probe1Active = false;
                return status;
            });

        ModelRuntimeRegistration reg2 = CreateRegistration(
            "provider-a",
            "model-2",
            _ =>
            {
                if (probe1Active)
                {
                    overlapDetected = true;
                }

                probe2Started = true;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2]);

        Task<IReadOnlyList<ModelHealthSnapshot>> healthTask = runtime.CheckHealthAsync();

        await probe1StartedTcs.Task;

        Assert.False(probe2Started);

        probe1CompleteTcs.SetResult(ModelHealthStatus.Healthy);

        IReadOnlyList<ModelHealthSnapshot> snapshots = await healthTask;

        Assert.False(overlapDetected);
        Assert.True(probe2Started);
        Assert.Equal(2, snapshots.Count);
    }

    [Fact]
    public async Task CheckHealthAsync_EachConfiguredProbeInvokedOncePerHealthCall()
    {
        int probe1Count = 0;
        int probe2Count = 0;

        ModelRuntimeRegistration reg1 = CreateRegistration(
            "provider-a",
            "model-1",
            _ =>
            {
                probe1Count++;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ModelRuntimeRegistration reg2 = CreateRegistration(
            "provider-a",
            "model-2",
            _ =>
            {
                probe2Count++;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2]);
        await runtime.CheckHealthAsync();

        Assert.Equal(1, probe1Count);
        Assert.Equal(1, probe2Count);
    }

    [Fact]
    public async Task CheckHealthAsync_RepeatedCalls_InvokeProbesAgainWithoutCaching()
    {
        int probeCount = 0;
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ =>
            {
                probeCount++;
                return ValueTask.FromResult(ModelHealthStatus.Healthy);
            });

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        await runtime.CheckHealthAsync();
        Assert.Equal(1, probeCount);

        await runtime.CheckHealthAsync();
        Assert.Equal(2, probeCount);
    }

    [Fact]
    public async Task CheckHealthAsync_SnapshotOrderingEqualsDiscoverOrdering()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider-b", "model-z");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider-b", "model-a");
        ModelRuntimeRegistration reg3 = CreateRegistration("provider-a", "model-2");
        ModelRuntimeRegistration reg4 = CreateRegistration("provider-a", "model-1");

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2, reg3, reg4]);

        IReadOnlyList<ModelRuntimeRegistration> discovered = runtime.Discover();
        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.Equal(discovered.Count, snapshots.Count);
        for (int i = 0; i < discovered.Count; i++)
        {
            Assert.Equal(discovered[i].ProviderId.Value, snapshots[i].ProviderId.Value);
            Assert.Equal(discovered[i].ModelId, snapshots[i].ModelId);
        }
    }

    [Fact]
    public async Task CheckHealthAsync_ResultCollection_CannotBeMutated()
    {
        ModelRuntimeRegistration reg = CreateRegistration("provider-a", "model-1");
        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.False(snapshots is List<ModelHealthSnapshot>);
        Assert.False(snapshots is ModelHealthSnapshot[]);

        IList<ModelHealthSnapshot> list = Assert.IsAssignableFrom<IList<ModelHealthSnapshot>>(snapshots);
        Assert.True(list.IsReadOnly);

        ModelHealthSnapshot dummy = new(new AgentProviderId("provider-b"), "model-2", ModelHealthStatus.Healthy);
        Assert.Throws<NotSupportedException>(() => list.Add(dummy));
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }

    [Fact]
    public async Task CheckHealthAsync_InvalidCallbackResult_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => ValueTask.FromResult((ModelHealthStatus)999));

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.CheckHealthAsync());
    }

    [Fact]
    public async Task CheckHealthAsync_InvalidCallbackResult_IsNotNormalizedToUnhealthy()
    {
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => ValueTask.FromResult((ModelHealthStatus)999));

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.CheckHealthAsync());

        Assert.Contains("undefined status value 999", ex.Message);
    }

    [Fact]
    public async Task CheckHealthAsync_OneUnhealthyCallback_DoesNotPreventLaterRegistrationsFromBeingEvaluated()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration(
            "provider-a",
            "model-1",
            _ => throw new InvalidOperationException("Probe 1 failed."));

        ModelRuntimeRegistration reg2 = CreateRegistration(
            "provider-a",
            "model-2",
            _ => ValueTask.FromResult(ModelHealthStatus.Healthy));

        ConfiguredModelDiscoveryRuntime runtime = new([reg1, reg2]);
        IReadOnlyList<ModelHealthSnapshot> snapshots = await runtime.CheckHealthAsync();

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(ModelHealthStatus.Unhealthy, snapshots[0].Status);
        Assert.Equal(ModelHealthStatus.Healthy, snapshots[1].Status);
    }

    [Fact]
    public async Task CheckHealthAsync_NoProbeRegistration_DoesNotInvokeModelRuntime()
    {
        TestModelExecutionRuntime testRuntime = new();
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            healthProbe_: null,
            runtime_: testRuntime);

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        await runtime.CheckHealthAsync();

        Assert.Equal(0, testRuntime.InvocationCount);
    }

    [Fact]
    public async Task CheckHealthAsync_HealthCallbackFailure_DoesNotInvokeModelRuntime()
    {
        TestModelExecutionRuntime testRuntime = new();
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => throw new InvalidOperationException("Probe error."),
            runtime_: testRuntime);

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        await runtime.CheckHealthAsync();

        Assert.Equal(0, testRuntime.InvocationCount);
    }

    [Fact]
    public async Task CheckHealthAsync_SuccessfulCallback_DoesNotInvokeModelRuntime()
    {
        TestModelExecutionRuntime testRuntime = new();
        ModelRuntimeRegistration reg = CreateRegistration(
            "provider-a",
            "model-1",
            _ => ValueTask.FromResult(ModelHealthStatus.Healthy),
            runtime_: testRuntime);

        ConfiguredModelDiscoveryRuntime runtime = new([reg]);
        await runtime.CheckHealthAsync();

        Assert.Equal(0, testRuntime.InvocationCount);
    }

    private sealed class CountingEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _inner;

        public int EnumerationCount
        {
            get;
            private set;
        }

        public CountingEnumerable(IEnumerable<T> inner_)
        {
            this._inner = inner_;
        }

        public IEnumerator<T> GetEnumerator()
        {
            this.EnumerationCount++;
            return this._inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}

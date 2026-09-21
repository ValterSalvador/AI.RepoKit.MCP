namespace AiRepoKit.Agents.Runtime.Tests;

using System.Collections.ObjectModel;
using System.Reflection;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class DeterministicModelRoutingRuntimeTests
{
    private static readonly AgentCapability _capStreaming = new("streaming");
    private static readonly AgentCapability _capTools = new("tools");
    private static readonly AgentCapability _capVision = new("vision");

    private static ModelRuntimeRegistration CreateRegistration(
        string providerId_,
        string modelId_,
        AgentCapabilitySet? capabilities_ = null,
        IModelExecutionRuntime? executionRuntime_ = null)
    {
        return new ModelRuntimeRegistration(
            new AgentProviderId(providerId_),
            modelId_,
            capabilities_ ?? AgentCapabilitySet.Empty,
            executionRuntime_ ?? new TestModelExecutionRuntime());
    }

    private static ModelHealthSnapshot CreateSnapshot(
        string providerId_,
        string modelId_,
        ModelHealthStatus status_)
    {
        return new ModelHealthSnapshot(
            new AgentProviderId(providerId_),
            modelId_,
            status_);
    }

    // 1. Constructor
    [Fact]
    public void Constructor_NullDiscoveryRuntime_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DeterministicModelRoutingRuntime(null!));
    }

    // 2. Route input
    [Fact]
    public async Task RouteAsync_NullRequiredCapabilities_ThrowsArgumentNullException()
    {
        TestModelDiscoveryRuntime discovery = new([], []);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runtime.RouteAsync(null!));
    }

    [Fact]
    public async Task RouteAsync_CallerPreCancellation_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        TestModelDiscoveryRuntime discovery = new([], []);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty, cts.Token));
    }

    [Fact]
    public async Task RouteAsync_CallerPreCancellation_InvokesZeroDiscoverAndZeroHealth()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        TestModelDiscoveryRuntime discovery = new([], []);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        try
        {
            await runtime.RouteAsync(AgentCapabilitySet.Empty, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(0, discovery.DiscoverCount);
        Assert.Equal(0, discovery.CheckHealthCount);
    }

    [Fact]
    public async Task RouteAsync_PassesExactCallerCancellationTokenToHealth()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        using CancellationTokenSource cts = new();
        await runtime.RouteAsync(AgentCapabilitySet.Empty, cts.Token);

        Assert.Equal(cts.Token, discovery.CapturedHealthCancellationToken);
    }

    // 3. Discovery
    [Fact]
    public async Task RouteAsync_DiscoverCalledExactlyOnce_ForNormalRouteCall()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(1, discovery.DiscoverCount);
    }

    [Fact]
    public async Task RouteAsync_EmptyDiscoverResult_ReturnsEmptyReadOnlyRoute()
    {
        TestModelDiscoveryRuntime discovery = new([], []);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Empty(result);
        Assert.Equal(0, discovery.CheckHealthCount);
    }

    [Fact]
    public async Task RouteAsync_NullDiscoverResult_ThrowsInvalidOperationException()
    {
        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => null!);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_NullRegistrationElement_ThrowsInvalidOperationException()
    {
        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => new ModelRuntimeRegistration[] { null! });
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_DuplicateSameProviderIdValueAndSameModelId_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "m1");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "m1");

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg1, reg2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_DuplicateIdentityWithDistinctProviderIdInstances_ThrowsInvalidOperationException()
    {
        AgentProviderId prov1 = new("prov");
        AgentProviderId prov2 = new("prov");
        Assert.NotSame(prov1, prov2);

        ModelRuntimeRegistration reg1 = new(
            prov1,
            "model-1",
            AgentCapabilitySet.Empty,
            new TestModelExecutionRuntime());
        ModelRuntimeRegistration reg2 = new(
            prov2,
            "model-1",
            AgentCapabilitySet.Empty,
            new TestModelExecutionRuntime());

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg1, reg2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_SameModelIdUnderDifferentProviders_Allowed()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "common-model");
        ModelRuntimeRegistration reg2 = CreateRegistration("p2", "common-model");
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "common-model", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p2", "common-model", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task RouteAsync_DifferentModelIdsUnderOneProvider_Allowed()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "model-a");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "model-b");
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "model-a", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "model-b", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task RouteAsync_CaseDistinctModelIds_RemainDistinct()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "model");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "MODEL");
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "model", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "MODEL", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, result.Count);
    }

    // 4. Capability coverage
    [Fact]
    public async Task RouteAsync_AgentCapabilitySetEmpty_AcceptsAllRegistrations()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "m1", new AgentCapabilitySet([_capStreaming]));
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "m2", AgentCapabilitySet.Empty);
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "m2", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task RouteAsync_ExactCapabilitySetMatch_Accepted()
    {
        AgentCapabilitySet caps = new([_capStreaming, _capTools]);
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1", caps);
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(caps);

        Assert.Single(result);
        Assert.Same(reg, result[0].Registration);
    }

    [Fact]
    public async Task RouteAsync_CapabilitySupersetMatch_Accepted()
    {
        AgentCapabilitySet regCaps = new([_capStreaming, _capTools, _capVision]);
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1", regCaps);
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        AgentCapabilitySet requiredCaps = new([_capStreaming, _capTools]);
        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(requiredCaps);

        Assert.Single(result);
        Assert.Same(reg, result[0].Registration);
    }

    [Fact]
    public async Task RouteAsync_MissingOneRequiredCapability_ExcludesRegistration()
    {
        AgentCapabilitySet regCaps = new([_capStreaming]);
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1", regCaps);

        TestModelDiscoveryRuntime discovery = new([reg], []);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        AgentCapabilitySet requiredCaps = new([_capStreaming, _capTools]);
        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(requiredCaps);

        Assert.Empty(result);
    }

    [Fact]
    public async Task RouteAsync_MultipleRequiredCapabilities_RequiresAll()
    {
        ModelRuntimeRegistration regAll = CreateRegistration("p1", "m-all", new AgentCapabilitySet([_capStreaming, _capTools, _capVision]));
        ModelRuntimeRegistration regPartial = CreateRegistration("p1", "m-partial", new AgentCapabilitySet([_capStreaming, _capTools]));
        ModelHealthSnapshot snapAll = CreateSnapshot("p1", "m-all", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snapPartial = CreateSnapshot("p1", "m-partial", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([regAll, regPartial], [snapAll, snapPartial]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        AgentCapabilitySet required = new([_capStreaming, _capTools, _capVision]);
        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(required);

        Assert.Single(result);
        Assert.Same(regAll, result[0].Registration);
    }

    [Fact]
    public async Task RouteAsync_IncompatibleRegistrations_DoNotBecomeCandidatesRegardlessOfHealth()
    {
        ModelRuntimeRegistration regIncompatible = CreateRegistration("p1", "m-incomp", new AgentCapabilitySet([_capStreaming]));
        ModelRuntimeRegistration regCompatible = CreateRegistration("p1", "m-comp", new AgentCapabilitySet([_capTools]));
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m-incomp", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "m-comp", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([regIncompatible, regCompatible], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(new AgentCapabilitySet([_capTools]));

        Assert.Single(result);
        Assert.Same(regCompatible, result[0].Registration);
    }

    [Fact]
    public async Task RouteAsync_ZeroCompatibleRegistrations_InvokesZeroHealthCalls()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1", new AgentCapabilitySet([_capStreaming]));

        TestModelDiscoveryRuntime discovery = new([reg], []);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> result =
            await runtime.RouteAsync(new AgentCapabilitySet([_capTools]));

        Assert.Empty(result);
        Assert.Equal(0, discovery.CheckHealthCount);
    }

    // 5. Health validation
    [Fact]
    public async Task RouteAsync_AtLeastOneCompatibleRegistration_CallsCheckHealthAsyncExactlyOnce()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(1, discovery.CheckHealthCount);
    }

    [Fact]
    public async Task RouteAsync_NullHealthResult_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg],
            checkHealthHandler_: _ => Task.FromResult<IReadOnlyList<ModelHealthSnapshot>>(null!));
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_NullHealthSnapshotElement_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg],
            checkHealthHandler_: _ => Task.FromResult<IReadOnlyList<ModelHealthSnapshot>>(new ModelHealthSnapshot[] { null! }));
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_DuplicateHealthIdentity_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg],
            checkHealthHandler_: _ => Task.FromResult<IReadOnlyList<ModelHealthSnapshot>>([snap1, snap2]));
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_DiscoveredIdentityMissingFromHealth_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "m1");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "m2");
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_ExtraHealthIdentityAbsentFromDiscovery_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snapExtra = CreateSnapshot("p1", "m2", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1], [snap1, snapExtra]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    [Fact]
    public async Task RouteAsync_HealthIdentitySetValidatedAgainstCompleteDiscoveryInventory_IncludingCapabilityIncompatible()
    {
        ModelRuntimeRegistration regCompatible = CreateRegistration("p1", "m-comp", new AgentCapabilitySet([_capTools]));
        ModelRuntimeRegistration regIncompatible = CreateRegistration("p1", "m-incomp", new AgentCapabilitySet([_capStreaming]));

        // Health returns only the compatible one, omitting the discovered incompatible registration
        ModelHealthSnapshot snapCompatible = CreateSnapshot("p1", "m-comp", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([regCompatible, regIncompatible], [snapCompatible]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(new AgentCapabilitySet([_capTools])));
    }

    [Fact]
    public async Task RouteAsync_HealthOrderIndependent_SortsCorrectlyRegardlessOfHealthSnapshotOrder()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "m1");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "m2");

        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "m2", ModelHealthStatus.Unknown);

        TestModelDiscoveryRuntime discovery1 = new([reg1, reg2], [snap1, snap2]);
        TestModelDiscoveryRuntime discovery2 = new([reg1, reg2], [snap2, snap1]);

        DeterministicModelRoutingRuntime runtime1 = new(discovery1);
        DeterministicModelRoutingRuntime runtime2 = new(discovery2);

        IReadOnlyList<ModelRouteCandidate> route1 = await runtime1.RouteAsync(AgentCapabilitySet.Empty);
        IReadOnlyList<ModelRouteCandidate> route2 = await runtime2.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(route1.Count, route2.Count);
        Assert.Equal(route1[0].Registration.ModelId, route2[0].Registration.ModelId);
        Assert.Equal(route1[1].Registration.ModelId, route2[1].Registration.ModelId);
    }

    [Fact]
    public async Task RouteAsync_RetainsExactRegistrationReferencesAfterHealthJoin()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "m1");
        ModelRuntimeRegistration reg2 = CreateRegistration("p2", "m2");

        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p2", "m2", ModelHealthStatus.Unknown);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Same(reg1, route[0].Registration);
        Assert.Same(reg2, route[1].Registration);
    }

    [Fact]
    public async Task RouteAsync_HealthyCandidate_Eligible()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Single(route);
        Assert.Equal(ModelHealthStatus.Healthy, route[0].HealthStatus);
    }

    [Fact]
    public async Task RouteAsync_UnknownCandidate_Eligible()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Unknown);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Single(route);
        Assert.Equal(ModelHealthStatus.Unknown, route[0].HealthStatus);
    }

    [Fact]
    public async Task RouteAsync_UnhealthyCandidate_Excluded()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Unhealthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Empty(route);
    }

    [Fact]
    public async Task RouteAsync_AllCompatibleCandidatesUnhealthy_ReturnsEmptyRoute()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p1", "m1");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "m2");

        ModelHealthSnapshot snap1 = CreateSnapshot("p1", "m1", ModelHealthStatus.Unhealthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "m2", ModelHealthStatus.Unhealthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Empty(route);
    }

    [Fact]
    public async Task RouteAsync_UndefinedHealthStatusInSnapshot_ThrowsInvalidOperationException()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg],
            checkHealthHandler_: _ =>
            {
                // Reflection to simulate undefined status on unvalidated snapshot
                ModelHealthSnapshot snap = (ModelHealthSnapshot)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ModelHealthSnapshot));
                typeof(ModelHealthSnapshot).GetField("<ProviderId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(snap, new AgentProviderId("p1"));
                typeof(ModelHealthSnapshot).GetField("<ModelId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(snap, "m1");
                typeof(ModelHealthSnapshot).GetField("<Status>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(snap, (ModelHealthStatus)999);
                return Task.FromResult<IReadOnlyList<ModelHealthSnapshot>>([snap]);
            });

        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty));
    }

    // 6. Ordering
    [Fact]
    public async Task RouteAsync_SortsHealthyBeforeUnknown()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("provider", "model-unknown");
        ModelRuntimeRegistration reg2 = CreateRegistration("provider", "model-healthy");

        ModelHealthSnapshot snap1 = CreateSnapshot("provider", "model-unknown", ModelHealthStatus.Unknown);
        ModelHealthSnapshot snap2 = CreateSnapshot("provider", "model-healthy", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, route.Count);
        Assert.Equal("model-healthy", route[0].Registration.ModelId);
        Assert.Equal(ModelHealthStatus.Healthy, route[0].HealthStatus);
        Assert.Equal("model-unknown", route[1].Registration.ModelId);
        Assert.Equal(ModelHealthStatus.Unknown, route[1].HealthStatus);
    }

    [Fact]
    public async Task RouteAsync_UnknownProviderAlphabeticallyEarlierThanHealthy_ComesAfterHealthy()
    {
        ModelRuntimeRegistration regUnknown = CreateRegistration("a-provider", "m1");
        ModelRuntimeRegistration regHealthy = CreateRegistration("z-provider", "m1");

        ModelHealthSnapshot snapUnknown = CreateSnapshot("a-provider", "m1", ModelHealthStatus.Unknown);
        ModelHealthSnapshot snapHealthy = CreateSnapshot("z-provider", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([regUnknown, regHealthy], [snapUnknown, snapHealthy]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, route.Count);
        Assert.Equal("z-provider", route[0].Registration.ProviderId.Value);
        Assert.Equal(ModelHealthStatus.Healthy, route[0].HealthStatus);
        Assert.Equal("a-provider", route[1].Registration.ProviderId.Value);
        Assert.Equal(ModelHealthStatus.Unknown, route[1].HealthStatus);
    }

    [Fact]
    public async Task RouteAsync_SortsByProviderIdOrdinal_WhenHealthTierEqual()
    {
        ModelRuntimeRegistration regZ = CreateRegistration("z-provider", "m1");
        ModelRuntimeRegistration regA = CreateRegistration("a-provider", "m1");

        ModelHealthSnapshot snapZ = CreateSnapshot("z-provider", "m1", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snapA = CreateSnapshot("a-provider", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([regZ, regA], [snapZ, snapA]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, route.Count);
        Assert.Equal("a-provider", route[0].Registration.ProviderId.Value);
        Assert.Equal("z-provider", route[1].Registration.ProviderId.Value);
    }

    [Fact]
    public async Task RouteAsync_SortsByModelIdOrdinal_WhenHealthTierAndProviderEqual()
    {
        ModelRuntimeRegistration regZ = CreateRegistration("provider", "z-model");
        ModelRuntimeRegistration regA = CreateRegistration("provider", "a-model");

        ModelHealthSnapshot snapZ = CreateSnapshot("provider", "z-model", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snapA = CreateSnapshot("provider", "a-model", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([regZ, regA], [snapZ, snapA]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(2, route.Count);
        Assert.Equal("a-model", route[0].Registration.ModelId);
        Assert.Equal("z-model", route[1].Registration.ModelId);
    }

    [Fact]
    public async Task RouteAsync_DiscoveryInputOrder_DoesNotAffectFinalRouteOrder()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p2", "m2");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "m1");

        ModelHealthSnapshot snap1 = CreateSnapshot("p2", "m2", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discoveryForward = new([reg1, reg2], [snap1, snap2]);
        TestModelDiscoveryRuntime discoveryReverse = new([reg2, reg1], [snap2, snap1]);

        DeterministicModelRoutingRuntime runtimeForward = new(discoveryForward);
        DeterministicModelRoutingRuntime runtimeReverse = new(discoveryReverse);

        IReadOnlyList<ModelRouteCandidate> routeForward = await runtimeForward.RouteAsync(AgentCapabilitySet.Empty);
        IReadOnlyList<ModelRouteCandidate> routeReverse = await runtimeReverse.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal("p1", routeForward[0].Registration.ProviderId.Value);
        Assert.Equal("p2", routeForward[1].Registration.ProviderId.Value);

        Assert.Equal("p1", routeReverse[0].Registration.ProviderId.Value);
        Assert.Equal("p2", routeReverse[1].Registration.ProviderId.Value);
    }

    [Fact]
    public async Task RouteAsync_RepeatedCallsWithEquivalentData_ProducesIdenticalRouteOrder()
    {
        ModelRuntimeRegistration reg1 = CreateRegistration("p2", "m2");
        ModelRuntimeRegistration reg2 = CreateRegistration("p1", "m1");

        ModelHealthSnapshot snap1 = CreateSnapshot("p2", "m2", ModelHealthStatus.Healthy);
        ModelHealthSnapshot snap2 = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg1, reg2], [snap1, snap2]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route1 = await runtime.RouteAsync(AgentCapabilitySet.Empty);
        IReadOnlyList<ModelRouteCandidate> route2 = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Equal(route1.Count, route2.Count);
        for (int i = 0; i < route1.Count; i++)
        {
            Assert.Same(route1[i].Registration, route2[i].Registration);
            Assert.Equal(route1[i].HealthStatus, route2[i].HealthStatus);
        }
    }

    // 7. Immutability
    [Fact]
    public async Task RouteAsync_EmptyRoute_IsReadOnlyAndCannotBeDowncastOrMutated()
    {
        TestModelDiscoveryRuntime discovery = new([], []);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Empty(route);
        Assert.False(route is List<ModelRouteCandidate>);
        Assert.False(route is ModelRouteCandidate[]);

        ICollection<ModelRouteCandidate> collection =
            Assert.IsAssignableFrom<ICollection<ModelRouteCandidate>>(route);
        Assert.True(collection.IsReadOnly);

        ModelRouteCandidate dummy = new(
            CreateRegistration("p", "m"),
            ModelHealthStatus.Healthy);

        Assert.Throws<NotSupportedException>(
            () => collection.Add(dummy));
    }

    [Fact]
    public async Task RouteAsync_NonEmptyRoute_IsReadOnlyAndCannotBeDowncastOrMutated()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Single(route);
        Assert.False(route is List<ModelRouteCandidate>);
        Assert.False(route is ModelRouteCandidate[]);

        ICollection<ModelRouteCandidate> collection =
            Assert.IsAssignableFrom<ICollection<ModelRouteCandidate>>(route);
        Assert.True(collection.IsReadOnly);

        ModelRouteCandidate dummy = new(
            CreateRegistration("p2", "m2"),
            ModelHealthStatus.Healthy);

        Assert.Throws<NotSupportedException>(
            () => collection.Add(dummy));
    }

    // 8. No cache
    [Fact]
    public async Task RouteAsync_SecondCall_InvokesDiscoverAgain()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await runtime.RouteAsync(AgentCapabilitySet.Empty);
        Assert.Equal(1, discovery.DiscoverCount);

        await runtime.RouteAsync(AgentCapabilitySet.Empty);
        Assert.Equal(2, discovery.DiscoverCount);
    }

    [Fact]
    public async Task RouteAsync_SecondCallWithCompatibleRegistrations_InvokesHealthAgain()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        await runtime.RouteAsync(AgentCapabilitySet.Empty);
        Assert.Equal(1, discovery.CheckHealthCount);

        await runtime.RouteAsync(AgentCapabilitySet.Empty);
        Assert.Equal(2, discovery.CheckHealthCount);
    }

    [Fact]
    public async Task RouteAsync_HealthStatusChangedBetweenCalls_IsObserved()
    {
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");

        int callIndex = 0;
        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg],
            checkHealthHandler_: _ =>
            {
                callIndex++;
                ModelHealthStatus status = callIndex == 1
                    ? ModelHealthStatus.Healthy
                    : ModelHealthStatus.Unhealthy;
                return Task.FromResult<IReadOnlyList<ModelHealthSnapshot>>([CreateSnapshot("p1", "m1", status)]);
            });

        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> first = await runtime.RouteAsync(AgentCapabilitySet.Empty);
        Assert.Single(first);
        Assert.Equal(ModelHealthStatus.Healthy, first[0].HealthStatus);

        IReadOnlyList<ModelRouteCandidate> second = await runtime.RouteAsync(AgentCapabilitySet.Empty);
        Assert.Empty(second);
    }

    // 9. Execution boundary
    [Fact]
    public async Task RouteAsync_InvokesZeroModelExecutionRuntimeExecuteAsync()
    {
        TestModelExecutionRuntime executionRuntime = new();
        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1", executionRuntime_: executionRuntime);
        ModelHealthSnapshot snap = CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy);

        TestModelDiscoveryRuntime discovery = new([reg], [snap]);
        DeterministicModelRoutingRuntime runtime = new(discovery);

        IReadOnlyList<ModelRouteCandidate> route = await runtime.RouteAsync(AgentCapabilitySet.Empty);

        Assert.Single(route);
        Assert.Equal(0, executionRuntime.InvocationCount);
    }

    [Fact]
    public void RouteAsync_HasNoDependencyOnModelSessionOrStreamingRuntime()
    {
        Type routingRuntimeType = typeof(DeterministicModelRoutingRuntime);
        Type interfaceType = typeof(IModelRoutingRuntime);

        Type[] forbiddenTypes =
        [
            typeof(IModelSessionRuntime),
            typeof(IModelStreamingExecutionRuntime)
        ];

        foreach (Type forbidden in forbiddenTypes)
        {
            Assert.False(
                forbidden.IsAssignableFrom(routingRuntimeType),
                $"DeterministicModelRoutingRuntime must not implement {forbidden.Name}");
            Assert.False(
                forbidden.IsAssignableFrom(interfaceType),
                $"IModelRoutingRuntime must not implement {forbidden.Name}");
        }
    }

    // 10. Cancellation
    [Fact]
    public async Task RouteAsync_CancellationAfterDiscoveryBeforeHealth_ThrowsOperationCanceledExceptionAndZeroHealth()
    {
        using CancellationTokenSource cts = new();

        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () =>
            {
                cts.Cancel();
                return [reg];
            },
            checkHealthHandler_: _ =>
                Task.FromResult<IReadOnlyList<ModelHealthSnapshot>>([CreateSnapshot("p1", "m1", ModelHealthStatus.Healthy)]));

        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty, cts.Token));

        Assert.Equal(1, discovery.DiscoverCount);
        Assert.Equal(0, discovery.CheckHealthCount);
    }

    [Fact]
    public async Task RouteAsync_HealthCheckThrowsOperationCanceledException_Propagates()
    {
        using CancellationTokenSource cts = new();

        ModelRuntimeRegistration reg = CreateRegistration("p1", "m1");

        TestModelDiscoveryRuntime discovery = new(
            discoverHandler_: () => [reg],
            checkHealthHandler_: token =>
            {
                token.ThrowIfCancellationRequested();
                throw new OperationCanceledException(token);
            });

        DeterministicModelRoutingRuntime runtime = new(discovery);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.RouteAsync(AgentCapabilitySet.Empty, cts.Token));
    }
}

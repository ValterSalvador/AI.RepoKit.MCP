namespace AiRepoKit.Agents.Runtime;

using System.Collections.ObjectModel;
using AiRepoKit.Agents;

public sealed class DeterministicModelRoutingRuntime :
    IModelRoutingRuntime
{
    private static readonly ReadOnlyCollection<ModelRouteCandidate> _emptyRoute =
        new(Array.Empty<ModelRouteCandidate>());

    private readonly IModelDiscoveryRuntime _discoveryRuntime;

    public DeterministicModelRoutingRuntime(
        IModelDiscoveryRuntime discoveryRuntime_)
    {
        ArgumentNullException.ThrowIfNull(
            discoveryRuntime_,
            nameof(discoveryRuntime_));

        this._discoveryRuntime =
            discoveryRuntime_;
    }

    public async Task<IReadOnlyList<ModelRouteCandidate>> RouteAsync(
        AgentCapabilitySet requiredCapabilities_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            requiredCapabilities_,
            nameof(requiredCapabilities_));

        cancellationToken_.ThrowIfCancellationRequested();

        IReadOnlyList<ModelRuntimeRegistration> discovered =
            this._discoveryRuntime.Discover();

        if (discovered is null)
        {
            throw new InvalidOperationException(
                "Discovery result cannot be null.");
        }

        Dictionary<ProviderModelIdentity, ModelRuntimeRegistration> discoveredMap =
            new(discovered.Count, ProviderModelIdentityComparer.Instance);

        foreach (ModelRuntimeRegistration registration in discovered)
        {
            if (registration is null)
            {
                throw new InvalidOperationException(
                    "Discovered registrations cannot contain null elements.");
            }

            if (registration.ProviderId is null || registration.ModelId is null)
            {
                throw new InvalidOperationException(
                    "Discovered registration contains null provider or model identity.");
            }

            ProviderModelIdentity identity =
                new(registration.ProviderId.Value, registration.ModelId);

            if (!discoveredMap.TryAdd(identity, registration))
            {
                throw new InvalidOperationException(
                    $"Duplicate discovered registration detected for provider '{identity.ProviderId}' and model '{identity.ModelId}'.");
            }
        }

        List<ModelRuntimeRegistration> compatible =
            new();

        foreach (ModelRuntimeRegistration registration in discovered)
        {
            if (registration.Capabilities.SupportsAll(requiredCapabilities_))
            {
                compatible.Add(registration);
            }
        }

        cancellationToken_.ThrowIfCancellationRequested();

        if (compatible.Count == 0)
        {
            return _emptyRoute;
        }

        cancellationToken_.ThrowIfCancellationRequested();

        IReadOnlyList<ModelHealthSnapshot> healthSnapshots =
            await this._discoveryRuntime.CheckHealthAsync(
                cancellationToken_).ConfigureAwait(false);

        if (healthSnapshots is null)
        {
            throw new InvalidOperationException(
                "Health check result cannot be null.");
        }

        Dictionary<ProviderModelIdentity, ModelHealthStatus> healthMap =
            new(healthSnapshots.Count, ProviderModelIdentityComparer.Instance);

        foreach (ModelHealthSnapshot snapshot in healthSnapshots)
        {
            if (snapshot is null)
            {
                throw new InvalidOperationException(
                    "Health snapshots cannot contain null elements.");
            }

            if (snapshot.ProviderId is null || snapshot.ModelId is null)
            {
                throw new InvalidOperationException(
                    "Health snapshot contains null provider or model identity.");
            }

            if (!Enum.IsDefined(
                    typeof(ModelHealthStatus),
                    snapshot.Status))
            {
                throw new InvalidOperationException(
                    $"Health snapshot contains undefined health status value {(int)snapshot.Status}.");
            }

            ProviderModelIdentity identity =
                new(snapshot.ProviderId.Value, snapshot.ModelId);

            if (!discoveredMap.ContainsKey(identity))
            {
                throw new InvalidOperationException(
                    $"Health snapshot contains identity for provider '{identity.ProviderId}' and model '{identity.ModelId}' not present in discovery.");
            }

            if (!healthMap.TryAdd(identity, snapshot.Status))
            {
                throw new InvalidOperationException(
                    $"Duplicate health snapshot detected for provider '{identity.ProviderId}' and model '{identity.ModelId}'.");
            }
        }

        if (healthMap.Count != discoveredMap.Count)
        {
            foreach (ProviderModelIdentity discoveredIdentity in discoveredMap.Keys)
            {
                if (!healthMap.ContainsKey(discoveredIdentity))
                {
                    throw new InvalidOperationException(
                        $"Discovered registration for provider '{discoveredIdentity.ProviderId}' and model '{discoveredIdentity.ModelId}' is missing from health snapshots.");
                }
            }

            throw new InvalidOperationException(
                $"Health snapshot count ({healthMap.Count}) does not match discovery count ({discoveredMap.Count}).");
        }

        List<ModelRouteCandidate> candidates =
            new();

        foreach (ModelRuntimeRegistration registration in compatible)
        {
            ProviderModelIdentity identity =
                new(registration.ProviderId.Value, registration.ModelId);

            ModelHealthStatus healthStatus =
                healthMap[identity];

            if (healthStatus == ModelHealthStatus.Unhealthy)
            {
                continue;
            }

            candidates.Add(
                new ModelRouteCandidate(
                    registration,
                    healthStatus));
        }

        if (candidates.Count == 0)
        {
            return _emptyRoute;
        }

        candidates.Sort(
            static (left, right) =>
            {
                int leftTier =
                    GetHealthTier(left.HealthStatus);
                int rightTier =
                    GetHealthTier(right.HealthStatus);

                if (leftTier != rightTier)
                {
                    return leftTier.CompareTo(rightTier);
                }

                int providerComparison =
                    string.CompareOrdinal(
                        left.Registration.ProviderId.Value,
                        right.Registration.ProviderId.Value);

                if (providerComparison != 0)
                {
                    return providerComparison;
                }

                return string.CompareOrdinal(
                    left.Registration.ModelId,
                    right.Registration.ModelId);
            });

        return candidates.AsReadOnly();
    }

    private static int GetHealthTier(
        ModelHealthStatus status_)
    {
        return status_ switch
        {
            ModelHealthStatus.Healthy => 0,
            ModelHealthStatus.Unknown => 1,
            _ => 2
        };
    }

    private readonly record struct ProviderModelIdentity(
        string ProviderId,
        string ModelId);

    private sealed class ProviderModelIdentityComparer :
        IEqualityComparer<ProviderModelIdentity>
    {
        public static readonly ProviderModelIdentityComparer Instance =
            new();

        public bool Equals(
            ProviderModelIdentity x,
            ProviderModelIdentity y)
        {
            return string.Equals(
                    x.ProviderId,
                    y.ProviderId,
                    StringComparison.Ordinal)
                && string.Equals(
                    x.ModelId,
                    y.ModelId,
                    StringComparison.Ordinal);
        }

        public int GetHashCode(
            ProviderModelIdentity obj)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.ProviderId),
                StringComparer.Ordinal.GetHashCode(obj.ModelId));
        }
    }
}

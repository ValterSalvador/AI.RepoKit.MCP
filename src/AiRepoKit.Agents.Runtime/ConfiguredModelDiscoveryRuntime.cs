namespace AiRepoKit.Agents.Runtime;

using System.Collections.ObjectModel;

public sealed class ConfiguredModelDiscoveryRuntime :
    IModelDiscoveryRuntime
{
    private readonly ReadOnlyCollection<ModelRuntimeRegistration> _registrations;

    public ConfiguredModelDiscoveryRuntime(
        IEnumerable<ModelRuntimeRegistration> registrations_)
    {
        ArgumentNullException.ThrowIfNull(
            registrations_,
            nameof(registrations_));

        Dictionary<string, HashSet<string>> seen =
            new(StringComparer.Ordinal);
        List<ModelRuntimeRegistration> list =
            new();

        foreach (ModelRuntimeRegistration registration in registrations_)
        {
            if (registration is null)
            {
                throw new ArgumentException(
                    "Registrations cannot contain null elements.",
                    nameof(registrations_));
            }

            string providerKey =
                registration.ProviderId.Value;

            if (!seen.TryGetValue(
                    providerKey,
                    out HashSet<string>? models))
            {
                models =
                    new HashSet<string>(StringComparer.Ordinal);
                seen[providerKey] =
                    models;
            }

            if (!models.Add(
                    registration.ModelId))
            {
                throw new ArgumentException(
                    $"Duplicate registration detected for provider '{providerKey}' and model '{registration.ModelId}'.",
                    nameof(registrations_));
            }

            list.Add(
                registration);
        }

        list.Sort(
            static (left, right) =>
            {
                int providerComparison =
                    string.CompareOrdinal(
                        left.ProviderId.Value,
                        right.ProviderId.Value);

                if (providerComparison != 0)
                {
                    return providerComparison;
                }

                return string.CompareOrdinal(
                    left.ModelId,
                    right.ModelId);
            });

        this._registrations =
            list.AsReadOnly();
    }

    public IReadOnlyList<ModelRuntimeRegistration> Discover()
    {
        return this._registrations;
    }

    public async Task<IReadOnlyList<ModelHealthSnapshot>> CheckHealthAsync(
        CancellationToken cancellationToken_ = default)
    {
        cancellationToken_.ThrowIfCancellationRequested();

        List<ModelHealthSnapshot> snapshots =
            new(this._registrations.Count);

        foreach (ModelRuntimeRegistration registration in this._registrations)
        {
            if (!registration.HasHealthProbe)
            {
                snapshots.Add(
                    new ModelHealthSnapshot(
                        registration.ProviderId,
                        registration.ModelId,
                        ModelHealthStatus.Unknown));

                continue;
            }

            cancellationToken_.ThrowIfCancellationRequested();

            ModelHealthStatus status;

            try
            {
                status =
                    await registration.InvokeHealthProbeAsync(
                        cancellationToken_).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken_.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                status =
                    ModelHealthStatus.Unhealthy;
            }

            if (!Enum.IsDefined(
                    typeof(ModelHealthStatus),
                    status))
            {
                throw new InvalidOperationException(
                    $"Health probe for provider '{registration.ProviderId.Value}' and model '{registration.ModelId}' returned undefined status value {(int)status}.");
            }

            snapshots.Add(
                new ModelHealthSnapshot(
                    registration.ProviderId,
                    registration.ModelId,
                    status));
        }

        return snapshots.AsReadOnly();
    }
}

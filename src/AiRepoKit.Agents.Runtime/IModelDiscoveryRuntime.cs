namespace AiRepoKit.Agents.Runtime;

public interface IModelDiscoveryRuntime
{
    IReadOnlyList<ModelRuntimeRegistration> Discover();

    Task<IReadOnlyList<ModelHealthSnapshot>> CheckHealthAsync(
        CancellationToken cancellationToken_ = default);
}

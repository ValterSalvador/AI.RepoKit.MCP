namespace AiRepoKit.Agents.Runtime;

using AiRepoKit.Agents;

public interface IModelRoutingRuntime
{
    Task<IReadOnlyList<ModelRouteCandidate>> RouteAsync(
        AgentCapabilitySet requiredCapabilities_,
        CancellationToken cancellationToken_ = default);
}

using AiRepoKit.Workbench.Core.Models;
using AiRepoKit.Workbench.Core.Policies;

namespace AiRepoKit.Workbench.Core.Routing;

/// <summary>
/// Core contract for the Work Intent Router and Policy & Risk Resolver.
/// Resolves intent, planning mode, risk, capabilities, gates and stages deterministically.
/// </summary>
public interface IWorkIntentRouter
{
    WorkbenchRoutingDecision Route(
        WorkbenchRoutingRequest request_,
        WorkbenchRoutingPolicy? policy_ = null);
}

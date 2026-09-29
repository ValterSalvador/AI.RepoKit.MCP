namespace AiRepoKit.WorkItems;

public interface IWorkItemProvider
{
    string ProviderId { get; }

    Task<WorkItemSnapshot?> GetAsync(
        WorkItemReference reference_,
        CancellationToken cancellationToken_ = default);
}

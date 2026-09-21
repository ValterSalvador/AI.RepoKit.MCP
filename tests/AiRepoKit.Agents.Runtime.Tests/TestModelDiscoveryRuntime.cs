namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents.Runtime;

internal sealed class TestModelDiscoveryRuntime : IModelDiscoveryRuntime
{
    private readonly Func<IReadOnlyList<ModelRuntimeRegistration>>? _discoverHandler;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ModelHealthSnapshot>>>? _checkHealthHandler;

    public int DiscoverCount
    {
        get;
        private set;
    }

    public int CheckHealthCount
    {
        get;
        private set;
    }

    public CancellationToken CapturedHealthCancellationToken
    {
        get;
        private set;
    }

    public TestModelDiscoveryRuntime(
        IEnumerable<ModelRuntimeRegistration>? registrations_ = null,
        IEnumerable<ModelHealthSnapshot>? healthSnapshots_ = null)
    {
        IReadOnlyList<ModelRuntimeRegistration>? regList =
            registrations_?.ToList().AsReadOnly();
        IReadOnlyList<ModelHealthSnapshot>? healthList =
            healthSnapshots_?.ToList().AsReadOnly();

        this._discoverHandler =
            () => regList!;
        this._checkHealthHandler =
            _ => Task.FromResult(healthList!);
    }

    public TestModelDiscoveryRuntime(
        Func<IReadOnlyList<ModelRuntimeRegistration>> discoverHandler_,
        Func<CancellationToken, Task<IReadOnlyList<ModelHealthSnapshot>>>? checkHealthHandler_ = null)
    {
        this._discoverHandler =
            discoverHandler_;
        this._checkHealthHandler =
            checkHealthHandler_;
    }

    public IReadOnlyList<ModelRuntimeRegistration> Discover()
    {
        this.DiscoverCount++;

        if (this._discoverHandler is not null)
        {
            return this._discoverHandler();
        }

        return Array.Empty<ModelRuntimeRegistration>();
    }

    public Task<IReadOnlyList<ModelHealthSnapshot>> CheckHealthAsync(
        CancellationToken cancellationToken_ = default)
    {
        this.CheckHealthCount++;
        this.CapturedHealthCancellationToken =
            cancellationToken_;

        if (this._checkHealthHandler is not null)
        {
            return this._checkHealthHandler(
                cancellationToken_);
        }

        return Task.FromResult<IReadOnlyList<ModelHealthSnapshot>>(
            Array.Empty<ModelHealthSnapshot>());
    }
}

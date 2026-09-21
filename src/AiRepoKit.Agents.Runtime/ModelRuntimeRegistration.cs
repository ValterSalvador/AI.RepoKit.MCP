namespace AiRepoKit.Agents.Runtime;

using AiRepoKit.Agents;

public sealed class ModelRuntimeRegistration
{
    private readonly Func<CancellationToken, ValueTask<ModelHealthStatus>>? _healthProbe;

    public AgentProviderId ProviderId
    {
        get;
    }

    public string ModelId
    {
        get;
    }

    public AgentCapabilitySet Capabilities
    {
        get;
    }

    public IModelExecutionRuntime Runtime
    {
        get;
    }

    internal bool HasHealthProbe
    {
        get
        {
            return this._healthProbe is not null;
        }
    }

    public ModelRuntimeRegistration(
        AgentProviderId providerId_,
        string modelId_,
        AgentCapabilitySet capabilities_,
        IModelExecutionRuntime runtime_,
        Func<CancellationToken, ValueTask<ModelHealthStatus>>? healthProbe_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            providerId_,
            nameof(providerId_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            modelId_,
            nameof(modelId_));

        ArgumentNullException.ThrowIfNull(
            capabilities_,
            nameof(capabilities_));

        ArgumentNullException.ThrowIfNull(
            runtime_,
            nameof(runtime_));

        this.ProviderId =
            providerId_;
        this.ModelId =
            modelId_;
        this.Capabilities =
            capabilities_;
        this.Runtime =
            runtime_;
        this._healthProbe =
            healthProbe_;
    }

    internal ValueTask<ModelHealthStatus> InvokeHealthProbeAsync(
        CancellationToken cancellationToken_)
    {
        if (this._healthProbe is null)
        {
            return ValueTask.FromResult(
                ModelHealthStatus.Unknown);
        }

        return this._healthProbe(
            cancellationToken_);
    }
}

namespace AiRepoKit.Agents.Runtime;

using AiRepoKit.Agents;

public sealed record ModelHealthSnapshot
{
    public AgentProviderId ProviderId
    {
        get;
    }

    public string ModelId
    {
        get;
    }

    public ModelHealthStatus Status
    {
        get;
    }

    public ModelHealthSnapshot(
        AgentProviderId providerId_,
        string modelId_,
        ModelHealthStatus status_)
    {
        ArgumentNullException.ThrowIfNull(
            providerId_,
            nameof(providerId_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            modelId_,
            nameof(modelId_));

        if (!Enum.IsDefined(
                typeof(ModelHealthStatus),
                status_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status_),
                status_,
                $"Status value {(int)status_} is not a defined {nameof(ModelHealthStatus)} value.");
        }

        this.ProviderId =
            providerId_;
        this.ModelId =
            modelId_;
        this.Status =
            status_;
    }
}

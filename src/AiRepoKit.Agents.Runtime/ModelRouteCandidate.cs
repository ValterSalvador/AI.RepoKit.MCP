namespace AiRepoKit.Agents.Runtime;

public sealed record ModelRouteCandidate
{
    public ModelRuntimeRegistration Registration
    {
        get;
    }

    public ModelHealthStatus HealthStatus
    {
        get;
    }

    public ModelRouteCandidate(
        ModelRuntimeRegistration registration_,
        ModelHealthStatus healthStatus_)
    {
        ArgumentNullException.ThrowIfNull(
            registration_,
            nameof(registration_));

        if (!Enum.IsDefined(
                typeof(ModelHealthStatus),
                healthStatus_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(healthStatus_),
                healthStatus_,
                $"HealthStatus value {(int)healthStatus_} is not a defined {nameof(ModelHealthStatus)} value.");
        }

        this.Registration =
            registration_;
        this.HealthStatus =
            healthStatus_;
    }
}

namespace AiRepoKit.Agents.Runtime;

public sealed record ModelRuntimeResiliencePolicy
{
    public int MaxAttemptsPerCandidate
    {
        get;
    }

    public TimeSpan RetryBackoff
    {
        get;
    }

    public ModelRuntimeResiliencePolicy(
        int maxAttemptsPerCandidate_,
        TimeSpan retryBackoff_)
    {
        if (maxAttemptsPerCandidate_ < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttemptsPerCandidate_),
                maxAttemptsPerCandidate_,
                "Max attempts per candidate must be at least 1.");
        }

        if (retryBackoff_ < TimeSpan.Zero || retryBackoff_.TotalMilliseconds > 4294967294.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryBackoff_),
                retryBackoff_,
                "Retry backoff must be non-negative and at most 4294967294 milliseconds.");
        }

        this.MaxAttemptsPerCandidate =
            maxAttemptsPerCandidate_;
        this.RetryBackoff =
            retryBackoff_;
    }
}

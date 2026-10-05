namespace AiRepoKit.WorkflowExecution;

public sealed record WorkflowRepairPolicy
{
    public int MaxAttemptsPerProvider
    {
        get;
    }

    public int MaxTotalAttempts
    {
        get;
    }

    public WorkflowRepairPolicy(
        int maxAttemptsPerProvider_,
        int maxTotalAttempts_)
    {
        if (maxAttemptsPerProvider_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttemptsPerProvider_),
                maxAttemptsPerProvider_,
                "Maximum attempts per provider must be greater than zero.");
        }

        if (maxTotalAttempts_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTotalAttempts_),
                maxTotalAttempts_,
                "Maximum total attempts must be greater than zero.");
        }

        this.MaxAttemptsPerProvider =
            maxAttemptsPerProvider_;
        this.MaxTotalAttempts =
            maxTotalAttempts_;
    }
}

namespace AiRepoKit.WorkflowExecution;

public sealed record WorkflowSchedulerPolicy
{
    public int MaxConcurrentWorkflows
    {
        get;
    }

    public bool AllowUnrestrictedExecution
    {
        get;
    }

    public bool ContinueUntilBlockedOrTerminal
    {
        get;
    }

    public TimeSpan DomainLockTimeout
    {
        get;
    }

    public WorkflowSchedulerPolicy(
        int maxConcurrentWorkflows_,
        bool allowUnrestrictedExecution_ = false,
        bool continueUntilBlockedOrTerminal_ = true,
        TimeSpan? domainLockTimeout_ = null)
    {
        if (maxConcurrentWorkflows_ < 1 || maxConcurrentWorkflows_ > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentWorkflows_),
                maxConcurrentWorkflows_,
                "MaxConcurrentWorkflows must be between 1 and 32, inclusive.");
        }

        TimeSpan timeout =
            domainLockTimeout_ ?? TimeSpan.FromSeconds(5);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domainLockTimeout_),
                timeout,
                "DomainLockTimeout must be greater than zero.");
        }

        this.MaxConcurrentWorkflows =
            maxConcurrentWorkflows_;
        this.AllowUnrestrictedExecution =
            allowUnrestrictedExecution_;
        this.ContinueUntilBlockedOrTerminal =
            continueUntilBlockedOrTerminal_;
        this.DomainLockTimeout =
            timeout;
    }
}

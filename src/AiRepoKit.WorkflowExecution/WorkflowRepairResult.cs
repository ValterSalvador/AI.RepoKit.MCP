namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowRepairResult
{
    public WorkflowState State
    {
        get;
    }

    public IReadOnlyList<WorkflowRepairAttempt> Attempts
    {
        get;
    }

    public bool BudgetExhausted
    {
        get;
    }

    internal WorkflowRepairResult(
        WorkflowState state_,
        IReadOnlyList<WorkflowRepairAttempt> attempts_,
        bool budgetExhausted_)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ArgumentNullException.ThrowIfNull(
            attempts_,
            nameof(attempts_));

        WorkflowRepairAttempt[] snapshot =
            attempts_.ToArray();

        for (int index = 0; index < snapshot.Length; index++)
        {
            if (snapshot[index] is null)
            {
                throw new ArgumentException(
                    "Repair attempt history cannot contain null elements.",
                    nameof(attempts_));
            }
        }

        this.State =
            state_;
        this.Attempts =
            Array.AsReadOnly(
                snapshot);
        this.BudgetExhausted =
            budgetExhausted_;
    }
}

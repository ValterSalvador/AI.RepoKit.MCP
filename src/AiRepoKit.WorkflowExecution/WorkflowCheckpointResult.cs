namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowCheckpointResult
{
    public WorkflowPersistenceSnapshot Snapshot
    {
        get;
    }

    public WorkflowRepairResult RepairResult
    {
        get;
    }

    public bool Resumed
    {
        get;
    }

    public bool ReconciliationUsed
    {
        get;
    }

    internal WorkflowCheckpointResult(
        WorkflowPersistenceSnapshot snapshot_,
        WorkflowRepairResult repairResult_,
        bool resumed_,
        bool reconciliationUsed_)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot_,
            nameof(snapshot_));
        ArgumentNullException.ThrowIfNull(
            repairResult_,
            nameof(repairResult_));

        this.Snapshot =
            snapshot_;
        this.RepairResult =
            repairResult_;
        this.Resumed =
            resumed_;
        this.ReconciliationUsed =
            reconciliationUsed_;
    }
}

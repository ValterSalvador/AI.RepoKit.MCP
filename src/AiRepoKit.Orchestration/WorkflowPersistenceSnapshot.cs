namespace AiRepoKit.Orchestration;

public sealed record WorkflowPersistenceSnapshot
{
    public WorkflowId WorkflowId
    {
        get;
    }

    public long Revision
    {
        get;
    }

    public WorkflowState State
    {
        get;
    }

    public WorkflowExecutionEvent LastEvent
    {
        get;
    }

    internal WorkflowPersistenceSnapshot(
        WorkflowId workflowId_,
        long revision_,
        WorkflowState state_,
        WorkflowExecutionEvent lastEvent_)
    {
        ArgumentNullException.ThrowIfNull(
            workflowId_,
            nameof(workflowId_));

        if (revision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision_),
                revision_,
                "Persistence revision must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));
        ArgumentNullException.ThrowIfNull(
            lastEvent_,
            nameof(lastEvent_));

        if (!workflowId_.Equals(
                lastEvent_.WorkflowId))
        {
            throw new ArgumentException(
                "Snapshot workflow ID must match the last event workflow ID.",
                nameof(lastEvent_));
        }

        if (revision_ !=
            lastEvent_.Sequence)
        {
            throw new ArgumentException(
                "Snapshot revision must equal the last event sequence.",
                nameof(lastEvent_));
        }

        this.WorkflowId =
            workflowId_;
        this.Revision =
            revision_;
        this.State =
            state_;
        this.LastEvent =
            lastEvent_;
    }
}

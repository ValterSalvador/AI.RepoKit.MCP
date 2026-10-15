namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowTaskEvidence
{
    public string TaskId
    {
        get;
    }

    public WorkflowStepStatus StepStatus
    {
        get;
    }

    public IReadOnlyList<WorkflowCheckpointJournalEvidence> CheckpointJournals
    {
        get;
    }

    public IReadOnlyList<WorkflowGateJournalEvidence> GateJournals
    {
        get;
    }

    internal WorkflowTaskEvidence(
        string taskId_,
        WorkflowStepStatus stepStatus_,
        IReadOnlyList<WorkflowCheckpointJournalEvidence> checkpointJournals_,
        IReadOnlyList<WorkflowGateJournalEvidence> gateJournals_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        if (!Enum.IsDefined(typeof(WorkflowStepStatus), stepStatus_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepStatus_),
                stepStatus_,
                "Workflow step status is not defined.");
        }

        ArgumentNullException.ThrowIfNull(
            checkpointJournals_,
            nameof(checkpointJournals_));
        ArgumentNullException.ThrowIfNull(
            gateJournals_,
            nameof(gateJournals_));

        this.TaskId =
            taskId_;
        this.StepStatus =
            stepStatus_;
        this.CheckpointJournals =
            Array.AsReadOnly(
                checkpointJournals_.ToArray());
        this.GateJournals =
            Array.AsReadOnly(
                gateJournals_.ToArray());
    }
}

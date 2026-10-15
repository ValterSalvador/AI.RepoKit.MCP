namespace AiRepoKit.WorkflowExecution;

public sealed record WorkflowCheckpointJournalEvidence
{
    public long BasePersistenceRevision
    {
        get;
    }

    public string BaseWorkflowStateFingerprint
    {
        get;
    }

    public string InputFingerprint
    {
        get;
    }

    public string RegistryFingerprint
    {
        get;
    }

    public bool ExecutionCompleted
    {
        get;
    }

    public string? FinalStateFingerprint
    {
        get;
    }

    public int? AttemptCount
    {
        get;
    }

    public bool? BudgetExhausted
    {
        get;
    }

    public int? ExpectedProjectionTransitionCount
    {
        get;
    }

    public bool ProjectionCompleted
    {
        get;
    }

    public long? FinalPersistenceRevision
    {
        get;
    }

    public IReadOnlyList<WorkflowJournalRecordEvidence> Records
    {
        get;
    }

    internal WorkflowCheckpointJournalEvidence(
        long basePersistenceRevision_,
        string baseWorkflowStateFingerprint_,
        string inputFingerprint_,
        string registryFingerprint_,
        bool executionCompleted_,
        string? finalStateFingerprint_,
        int? attemptCount_,
        bool? budgetExhausted_,
        int? expectedProjectionTransitionCount_,
        bool projectionCompleted_,
        long? finalPersistenceRevision_,
        IReadOnlyList<WorkflowJournalRecordEvidence> records_)
    {
        if (basePersistenceRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basePersistenceRevision_),
                basePersistenceRevision_,
                "Base persistence revision must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            baseWorkflowStateFingerprint_,
            nameof(baseWorkflowStateFingerprint_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            inputFingerprint_,
            nameof(inputFingerprint_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            registryFingerprint_,
            nameof(registryFingerprint_));
        ArgumentNullException.ThrowIfNull(
            records_,
            nameof(records_));

        this.BasePersistenceRevision =
            basePersistenceRevision_;
        this.BaseWorkflowStateFingerprint =
            baseWorkflowStateFingerprint_;
        this.InputFingerprint =
            inputFingerprint_;
        this.RegistryFingerprint =
            registryFingerprint_;
        this.ExecutionCompleted =
            executionCompleted_;
        this.FinalStateFingerprint =
            finalStateFingerprint_;
        this.AttemptCount =
            attemptCount_;
        this.BudgetExhausted =
            budgetExhausted_;
        this.ExpectedProjectionTransitionCount =
            expectedProjectionTransitionCount_;
        this.ProjectionCompleted =
            projectionCompleted_;
        this.FinalPersistenceRevision =
            finalPersistenceRevision_;
        this.Records =
            Array.AsReadOnly(
                records_.ToArray());
    }
}

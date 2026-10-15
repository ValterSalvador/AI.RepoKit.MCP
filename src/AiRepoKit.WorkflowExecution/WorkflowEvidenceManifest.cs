namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowEvidenceManifest
{
    public string SchemaId
    {
        get;
    }

    public int SchemaVersion
    {
        get;
    }

    public string AlgorithmId
    {
        get;
    }

    public string PackId
    {
        get;
    }

    public WorkflowId WorkflowId
    {
        get;
    }

    public int SourceImplementationPlanRevision
    {
        get;
    }

    public long CapturedPersistenceRevision
    {
        get;
    }

    public WorkflowStatus WorkflowStatus
    {
        get;
    }

    public string SourceSetFingerprint
    {
        get;
    }

    public IReadOnlyList<WorkflowEvidenceSourceRecord> SourceRecords
    {
        get;
    }

    public IReadOnlyList<WorkflowEvidenceEvent> Events
    {
        get;
    }

    public IReadOnlyList<WorkflowTaskEvidence> Tasks
    {
        get;
    }

    internal WorkflowEvidenceManifest(
        string schemaId_,
        int schemaVersion_,
        string algorithmId_,
        string packId_,
        WorkflowId workflowId_,
        int sourceImplementationPlanRevision_,
        long capturedPersistenceRevision_,
        WorkflowStatus workflowStatus_,
        string sourceSetFingerprint_,
        IReadOnlyList<WorkflowEvidenceSourceRecord> sourceRecords_,
        IReadOnlyList<WorkflowEvidenceEvent> events_,
        IReadOnlyList<WorkflowTaskEvidence> tasks_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            schemaId_,
            nameof(schemaId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            algorithmId_,
            nameof(algorithmId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            packId_,
            nameof(packId_));
        ArgumentNullException.ThrowIfNull(
            workflowId_,
            nameof(workflowId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceSetFingerprint_,
            nameof(sourceSetFingerprint_));
        ArgumentNullException.ThrowIfNull(
            sourceRecords_,
            nameof(sourceRecords_));
        ArgumentNullException.ThrowIfNull(
            events_,
            nameof(events_));
        ArgumentNullException.ThrowIfNull(
            tasks_,
            nameof(tasks_));

        if (schemaVersion_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion_),
                schemaVersion_,
                "Schema version must be greater than zero.");
        }

        if (sourceImplementationPlanRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceImplementationPlanRevision_),
                sourceImplementationPlanRevision_,
                "Source implementation plan revision must be greater than zero.");
        }

        if (capturedPersistenceRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capturedPersistenceRevision_),
                capturedPersistenceRevision_,
                "Captured persistence revision must be greater than zero.");
        }

        if (!Enum.IsDefined(typeof(WorkflowStatus), workflowStatus_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(workflowStatus_),
                workflowStatus_,
                "Workflow status is not defined.");
        }

        this.SchemaId =
            schemaId_;
        this.SchemaVersion =
            schemaVersion_;
        this.AlgorithmId =
            algorithmId_;
        this.PackId =
            packId_;
        this.WorkflowId =
            workflowId_;
        this.SourceImplementationPlanRevision =
            sourceImplementationPlanRevision_;
        this.CapturedPersistenceRevision =
            capturedPersistenceRevision_;
        this.WorkflowStatus =
            workflowStatus_;
        this.SourceSetFingerprint =
            sourceSetFingerprint_;
        this.SourceRecords =
            Array.AsReadOnly(
                sourceRecords_.ToArray());
        this.Events =
            Array.AsReadOnly(
                events_.ToArray());
        this.Tasks =
            Array.AsReadOnly(
                tasks_.ToArray());
    }
}

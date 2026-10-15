namespace AiRepoKit.WorkflowExecution;

public sealed record WorkflowJournalRecordEvidence
{
    public long JournalSequence
    {
        get;
    }

    public string RecordKind
    {
        get;
    }

    public long? OperationOrdinal
    {
        get;
    }

    public string? OperationId
    {
        get;
    }

    public string? OperationKind
    {
        get;
    }

    public int? InvocationGeneration
    {
        get;
    }

    public string? RequestFingerprint
    {
        get;
    }

    public string? ProviderId
    {
        get;
    }

    public string? ValidationExternalIdentityDigest
    {
        get;
    }

    public bool? IsNullReturn
    {
        get;
    }

    public int? AgentStatus
    {
        get;
    }

    public string? AgentOutputDigest
    {
        get;
    }

    public string? AgentDiagnosticDigest
    {
        get;
    }

    public bool? ValidationPassed
    {
        get;
    }

    public string? ValidationEvidenceDigest
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

    public long? FinalPersistenceRevision
    {
        get;
    }

    internal WorkflowJournalRecordEvidence(
        long journalSequence_,
        string recordKind_,
        long? operationOrdinal_,
        string? operationId_,
        string? operationKind_,
        int? invocationGeneration_,
        string? requestFingerprint_,
        string? providerId_,
        string? validationExternalIdentityDigest_,
        bool? isNullReturn_,
        int? agentStatus_,
        string? agentOutputDigest_,
        string? agentDiagnosticDigest_,
        bool? validationPassed_,
        string? validationEvidenceDigest_,
        string? finalStateFingerprint_,
        int? attemptCount_,
        bool? budgetExhausted_,
        int? expectedProjectionTransitionCount_,
        long? finalPersistenceRevision_)
    {
        if (journalSequence_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(journalSequence_),
                journalSequence_,
                "Journal sequence must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            recordKind_,
            nameof(recordKind_));

        this.JournalSequence =
            journalSequence_;
        this.RecordKind =
            recordKind_;
        this.OperationOrdinal =
            operationOrdinal_;
        this.OperationId =
            operationId_;
        this.OperationKind =
            operationKind_;
        this.InvocationGeneration =
            invocationGeneration_;
        this.RequestFingerprint =
            requestFingerprint_;
        this.ProviderId =
            providerId_;
        this.ValidationExternalIdentityDigest =
            validationExternalIdentityDigest_;
        this.IsNullReturn =
            isNullReturn_;
        this.AgentStatus =
            agentStatus_;
        this.AgentOutputDigest =
            agentOutputDigest_;
        this.AgentDiagnosticDigest =
            agentDiagnosticDigest_;
        this.ValidationPassed =
            validationPassed_;
        this.ValidationEvidenceDigest =
            validationEvidenceDigest_;
        this.FinalStateFingerprint =
            finalStateFingerprint_;
        this.AttemptCount =
            attemptCount_;
        this.BudgetExhausted =
            budgetExhausted_;
        this.ExpectedProjectionTransitionCount =
            expectedProjectionTransitionCount_;
        this.FinalPersistenceRevision =
            finalPersistenceRevision_;
    }
}

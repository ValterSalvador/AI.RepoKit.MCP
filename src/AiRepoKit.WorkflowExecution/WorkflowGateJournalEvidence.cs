namespace AiRepoKit.WorkflowExecution;

public sealed record WorkflowGateJournalEvidence
{
    public long BasePersistenceRevision
    {
        get;
    }

    public WorkflowGateChallenge Challenge
    {
        get;
    }

    public bool? Authorized
    {
        get;
    }

    public string? PrincipalId
    {
        get;
    }

    public string? MechanismId
    {
        get;
    }

    public string? EvidenceFingerprint
    {
        get;
    }

    internal WorkflowGateJournalEvidence(
        long basePersistenceRevision_,
        WorkflowGateChallenge challenge_,
        bool? authorized_,
        string? principalId_,
        string? mechanismId_,
        string? evidenceFingerprint_)
    {
        if (basePersistenceRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basePersistenceRevision_),
                basePersistenceRevision_,
                "Base persistence revision must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(
            challenge_,
            nameof(challenge_));

        this.BasePersistenceRevision =
            basePersistenceRevision_;
        this.Challenge =
            challenge_;
        this.Authorized =
            authorized_;
        this.PrincipalId =
            principalId_;
        this.MechanismId =
            mechanismId_;
        this.EvidenceFingerprint =
            evidenceFingerprint_;
    }
}

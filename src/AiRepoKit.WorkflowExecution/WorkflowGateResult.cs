namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowGateResult
{
    public WorkflowGateStatus Status
    {
        get;
    }

    public WorkflowGateChallenge? Challenge
    {
        get;
    }

    public WorkflowCheckpointResult? CheckpointResult
    {
        get;
    }

    public WorkflowPersistenceSnapshot Snapshot
    {
        get;
    }

    internal WorkflowGateResult(
        WorkflowGateStatus status_,
        WorkflowGateChallenge? challenge_,
        WorkflowCheckpointResult? checkpointResult_,
        WorkflowPersistenceSnapshot snapshot_)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot_,
            nameof(snapshot_));

        if (!Enum.IsDefined(typeof(WorkflowGateStatus), status_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status_),
                status_,
                "Workflow gate status must be a defined value.");
        }

        switch (status_)
        {
            case WorkflowGateStatus.Pending:
                if (challenge_ is null)
                {
                    throw new ArgumentException(
                        "Pending result requires a non-null challenge.",
                        nameof(challenge_));
                }

                if (checkpointResult_ is not null)
                {
                    throw new ArgumentException(
                        "Pending result requires a null checkpoint result.",
                        nameof(checkpointResult_));
                }

                break;

            case WorkflowGateStatus.Denied:
                if (challenge_ is null)
                {
                    throw new ArgumentException(
                        "Denied result requires a non-null challenge.",
                        nameof(challenge_));
                }

                if (checkpointResult_ is not null)
                {
                    throw new ArgumentException(
                        "Denied result requires a null checkpoint result.",
                        nameof(checkpointResult_));
                }

                break;

            case WorkflowGateStatus.Approved:
                if (challenge_ is null)
                {
                    throw new ArgumentException(
                        "Approved result requires a non-null challenge.",
                        nameof(challenge_));
                }

                if (checkpointResult_ is null)
                {
                    throw new ArgumentException(
                        "Approved result requires a non-null checkpoint result.",
                        nameof(checkpointResult_));
                }

                if (!ReferenceEquals(snapshot_, checkpointResult_.Snapshot) && !snapshot_.Equals(checkpointResult_.Snapshot))
                {
                    throw new ArgumentException(
                        "Approved result snapshot must equal the checkpoint result snapshot.",
                        nameof(snapshot_));
                }

                break;

            case WorkflowGateStatus.Bypassed:
                if (challenge_ is not null)
                {
                    throw new ArgumentException(
                        "Bypassed result requires a null challenge.",
                        nameof(challenge_));
                }

                if (checkpointResult_ is null)
                {
                    throw new ArgumentException(
                        "Bypassed result requires a non-null checkpoint result.",
                        nameof(checkpointResult_));
                }

                if (!ReferenceEquals(snapshot_, checkpointResult_.Snapshot) && !snapshot_.Equals(checkpointResult_.Snapshot))
                {
                    throw new ArgumentException(
                        "Bypassed result snapshot must equal the checkpoint result snapshot.",
                        nameof(snapshot_));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(status_),
                    status_,
                    "Unexpected workflow gate status.");
        }

        this.Status = status_;
        this.Challenge = challenge_;
        this.CheckpointResult = checkpointResult_;
        this.Snapshot = snapshot_;
    }

    internal static WorkflowGateResult CreatePending(
        WorkflowGateChallenge challenge_,
        WorkflowPersistenceSnapshot snapshot_)
    {
        return new WorkflowGateResult(
            WorkflowGateStatus.Pending,
            challenge_,
            null,
            snapshot_);
    }

    internal static WorkflowGateResult CreateDenied(
        WorkflowGateChallenge challenge_,
        WorkflowPersistenceSnapshot snapshot_)
    {
        return new WorkflowGateResult(
            WorkflowGateStatus.Denied,
            challenge_,
            null,
            snapshot_);
    }

    internal static WorkflowGateResult CreateApproved(
        WorkflowGateChallenge challenge_,
        WorkflowCheckpointResult checkpointResult_)
    {
        ArgumentNullException.ThrowIfNull(
            checkpointResult_,
            nameof(checkpointResult_));

        return new WorkflowGateResult(
            WorkflowGateStatus.Approved,
            challenge_,
            checkpointResult_,
            checkpointResult_.Snapshot);
    }

    internal static WorkflowGateResult CreateBypassed(
        WorkflowCheckpointResult checkpointResult_)
    {
        ArgumentNullException.ThrowIfNull(
            checkpointResult_,
            nameof(checkpointResult_));

        return new WorkflowGateResult(
            WorkflowGateStatus.Bypassed,
            null,
            checkpointResult_,
            checkpointResult_.Snapshot);
    }
}

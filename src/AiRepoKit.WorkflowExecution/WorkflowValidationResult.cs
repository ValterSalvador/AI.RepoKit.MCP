namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowValidationResult
{
    public WorkflowState State
    {
        get;
    }

    public IReadOnlyList<WorkflowValidationEvidence> Evidence
    {
        get;
    }

    internal WorkflowValidationResult(
        WorkflowState state_,
        IReadOnlyList<WorkflowValidationEvidence> evidence_)
    {
        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        ArgumentNullException.ThrowIfNull(
            evidence_,
            nameof(evidence_));

        WorkflowValidationEvidence[] evidenceSnapshot =
            evidence_.ToArray();

        for (int index = 0; index < evidenceSnapshot.Length; index++)
        {
            if (evidenceSnapshot[index] is null)
            {
                throw new ArgumentException(
                    "Validation evidence collection cannot contain null elements.",
                    nameof(evidence_));
            }
        }

        this.State =
            state_;
        this.Evidence =
            Array.AsReadOnly(
                evidenceSnapshot);
    }
}

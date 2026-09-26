namespace AiRepoKit.Orchestration;

public sealed record WorkflowState
{
    public int SourceImplementationPlanRevision
    {
        get;
    }

    public WorkflowStatus Status
    {
        get;
    }

    public IReadOnlyList<WorkflowStepState> Steps
    {
        get;
    }

    internal WorkflowState(
        int sourceImplementationPlanRevision_,
        WorkflowStatus status_,
        IReadOnlyList<WorkflowStepState> steps_)
    {
        if (sourceImplementationPlanRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceImplementationPlanRevision_),
                sourceImplementationPlanRevision_,
                "Source implementation plan revision must be greater than zero.");
        }

        if (!Enum.IsDefined(
                typeof(WorkflowStatus),
                status_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status_),
                status_,
                "Workflow status must be a defined value.");
        }

        ArgumentNullException.ThrowIfNull(
            steps_,
            nameof(steps_));

        WorkflowStepState[] stepSnapshot =
            steps_.ToArray();

        HashSet<string> taskIds =
            new(
                stepSnapshot.Length,
                StringComparer.Ordinal);

        for (int index = 0; index < stepSnapshot.Length; index++)
        {
            WorkflowStepState step =
                stepSnapshot[index];

            if (step is null)
            {
                throw new ArgumentException(
                    "Workflow step collection cannot contain null elements.",
                    nameof(steps_));
            }

            if (!taskIds.Add(step.TaskId))
            {
                throw new ArgumentException(
                    $"Duplicate workflow task identifier detected: '{step.TaskId}'.",
                    nameof(steps_));
            }
        }

        this.SourceImplementationPlanRevision =
            sourceImplementationPlanRevision_;
        this.Status =
            status_;
        this.Steps =
            Array.AsReadOnly(stepSnapshot);
    }

    public bool Equals(
        WorkflowState? other_)
    {
        if (ReferenceEquals(
                this,
                other_))
        {
            return true;
        }

        if (other_ is null)
        {
            return false;
        }

        if (this.SourceImplementationPlanRevision !=
                other_.SourceImplementationPlanRevision ||
            this.Status != other_.Status ||
            this.Steps.Count != other_.Steps.Count)
        {
            return false;
        }

        for (int index = 0; index < this.Steps.Count; index++)
        {
            if (!this.Steps[index].Equals(
                    other_.Steps[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        HashCode hash =
            new();

        hash.Add(
            this.SourceImplementationPlanRevision);
        hash.Add(
            this.Status);

        for (int index = 0; index < this.Steps.Count; index++)
        {
            hash.Add(
                this.Steps[index]);
        }

        return hash.ToHashCode();
    }
}
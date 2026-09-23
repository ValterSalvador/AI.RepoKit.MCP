namespace AiRepoKit.Execution;

using System.Collections.ObjectModel;

public sealed record ExecutableWork
{
    public const string CurrentSchemaId =
        "ai.repokit.executable-work";

    public const int CurrentSchemaVersion =
        1;

    public string SchemaId
    {
        get;
    }

    public int SchemaVersion
    {
        get;
    }

    public int SourceImplementationPlanRevision
    {
        get;
    }

    public IReadOnlyList<ExecutableTask> Tasks
    {
        get;
    }

    public ExecutableWork(
        int sourceImplementationPlanRevision_,
        IReadOnlyList<ExecutableTask> tasks_)
    {
        if (sourceImplementationPlanRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceImplementationPlanRevision_),
                sourceImplementationPlanRevision_,
                "Source implementation plan revision must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(
            tasks_,
            nameof(tasks_));

        ExecutableTask[] snapshot =
            tasks_.ToArray();

        HashSet<string> seenIds =
            new(snapshot.Length, StringComparer.Ordinal);

        for (int i = 0; i < snapshot.Length; i++)
        {
            ExecutableTask task =
                snapshot[i];

            if (task is null)
            {
                throw new ArgumentException(
                    "Task collection cannot contain null elements.",
                    nameof(tasks_));
            }

            if (!seenIds.Add(task.Id))
            {
                throw new ArgumentException(
                    $"Duplicate task identifier detected: '{task.Id}'. Task IDs must be unique.",
                    nameof(tasks_));
            }
        }

        this.SchemaId =
            CurrentSchemaId;
        this.SchemaVersion =
            CurrentSchemaVersion;
        this.SourceImplementationPlanRevision =
            sourceImplementationPlanRevision_;
        this.Tasks =
            Array.AsReadOnly(snapshot);
    }

    public bool Equals(
        ExecutableWork? other_)
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

        if (this.SourceImplementationPlanRevision != other_.SourceImplementationPlanRevision ||
            !string.Equals(this.SchemaId, other_.SchemaId, StringComparison.Ordinal) ||
            this.SchemaVersion != other_.SchemaVersion ||
            this.Tasks.Count != other_.Tasks.Count)
        {
            return false;
        }

        for (int i = 0; i < this.Tasks.Count; i++)
        {
            if (!this.Tasks[i].Equals(other_.Tasks[i]))
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
            this.SchemaId,
            StringComparer.Ordinal);
        hash.Add(
            this.SchemaVersion);
        hash.Add(
            this.SourceImplementationPlanRevision);

        for (int i = 0; i < this.Tasks.Count; i++)
        {
            hash.Add(
                this.Tasks[i]);
        }

        return hash.ToHashCode();
    }
}

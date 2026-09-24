namespace AiRepoKit.Execution;

public sealed record ExecutableWork
{
    public const string CurrentSchemaId =
        "ai.repokit.executable-work";

    public const int CurrentSchemaVersion =
        3;

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

    public IReadOnlyList<ExecutableTaskDependency> Dependencies
    {
        get;
    }

    public IReadOnlyList<ValidationRequirement> ValidationRequirements
    {
        get;
    }

    public ExecutableWork(
        int sourceImplementationPlanRevision_,
        IReadOnlyList<ExecutableTask> tasks_)
        : this(
            sourceImplementationPlanRevision_,
            tasks_,
            Array.Empty<ExecutableTaskDependency>(),
            Array.Empty<ValidationRequirement>())
    {
    }

    public ExecutableWork(
        int sourceImplementationPlanRevision_,
        IReadOnlyList<ExecutableTask> tasks_,
        IReadOnlyList<ExecutableTaskDependency> dependencies_)
        : this(
            sourceImplementationPlanRevision_,
            tasks_,
            dependencies_,
            Array.Empty<ValidationRequirement>())
    {
    }

    public ExecutableWork(
        int sourceImplementationPlanRevision_,
        IReadOnlyList<ExecutableTask> tasks_,
        IReadOnlyList<ExecutableTaskDependency> dependencies_,
        IReadOnlyList<ValidationRequirement> validationRequirements_)
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

        ArgumentNullException.ThrowIfNull(
            dependencies_,
            nameof(dependencies_));

        ArgumentNullException.ThrowIfNull(
            validationRequirements_,
            nameof(validationRequirements_));

        ExecutableTask[] taskSnapshot =
            tasks_.ToArray();

        HashSet<string> taskIds =
            new(
                taskSnapshot.Length,
                StringComparer.Ordinal);

        for (int i = 0; i < taskSnapshot.Length; i++)
        {
            ExecutableTask task =
                taskSnapshot[i];

            if (task is null)
            {
                throw new ArgumentException(
                    "Task collection cannot contain null elements.",
                    nameof(tasks_));
            }

            if (!taskIds.Add(task.Id))
            {
                throw new ArgumentException(
                    $"Duplicate task identifier detected: '{task.Id}'. Task IDs must be unique.",
                    nameof(tasks_));
            }
        }

        ExecutableTaskDependency[] dependencySnapshot =
            dependencies_.ToArray();

        HashSet<(string TaskId, string DependsOnTaskId)> seenEdges =
            new();

        for (int i = 0; i < dependencySnapshot.Length; i++)
        {
            ExecutableTaskDependency dependency =
                dependencySnapshot[i];

            if (dependency is null)
            {
                throw new ArgumentException(
                    "Dependency collection cannot contain null elements.",
                    nameof(dependencies_));
            }

            if (!taskIds.Contains(dependency.TaskId))
            {
                throw new ArgumentException(
                    $"Dependency references unknown task identifier '{dependency.TaskId}'.",
                    nameof(dependencies_));
            }

            if (!taskIds.Contains(dependency.DependsOnTaskId))
            {
                throw new ArgumentException(
                    $"Dependency references unknown prerequisite task identifier '{dependency.DependsOnTaskId}'.",
                    nameof(dependencies_));
            }

            if (!seenEdges.Add(
                    (
                        dependency.TaskId,
                        dependency.DependsOnTaskId
                    )))
            {
                throw new ArgumentException(
                    $"Duplicate dependency edge detected: '{dependency.TaskId}' depends on '{dependency.DependsOnTaskId}'.",
                    nameof(dependencies_));
            }
        }

        if (HasDirectedCycle(
                taskSnapshot,
                dependencySnapshot))
        {
            throw new ArgumentException(
                "Dependency graph must be acyclic.",
                nameof(dependencies_));
        }

        ValidationRequirement[] validationRequirementSnapshot =
            validationRequirements_.ToArray();

        HashSet<string> validationRequirementIds =
            new(
                validationRequirementSnapshot.Length,
                StringComparer.Ordinal);

        for (int i = 0; i < validationRequirementSnapshot.Length; i++)
        {
            ValidationRequirement requirement =
                validationRequirementSnapshot[i];

            if (requirement is null)
            {
                throw new ArgumentException(
                    "Validation requirement collection cannot contain null elements.",
                    nameof(validationRequirements_));
            }

            if (!validationRequirementIds.Add(requirement.Id))
            {
                throw new ArgumentException(
                    $"Duplicate validation requirement identifier detected: '{requirement.Id}'.",
                    nameof(validationRequirements_));
            }

            if (!taskIds.Contains(requirement.TaskId))
            {
                throw new ArgumentException(
                    $"Validation requirement references unknown task identifier '{requirement.TaskId}'.",
                    nameof(validationRequirements_));
            }
        }

        this.SchemaId =
            CurrentSchemaId;
        this.SchemaVersion =
            CurrentSchemaVersion;
        this.SourceImplementationPlanRevision =
            sourceImplementationPlanRevision_;
        this.Tasks =
            Array.AsReadOnly(taskSnapshot);
        this.Dependencies =
            Array.AsReadOnly(dependencySnapshot);
        this.ValidationRequirements =
            Array.AsReadOnly(validationRequirementSnapshot);
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
            this.Tasks.Count != other_.Tasks.Count ||
            this.Dependencies.Count != other_.Dependencies.Count ||
            this.ValidationRequirements.Count != other_.ValidationRequirements.Count)
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

        for (int i = 0; i < this.Dependencies.Count; i++)
        {
            if (!this.Dependencies[i].Equals(other_.Dependencies[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < this.ValidationRequirements.Count; i++)
        {
            if (!this.ValidationRequirements[i].Equals(
                    other_.ValidationRequirements[i]))
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

        for (int i = 0; i < this.Dependencies.Count; i++)
        {
            hash.Add(
                this.Dependencies[i]);
        }

        for (int i = 0; i < this.ValidationRequirements.Count; i++)
        {
            hash.Add(
                this.ValidationRequirements[i]);
        }

        return hash.ToHashCode();
    }

    private static bool HasDirectedCycle(
        IReadOnlyList<ExecutableTask> tasks_,
        IReadOnlyList<ExecutableTaskDependency> dependencies_)
    {
        Dictionary<string, List<string>> successors =
            new(
                tasks_.Count,
                StringComparer.Ordinal);

        for (int i = 0; i < tasks_.Count; i++)
        {
            successors.Add(
                tasks_[i].Id,
                []);
        }

        for (int i = 0; i < dependencies_.Count; i++)
        {
            ExecutableTaskDependency dependency =
                dependencies_[i];

            successors[dependency.DependsOnTaskId].Add(
                dependency.TaskId);
        }

        Dictionary<string, byte> state =
            new(
                tasks_.Count,
                StringComparer.Ordinal);

        for (int i = 0; i < tasks_.Count; i++)
        {
            if (Visit(
                    tasks_[i].Id,
                    successors,
                    state))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Visit(
        string taskId_,
        IReadOnlyDictionary<string, List<string>> successors_,
        IDictionary<string, byte> state_)
    {
        if (state_.TryGetValue(
                taskId_,
                out byte currentState))
        {
            if (currentState == 1)
            {
                return true;
            }

            if (currentState == 2)
            {
                return false;
            }
        }

        state_[taskId_] = 1;

        IReadOnlyList<string> successors =
            successors_[taskId_];

        for (int i = 0; i < successors.Count; i++)
        {
            if (Visit(
                    successors[i],
                    successors_,
                    state_))
            {
                return true;
            }
        }

        state_[taskId_] = 2;

        return false;
    }
}
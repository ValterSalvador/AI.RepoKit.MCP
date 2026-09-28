namespace AiRepoKit.Orchestration;

using AiRepoKit.Execution;

public static class WorkflowTaskDagScheduler
{
    public const string AlgorithmId =
        "ai.repokit.task-dag-scheduler/v1";

    public static IReadOnlyList<string> GetReadyTaskIds(
        ExecutableWork work_,
        WorkflowState state_)
    {
        ValidateAlignment(
            work_,
            state_);

        return GetReadyTaskIdsCore(
            work_,
            state_);
    }

    public static string? SelectNextTaskId(
        ExecutableWork work_,
        WorkflowState state_)
    {
        ValidateAlignment(
            work_,
            state_);

        if (state_.Status !=
            WorkflowStatus.Running)
        {
            return null;
        }

        for (int index = 0;
             index < state_.Steps.Count;
             index++)
        {
            if (SuppressesSelection(
                    state_.Steps[index].Status))
            {
                return null;
            }
        }

        IReadOnlyList<string> readyTaskIds =
            GetReadyTaskIdsCore(
                work_,
                state_);

        if (readyTaskIds.Count == 0)
        {
            return null;
        }

        return readyTaskIds[0];
    }

    private static IReadOnlyList<string> GetReadyTaskIdsCore(
        ExecutableWork work_,
        WorkflowState state_)
    {
        if (state_.Status !=
            WorkflowStatus.Running)
        {
            return Array.AsReadOnly(
                Array.Empty<string>());
        }

        Dictionary<string, WorkflowStepStatus> statusByTaskId =
            new(
                work_.Tasks.Count,
                StringComparer.Ordinal);

        Dictionary<string, List<string>> predecessors =
            new(
                work_.Tasks.Count,
                StringComparer.Ordinal);

        for (int index = 0;
             index < work_.Tasks.Count;
             index++)
        {
            string taskId =
                work_.Tasks[index].Id;

            statusByTaskId.Add(
                taskId,
                state_.Steps[index].Status);

            predecessors.Add(
                taskId,
                []);
        }

        for (int index = 0;
             index < work_.Dependencies.Count;
             index++)
        {
            ExecutableTaskDependency dependency =
                work_.Dependencies[index];

            predecessors[dependency.TaskId].Add(
                dependency.DependsOnTaskId);
        }

        List<string> readyTaskIds =
            [];

        for (int index = 0;
             index < work_.Tasks.Count;
             index++)
        {
            if (state_.Steps[index].Status !=
                WorkflowStepStatus.Pending)
            {
                continue;
            }

            string taskId =
                work_.Tasks[index].Id;

            HashSet<string> visitedAncestors =
                new(
                    StringComparer.Ordinal);

            if (AllDependencyAncestorsCompleted(
                    taskId,
                    predecessors,
                    statusByTaskId,
                    visitedAncestors))
            {
                readyTaskIds.Add(
                    taskId);
            }
        }

        return Array.AsReadOnly(
            readyTaskIds.ToArray());
    }

    private static bool AllDependencyAncestorsCompleted(
        string taskId_,
        IReadOnlyDictionary<string, List<string>> predecessors_,
        IReadOnlyDictionary<string, WorkflowStepStatus> statusByTaskId_,
        ISet<string> visitedAncestors_)
    {
        IReadOnlyList<string> directPredecessors =
            predecessors_[taskId_];

        for (int index = 0;
             index < directPredecessors.Count;
             index++)
        {
            string predecessorTaskId =
                directPredecessors[index];

            if (!visitedAncestors_.Add(
                    predecessorTaskId))
            {
                continue;
            }

            if (statusByTaskId_[predecessorTaskId] !=
                WorkflowStepStatus.Completed)
            {
                return false;
            }

            if (!AllDependencyAncestorsCompleted(
                    predecessorTaskId,
                    predecessors_,
                    statusByTaskId_,
                    visitedAncestors_))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SuppressesSelection(
        WorkflowStepStatus status_)
    {
        return
            status_ == WorkflowStepStatus.Running ||
            status_ == WorkflowStepStatus.AwaitingValidation ||
            status_ == WorkflowStepStatus.Failed ||
            status_ == WorkflowStepStatus.Cancelled;
    }

    private static void ValidateAlignment(
        ExecutableWork work_,
        WorkflowState state_)
    {
        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));

        ArgumentNullException.ThrowIfNull(
            state_,
            nameof(state_));

        if (work_.SourceImplementationPlanRevision !=
            state_.SourceImplementationPlanRevision)
        {
            throw new ArgumentException(
                "Workflow state source implementation plan revision does not match executable work.",
                nameof(state_));
        }

        if (work_.Tasks.Count !=
            state_.Steps.Count)
        {
            throw new ArgumentException(
                "Workflow state task count does not match executable work.",
                nameof(state_));
        }

        for (int index = 0;
             index < work_.Tasks.Count;
             index++)
        {
            if (!string.Equals(
                    work_.Tasks[index].Id,
                    state_.Steps[index].TaskId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Workflow state task identity or order does not match executable work.",
                    nameof(state_));
            }
        }
    }
}

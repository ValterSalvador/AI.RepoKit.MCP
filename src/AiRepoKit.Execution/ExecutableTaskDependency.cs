namespace AiRepoKit.Execution;

public sealed record ExecutableTaskDependency
{
    public string TaskId
    {
        get;
    }

    public string DependsOnTaskId
    {
        get;
    }

    public ExecutableTaskDependency(
        string taskId_,
        string dependsOnTaskId_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            dependsOnTaskId_,
            nameof(dependsOnTaskId_));

        if (string.Equals(
                taskId_,
                dependsOnTaskId_,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A task cannot depend directly on itself.",
                nameof(dependsOnTaskId_));
        }

        this.TaskId =
            taskId_;
        this.DependsOnTaskId =
            dependsOnTaskId_;
    }
}
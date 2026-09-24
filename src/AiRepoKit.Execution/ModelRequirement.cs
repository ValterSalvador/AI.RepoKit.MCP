namespace AiRepoKit.Execution;

using AiRepoKit.Agents;

public sealed record ModelRequirement
{
    public string TaskId
    {
        get;
    }

    public AgentCapabilitySet RequiredCapabilities
    {
        get;
    }

    public ModelRequirement(
        string taskId_,
        AgentCapabilitySet requiredCapabilities_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        ArgumentNullException.ThrowIfNull(
            requiredCapabilities_,
            nameof(requiredCapabilities_));

        this.TaskId =
            taskId_;
        this.RequiredCapabilities =
            requiredCapabilities_;
    }
}
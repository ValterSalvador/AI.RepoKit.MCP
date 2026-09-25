namespace AiRepoKit.Execution;

using AiRepoKit.Agents;

public sealed record ExecutionEnvelope
{
    private const long MaxTimeoutMilliseconds =
        4294967294L;

    public string TaskId
    {
        get;
    }

    public CompiledPrompt Prompt
    {
        get;
    }

    public AgentCapabilitySet ModelRequiredCapabilities
    {
        get;
    }

    public AgentCapabilitySet AgentRequiredCapabilities
    {
        get;
    }

    public ExecutionPermission Permission
    {
        get;
    }

    public ExecutionEnvironment Environment
    {
        get;
    }

    public AgentSessionReference? SessionReference
    {
        get;
    }

    public StructuredOutputContract? StructuredOutput
    {
        get;
    }

    public TimeSpan? Timeout
    {
        get;
    }

    private ExecutionEnvelope(
        string taskId_,
        CompiledPrompt prompt_,
        AgentCapabilitySet modelRequiredCapabilities_,
        AgentCapabilitySet agentRequiredCapabilities_,
        ExecutionPermission permission_,
        ExecutionEnvironment environment_,
        AgentSessionReference? sessionReference_,
        StructuredOutputContract? structuredOutput_,
        TimeSpan? timeout_)
    {
        this.TaskId =
            taskId_;
        this.Prompt =
            prompt_;
        this.ModelRequiredCapabilities =
            modelRequiredCapabilities_;
        this.AgentRequiredCapabilities =
            agentRequiredCapabilities_;
        this.Permission =
            permission_;
        this.Environment =
            environment_;
        this.SessionReference =
            sessionReference_;
        this.StructuredOutput =
            structuredOutput_;
        this.Timeout =
            timeout_;
    }

    public static ExecutionEnvelope Create(
        ExecutableWork work_,
        CompiledPrompt prompt_,
        ExecutionPermission permission_,
        ExecutionEnvironment environment_,
        AgentSessionReference? sessionReference_,
        StructuredOutputContract? structuredOutput_,
        TimeSpan? timeout_)
    {
        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));

        ArgumentNullException.ThrowIfNull(
            prompt_,
            nameof(prompt_));

        ArgumentNullException.ThrowIfNull(
            environment_,
            nameof(environment_));

        if (!Enum.IsDefined(
                typeof(ExecutionPermission),
                permission_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(permission_),
                permission_,
                "Execution permission must be a defined value.");
        }

        if (timeout_.HasValue)
        {
            if (timeout_.Value <= TimeSpan.Zero ||
                timeout_.Value.TotalMilliseconds > MaxTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout_),
                    timeout_.Value,
                    $"Timeout must be greater than zero and less than or equal to {MaxTimeoutMilliseconds} milliseconds.");
            }
        }

        bool taskFound =
            false;

        for (
            int index = 0;
            index < work_.Tasks.Count;
            index++)
        {
            if (string.Equals(
                    work_.Tasks[index].Id,
                    prompt_.TaskId,
                    StringComparison.Ordinal))
            {
                taskFound =
                    true;

                break;
            }
        }

        if (!taskFound)
        {
            throw new ArgumentException(
                $"Unknown task identifier '{prompt_.TaskId}'.",
                nameof(prompt_));
        }

        AgentCapabilitySet modelRequiredCapabilities =
            AgentCapabilitySet.Empty;

        for (
            int index = 0;
            index < work_.ModelRequirements.Count;
            index++)
        {
            ModelRequirement requirement =
                work_.ModelRequirements[index];

            if (string.Equals(
                    requirement.TaskId,
                    prompt_.TaskId,
                    StringComparison.Ordinal))
            {
                modelRequiredCapabilities =
                    requirement.RequiredCapabilities;

                break;
            }
        }

        AgentCapabilitySet agentRequiredCapabilities =
            AgentCapabilitySet.Empty;

        for (
            int index = 0;
            index < work_.AgentRequirements.Count;
            index++)
        {
            AgentRequirement requirement =
                work_.AgentRequirements[index];

            if (string.Equals(
                    requirement.TaskId,
                    prompt_.TaskId,
                    StringComparison.Ordinal))
            {
                agentRequiredCapabilities =
                    requirement.RequiredCapabilities;

                break;
            }
        }

        return new ExecutionEnvelope(
            prompt_.TaskId,
            prompt_,
            modelRequiredCapabilities,
            agentRequiredCapabilities,
            permission_,
            environment_,
            sessionReference_,
            structuredOutput_,
            timeout_);
    }
}

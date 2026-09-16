namespace AiRepoKit.Agents;

public sealed record AgentExecutionRequest
{
    public string Instruction
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

    public AgentExecutionRequest(
        string instruction_,
        ExecutionPermission permission_,
        ExecutionEnvironment environment_,
        AgentSessionReference? sessionReference_ = null,
        StructuredOutputContract? structuredOutput_ = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            instruction_,
            nameof(instruction_));

        if (!Enum.IsDefined(
                permission_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(permission_),
                permission_,
                "Execution permission must be a defined ExecutionPermission value.");
        }

        ArgumentNullException.ThrowIfNull(
            environment_,
            nameof(environment_));

        this.Instruction =
            instruction_;
        this.Permission =
            permission_;
        this.Environment =
            environment_;
        this.SessionReference =
            sessionReference_;
        this.StructuredOutput =
            structuredOutput_;
    }
}

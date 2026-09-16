namespace AiRepoKit.Agents;

public sealed record AgentExecutionRequest
{
    public string Instruction
    {
        get;
    }

    public AgentSessionReference? SessionReference
    {
        get;
    }

    public AgentExecutionRequest(
        string instruction_,
        AgentSessionReference? sessionReference_ = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            instruction_);

        this.Instruction =
            instruction_;
        this.SessionReference =
            sessionReference_;
    }
}

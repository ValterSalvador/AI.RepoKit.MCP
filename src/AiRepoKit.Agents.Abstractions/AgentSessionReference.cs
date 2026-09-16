namespace AiRepoKit.Agents;

public sealed record AgentSessionReference
{
    public string Value
    {
        get;
    }

    public AgentSessionReference(
        string value_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value_);

        this.Value =
            value_;
    }

    public override string ToString()
    {
        return this.Value;
    }
}

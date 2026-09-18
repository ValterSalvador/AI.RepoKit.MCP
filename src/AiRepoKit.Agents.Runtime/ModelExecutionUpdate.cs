namespace AiRepoKit.Agents.Runtime;

public sealed record ModelExecutionUpdate
{
    public string ResponseTextDelta
    {
        get;
    }

    public ModelExecutionUpdate(string responseTextDelta_)
    {
        ArgumentNullException.ThrowIfNull(
            responseTextDelta_,
            nameof(responseTextDelta_));

        this.ResponseTextDelta =
            responseTextDelta_;
    }
}

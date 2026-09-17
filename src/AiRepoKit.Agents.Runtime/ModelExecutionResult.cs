namespace AiRepoKit.Agents.Runtime;

public sealed record ModelExecutionResult
{
    public string ResponseText
    {
        get;
    }

    public ModelExecutionResult(
        string responseText_)
    {
        ArgumentNullException.ThrowIfNull(
            responseText_,
            nameof(responseText_));

        this.ResponseText =
            responseText_;
    }
}

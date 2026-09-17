namespace AiRepoKit.Agents.Runtime;

using AiRepoKit.Agents;

public sealed record ModelExecutionRequest
{
    public string Prompt
    {
        get;
    }

    public StructuredOutputContract? StructuredOutput
    {
        get;
    }

    public ModelExecutionRequest(
        string prompt_,
        StructuredOutputContract? structuredOutput_ = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            prompt_,
            nameof(prompt_));

        this.Prompt =
            prompt_;
        this.StructuredOutput =
            structuredOutput_;
    }
}

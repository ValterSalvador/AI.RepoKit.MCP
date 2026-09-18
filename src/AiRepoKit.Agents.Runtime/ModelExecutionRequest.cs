namespace AiRepoKit.Agents.Runtime;

using AiRepoKit.Agents;

public sealed record ModelExecutionRequest
{
    private const long MaxTimeoutMilliseconds = 4294967294L;

    public string Prompt
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

    public ModelExecutionRequest(
        string prompt_,
        StructuredOutputContract? structuredOutput_ = null,
        TimeSpan? timeout_ = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            prompt_,
            nameof(prompt_));

        if (timeout_.HasValue)
        {
            if (timeout_.Value <= TimeSpan.Zero || timeout_.Value.TotalMilliseconds > MaxTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout_),
                    timeout_.Value,
                    $"Timeout must be greater than zero and less than or equal to {MaxTimeoutMilliseconds} milliseconds.");
            }
        }

        this.Prompt =
            prompt_;
        this.StructuredOutput =
            structuredOutput_;
        this.Timeout =
            timeout_;
    }
}

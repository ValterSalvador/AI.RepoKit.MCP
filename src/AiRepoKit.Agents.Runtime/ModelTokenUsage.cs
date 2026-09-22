namespace AiRepoKit.Agents.Runtime;

public sealed record ModelTokenUsage
{
    public long? InputTokenCount
    {
        get;
    }

    public long? OutputTokenCount
    {
        get;
    }

    public long? TotalTokenCount
    {
        get;
    }

    public long? CachedInputTokenCount
    {
        get;
    }

    public long? ReasoningTokenCount
    {
        get;
    }

    public ModelTokenUsage(
        long? inputTokenCount_ = null,
        long? outputTokenCount_ = null,
        long? totalTokenCount_ = null,
        long? cachedInputTokenCount_ = null,
        long? reasoningTokenCount_ = null)
    {
        if (inputTokenCount_.HasValue && inputTokenCount_.Value < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputTokenCount_),
                inputTokenCount_.Value,
                "Input token count must be non-negative.");
        }

        if (outputTokenCount_.HasValue && outputTokenCount_.Value < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputTokenCount_),
                outputTokenCount_.Value,
                "Output token count must be non-negative.");
        }

        if (totalTokenCount_.HasValue && totalTokenCount_.Value < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalTokenCount_),
                totalTokenCount_.Value,
                "Total token count must be non-negative.");
        }

        if (cachedInputTokenCount_.HasValue && cachedInputTokenCount_.Value < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cachedInputTokenCount_),
                cachedInputTokenCount_.Value,
                "Cached input token count must be non-negative.");
        }

        if (reasoningTokenCount_.HasValue && reasoningTokenCount_.Value < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasoningTokenCount_),
                reasoningTokenCount_.Value,
                "Reasoning token count must be non-negative.");
        }

        if (cachedInputTokenCount_.HasValue && inputTokenCount_.HasValue && cachedInputTokenCount_.Value > inputTokenCount_.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cachedInputTokenCount_),
                cachedInputTokenCount_.Value,
                "Cached input token count cannot exceed input token count.");
        }

        if (reasoningTokenCount_.HasValue && outputTokenCount_.HasValue && reasoningTokenCount_.Value > outputTokenCount_.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasoningTokenCount_),
                reasoningTokenCount_.Value,
                "Reasoning token count cannot exceed output token count.");
        }

        this.InputTokenCount = inputTokenCount_;
        this.OutputTokenCount = outputTokenCount_;
        this.TotalTokenCount = totalTokenCount_;
        this.CachedInputTokenCount = cachedInputTokenCount_;
        this.ReasoningTokenCount = reasoningTokenCount_;
    }
}

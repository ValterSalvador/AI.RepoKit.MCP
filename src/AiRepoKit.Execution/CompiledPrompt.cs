namespace AiRepoKit.Execution;

public sealed record CompiledPrompt
{
    public string TaskId
    {
        get;
    }

    public string Content
    {
        get;
    }

    public int EstimatedTokens
    {
        get;
    }

    public CompiledPrompt(
        string taskId_,
        string content_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        ArgumentNullException.ThrowIfNull(
            content_,
            nameof(content_));

        if (content_.Length == 0)
        {
            throw new ArgumentException(
                "Content must not be empty.",
                nameof(content_));
        }

        this.TaskId =
            taskId_;
        this.Content =
            content_;
        this.EstimatedTokens =
            DeterministicWorkEstimator.EstimateTokens(
                content_);
    }
}

namespace AiRepoKit.Execution;

public sealed record ExecutableTaskEstimate
{
    public string TaskId
    {
        get;
    }

    public int ComplexityScore
    {
        get;
    }

    public int EstimatedInstructionTokens
    {
        get;
    }

    public ExecutableTaskEstimate(
        string taskId_,
        int complexityScore_,
        int estimatedInstructionTokens_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId_,
            nameof(taskId_));

        if (complexityScore_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(complexityScore_),
                complexityScore_,
                "Complexity score must be greater than zero.");
        }

        if (estimatedInstructionTokens_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedInstructionTokens_),
                estimatedInstructionTokens_,
                "Estimated instruction tokens must be greater than zero.");
        }

        this.TaskId =
            taskId_;
        this.ComplexityScore =
            complexityScore_;
        this.EstimatedInstructionTokens =
            estimatedInstructionTokens_;
    }
}

namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Execution;

public sealed record WorkflowValidationEvidence
{
    public ValidationRequirement Requirement
    {
        get;
    }

    public ValidationExecutionResult ValidationResult
    {
        get;
    }

    internal WorkflowValidationEvidence(
        ValidationRequirement requirement_,
        ValidationExecutionResult validationResult_)
    {
        ArgumentNullException.ThrowIfNull(
            requirement_,
            nameof(requirement_));

        ArgumentNullException.ThrowIfNull(
            validationResult_,
            nameof(validationResult_));

        this.Requirement =
            requirement_;
        this.ValidationResult =
            validationResult_;
    }
}

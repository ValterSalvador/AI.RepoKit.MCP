namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;

public sealed record WorkflowSideEffectReconciliationResult
{
    public bool IsProvenNotExecuted
    {
        get;
    }

    public AgentExecutionResult? AgentResult
    {
        get;
    }

    public ValidationExecutionResult? ValidationResult
    {
        get;
    }

    public WorkflowSideEffectReconciliationResult(
        bool isProvenNotExecuted_,
        AgentExecutionResult? agentResult_,
        ValidationExecutionResult? validationResult_)
    {
        if (isProvenNotExecuted_ && (agentResult_ is not null || validationResult_ is not null))
        {
            throw new ArgumentException(
                "A reconciliation result proven not executed cannot specify an agent result or validation result.");
        }

        if (agentResult_ is not null && validationResult_ is not null)
        {
            throw new ArgumentException(
                "A reconciliation result cannot specify both an agent result and a validation result.");
        }

        this.IsProvenNotExecuted = isProvenNotExecuted_;
        this.AgentResult = agentResult_;
        this.ValidationResult = validationResult_;
    }
}

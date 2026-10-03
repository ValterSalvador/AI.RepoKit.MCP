namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Execution;

public interface IValidationExecutor
{
    ValidationStrategy Strategy
    {
        get;
    }

    Task<ValidationExecutionResult> ValidateAsync(
        ValidationRequirement requirement_,
        ExecutionEnvironment environment_,
        CancellationToken cancellationToken_ = default);
}

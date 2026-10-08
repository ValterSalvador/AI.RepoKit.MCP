namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public interface IWorkflowSideEffectReconciler
{
    Task<WorkflowSideEffectReconciliationResult> ReconcileAgentAsync(
        WorkflowId workflowId_,
        string taskId_,
        string operationId_,
        long operationOrdinal_,
        int invocationGeneration_,
        AgentProviderId providerId_,
        AgentExecutionRequest request_,
        CancellationToken cancellationToken_ = default);

    Task<WorkflowSideEffectReconciliationResult> ReconcileValidationAsync(
        WorkflowId workflowId_,
        string taskId_,
        string operationId_,
        long operationOrdinal_,
        int invocationGeneration_,
        ValidationRequirement requirement_,
        ExecutionEnvironment environment_,
        CancellationToken cancellationToken_ = default);
}

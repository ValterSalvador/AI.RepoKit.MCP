namespace AiRepoKit.Orchestration;

public enum WorkflowExecutionEventKind
{
    WorkflowInitialized = 1,
    WorkflowStatusTransitioned = 2,
    StepStatusTransitioned = 3
}

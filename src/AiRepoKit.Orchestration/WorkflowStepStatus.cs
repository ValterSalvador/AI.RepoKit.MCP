namespace AiRepoKit.Orchestration;

public enum WorkflowStepStatus
{
    Pending = 1,
    Running = 2,
    AwaitingValidation = 3,
    Blocked = 4,
    Failed = 5,
    Completed = 6,
    Cancelled = 7
}
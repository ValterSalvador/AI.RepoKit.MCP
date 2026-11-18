namespace AiRepoKit.WorkflowExecution;

internal enum WorkflowSchedulerLeaseState
{
    Active = 1,
    Quarantined = 2,
    MaintenanceInProgress = 3
}

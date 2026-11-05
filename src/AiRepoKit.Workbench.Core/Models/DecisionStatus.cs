namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Transient decision states resulting from workbench policy and risk evaluation.
/// </summary>
public enum DecisionStatus
{
    READY_FOR_NEXT_STAGE,
    NEEDS_INPUT,
    NEEDS_APPROVAL,
    BLOCKED
}

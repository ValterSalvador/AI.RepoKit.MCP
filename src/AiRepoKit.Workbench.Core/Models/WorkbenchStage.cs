namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Logical stages of workflow progression indicated by the router.
/// </summary>
public enum WorkbenchStage
{
    SpecificationPlanning,
    LightContractDrafting,
    PromptExecution,
    ImplementationExecution,
    VerificationAndQa,
    ReviewGate,
    ApprovalWait,
    InputClarification,
    StatusReporting,
    DryRunSimulation,
    Halted
}

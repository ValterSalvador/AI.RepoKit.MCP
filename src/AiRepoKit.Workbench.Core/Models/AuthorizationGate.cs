namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Required authorization gates that must be satisfied before progressing to execution.
/// </summary>
public enum AuthorizationGate
{
    HumanApproval,
    SecurityReview,
    ArchitectureApproval,
    BaselineAlignment,
    ApprovalReassessment,
    SchemaMigrationApproval,
    VerificationAcceptance,
    CapabilityAlignment,
    WorkflowCoordination
}

namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Transient structured routing decision returned by the workbench router.
/// Fully immutable public snapshot backed by read-only BCL collections.
/// </summary>
public sealed class WorkbenchRoutingDecision
{
    public string ContractVersion { get; }
    public string RepositoryId { get; }
    public string ObservedBaselineSha { get; }
    public string PolicyVersion { get; }
    public WorkIntent ResolvedIntent { get; }
    public PlanningMode PlanningMode { get; }
    public RiskLevel AssessedRisk { get; }
    public DecisionStatus DecisionStatus { get; }
    public IReadOnlyList<string> DiagnosticReasons { get; }
    public IReadOnlyList<string> MissingInformation { get; }
    public IReadOnlyList<WorkbenchCapability> RequiredCapabilities { get; }
    public IReadOnlyList<AuthorizationGate> RequiredAuthorizationGates { get; }
    public WorkbenchStage NextLogicalStage { get; }

    public WorkbenchRoutingDecision(
        string contractVersion_,
        string repositoryId_,
        string observedBaselineSha_,
        string policyVersion_,
        WorkIntent resolvedIntent_,
        PlanningMode planningMode_,
        RiskLevel assessedRisk_,
        DecisionStatus decisionStatus_,
        IEnumerable<string>? diagnosticReasons_ = null,
        IEnumerable<string>? missingInformation_ = null,
        IEnumerable<WorkbenchCapability>? requiredCapabilities_ = null,
        IEnumerable<AuthorizationGate>? requiredAuthorizationGates_ = null,
        WorkbenchStage nextLogicalStage_ = WorkbenchStage.Halted)
    {
        this.ContractVersion = contractVersion_ ?? string.Empty;
        this.RepositoryId = repositoryId_ ?? string.Empty;
        this.ObservedBaselineSha = observedBaselineSha_ ?? string.Empty;
        this.PolicyVersion = policyVersion_ ?? string.Empty;
        this.ResolvedIntent = resolvedIntent_;
        this.PlanningMode = planningMode_;
        this.AssessedRisk = assessedRisk_;
        this.DecisionStatus = decisionStatus_;
        this.DiagnosticReasons = Array.AsReadOnly(diagnosticReasons_ != null ? diagnosticReasons_.ToArray() : Array.Empty<string>());
        this.MissingInformation = Array.AsReadOnly(missingInformation_ != null ? missingInformation_.ToArray() : Array.Empty<string>());
        this.RequiredCapabilities = Array.AsReadOnly(requiredCapabilities_ != null ? requiredCapabilities_.ToArray() : Array.Empty<WorkbenchCapability>());
        this.RequiredAuthorizationGates = Array.AsReadOnly(requiredAuthorizationGates_ != null ? requiredAuthorizationGates_.ToArray() : Array.Empty<AuthorizationGate>());
        this.NextLogicalStage = nextLogicalStage_;
    }
}

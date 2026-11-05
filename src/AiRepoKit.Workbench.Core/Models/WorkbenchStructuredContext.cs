using System.Collections.Frozen;

namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Trusted structured context passed to the workbench router.
/// Security-sensitive decisions rely on structured signals rather than untrusted free text.
/// Defensively copies and wraps all input collections into truly immutable BCL snapshots.
/// </summary>
public sealed class WorkbenchStructuredContext
{
    public ChangeScope Scope { get; }
    public IReadOnlyList<string> AffectedFiles { get; }
    public bool HasAmbiguousRequirements { get; }
    public bool BypassApprovalRequested { get; }
    public bool ForcePushRequested { get; }
    public bool IsDryRun { get; }
    public IReadOnlySet<string> SupportedProviderCapabilities { get; }
    public IReadOnlyList<string> RequiredProviderCapabilities { get; }
    public IReadOnlyList<WorkbenchApproval> Approvals { get; }
    public bool AgentExecutionCompleted { get; }
    public bool WorkAccepted { get; }
    public string? LegacyCommand { get; }
    public IReadOnlyDictionary<string, string> CustomProperties { get; }

    public WorkbenchStructuredContext(
        ChangeScope scope_ = ChangeScope.Unknown,
        IEnumerable<string>? affectedFiles_ = null,
        bool hasAmbiguousRequirements_ = false,
        bool bypassApprovalRequested_ = false,
        bool forcePushRequested_ = false,
        bool isDryRun_ = false,
        IEnumerable<string>? supportedProviderCapabilities_ = null,
        IEnumerable<string>? requiredProviderCapabilities_ = null,
        IEnumerable<WorkbenchApproval>? approvals_ = null,
        bool agentExecutionCompleted_ = false,
        bool workAccepted_ = false,
        string? legacyCommand_ = null,
        IDictionary<string, string>? customProperties_ = null)
    {
        this.Scope = scope_;
        this.AffectedFiles = Array.AsReadOnly(affectedFiles_ != null ? affectedFiles_.ToArray() : Array.Empty<string>());
        this.HasAmbiguousRequirements = hasAmbiguousRequirements_;
        this.BypassApprovalRequested = bypassApprovalRequested_;
        this.ForcePushRequested = forcePushRequested_;
        this.IsDryRun = isDryRun_;
        this.SupportedProviderCapabilities = supportedProviderCapabilities_ != null
            ? supportedProviderCapabilities_.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
            : FrozenSet<string>.Empty;
        this.RequiredProviderCapabilities = Array.AsReadOnly(requiredProviderCapabilities_ != null ? requiredProviderCapabilities_.ToArray() : Array.Empty<string>());
        this.Approvals = Array.AsReadOnly(approvals_ != null ? approvals_.ToArray() : Array.Empty<WorkbenchApproval>());
        this.AgentExecutionCompleted = agentExecutionCompleted_;
        this.WorkAccepted = workAccepted_;
        this.LegacyCommand = legacyCommand_;
        this.CustomProperties = customProperties_ != null
            ? customProperties_.ToFrozenDictionary(StringComparer.Ordinal)
            : FrozenDictionary<string, string>.Empty;
    }
}

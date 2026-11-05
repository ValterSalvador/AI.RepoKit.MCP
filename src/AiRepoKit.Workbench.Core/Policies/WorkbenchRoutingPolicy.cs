using System.Collections.Frozen;
using AiRepoKit.Workbench.Core.Models;

namespace AiRepoKit.Workbench.Core.Policies;

/// <summary>
/// Policy definition and invariant configuration for workbench routing.
/// Truly immutable snapshot with frozen capability sets.
/// </summary>
public sealed class WorkbenchRoutingPolicy
{
    public const string DefaultPolicyVersion = "wb.policy/v1";

    public string PolicyVersion { get; }
    public IReadOnlySet<string> AllowedCapabilities { get; }
    public bool EnforceStrictBaselineMatch { get; }
    public bool ProhibitMutationsOnDryRun { get; }

    public WorkbenchRoutingPolicy(
        string policyVersion_ = DefaultPolicyVersion,
        IEnumerable<string>? allowedCapabilities_ = null,
        bool enforceStrictBaselineMatch_ = true,
        bool prohibitMutationsOnDryRun_ = true)
    {
        this.PolicyVersion = policyVersion_ ?? DefaultPolicyVersion;
        this.EnforceStrictBaselineMatch = enforceStrictBaselineMatch_;
        this.ProhibitMutationsOnDryRun = prohibitMutationsOnDryRun_;
        this.AllowedCapabilities = (allowedCapabilities_ != null
            ? allowedCapabilities_
            : new[]
            {
                "repo:read",
                "proc:exec",
                "code:edit",
                "git:branch",
                "git:read",
                "fs:read",
                "terminal:exec",
                "RepositoryReading",
                "ProcessExecution",
                "SourceModification",
                "BranchWorktreeCreation"
            }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a specific capability is permitted by this policy snapshot.
    /// </summary>
    public bool IsCapabilityAllowed(WorkbenchCapability capability_)
    {
        string policyName = capability_.ToPolicyName();
        string enumName = capability_.ToString();
        return this.AllowedCapabilities.Contains(policyName) || this.AllowedCapabilities.Contains(enumName);
    }
}

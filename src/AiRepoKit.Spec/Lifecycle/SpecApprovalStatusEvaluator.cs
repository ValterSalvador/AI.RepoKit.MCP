using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Spec.Lifecycle;

public static class SpecApprovalStatusEvaluator
{
    public static IReadOnlyList<SpecArtifactApprovalStatus> Evaluate(
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalLedger? ledger_)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot_);

        return Evaluate(
            snapshot_,
            ledger_?.Approvals);
    }

    public static IReadOnlyList<SpecArtifactApprovalStatus> Evaluate(
        SpecWorkspaceSnapshot snapshot_,
        IEnumerable<Approval>? approvals_)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot_);

        List<SpecArtifactApprovalStatus> statuses =
            [];

        IReadOnlyList<Approval> approvalList =
            approvals_?
                .Where(
                    static approval_ =>
                        approval_ is not null)
                .ToArray() ??
            [];

        SpecApprovalStatus? requirementSetStatus =
            null;

        if (snapshot_.RequirementSet is not null)
        {
            requirementSetStatus =
                EvaluateRequirementSetCore(
                    snapshot_.RequirementSet,
                    approvalList);

            statuses.Add(
                new SpecArtifactApprovalStatus
                {
                    ArtifactKind =
                        SpecArtifactKind.RequirementSet,
                    ArtifactIdentity =
                        snapshot_.RequirementSet.ArtifactIdentity,
                    Status =
                        requirementSetStatus.Value
                });
        }

        SpecApprovalStatus? workSpecStatus =
            null;

        if (snapshot_.WorkSpec is not null)
        {
            workSpecStatus =
                EvaluateWorkSpecCore(
                    snapshot_,
                    requirementSetStatus,
                    approvalList);

            statuses.Add(
                new SpecArtifactApprovalStatus
                {
                    ArtifactKind =
                        SpecArtifactKind.WorkSpec,
                    ArtifactIdentity =
                        snapshot_.WorkSpec.ArtifactIdentity,
                    Status =
                        workSpecStatus.Value
                });
        }

        if (snapshot_.ImplementationPlan is not null)
        {
            SpecApprovalStatus planStatus =
                EvaluateImplementationPlanCore(
                    snapshot_,
                    workSpecStatus,
                    approvalList);

            statuses.Add(
                new SpecArtifactApprovalStatus
                {
                    ArtifactKind =
                        SpecArtifactKind.ImplementationPlan,
                    ArtifactIdentity =
                        snapshot_.ImplementationPlan.ArtifactIdentity,
                    Status =
                        planStatus
                });
        }

        return statuses.ToArray();
    }

    public static SpecArtifactApprovalStatus? Evaluate(
        SpecArtifactKind artifactKind_,
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalLedger? ledger_)
    {
        return Evaluate(
            artifactKind_,
            snapshot_,
            ledger_?.Approvals);
    }

    public static SpecArtifactApprovalStatus? Evaluate(
        SpecArtifactKind artifactKind_,
        SpecWorkspaceSnapshot snapshot_,
        IEnumerable<Approval>? approvals_)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot_);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            Evaluate(
                snapshot_,
                approvals_);

        return statuses.FirstOrDefault(
            status_ =>
                status_.ArtifactKind == artifactKind_);
    }

    private static SpecApprovalStatus EvaluateRequirementSetCore(
        RequirementSet requirementSet_,
        IReadOnlyList<Approval> approvals_)
    {
        bool hasHistory =
            approvals_.Any(
                approval_ =>
                    approval_.ArtifactKind == SpecArtifactKind.RequirementSet);

        if (!hasHistory)
        {
            return SpecApprovalStatus.NotApproved;
        }

        bool hasValidBinding =
            approvals_
                .Where(
                    approval_ =>
                        approval_.ArtifactKind == SpecArtifactKind.RequirementSet)
                .Any(
                    approval_ =>
                        ApprovalBindingValidator.Validate(
                            approval_,
                            requirementSet_).Count == 0);

        return hasValidBinding
            ? SpecApprovalStatus.Current
            : SpecApprovalStatus.Stale;
    }

    private static SpecApprovalStatus EvaluateWorkSpecCore(
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalStatus? requirementSetStatus_,
        IReadOnlyList<Approval> approvals_)
    {
        WorkSpec workSpec =
            snapshot_.WorkSpec!;

        bool hasHistory =
            approvals_.Any(
                approval_ =>
                    approval_.ArtifactKind == SpecArtifactKind.WorkSpec);

        if (!hasHistory)
        {
            return SpecApprovalStatus.NotApproved;
        }

        bool hasValidBinding =
            approvals_
                .Where(
                    approval_ =>
                        approval_.ArtifactKind == SpecArtifactKind.WorkSpec)
                .Any(
                    approval_ =>
                        ApprovalBindingValidator.Validate(
                            approval_,
                            workSpec).Count == 0);

        bool isCurrent =
            hasValidBinding &&
            !snapshot_.IsWorkSpecStale &&
            snapshot_.RequirementSet is not null &&
            requirementSetStatus_ == SpecApprovalStatus.Current;

        return isCurrent
            ? SpecApprovalStatus.Current
            : SpecApprovalStatus.Stale;
    }

    private static SpecApprovalStatus EvaluateImplementationPlanCore(
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalStatus? workSpecStatus_,
        IReadOnlyList<Approval> approvals_)
    {
        ImplementationPlan implementationPlan =
            snapshot_.ImplementationPlan!;

        bool hasHistory =
            approvals_.Any(
                approval_ =>
                    approval_.ArtifactKind == SpecArtifactKind.ImplementationPlan);

        if (!hasHistory)
        {
            return SpecApprovalStatus.NotApproved;
        }

        bool hasValidBinding =
            approvals_
                .Where(
                    approval_ =>
                        approval_.ArtifactKind == SpecArtifactKind.ImplementationPlan)
                .Any(
                    approval_ =>
                        ApprovalBindingValidator.Validate(
                            approval_,
                            implementationPlan).Count == 0);

        bool isCurrent =
            hasValidBinding &&
            !snapshot_.IsImplementationPlanStale &&
            snapshot_.WorkSpec is not null &&
            workSpecStatus_ == SpecApprovalStatus.Current;

        return isCurrent
            ? SpecApprovalStatus.Current
            : SpecApprovalStatus.Stale;
    }
}

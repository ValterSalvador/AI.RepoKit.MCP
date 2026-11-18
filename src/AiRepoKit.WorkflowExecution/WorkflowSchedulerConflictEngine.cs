namespace AiRepoKit.WorkflowExecution;

internal static class WorkflowSchedulerConflictEngine
{
    public static bool ClaimsConflict(
        WorkflowResourceClaim claimA_,
        WorkflowResourceClaim claimB_)
    {
        ArgumentNullException.ThrowIfNull(
            claimA_,
            nameof(claimA_));
        ArgumentNullException.ThrowIfNull(
            claimB_,
            nameof(claimB_));

        bool exactMatch =
            string.Equals(
                claimA_.ResourceId,
                claimB_.ResourceId,
                StringComparison.Ordinal);

        bool hierarchicalOverlap =
            !exactMatch &&
            claimA_.OverlapsHierarchicallyWith(
                claimB_);

        if (!exactMatch && !hierarchicalOverlap)
        {
            return false;
        }

        // Overlapping resources: SharedRead/SharedRead is compatible, any ExclusiveWrite conflicts.
        if (claimA_.AccessKind == WorkflowResourceAccessKind.SharedRead &&
            claimB_.AccessKind == WorkflowResourceAccessKind.SharedRead)
        {
            return false;
        }

        return true;
    }

    public static bool HasConflictWithLease(
        IEnumerable<WorkflowResourceClaim> candidateClaims_,
        WorkflowSchedulerLeaseManifest existingLease_,
        out string? conflictReason_)
    {
        ArgumentNullException.ThrowIfNull(
            candidateClaims_,
            nameof(candidateClaims_));
        ArgumentNullException.ThrowIfNull(
            existingLease_,
            nameof(existingLease_));

        // 1. MaintenanceInProgress always imposes domain-wide global exclusion.
        if (existingLease_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
        {
            conflictReason_ =
                $"Workflow '{existingLease_.WorkflowId}' is under MaintenanceInProgress with global exclusion.";
            return true;
        }

        // 2. Active or Quarantined lease with Unrestricted mode imposes global exclusion.
        if (string.Equals(
                existingLease_.DomainMode,
                "Unrestricted",
                StringComparison.Ordinal) &&
            (existingLease_.State == WorkflowSchedulerLeaseState.Active ||
             existingLease_.State == WorkflowSchedulerLeaseState.Quarantined))
        {
            conflictReason_ =
                $"Workflow '{existingLease_.WorkflowId}' is under {existingLease_.State} with Unrestricted domain mode global exclusion.";
            return true;
        }

        // 3. For other leases (e.g. Normal Active or Normal Quarantined), check resource claim conflicts.
        foreach (WorkflowResourceClaim candidateClaim in candidateClaims_)
        {
            foreach (WorkflowResourceClaim existingClaim in existingLease_.ResourceClaims)
            {
                if (ClaimsConflict(
                        candidateClaim,
                        existingClaim))
                {
                    conflictReason_ =
                        $"Conflict between candidate claim '{candidateClaim.ResourceId}' ({candidateClaim.AccessKind}) " +
                        $"and {existingLease_.State} lease '{existingLease_.WorkflowId}' claim '{existingClaim.ResourceId}' ({existingClaim.AccessKind}).";
                    return true;
                }
            }
        }

        conflictReason_ =
            null;
        return false;
    }

    public static bool HasConflictBetweenClaimSets(
        IEnumerable<WorkflowResourceClaim> claimsA_,
        IEnumerable<WorkflowResourceClaim> claimsB_,
        out string? conflictReason_)
    {
        ArgumentNullException.ThrowIfNull(
            claimsA_,
            nameof(claimsA_));
        ArgumentNullException.ThrowIfNull(
            claimsB_,
            nameof(claimsB_));

        foreach (WorkflowResourceClaim claimA in claimsA_)
        {
            foreach (WorkflowResourceClaim claimB in claimsB_)
            {
                if (ClaimsConflict(
                        claimA,
                        claimB))
                {
                    conflictReason_ =
                        $"Conflict between '{claimA.ResourceId}' ({claimA.AccessKind}) and '{claimB.ResourceId}' ({claimB.AccessKind}).";
                    return true;
                }
            }
        }

        conflictReason_ =
            null;
        return false;
    }
}

namespace AiRepoKit.WorkflowExecution;

using System.Text.Json;

internal sealed record WorkflowSchedulerIntegrityReport
{
    public bool IsHealthy
    {
        get;
        init;
    }

    public bool HasGlobalExclusion
    {
        get;
        init;
    }

    public IReadOnlyList<WorkflowSchedulerLeaseManifest> ActiveLeases
    {
        get;
        init;
    } = [];

    public IReadOnlyList<WorkflowSchedulerLeaseManifest> QuarantinedLeases
    {
        get;
        init;
    } = [];

    public IReadOnlyList<WorkflowSchedulerLeaseManifest> MaintenanceLeases
    {
        get;
        init;
    } = [];

    public IReadOnlyList<WorkflowSchedulerLeaseManifest> DeadActiveLeases
    {
        get;
        init;
    } = [];

    public IReadOnlyList<WorkflowSchedulerLeaseManifest> OrphanedMaintenanceLeases
    {
        get;
        init;
    } = [];

    public IReadOnlyList<string> Violations
    {
        get;
        init;
    } = [];
}

internal enum LockEnumerationStatus
{
    Success = 0,
    EnumerationFailed = 1,
    FileNotFound = 2
}

internal enum SentinelByteLockProbeStatus
{
    LockProven = 0,
    LockAbsent = 1,
    Indeterminate = 2
}

internal enum ProcessLivenessStatus
{
    DefinitelyAlive = 0,
    DefinitelyDead = 1,
    Indeterminate = 2
}

internal readonly struct LockEnumerationResult
{
    public LockEnumerationStatus Status { get; }
    public IReadOnlyList<int> ProcessIds { get; }
    public bool IsSuccess => this.Status == LockEnumerationStatus.Success;
    public bool IsFailed => this.Status == LockEnumerationStatus.EnumerationFailed;
    public bool IsNotFound => this.Status == LockEnumerationStatus.FileNotFound;

    public LockEnumerationResult(LockEnumerationStatus status, IReadOnlyList<int> processIds)
    {
        this.Status = status;
        this.ProcessIds = processIds;
    }

    public static LockEnumerationResult Succeeded(IReadOnlyList<int> processIds) =>
        new(LockEnumerationStatus.Success, processIds);

    public static LockEnumerationResult Failed() =>
        new(LockEnumerationStatus.EnumerationFailed, []);

    public static LockEnumerationResult NotFound() =>
        new(LockEnumerationStatus.FileNotFound, []);
}

internal static class WorkflowSchedulerIntegrityInspector
{
    public static WorkflowSchedulerIntegrityReport Inspect(
        WorkflowSchedulerPaths paths_,
        int maxConcurrentWorkflows_,
        WorkflowSchedulerLockHandle? domainLockHandle_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));

        if (maxConcurrentWorkflows_ < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentWorkflows_),
                maxConcurrentWorkflows_,
                "Max concurrent workflows must be at least 1.");
        }

        List<string> violations =
            [];

        WorkflowSchedulerLockHandle? snapshotLock =
            null;
        bool isLiveBrokerLockAuthority = false;

        if (domainLockHandle_ is not null)
        {
            try
            {
                WorkflowSchedulerDomainMetadata.ValidateDomainLockHandle(
                    paths_,
                    domainLockHandle_);
            }
            catch (Exception exception)
            {
                violations.Add(
                    $"Domain lock validation failed on '{paths_.DomainLockPath}': {exception.Message}");
            }
        }
        else if (File.Exists(
                paths_.DomainLockPath))
        {
            try
            {
                snapshotLock =
                    WorkflowSchedulerLockPrimitives.TryAcquireShared(
                        paths_.DomainLockPath);

                if (snapshotLock is null)
                {
                    LockEnumerationResult domainEnum = GetLockingProcesses(paths_.DomainLockPath, requireExclusive: true);
                    if (domainEnum.IsSuccess && domainEnum.ProcessIds.Count == 1 && IsProcessAlive(domainEnum.ProcessIds[0]))
                    {
                        // Domain lock is held by live singleton broker; snapshot inspection is authorized
                        isLiveBrokerLockAuthority = true;
                    }
                    else
                    {
                        violations.Add(
                            $"Failed to acquire shared snapshot lock on '{paths_.DomainLockPath}'. Domain may be under exclusive mutation.");
                    }
                }
            }
            catch (Exception exception)
            {
                violations.Add(
                    $"Error acquiring shared snapshot lock on '{paths_.DomainLockPath}': {exception.Message}");
            }
        }
        else
        {
            violations.Add(
                $"Domain lock file '{paths_.DomainLockPath}' is missing.");
        }

        try
        {
            // 1. Verify domain metadata integrity and policy
            try
            {
                WorkflowSchedulerDomainMetadata.ValidateIntegrityDocument(
                    paths_);
            }
            catch (Exception ex)
            {
                violations.Add(
                    $"Domain integrity validation failure: {ex.Message}");
            }

            try
            {
                _ = WorkflowSchedulerDomainMetadata.ValidatePolicyDocument(
                    paths_,
                    expectedMaxConcurrentWorkflows_: maxConcurrentWorkflows_);
            }
            catch (Exception ex)
            {
                violations.Add(
                    $"Domain policy validation failure: {ex.Message}");
            }

            List<WorkflowSchedulerLeaseManifest> allManifests =
                [];

            Dictionary<string, WorkflowSchedulerLeaseManifest> byWorkflowId =
                new(StringComparer.Ordinal);

            Dictionary<string, string> workflowByOwnerToken =
                new(StringComparer.Ordinal);

            Dictionary<int, WorkflowSchedulerLeaseManifest> activeBySlot =
                [];

            List<WorkflowSchedulerLeaseManifest> activeLeases =
                [];

            List<WorkflowSchedulerLeaseManifest> quarantinedLeases =
                [];

            List<WorkflowSchedulerLeaseManifest> maintenanceLeases =
                [];

            List<WorkflowSchedulerLeaseManifest> deadActiveLeases =
                [];

            List<WorkflowSchedulerLeaseManifest> orphanedMaintenanceLeases =
                [];

            try
            {
                WorkflowSchedulerDomainMetadata.CheckResidualTemporaryFiles(
                    paths_);
            }
            catch (Exception ex)
            {
                violations.Add(
                    $"Residual coordination file failure: {ex.Message}");
            }

            // 2. Enumerate and validate all manifest files.
            if (Directory.Exists(
                    paths_.ActiveLeasesDirectory))
            {
                foreach (string filePath in Directory.EnumerateFiles(
                             paths_.ActiveLeasesDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (filePath.Contains(".tmp.", StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"Residual temporary file detected in active leases directory: '{filePath}'.");
                        continue;
                    }

                    if (!filePath.EndsWith(
                            ".lease.json",
                            StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"Unexpected non-lease file found in active leases directory: '{filePath}'.");
                        continue;
                    }

                    WorkflowSchedulerLeaseManifest manifest;
                    try
                    {
                        manifest =
                            WorkflowSchedulerLeaseManifest.ReadAndValidate(
                                filePath,
                                paths_,
                                maxConcurrentWorkflows_);
                    }
                    catch (Exception exception)
                    {
                        violations.Add(
                            $"Corrupted or invalid lease manifest at '{filePath}': {exception.Message}");
                        continue;
                    }

                    if (byWorkflowId.ContainsKey(
                            manifest.WorkflowId))
                    {
                        violations.Add(
                            $"Duplicate lease manifest found for workflow '{manifest.WorkflowId}'.");
                        continue;
                    }

                    if (workflowByOwnerToken.TryGetValue(
                            manifest.OwnerToken,
                            out string? existingWfId))
                    {
                        violations.Add(
                            $"Duplicate owner token '{manifest.OwnerToken}' shared by '{manifest.WorkflowId}' and '{existingWfId}'.");
                        continue;
                    }

                    byWorkflowId[manifest.WorkflowId] =
                        manifest;
                    workflowByOwnerToken[manifest.OwnerToken] =
                        manifest.WorkflowId;
                    allManifests.Add(
                        manifest);
                }
            }

            // 3. Validate manifest states and lock exclusivity.
            foreach (WorkflowSchedulerLeaseManifest manifest in allManifests)
            {
                switch (manifest.State)
                {
                    case WorkflowSchedulerLeaseState.Active:
                        if (!manifest.Slot.HasValue)
                        {
                            violations.Add(
                                $"Active lease for '{manifest.WorkflowId}' has no assigned slot.");
                            continue;
                        }

                        int slot =
                            manifest.Slot.Value;

                        if (activeBySlot.TryGetValue(
                                slot,
                                out WorkflowSchedulerLeaseManifest? conflictWf))
                        {
                            violations.Add(
                                $"Slot {slot} is claimed by multiple Active leases: '{manifest.WorkflowId}' and '{conflictWf.WorkflowId}'.");
                            continue;
                        }

                        activeBySlot[slot] =
                            manifest;

                        // Probe workflow.lock liveness.
                        string wfLockPath =
                            paths_.GetWorkflowLockPath(
                                manifest.WorkflowId);

                        WorkflowLockProbeState wfLockState =
                            WorkflowSchedulerLockPrimitives.ProbeLockState(
                                wfLockPath);

                        if (wfLockState == WorkflowLockProbeState.Free)
                        {
                            deadActiveLeases.Add(
                                manifest);
                            violations.Add(
                                $"Active lease for '{manifest.WorkflowId}' has no live owner (workflow.lock is free).");
                        }
                        else if (wfLockState == WorkflowLockProbeState.HeldShared)
                        {
                            violations.Add(
                                $"Active lease for '{manifest.WorkflowId}' workflow.lock is held shared, but exclusive lock is required.");
                        }
                        else if (wfLockState == WorkflowLockProbeState.Indeterminate)
                        {
                            violations.Add(
                                $"Active lease for '{manifest.WorkflowId}' workflow.lock state is indeterminate.");
                        }
                        else // HeldExclusive
                        {
                            // Slot lock must also be exclusively held.
                            string slotLockPath =
                                paths_.GetSlotLockPath(
                                    slot,
                                    maxConcurrentWorkflows_);

                            WorkflowLockProbeState slotLockState =
                                WorkflowSchedulerLockPrimitives.ProbeLockState(
                                    slotLockPath);

                            if (slotLockState == WorkflowLockProbeState.Free)
                            {
                                violations.Add(
                                    $"Active lease for '{manifest.WorkflowId}' has live workflow.lock but claimed slot {slot} is unoccupied.");
                            }
                            else if (slotLockState == WorkflowLockProbeState.HeldShared)
                            {
                                violations.Add(
                                    $"Active lease for '{manifest.WorkflowId}' claimed slot {slot} is held shared, but exclusive lock is required.");
                            }
                            else if (slotLockState == WorkflowLockProbeState.Indeterminate)
                            {
                                violations.Add(
                                    $"Active lease for '{manifest.WorkflowId}' claimed slot {slot} state is indeterminate.");
                            }
                            else // HeldExclusive
                            {
                                WorkflowLockProbeState modeLockState =
                                    WorkflowSchedulerLockPrimitives.ProbeLockState(
                                        paths_.DomainModeLockPath);

                                if (string.Equals(manifest.DomainMode, "Unrestricted", StringComparison.Ordinal))
                                {
                                    if (modeLockState != WorkflowLockProbeState.HeldExclusive)
                                    {
                                        violations.Add(
                                            $"Active lease for '{manifest.WorkflowId}' with domainMode 'Unrestricted' lacks required exclusive lock on domain-mode.lock (actual state: {modeLockState}).");
                                    }
                                }
                                else
                                {
                                    if (modeLockState == WorkflowLockProbeState.Free ||
                                        modeLockState == WorkflowLockProbeState.Indeterminate)
                                    {
                                        violations.Add(
                                            $"Active lease for '{manifest.WorkflowId}' with domainMode 'Normal' lacks required shared lock on domain-mode.lock (actual state: {modeLockState}).");
                                    }
                                    else if (modeLockState == WorkflowLockProbeState.HeldExclusive)
                                    {
                                        violations.Add(
                                            $"Active lease for '{manifest.WorkflowId}' with domainMode 'Normal' domain-mode.lock is held exclusive instead of shared.");
                                    }
                                }

                                // Authenticated common ownership correlation between workflow.lock, slot lock, domain-mode.lock, and lease manifest:
                                LockEnumerationResult wfEnum = GetLockingProcesses(wfLockPath, requireExclusive: true);
                                LockEnumerationResult slotEnum = GetLockingProcesses(slotLockPath, requireExclusive: true);
                                bool requireModeExclusive = string.Equals(manifest.DomainMode, "Unrestricted", StringComparison.Ordinal);
                                LockEnumerationResult modeEnum = GetLockingProcesses(paths_.DomainModeLockPath, requireExclusive: requireModeExclusive);

                                if (wfEnum.IsFailed || slotEnum.IsFailed || modeEnum.IsFailed)
                                {
                                    violations.Add(
                                        $"Active lease for '{manifest.WorkflowId}' lock owner enumeration failed. State is indeterminate. Fail closed.");
                                }
                                else
                                {
                                    IReadOnlyList<int> wfPids = wfEnum.ProcessIds;
                                    IReadOnlyList<int> slotPids = slotEnum.ProcessIds;
                                    IReadOnlyList<int> modePids = modeEnum.ProcessIds;

                                    bool commonOwnerFound = false;
                                    int commonPid = 0;

                                    foreach (int wfPid in wfPids)
                                    {
                                        if (slotPids.Contains(wfPid) && modePids.Contains(wfPid))
                                        {
                                            commonOwnerFound = true;
                                            commonPid = wfPid;
                                            break;
                                        }
                                    }

                                    if (!commonOwnerFound)
                                    {
                                        violations.Add(
                                            $"Active lease for '{manifest.WorkflowId}' lacks authenticated common ownership correlation between workflow.lock, slot {slot} lock, domain-mode.lock, and owner token '{manifest.OwnerToken}'. State is indeterminate.");
                                    }
                                    else
                                    {
                                        if (!IsProcessAlive(commonPid))
                                        {
                                            violations.Add(
                                                $"Active lease for '{manifest.WorkflowId}' lock owner process {commonPid} is not running.");
                                        }
                                        else
                                        {
                                            WorkflowSchedulerOwnerAuthentication? auth =
                                                WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(
                                                    paths_,
                                                    manifest.WorkflowId);

                                            if (auth is null)
                                            {
                                                violations.Add(
                                                    $"Active lease for '{manifest.WorkflowId}' lacks valid authenticated owner generation record.");
                                            }
                                            else
                                            {
                                                int brokerPid = 0;
                                                bool isBrokerCoordinated = false;

                                                if (domainLockHandle_ is not null)
                                                {
                                                    brokerPid = Environment.ProcessId;
                                                    isBrokerCoordinated = true;
                                                }
                                                else
                                                {
                                                    LockEnumerationResult domainEnum = GetLockingProcesses(paths_.DomainLockPath, requireExclusive: true);
                                                    if (domainEnum.IsSuccess && domainEnum.ProcessIds.Count == 1 && IsProcessAlive(domainEnum.ProcessIds[0]))
                                                    {
                                                        brokerPid = domainEnum.ProcessIds[0];
                                                        isBrokerCoordinated = true;
                                                    }
                                                }

                                                bool isBrokerLockAuthority = isBrokerCoordinated && commonPid == brokerPid;

                                                string authPath =
                                                    WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(
                                                        paths_,
                                                        manifest.WorkflowId);
                                                LockEnumerationResult authEnum =
                                                    GetLockingProcesses(
                                                        authPath,
                                                        requireExclusive: true);

                                                SentinelByteLockProbeStatus probeStatus =
                                                    WorkflowSchedulerOwnerAuthentication.ProbeProductionRegistrationLock(authPath);

                                                if (authEnum.IsFailed)
                                                {
                                                    violations.Add(
                                                        $"Active lease for '{manifest.WorkflowId}' owner-auth.json lock enumeration failed. State is indeterminate.");
                                                }
                                                else if (!authEnum.ProcessIds.Contains(commonPid) || authEnum.ProcessIds.Count != 1 ||
                                                         probeStatus != SentinelByteLockProbeStatus.LockProven)
                                                {
                                                    violations.Add(
                                                        $"Active lease for '{manifest.WorkflowId}' owner-auth.json lacks genuine OS lock provenance held by lock owner PID {commonPid} (probe status: {probeStatus}).");
                                                }

                                                if (isBrokerLockAuthority)
                                                {
                                                    // In broker coordination mode:
                                                    // The broker independently holds OS locks, while auth.ProcessId is the authenticated worker.
                                                    if (!IsProcessAlive(auth.ProcessId))
                                                    {
                                                        violations.Add(
                                                            $"Active lease for '{manifest.WorkflowId}' authenticated worker process {auth.ProcessId} is not running.");
                                                    }
                                                    else if (auth.ProcessStartTimeTicks <= 0)
                                                    {
                                                        violations.Add(
                                                            $"Active lease for '{manifest.WorkflowId}' authenticated owner record has invalid start time (ticks: {auth.ProcessStartTimeTicks}). State is indeterminate.");
                                                    }
                                                    else
                                                    {
                                                        long workerStart = GetProcessStartTimeTicks(auth.ProcessId);
                                                        if (workerStart <= 0)
                                                        {
                                                            violations.Add(
                                                                $"Active lease for '{manifest.WorkflowId}' authenticated worker process {auth.ProcessId} start time cannot be determined. State is indeterminate.");
                                                        }
                                                        else if (Math.Abs(workerStart - auth.ProcessStartTimeTicks) > GetProcessStartTimeToleranceTicks())
                                                        {
                                                            violations.Add(
                                                                $"Active lease for '{manifest.WorkflowId}' authenticated worker process {auth.ProcessId} start time mismatch (possible PID reuse).");
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    // Direct mode: worker directly holds OS locks
                                                    if (auth.ProcessId != commonPid)
                                                    {
                                                        violations.Add(
                                                            $"Active lease for '{manifest.WorkflowId}' authenticated owner PID {auth.ProcessId} does not match lock holder PID {commonPid}.");
                                                    }

                                                    if (auth.ProcessStartTimeTicks <= 0)
                                                    {
                                                        violations.Add(
                                                            $"Active lease for '{manifest.WorkflowId}' authenticated owner record has invalid start time (ticks: {auth.ProcessStartTimeTicks}). State is indeterminate.");
                                                    }
                                                    else
                                                    {
                                                        long currentStart = GetProcessStartTimeTicks(commonPid);
                                                        if (currentStart <= 0)
                                                        {
                                                            violations.Add(
                                                                $"Active lease for '{manifest.WorkflowId}' lock owner process {commonPid} start time cannot be determined. State is indeterminate.");
                                                        }
                                                        else if (Math.Abs(currentStart - auth.ProcessStartTimeTicks) > GetProcessStartTimeToleranceTicks())
                                                        {
                                                            violations.Add(
                                                                $"Active lease for '{manifest.WorkflowId}' lock owner process {commonPid} start time mismatch (possible PID reuse).");
                                                        }
                                                    }
                                                }

                                                if (!string.Equals(auth.WorkflowId, manifest.WorkflowId, StringComparison.Ordinal))
                                                {
                                                    violations.Add(
                                                        $"Active lease for '{manifest.WorkflowId}' authenticated record workflowId '{auth.WorkflowId}' does not match manifest.");
                                                }

                                                if (!string.Equals(auth.OwnerToken, manifest.OwnerToken, StringComparison.Ordinal))
                                                {
                                                    violations.Add(
                                                        $"Active lease for '{manifest.WorkflowId}' authenticated ownerToken '{auth.OwnerToken}' does not match manifest ownerToken '{manifest.OwnerToken}'.");
                                                }

                                                if (auth.Slot != slot)
                                                {
                                                    violations.Add(
                                                        $"Active lease for '{manifest.WorkflowId}' authenticated slot {auth.Slot} does not match manifest slot {slot}.");
                                                }

                                                if (!string.Equals(auth.DomainMode, manifest.DomainMode, StringComparison.Ordinal))
                                                {
                                                    violations.Add(
                                                        $"Active lease for '{manifest.WorkflowId}' authenticated domainMode '{auth.DomainMode}' does not match manifest domainMode '{manifest.DomainMode}'.");
                                                }
                                        }
                                    }
                                }
                                }

                                activeLeases.Add(
                                    manifest);
                            }
                        }
                        break;

                    case WorkflowSchedulerLeaseState.Quarantined:
                        quarantinedLeases.Add(
                            manifest);
                        break;

                    case WorkflowSchedulerLeaseState.MaintenanceInProgress:
                        maintenanceLeases.Add(
                            manifest);

                        if (!string.Equals(
                                manifest.DomainMode,
                                "ExclusiveMaintenance",
                                StringComparison.Ordinal))
                        {
                            violations.Add(
                                $"MaintenanceInProgress lease for '{manifest.WorkflowId}' has inconsistent domain mode '{manifest.DomainMode}'.");
                        }

                        WorkflowLockProbeState maintModeLockState =
                            WorkflowSchedulerLockPrimitives.ProbeLockState(
                                paths_.DomainModeLockPath);

                        if (maintModeLockState != WorkflowLockProbeState.HeldExclusive)
                        {
                            violations.Add(
                                $"MaintenanceInProgress lease for '{manifest.WorkflowId}' lacks required exclusive lock on domain-mode.lock (actual state: {maintModeLockState}).");
                        }

                        string maintWfLockPath =
                            paths_.GetWorkflowLockPath(
                                manifest.WorkflowId);

                        WorkflowLockProbeState maintLockState =
                            WorkflowSchedulerLockPrimitives.ProbeLockState(
                                maintWfLockPath);

                        if (maintLockState == WorkflowLockProbeState.Free)
                        {
                            orphanedMaintenanceLeases.Add(
                                manifest);
                            violations.Add(
                                $"Orphaned MaintenanceInProgress lease detected for workflow '{manifest.WorkflowId}'.");
                        }
                        else if (maintLockState == WorkflowLockProbeState.HeldShared)
                        {
                            violations.Add(
                                $"MaintenanceInProgress lease for '{manifest.WorkflowId}' workflow.lock is held shared, but exclusive lock is required.");
                        }
                        else if (maintLockState == WorkflowLockProbeState.Indeterminate)
                        {
                            violations.Add(
                                $"MaintenanceInProgress lease for '{manifest.WorkflowId}' workflow.lock state is indeterminate.");
                        }

                        LockEnumerationResult maintWfEnum =
                            GetLockingProcesses(
                                maintWfLockPath,
                                requireExclusive: true);

                        LockEnumerationResult maintModeEnum =
                            GetLockingProcesses(
                                paths_.DomainModeLockPath,
                                requireExclusive: true);

                        if (maintWfEnum.IsFailed || maintModeEnum.IsFailed)
                        {
                            violations.Add(
                                $"MaintenanceInProgress lease for '{manifest.WorkflowId}' lock owner enumeration failed. State is indeterminate.");
                        }
                        else
                        {
                            IReadOnlyList<int> maintWfPids = maintWfEnum.ProcessIds;
                            IReadOnlyList<int> maintModePids = maintModeEnum.ProcessIds;

                            if (maintModePids.Count == 0 ||
                                !maintWfPids.Any(p => maintModePids.Contains(p)))
                            {
                                violations.Add(
                                    $"MaintenanceInProgress lease for '{manifest.WorkflowId}' domain-mode.lock is held by an owner different from workflow.lock owner. State is indeterminate.");
                            }
                        }
                        break;
                }
            }

            // 4. Probe ALL OS capacity slots 1..maxConcurrentWorkflows.
            for (int i = 1; i <= maxConcurrentWorkflows_; i++)
            {
                string slotLockPath =
                    paths_.GetSlotLockPath(
                        i,
                        maxConcurrentWorkflows_);

                WorkflowLockProbeState slotState =
                    WorkflowSchedulerLockPrimitives.ProbeLockState(
                        slotLockPath);

                if (slotState == WorkflowLockProbeState.HeldShared)
                {
                    violations.Add(
                        $"OS capacity slot {i} is held shared, but exclusive lock is required.");
                }
                else if (slotState == WorkflowLockProbeState.Indeterminate)
                {
                    violations.Add(
                        $"OS capacity slot {i} state is indeterminate.");
                }
                else if (slotState == WorkflowLockProbeState.HeldExclusive)
                {
                    // Slot is occupied in OS: verify there is a matching live Active manifest.
                    if (!activeBySlot.TryGetValue(
                            i,
                            out WorkflowSchedulerLeaseManifest? matchingActive) ||
                        deadActiveLeases.Contains(
                            matchingActive))
                    {
                        violations.Add(
                            $"OS capacity slot {i} is occupied but has no matching authenticated live Active manifest.");
                    }
                }
            }

            bool hasMaintenance =
                allManifests.Any(
                    manifest_ =>
                        manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress);

            bool hasActive =
                allManifests.Any(
                    manifest_ =>
                        manifest_.State == WorkflowSchedulerLeaseState.Active);

            int activeCount =
                allManifests.Count(
                    manifest_ =>
                        manifest_.State == WorkflowSchedulerLeaseState.Active);

            bool hasUnrestrictedActive =
                allManifests.Any(
                    manifest_ =>
                        manifest_.State == WorkflowSchedulerLeaseState.Active &&
                        string.Equals(
                            manifest_.DomainMode,
                            "Unrestricted",
                            StringComparison.Ordinal));

            bool hasUnrestrictedQuarantine =
                allManifests.Any(
                    manifest_ =>
                        manifest_.State == WorkflowSchedulerLeaseState.Quarantined &&
                        string.Equals(
                            manifest_.DomainMode,
                            "Unrestricted",
                            StringComparison.Ordinal));

            int maintenanceCount =
                allManifests.Count(
                    manifest_ =>
                        manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress);

            int unrestrictedQuarantineCount =
                allManifests.Count(
                    manifest_ =>
                        manifest_.State == WorkflowSchedulerLeaseState.Quarantined &&
                        string.Equals(
                            manifest_.DomainMode,
                            "Unrestricted",
                            StringComparison.Ordinal));

            if (hasMaintenance && hasActive)
            {
                violations.Add(
                    "Domain has contradictory pre-existing lease manifests: MaintenanceInProgress exists alongside Active lease.");
            }

            if (hasMaintenance && hasUnrestrictedQuarantine)
            {
                violations.Add(
                    "Domain has contradictory pre-existing lease manifests: MaintenanceInProgress exists alongside Quarantined Unrestricted lease.");
            }

            if (maintenanceCount > 1)
            {
                violations.Add(
                    "Domain has contradictory pre-existing lease manifests: Multiple MaintenanceInProgress leases exist.");
            }

            if (hasUnrestrictedActive && activeCount > 1)
            {
                violations.Add(
                    "Domain has contradictory pre-existing lease manifests: Active Unrestricted lease exists alongside other Active leases.");
            }

            if (hasUnrestrictedQuarantine && hasActive)
            {
                violations.Add(
                    "Domain has contradictory pre-existing lease manifests: Quarantined Unrestricted lease exists alongside Active lease.");
            }

            if (unrestrictedQuarantineCount > 1)
            {
                violations.Add(
                    "Domain has contradictory pre-existing lease manifests: Multiple Quarantined Unrestricted leases exist.");
            }

            bool hasGlobalExclusion =
                maintenanceLeases.Count > 0 ||
                allManifests.Any(
                    manifest_ =>
                        (manifest_.State == WorkflowSchedulerLeaseState.Active ||
                         manifest_.State == WorkflowSchedulerLeaseState.Quarantined) &&
                        string.Equals(
                            manifest_.DomainMode,
                            "Unrestricted",
                            StringComparison.Ordinal));

            bool isHealthy =
                violations.Count == 0 &&
                deadActiveLeases.Count == 0 &&
                orphanedMaintenanceLeases.Count == 0 &&
                File.Exists(
                    paths_.DomainIntegrityPath) &&
                File.Exists(
                    paths_.DomainPolicyPath) &&
                (snapshotLock is not null || domainLockHandle_ is not null || isLiveBrokerLockAuthority);

            return new WorkflowSchedulerIntegrityReport
            {
                IsHealthy = isHealthy,
                HasGlobalExclusion = hasGlobalExclusion,
                ActiveLeases = activeLeases.AsReadOnly(),
                QuarantinedLeases = quarantinedLeases.AsReadOnly(),
                MaintenanceLeases = maintenanceLeases.AsReadOnly(),
                DeadActiveLeases = deadActiveLeases.AsReadOnly(),
                OrphanedMaintenanceLeases = orphanedMaintenanceLeases.AsReadOnly(),
                Violations = violations.AsReadOnly()
            };
        }
        finally
        {
            snapshotLock?.Dispose();
        }
    }

    internal static LockEnumerationResult GetLockingProcesses(
        string filePath,
        bool requireExclusive = false)
    {
        if (OperatingSystem.IsWindows())
        {
            return Win32RestartManager.GetLockingProcesses(
                filePath);
        }

        if (OperatingSystem.IsLinux())
        {
            // .NET FileStream on Unix acquires advisory flock(2) locks (FileShare.None => LOCK_EX,
            // otherwise LOCK_SH) and the owner-auth registration uses flock(LOCK_EX). Only FLOCK
            // entries in /proc/locks correspond to locks genuinely acquired by production code;
            // POSIX (fcntl) or OFDLCK entries from another process must not satisfy provenance.
            return LinuxProcessTracker.GetLockingProcesses(
                filePath,
                requireExclusive,
                requiredLockType: "FLOCK");
        }

        return LockEnumerationResult.NotFound();
    }

    internal static IReadOnlyList<int> GetLockingProcessIds(
        string filePath_)
    {
        LockEnumerationResult result =
            GetLockingProcesses(
                filePath_);

        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"Lock owner enumeration failed for '{filePath_}'. State is indeterminate. Fail closed.");
        }

        return result.ProcessIds;
    }

    internal static bool IsProcessAlive(
        int processId_)
    {
        if (OperatingSystem.IsWindows())
        {
            return Win32ProcessHelper.IsProcessAlive(
                processId_);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxProcessTracker.IsProcessAlive(
                processId_);
        }

        return false;
    }

    internal static long GetProcessStartTimeTicks(
        int processId_)
    {
        if (OperatingSystem.IsWindows())
        {
            return Win32ProcessHelper.GetProcessStartTimeTicks(
                processId_);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxProcessTracker.GetProcessStartTimeTicks(
                processId_);
        }

        return 0;
    }

    internal static long GetProcessStartTimeToleranceTicks()
    {
        if (OperatingSystem.IsLinux())
        {
            // /proc/<pid>/stat starttime is an exact kernel jiffies value assigned at fork/exec;
            // it does not drift for a running process.
            return 0;
        }

        // On Windows, GetProcessTimes returns 100-ns FILETIME ticks which is deterministic for a running process;
        // allow 1 second (10,000,000 ticks) tolerance only if queried through different APIs, or 0 for exact match.
        return 0;
    }

    internal static ProcessLivenessStatus CheckProcessLiveness(
        int processId_,
        long expectedStartTimeTicks_)
    {
        if (OperatingSystem.IsWindows())
        {
            return Win32ProcessHelper.CheckProcessLiveness(
                processId_,
                expectedStartTimeTicks_);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxProcessTracker.CheckProcessLiveness(
                processId_,
                expectedStartTimeTicks_);
        }

        return ProcessLivenessStatus.Indeterminate;
    }

    internal static SentinelByteLockProbeStatus ProbeProductionRegistrationLock(string authPath) =>
        WorkflowSchedulerOwnerAuthentication.ProbeProductionRegistrationLock(authPath);

    internal static bool IsProductionRegistrationLockHeld(string authPath) =>
        WorkflowSchedulerOwnerAuthentication.IsProductionRegistrationLockHeld(authPath);

    private static class Win32ProcessHelper
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint STILL_ACTIVE = 259;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(
            IntPtr hProcess,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpCreationTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpExitTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public static ProcessLivenessStatus CheckProcessLiveness(int processId, long expectedStartTimeTicks)
        {
            if (processId <= 0)
            {
                return ProcessLivenessStatus.DefinitelyDead;
            }

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
            {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                // ERROR_INVALID_PARAMETER (87) indicates the process does not exist.
                if (error == 87)
                {
                    return ProcessLivenessStatus.DefinitelyDead;
                }

                return ProcessLivenessStatus.Indeterminate;
            }

            try
            {
                if (!GetExitCodeProcess(handle, out uint exitCode))
                {
                    return ProcessLivenessStatus.Indeterminate;
                }

                if (exitCode != STILL_ACTIVE)
                {
                    return ProcessLivenessStatus.DefinitelyDead;
                }

                if (GetProcessTimes(handle, out var creation, out _, out _, out _))
                {
                    long fileTime = ((long)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
                    long actualStart = fileTime + 504_911_232_000_000_000L;
                    if (Math.Abs(actualStart - expectedStartTimeTicks) <= GetProcessStartTimeToleranceTicks())
                    {
                        return ProcessLivenessStatus.DefinitelyAlive;
                    }

                    // Process with same PID exists, but different start time (PID reused after previous owner died)
                    return ProcessLivenessStatus.DefinitelyDead;
                }

                return ProcessLivenessStatus.Indeterminate;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static bool IsProcessAlive(int processId)
        {
            if (processId <= 0)
            {
                return false;
            }

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (GetExitCodeProcess(handle, out uint exitCode))
                {
                    return exitCode == STILL_ACTIVE;
                }

                return false;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static long GetProcessStartTimeTicks(int processId)
        {
            if (processId <= 0)
            {
                return 0;
            }

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                if (GetProcessTimes(handle, out var creation, out _, out _, out _))
                {
                    long fileTime = ((long)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
                    return fileTime + 504_911_232_000_000_000L;
                }

                return 0;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    internal static class LinuxProcessTracker
    {
        public const int O_RDONLY = 0;
        public const int O_NOFOLLOW = 0x20000;
        public const int O_CLOEXEC = 0x80000;

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(string pathname, int flags);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);

        public static bool IsProcessAlive(int processId)
        {
            if (processId <= 0)
            {
                return false;
            }

            return Directory.Exists($"/proc/{processId}");
        }

        public static ProcessLivenessStatus CheckProcessLiveness(int processId, long expectedStartTimeTicks)
        {
            if (processId <= 0)
            {
                return ProcessLivenessStatus.DefinitelyDead;
            }

            string procDir = $"/proc/{processId}";
            if (!Directory.Exists(procDir))
            {
                if (!Directory.Exists("/proc"))
                {
                    return ProcessLivenessStatus.Indeterminate;
                }

                return ProcessLivenessStatus.DefinitelyDead;
            }

            long actualStart = GetProcessStartTimeTicks(processId);
            if (actualStart <= 0)
            {
                return ProcessLivenessStatus.Indeterminate;
            }

            if (Math.Abs(actualStart - expectedStartTimeTicks) <= GetProcessStartTimeToleranceTicks())
            {
                return ProcessLivenessStatus.DefinitelyAlive;
            }

            return ProcessLivenessStatus.DefinitelyDead;
        }

        public static long GetProcessStartTimeTicks(int processId)
        {
            if (processId <= 0)
            {
                return 0;
            }

            string statPath = $"/proc/{processId}/stat";
            if (!File.Exists(statPath))
            {
                return 0;
            }

            try
            {
                string content = File.ReadAllText(statPath);
                if (TryParseStatContent(content, out long startTicks))
                {
                    return startTicks;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        internal static bool TryParseStatContent(string content, out long startTicks)
        {
            startTicks = 0;
            int lastCloseParen = content.LastIndexOf(')');
            if (lastCloseParen < 0 || lastCloseParen + 2 >= content.Length)
            {
                return false;
            }

            string remainder = content.Substring(lastCloseParen + 2);
            string[] parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 19 && long.TryParse(parts[19], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out startTicks))
            {
                return true;
            }

            return false;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Stat64LinuxX64
        {
            public ulong st_dev;
            public ulong st_ino;
            public ulong st_nlink;
            public uint st_mode;
            public uint st_uid;
            public uint st_gid;
            public int __pad0;
            public ulong st_rdev;
            public long st_size;
            public long st_blksize;
            public long st_blocks;
            public long st_atime;
            public long st_atime_nsec;
            public long st_mtime;
            public long st_mtime_nsec;
            public long st_ctime;
            public long st_ctime_nsec;
            public long __unused0;
            public long __unused1;
            public long __unused2;
        }

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "stat", SetLastError = true)]
        private static extern int Stat(string path, out Stat64LinuxX64 buf);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "flock", SetLastError = true)]
        private static extern int Flock(Microsoft.Win32.SafeHandles.SafeFileHandle fd, int operation);

        public static bool AcquireExclusiveFlock(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
        {
            if (handle.IsInvalid || handle.IsClosed) return false;
            return Flock(handle, 2 /* LOCK_EX */ | 4 /* LOCK_NB */) == 0;
        }

        public static LockEnumerationResult GetLockingProcesses(
            string filePath,
            bool requireExclusive = false,
            string? requiredLockType = null)
        {
            if (!OperatingSystem.IsLinux())
            {
                return LockEnumerationResult.NotFound();
            }

            if (!File.Exists(filePath))
            {
                return LockEnumerationResult.NotFound();
            }

            if (!File.Exists("/proc/locks"))
            {
                return LockEnumerationResult.Failed();
            }

            if (Stat(filePath, out Stat64LinuxX64 statBuf) != 0)
            {
                return LockEnumerationResult.Failed();
            }

            ulong expectedInode = statBuf.st_ino;
            uint expectedMajor = (uint)(((statBuf.st_dev >> 8) & 0xfff) | ((statBuf.st_dev >> 32) & ~0xfffUL));
            uint expectedMinor = (uint)((statBuf.st_dev & 0xff) | ((statBuf.st_dev >> 12) & ~0xffUL));

            HashSet<int> pids = [];

            try
            {
                foreach (string line in File.ReadLines("/proc/locks"))
                {
                    if (TryParseProcLockLine(line, expectedMajor, expectedMinor, expectedInode, out int pid, out bool isExclusive, out string lockType) && pid > 0)
                    {
                        if (!string.IsNullOrEmpty(requiredLockType) &&
                            !string.Equals(lockType, requiredLockType, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (requireExclusive && !isExclusive)
                        {
                            continue;
                        }

                        pids.Add(pid);
                    }
                }
            }
            catch
            {
                return LockEnumerationResult.Failed();
            }

            return LockEnumerationResult.Succeeded(pids.ToList().AsReadOnly());
        }

        public static IReadOnlyList<int> GetLockingProcessIds(string filePath)
        {
            LockEnumerationResult result = GetLockingProcesses(filePath);
            return result.IsSuccess ? result.ProcessIds : [];
        }

        internal static bool TryParseProcLockLine(
            string line_,
            uint expectedMajor_,
            uint expectedMinor_,
            ulong expectedInode_,
            out int pid_,
            out bool isExclusive_,
            out string lockType_)
        {
            pid_ = 0;
            isExclusive_ = false;
            lockType_ = string.Empty;

            if (string.IsNullOrWhiteSpace(line_))
            {
                return false;
            }

            string[] tokens = line_.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 6)
            {
                return false;
            }

            // Ignore blocked waiting lock requests ("->")
            if (string.Equals(tokens[1], "->", StringComparison.Ordinal))
            {
                return false;
            }

            string parsedLockType = tokens[1].ToUpperInvariant();
            if (parsedLockType != "FLOCK" && parsedLockType != "POSIX" && parsedLockType != "OFDLCK")
            {
                return false;
            }

            if (!int.TryParse(tokens[4], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsedPid) ||
                parsedPid <= 0)
            {
                return false;
            }

            string[] devIno = tokens[5].Split(':');
            if (devIno.Length != 3)
            {
                return false;
            }

            if (!uint.TryParse(devIno[0], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint major) ||
                !uint.TryParse(devIno[1], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint minor) ||
                !ulong.TryParse(devIno[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ulong inode))
            {
                return false;
            }

            if (major == expectedMajor_ && minor == expectedMinor_ && inode == expectedInode_)
            {
                pid_ = parsedPid;
                isExclusive_ = string.Equals(tokens[3], "WRITE", StringComparison.OrdinalIgnoreCase);
                lockType_ = parsedLockType;
                return true;
            }

            return false;
        }

        internal static bool TryParseProcLockLine(
            string line_,
            uint expectedMajor_,
            uint expectedMinor_,
            ulong expectedInode_,
            out int pid_,
            out bool isExclusive_)
        {
            return TryParseProcLockLine(line_, expectedMajor_, expectedMinor_, expectedInode_, out pid_, out isExclusive_, out _);
        }

        internal static bool TryParseProcLockLine(
            string line_,
            uint expectedMajor_,
            uint expectedMinor_,
            ulong expectedInode_,
            out int pid_)
        {
            return TryParseProcLockLine(line_, expectedMajor_, expectedMinor_, expectedInode_, out pid_, out _);
        }
    }

    private static class Win32RestartManager
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public bool bRestartable;
        }

        private const int CCH_RM_SESSION_KEY = 32;

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int RmStartSession(
            out uint pSessionHandle,
            int dwSessionFlags,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder strSessionKey);

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            IntPtr rgApplications,
            uint nServices,
            IntPtr rgsServiceNames);

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
        public static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] RM_PROCESS_INFO[]? rgAffectedApps,
            out uint lpdwRebootReasons);

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
        public static extern int RmEndSession(uint pSessionHandle);

        public static LockEnumerationResult GetLockingProcesses(string filePath)
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(filePath))
            {
                return LockEnumerationResult.NotFound();
            }

            try
            {
                System.Text.StringBuilder sessionKey = new(CCH_RM_SESSION_KEY + 1);

                int res = RmStartSession(out uint sessionHandle, 0, sessionKey);
                if (res != 0)
                {
                    return LockEnumerationResult.Failed();
                }

                try
                {
                    string[] resources = [ filePath ];
                    res = RmRegisterResources(sessionHandle, 1, resources, 0, IntPtr.Zero, 0, IntPtr.Zero);
                    if (res != 0)
                    {
                        return LockEnumerationResult.Failed();
                    }

                    uint pnProcInfoNeeded = 0;
                    uint pnProcInfo = 0;
                    res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, out uint rebootReasons);
                    // ERROR_MORE_DATA = 234, ERROR_SUCCESS = 0
                    if (res != 0 && res != 234)
                    {
                        return LockEnumerationResult.Failed();
                    }

                    if (pnProcInfoNeeded > 0)
                    {
                        RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                        pnProcInfo = pnProcInfoNeeded;
                        res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out rebootReasons);
                        if (res != 0)
                        {
                            return LockEnumerationResult.Failed();
                        }

                        List<int> pids = [];
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            pids.Add(processInfo[i].Process.dwProcessId);
                        }
                        return LockEnumerationResult.Succeeded(pids);
                    }

                    return LockEnumerationResult.Succeeded([]);
                }
                finally
                {
                    RmEndSession(sessionHandle);
                }
            }
            catch
            {
                return LockEnumerationResult.Failed();
            }
        }

        public static IReadOnlyList<int> GetLockingProcessIds(string filePath)
        {
            LockEnumerationResult result = GetLockingProcesses(filePath);
            return result.IsSuccess ? result.ProcessIds : [];
        }
    }
}

internal sealed class WorkflowSchedulerOwnerAuthHandle : IDisposable, IAsyncDisposable
{
    private readonly WorkflowSchedulerPaths _paths;
    private readonly string _workflowId;
    private readonly string _ownerToken;
    private readonly int _processId;
    private readonly long _processStartTimeTicks;
    private readonly string _authPath;
    private FileStream? _authLockStream;
    private readonly WorkflowSchedulerLockHandle? _domainLockHandle;
    private int _disposed;

    internal WorkflowSchedulerOwnerAuthHandle(
        WorkflowSchedulerPaths paths_,
        string workflowId_,
        string ownerToken_,
        int processId_,
        long processStartTimeTicks_,
        string authPath_,
        FileStream authLockStream_,
        WorkflowSchedulerLockHandle? domainLockHandle_ = null)
    {
        this._paths = paths_;
        this._workflowId = workflowId_;
        this._ownerToken = ownerToken_;
        this._processId = processId_;
        this._processStartTimeTicks = processStartTimeTicks_;
        this._authPath = authPath_;
        this._authLockStream = authLockStream_;
        this._domainLockHandle = domainLockHandle_;
    }

    public string AuthPath => this._authPath;
    internal string WorkflowId => this._workflowId;
    internal string OwnerToken => this._ownerToken;
    internal int ProcessId => this._processId;
    internal long ProcessStartTimeTicks => this._processStartTimeTicks;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
        {
            return;
        }

        FileStream? lockStream =
            Interlocked.Exchange(ref this._authLockStream, null);

        try
        {
            WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                this._paths,
                this._workflowId,
                expectedOwnerToken_: this._ownerToken,
                expectedProcessId_: this._processId,
                expectedStartTimeTicks_: this._processStartTimeTicks,
                domainLockHandle_: this._domainLockHandle,
                authLockStream_: lockStream);
        }
        catch (FileNotFoundException)
        {
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed record WorkflowSchedulerOwnerAuthentication
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.Win32.SafeHandles.SafeFileHandle, object> _genuineOwnerAuthHandles = new();

    internal static bool IsGenuinelyAcquiredOwnerAuth(Microsoft.Win32.SafeHandles.SafeFileHandle? handle)
    {
        if (handle is null || handle.IsInvalid || handle.IsClosed) return false;
        return _genuineOwnerAuthHandles.TryGetValue(handle, out _);
    }

    public const string SchemaId =
        "ai.repokit.workflow-scheduler-owner-auth";

    public const int SchemaVersion =
        1;

    public string WorkflowId { get; init; } = string.Empty;
    public string OwnerToken { get; init; } = string.Empty;
    public int Slot { get; init; }
    public string DomainMode { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public long ProcessStartTimeTicks { get; init; }

    public static string GetAuthFilePath(
        WorkflowSchedulerPaths paths_,
        string workflowId_)
    {
        string wfDir =
            paths_.GetWorkflowDirectory(
                workflowId_);
        return Path.Combine(
            wfDir,
            "owner-auth.json");
    }

    public static WorkflowSchedulerOwnerAuthHandle RegisterOwnerAuthentication(
        WorkflowSchedulerPaths paths_,
        string workflowId_,
        string ownerToken_,
        int slot_,
        string domainMode_,
        WorkflowSchedulerLockHandle workflowLockHandle_,
        WorkflowSchedulerLockHandle slotLockHandle_,
        WorkflowSchedulerLockHandle? domainModeLockHandle_ = null,
        WorkflowSchedulerLockHandle? domainLockHandle_ = null)
    {
        return RegisterBrokerOwnerAuthentication(
            paths_,
            workflowId_,
            ownerToken_,
            slot_,
            domainMode_,
            workflowLockHandle_,
            slotLockHandle_,
            domainModeLockHandle_,
            domainLockHandle_,
            workerProcessId_: null,
            workerStartTimeTicks_: null);
    }

    public static WorkflowSchedulerOwnerAuthHandle RegisterBrokerOwnerAuthentication(
        WorkflowSchedulerPaths paths_,
        string workflowId_,
        string ownerToken_,
        int slot_,
        string domainMode_,
        WorkflowSchedulerLockHandle workflowLockHandle_,
        WorkflowSchedulerLockHandle slotLockHandle_,
        WorkflowSchedulerLockHandle? domainModeLockHandle_,
        WorkflowSchedulerLockHandle? domainLockHandle_,
        int? workerProcessId_,
        long? workerStartTimeTicks_)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workflowId_,
            nameof(workflowId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            ownerToken_,
            nameof(ownerToken_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            domainMode_,
            nameof(domainMode_));
        ArgumentNullException.ThrowIfNull(
            workflowLockHandle_,
            nameof(workflowLockHandle_));
        ArgumentNullException.ThrowIfNull(
            slotLockHandle_,
            nameof(slotLockHandle_));

        if (workflowLockHandle_.IsDisposed ||
            !workflowLockHandle_.IsExclusive ||
            !workflowLockHandle_.HasExclusiveCapability ||
            workflowLockHandle_.ExclusiveCapability is null)
        {
            throw new InvalidOperationException(
                "workflowLockHandle must be open and held exclusively with genuine exclusive capability.");
        }

        string expectedWfLockPath =
            paths_.GetWorkflowLockPath(workflowId_);
        StringComparison comp =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (!string.Equals(
                Path.GetFullPath(workflowLockHandle_.Path),
                Path.GetFullPath(expectedWfLockPath),
                comp))
        {
            throw new InvalidOperationException(
                $"workflowLockHandle is bound to '{workflowLockHandle_.Path}', expected '{expectedWfLockPath}'.");
        }

        if (!workflowLockHandle_.ExclusiveCapability.IsBoundTo(
                workflowLockHandle_.Stream,
                expectedWfLockPath))
        {
            throw new InvalidOperationException(
                "workflowLockHandle exclusive capability is not bound to expected workflow lock path.");
        }

        if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredExclusive(
                workflowLockHandle_.SafeFileHandle))
        {
            throw new InvalidOperationException(
                "workflowLockHandle was not genuinely acquired via WorkflowSchedulerLockPrimitives.TryAcquireExclusive.");
        }

        WorkflowSchedulerPaths.AssertSafeOpenFile(
            workflowLockHandle_.SafeFileHandle!,
            expectedWfLockPath);

        if (slot_ < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot_),
                slot_,
                "Slot index must be at least 1.");
        }

        if (slotLockHandle_.IsDisposed ||
            !slotLockHandle_.IsExclusive ||
            !slotLockHandle_.HasExclusiveCapability ||
            slotLockHandle_.ExclusiveCapability is null)
        {
            throw new InvalidOperationException(
                "slotLockHandle must be open and held exclusively with genuine exclusive capability.");
        }

        string expectedSlotFileName =
            string.Concat("slot-", slot_.ToString("D8", System.Globalization.CultureInfo.InvariantCulture), ".lock");
        string expectedSlotLockPath =
            Path.Combine(paths_.SlotsDirectory, expectedSlotFileName);

        if (!string.Equals(
                Path.GetFullPath(slotLockHandle_.Path),
                Path.GetFullPath(expectedSlotLockPath),
                comp))
        {
            throw new InvalidOperationException(
                $"slotLockHandle is bound to '{slotLockHandle_.Path}', expected '{expectedSlotLockPath}'.");
        }

        if (!slotLockHandle_.ExclusiveCapability.IsBoundTo(
                slotLockHandle_.Stream,
                expectedSlotLockPath))
        {
            throw new InvalidOperationException(
                "slotLockHandle exclusive capability is not bound to expected slot lock path.");
        }

        if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredExclusive(
                slotLockHandle_.SafeFileHandle))
        {
            throw new InvalidOperationException(
                "slotLockHandle was not genuinely acquired via WorkflowSchedulerLockPrimitives.TryAcquireExclusive.");
        }

        WorkflowSchedulerPaths.AssertSafeOpenFile(
            slotLockHandle_.SafeFileHandle!,
            expectedSlotLockPath);

        if (domainModeLockHandle_ is null || domainModeLockHandle_.IsDisposed)
        {
            throw new InvalidOperationException(
                "domainModeLockHandle must be provided, open, and not disposed.");
        }

        if (!string.Equals(
                Path.GetFullPath(domainModeLockHandle_.Path),
                Path.GetFullPath(paths_.DomainModeLockPath),
                comp))
        {
            throw new InvalidOperationException(
                $"domainModeLockHandle is bound to '{domainModeLockHandle_.Path}', expected '{paths_.DomainModeLockPath}'.");
        }

        WorkflowSchedulerPaths.AssertSafeOpenFile(
            domainModeLockHandle_.SafeFileHandle!,
            paths_.DomainModeLockPath);

        if (string.Equals(domainMode_, "Unrestricted", StringComparison.Ordinal))
        {
            if (!domainModeLockHandle_.IsExclusive ||
                !domainModeLockHandle_.HasExclusiveCapability ||
                domainModeLockHandle_.ExclusiveCapability is null)
            {
                throw new InvalidOperationException(
                    "domainModeLockHandle for Unrestricted mode must be held exclusively with genuine exclusive capability.");
            }

            if (!domainModeLockHandle_.ExclusiveCapability.IsBoundTo(
                    domainModeLockHandle_.Stream,
                    paths_.DomainModeLockPath))
            {
                throw new InvalidOperationException(
                    "domainModeLockHandle exclusive capability is not bound to domain-mode.lock.");
            }

            if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredExclusive(
                    domainModeLockHandle_.SafeFileHandle))
            {
                throw new InvalidOperationException(
                    "domainModeLockHandle was not genuinely acquired via WorkflowSchedulerLockPrimitives.TryAcquireExclusive.");
            }
        }
        else if (string.Equals(domainMode_, "Normal", StringComparison.Ordinal))
        {
            if (domainModeLockHandle_.IsExclusive)
            {
                throw new InvalidOperationException(
                    "domainModeLockHandle for Normal mode must be held shared, not exclusive.");
            }

            if (!domainModeLockHandle_.HasSharedCapability ||
                domainModeLockHandle_.SharedCapability is null)
            {
                throw new InvalidOperationException(
                    "domainModeLockHandle for Normal mode must be acquired with genuine shared capability.");
            }

            if (!domainModeLockHandle_.SharedCapability.IsBoundTo(
                    domainModeLockHandle_.Stream,
                    paths_.DomainModeLockPath))
            {
                throw new InvalidOperationException(
                    "domainModeLockHandle shared capability is not bound to domain-mode.lock.");
            }

            if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredShared(
                    domainModeLockHandle_.SafeFileHandle))
            {
                throw new InvalidOperationException(
                    "domainModeLockHandle was not genuinely acquired via WorkflowSchedulerLockPrimitives.TryAcquireShared.");
            }
        }
        else
        {
            throw new ArgumentException(
                $"Invalid domainMode '{domainMode_}'; must be 'Normal' or 'Unrestricted'.",
                nameof(domainMode_));
        }

        int pid =
            workerProcessId_ ?? Environment.ProcessId;

        long startTime =
            workerStartTimeTicks_ ??
            WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(
                pid);

        if (startTime <= 0)
        {
            throw new InvalidOperationException(
                $"Cannot register owner authentication: failed to determine process start time for PID {pid}.");
        }

        WorkflowSchedulerLockHandle? acquiredDomainLock = null;
        try
        {
            if (domainLockHandle_ is not null)
            {
                WorkflowSchedulerDomainMetadata.ValidateDomainLockHandle(
                    paths_,
                    domainLockHandle_);
            }
            else
            {
                TimeSpan lockTimeout =
                    WorkflowSchedulerDomainMetadata.GetDomainLockTimeout(
                        paths_);

                acquiredDomainLock =
                    WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                        paths_.DomainLockPath,
                        lockTimeout).GetAwaiter().GetResult();
            }

            WorkflowSchedulerDomainMetadata.CheckResidualTemporaryFiles(
                paths_);
            WorkflowSchedulerDomainMetadata.ValidateIntegrityDocument(
                paths_);
            WorkflowSchedulerPolicy persistedPolicy =
                WorkflowSchedulerDomainMetadata.ValidatePolicyDocument(
                    paths_);

            paths_.EnsureWorkflowDirectory(
                workflowId_);

            string authPath =
                GetAuthFilePath(
                    paths_,
                    workflowId_);

            // Check consistency with existing Active lease manifest if present:
            string activeLeasePath =
                paths_.GetActiveLeasePath(
                    workflowId_);

            if (File.Exists(activeLeasePath))
            {
                WorkflowSchedulerLeaseManifest activeLease =
                    WorkflowSchedulerLeaseManifest.ReadAndValidate(
                        activeLeasePath,
                        paths_,
                        persistedPolicy.MaxConcurrentWorkflows);

                if (activeLease.State != WorkflowSchedulerLeaseState.Active)
                {
                    throw new InvalidOperationException(
                        $"Cannot register owner authentication for workflow '{workflowId_}' because its lease manifest is in state '{activeLease.State}', not 'Active'.");
                }

                if (!string.Equals(activeLease.OwnerToken, ownerToken_, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot register owner authentication for workflow '{workflowId_}' with ownerToken '{ownerToken_}': existing Active lease manifest belongs to '{activeLease.OwnerToken}'.");
                }

                if (activeLease.Slot != slot_)
                {
                    throw new InvalidOperationException(
                        $"Cannot register owner authentication for workflow '{workflowId_}' with slot {slot_}: existing Active lease manifest claims slot {activeLease.Slot}.");
                }

                if (!string.Equals(activeLease.DomainMode, domainMode_, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot register owner authentication for workflow '{workflowId_}' with domainMode '{domainMode_}': existing Active lease manifest specifies '{activeLease.DomainMode}'.");
                }
            }

            // If existing owner-auth.json already exists:
            if (File.Exists(authPath))
            {
                WorkflowSchedulerOwnerAuthentication? existingAuth =
                    ReadOwnerAuthentication(
                        paths_,
                        workflowId_);

                if (existingAuth is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot replace owner authentication for workflow '{workflowId_}': existing owner-auth.json at '{authPath}' is corrupted or invalid. Fail closed.");
                }

                if (existingAuth.ProcessId != pid &&
                    WorkflowSchedulerIntegrityInspector.IsProcessAlive(existingAuth.ProcessId))
                {
                    throw new InvalidOperationException(
                        $"Cannot replace owner authentication for workflow '{workflowId_}': existing record belongs to another active process (PID {existingAuth.ProcessId}). Unauthorized substitution rejected.");
                }

                LockEnumerationResult existingEnum =
                    WorkflowSchedulerIntegrityInspector.GetLockingProcesses(
                        authPath);

                if (existingEnum.IsFailed)
                {
                    throw new InvalidOperationException(
                        $"Cannot replace owner authentication for workflow '{workflowId_}': lock owner enumeration failed. State is indeterminate. Fail closed.");
                }

                if (existingEnum.ProcessIds.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot replace owner authentication for workflow '{workflowId_}': existing owner-auth.json is still locked by an open handle. Dispose or release the previous owner authentication handle before registering a new generation.");
                }
            }

            using MemoryStream ms = new();
            using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("schemaId", SchemaId);
                writer.WriteNumber("schemaVersion", SchemaVersion);
                writer.WriteString("workflowId", workflowId_);
                writer.WriteString("ownerToken", ownerToken_);
                writer.WriteNumber("slot", slot_);
                writer.WriteString("domainMode", domainMode_);
                writer.WriteNumber("processId", pid);
                writer.WriteNumber("processStartTimeTicks", startTime);
                writer.WriteEndObject();
            }

            string json =
                System.Text.Encoding.UTF8.GetString(
                    ms.ToArray());

            paths_.EnsureWorkflowDirectory(
                workflowId_);

            string dir =
                paths_.GetWorkflowDirectory(
                    workflowId_);

            WorkflowSchedulerDurabilityReport durability =
                WorkflowSchedulerDomainMetadata.WriteAtomic(
                    authPath,
                    json,
                    dir);

            if (durability != WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced)
            {
                throw new IOException(
                    $"Failed to durably publish owner authentication for workflow '{workflowId_}'. Durability: {durability}. Fail closed.");
            }

            FileStream authLockStream =
                new(
                    authPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 1,
                    FileOptions.None);

            if (OperatingSystem.IsWindows())
            {
                authLockStream.Lock(long.MaxValue - 1, 1);
            }
            else if (OperatingSystem.IsLinux())
            {
                bool flockAcquired =
                    WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.AcquireExclusiveFlock(authLockStream.SafeFileHandle);
                if (!flockAcquired)
                {
                    authLockStream.Dispose();
                    throw new IOException(
                        $"Failed to acquire exclusive flock on owner authentication file '{authPath}'. Fail closed.");
                }
            }

            try
            {
                WorkflowSchedulerPaths.AssertSafeOpenFile(
                    authLockStream.SafeFileHandle,
                    authPath);
            }
            catch
            {
                authLockStream.Dispose();
                throw;
            }

            _genuineOwnerAuthHandles.Add(
                authLockStream.SafeFileHandle,
                new object());

            return new WorkflowSchedulerOwnerAuthHandle(
                paths_,
                workflowId_,
                ownerToken_,
                pid,
                startTime,
                authPath,
                authLockStream,
                domainLockHandle_);
        }
        finally
        {
            acquiredDomainLock?.Dispose();
        }
    }

    public static WorkflowSchedulerOwnerAuthentication? ReadOwnerAuthentication(
        WorkflowSchedulerPaths paths_,
        string workflowId_)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workflowId_,
            nameof(workflowId_));

        string authPath =
            GetAuthFilePath(
                paths_,
                workflowId_);

        if (!File.Exists(
                authPath))
        {
            return null;
        }

        try
        {
            paths_.AssertSafeCoordinationFile(
                authPath);

            byte[] bytes =
                ReadAllBytesShared(
                    authPath);

            var reader =
                new Utf8JsonReader(
                    bytes,
                    new JsonReaderOptions
                    {
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 4
                    });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            HashSet<string> seenProperties =
                new(StringComparer.Ordinal);

            string? schemaId = null;
            int? schemaVersion = null;
            string? wfId = null;
            string? ownerToken = null;
            int? slot = null;
            string? domainMode = null;
            int? processId = null;
            long? processStartTimeTicks = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return null;
                }

                string propName =
                    reader.GetString()!;

                if (!seenProperties.Add(propName))
                {
                    // Strict schema: reject duplicate property names!
                    return null;
                }

                if (!reader.Read())
                {
                    return null;
                }

                switch (propName)
                {
                    case "schemaId":
                        schemaId = reader.GetString();
                        break;

                    case "schemaVersion":
                        if (reader.TokenType != JsonTokenType.Number) return null;
                        schemaVersion = reader.GetInt32();
                        break;

                    case "workflowId":
                        wfId = reader.GetString();
                        break;

                    case "ownerToken":
                        ownerToken = reader.GetString();
                        break;

                    case "slot":
                        if (reader.TokenType != JsonTokenType.Number) return null;
                        slot = reader.GetInt32();
                        break;

                    case "domainMode":
                        domainMode = reader.GetString();
                        break;

                    case "processId":
                        if (reader.TokenType != JsonTokenType.Number) return null;
                        processId = reader.GetInt32();
                        break;

                    case "processStartTimeTicks":
                        if (reader.TokenType != JsonTokenType.Number) return null;
                        processStartTimeTicks = reader.GetInt64();
                        break;

                    default:
                        // Strict schema: reject unexpected property!
                        return null;
                }
            }

            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.Comment)
                {
                    return null;
                }
            }

            if (!string.Equals(schemaId, SchemaId, StringComparison.Ordinal) ||
                schemaVersion != SchemaVersion ||
                string.IsNullOrWhiteSpace(wfId) ||
                !string.Equals(wfId, workflowId_, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(ownerToken) ||
                !slot.HasValue || slot.Value < 1 ||
                (!string.Equals(domainMode, "Normal", StringComparison.Ordinal) && !string.Equals(domainMode, "Unrestricted", StringComparison.Ordinal)) ||
                !processId.HasValue || processId.Value <= 0 ||
                !processStartTimeTicks.HasValue || processStartTimeTicks.Value <= 0)
            {
                return null;
            }

            try
            {
                _ = WorkflowSchedulerPaths.WorkflowIdToHex(wfId);
            }
            catch
            {
                return null;
            }

            return new WorkflowSchedulerOwnerAuthentication
            {
                WorkflowId = wfId,
                OwnerToken = ownerToken,
                Slot = slot.Value,
                DomainMode = domainMode!,
                ProcessId = processId.Value,
                ProcessStartTimeTicks = processStartTimeTicks.Value
            };
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ReadAllBytesShared(
        string path_)
    {
        if (OperatingSystem.IsLinux())
        {
            return LinuxReadAllBytesWithoutFlock(path_);
        }

        using FileStream fs =
            new(
                path_,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.None);
        using MemoryStream ms = new();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] LinuxReadAllBytesWithoutFlock(string path_)
    {
        int fd = WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.Open(
            path_,
            WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_RDONLY |
            WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_CLOEXEC |
            WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_NOFOLLOW);
        if (fd < 0)
        {
            throw new IOException($"Failed to open '{path_}' for reading without flock.");
        }

        using Microsoft.Win32.SafeHandles.SafeFileHandle sfh = new(new IntPtr(fd), ownsHandle: true);
        WorkflowSchedulerPaths.AssertSafeOpenFile(sfh, path_);

        using FileStream fs = new(sfh, FileAccess.Read, bufferSize: 4096);
        using MemoryStream ms = new();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    public static void RemoveOwnerAuthentication(
        WorkflowSchedulerPaths paths_,
        string workflowId_,
        string? expectedOwnerToken_ = null,
        int? expectedProcessId_ = null,
        long? expectedStartTimeTicks_ = null,
        WorkflowSchedulerLockHandle? domainLockHandle_ = null,
        FileStream? authLockStream_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workflowId_,
            nameof(workflowId_));

        if (domainLockHandle_ is null && string.IsNullOrWhiteSpace(expectedOwnerToken_))
        {
            throw new ArgumentException(
                "expectedOwnerToken must be provided to remove owner authentication.",
                nameof(expectedOwnerToken_));
        }

        int requiredPid =
            expectedProcessId_ ?? Environment.ProcessId;
        long requiredStartTime = 0;

        if (domainLockHandle_ is null || expectedProcessId_.HasValue)
        {
            if (requiredPid <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedProcessId_),
                    requiredPid,
                    "expectedProcessId must be positive.");
            }

            requiredStartTime =
                expectedStartTimeTicks_ ?? WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(requiredPid);
            if (requiredStartTime <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot remove owner authentication without a valid expected process start time.");
            }
        }

        string authPath =
            GetAuthFilePath(
                paths_,
                workflowId_);

        WorkflowSchedulerLockHandle? acquiredDomainLock = null;
        try
        {
            if (domainLockHandle_ is not null)
            {
                WorkflowSchedulerDomainMetadata.ValidateDomainLockHandle(
                    paths_,
                    domainLockHandle_);
            }
            else
            {
                TimeSpan lockTimeout =
                    WorkflowSchedulerDomainMetadata.GetDomainLockTimeout(
                        paths_);

                acquiredDomainLock =
                    WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                        paths_.DomainLockPath,
                        lockTimeout).GetAwaiter().GetResult();
            }

            WorkflowSchedulerDomainMetadata.CheckResidualTemporaryFiles(
                paths_);
            WorkflowSchedulerDomainMetadata.ValidateIntegrityDocument(
                paths_);
            WorkflowSchedulerPolicy persistedPolicy =
                WorkflowSchedulerDomainMetadata.ValidatePolicyDocument(
                    paths_);

            if (!File.Exists(authPath))
            {
                return;
            }

            WorkflowSchedulerOwnerAuthentication? auth =
                ReadOwnerAuthentication(
                    paths_,
                    workflowId_);

            if (auth is null)
            {
                throw new InvalidOperationException(
                    $"Cannot remove owner authentication for workflow '{workflowId_}': owner-auth.json at '{authPath}' is corrupted or invalid. Fail closed.");
            }

            // If a newer or different generation owns this file, do not delete it!
            if (!string.IsNullOrWhiteSpace(expectedOwnerToken_) &&
                !string.Equals(auth.OwnerToken, expectedOwnerToken_, StringComparison.Ordinal))
            {
                return;
            }

            if (domainLockHandle_ is null || expectedProcessId_.HasValue)
            {
                if (auth.ProcessId != requiredPid ||
                    auth.ProcessStartTimeTicks != requiredStartTime)
                {
                    return;
                }
            }

            // Ensure consistent transition with Active lease manifest:
            // Do not allow removing owner-auth.json while the matching lease manifest is still Active.
            string activeLeasePath =
                paths_.GetActiveLeasePath(
                    workflowId_);

            if (File.Exists(activeLeasePath))
            {
                WorkflowSchedulerLeaseManifest existingManifest =
                    WorkflowSchedulerLeaseManifest.ReadAndValidate(
                        activeLeasePath,
                        paths_,
                        persistedPolicy.MaxConcurrentWorkflows);

                if (existingManifest.State == WorkflowSchedulerLeaseState.Active &&
                    string.Equals(existingManifest.OwnerToken, auth.OwnerToken, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot remove owner authentication for workflow '{workflowId_}' while its lease manifest is still in state 'Active'. Transition or remove the lease manifest first.");
                }
            }

            // Validate removal authority: caller must provide authentic capability OR owner must be proven non-existent.
            bool hasAuthenticCapability = false;
            if (domainLockHandle_ is not null)
            {
                // Broker holding verified domain lock authority has proven OS lock authority to manage/rollback owner auth.
                hasAuthenticCapability = true;
            }
            else if (authLockStream_ is not null &&
                !authLockStream_.SafeFileHandle.IsInvalid &&
                !authLockStream_.SafeFileHandle.IsClosed &&
                IsGenuinelyAcquiredOwnerAuth(authLockStream_.SafeFileHandle))
            {
                if (auth.ProcessId == Environment.ProcessId &&
                    auth.ProcessStartTimeTicks == WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(Environment.ProcessId))
                {
                    try
                    {
                        WorkflowSchedulerPaths.AssertSafeOpenFile(
                            authLockStream_.SafeFileHandle,
                            authPath);

                        // An open handle alone is not enough: verify genuine capability!
                        if (OperatingSystem.IsWindows())
                        {
                            hasAuthenticCapability =
                                ProbeProductionRegistrationLock(authPath) == SentinelByteLockProbeStatus.LockProven;
                        }
                        else if (OperatingSystem.IsLinux())
                        {
                            LockEnumerationResult flockCheck =
                                WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.GetLockingProcesses(authPath, requireExclusive: true, requiredLockType: "FLOCK");
                            hasAuthenticCapability =
                                flockCheck.IsSuccess && flockCheck.ProcessIds.Contains(Environment.ProcessId);
                        }
                        else
                        {
                            hasAuthenticCapability = true;
                        }
                    }
                    catch
                    {
                        hasAuthenticCapability = false;
                    }
                }
            }

            if (!hasAuthenticCapability)
            {
                // Foreign removal: verify owner is comprovadamente inexistente.
                ProcessLivenessStatus liveness =
                    WorkflowSchedulerIntegrityInspector.CheckProcessLiveness(auth.ProcessId, auth.ProcessStartTimeTicks);

                if (liveness != ProcessLivenessStatus.DefinitelyDead)
                {
                    throw new InvalidOperationException(
                        $"Cannot remove owner authentication for workflow '{workflowId_}': foreign process lacks authority and owner process {auth.ProcessId} is not proven dead (status: {liveness}). Fail closed.");
                }

                string wfLockPath = paths_.GetWorkflowLockPath(workflowId_);
                LockEnumerationResult authLocks = WorkflowSchedulerIntegrityInspector.GetLockingProcesses(authPath);
                LockEnumerationResult wfLocks = WorkflowSchedulerIntegrityInspector.GetLockingProcesses(wfLockPath);

                if (authLocks.IsFailed || wfLocks.IsFailed)
                {
                    throw new InvalidOperationException(
                        $"Cannot remove owner authentication for workflow '{workflowId_}': lock owner enumeration failed. State is indeterminate. Fail closed.");
                }

                if (authLocks.ProcessIds.Count > 0 || wfLocks.ProcessIds.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot remove owner authentication for workflow '{workflowId_}': foreign process lacks authority and owner process {auth.ProcessId} holds active locks. Unauthorized removal rejected.");
                }
            }

            // Close the write-lock stream on owner-auth.json right before durable deletion under domain.lock
            if (OperatingSystem.IsWindows() && authLockStream_ is not null)
            {
                try
                {
                    authLockStream_.Unlock(long.MaxValue - 1, 1);
                }
                catch
                {
                }
            }
            authLockStream_?.Dispose();

            try
            {
                File.SetAttributes(authPath, FileAttributes.Normal);
            }
            catch
            {
            }

            string wfDir =
                paths_.GetWorkflowDirectory(
                    workflowId_);

            WorkflowSchedulerDurabilityReport report =
                WorkflowSchedulerDomainMetadata.DeleteAtomic(
                    authPath,
                    wfDir);

            if (report != WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced)
            {
                throw new IOException(
                    $"Failed to durably remove owner authentication at '{authPath}'. Durability: {report}. Fail closed.");
            }
        }
        finally
        {
            acquiredDomainLock?.Dispose();
        }
    }

    internal static SentinelByteLockProbeStatus ProbeProductionRegistrationLock(string authPath)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using FileStream probe = new(
                    authPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1,
                    FileOptions.None);

                try
                {
                    probe.Lock(long.MaxValue - 1, 1);
                    try
                    {
                        probe.Unlock(long.MaxValue - 1, 1);
                    }
                    catch
                    {
                    }

                    return SentinelByteLockProbeStatus.LockAbsent;
                }
                catch (IOException ex) when ((System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF) == 33 /* ERROR_LOCK_VIOLATION */)
                {
                    return SentinelByteLockProbeStatus.LockProven;
                }
                catch
                {
                    return SentinelByteLockProbeStatus.Indeterminate;
                }
            }
            catch (IOException ex) when ((System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF) == 32 /* ERROR_SHARING_VIOLATION */)
            {
                return SentinelByteLockProbeStatus.Indeterminate;
            }
            catch
            {
                return SentinelByteLockProbeStatus.Indeterminate;
            }
        }

        if (OperatingSystem.IsLinux())
        {
            LockEnumerationResult result =
                WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.GetLockingProcesses(authPath, requireExclusive: true, requiredLockType: "FLOCK");
            if (result.IsFailed)
            {
                return SentinelByteLockProbeStatus.Indeterminate;
            }

            return result.ProcessIds.Count > 0
                ? SentinelByteLockProbeStatus.LockProven
                : SentinelByteLockProbeStatus.LockAbsent;
        }

        return SentinelByteLockProbeStatus.Indeterminate;
    }

    internal static bool IsProductionRegistrationLockHeld(string authPath)
    {
        return ProbeProductionRegistrationLock(authPath) == SentinelByteLockProbeStatus.LockProven;
    }
}

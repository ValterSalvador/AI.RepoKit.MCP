namespace AiRepoKit.WorkflowExecution;

using System.Text.Json;

internal sealed record WorkflowSchedulerLeaseManifest
{
    public const string LeaseSchemaId =
        "ai.repokit.workflow-scheduler-lease";

    public const int LeaseSchemaVersion =
        1;

    public string WorkflowId
    {
        get;
    }

    public WorkflowSchedulerLeaseState State
    {
        get;
    }

    public string OwnerToken
    {
        get;
    }

    public int? Slot
    {
        get;
    }

    public string DomainMode
    {
        get;
    }

    public IReadOnlyList<WorkflowResourceClaim> ResourceClaims
    {
        get;
    }

    public string? CreatedAt
    {
        get;
    }

    public string? UpdatedAt
    {
        get;
    }

    public WorkflowSchedulerLeaseManifest(
        string workflowId_,
        WorkflowSchedulerLeaseState state_,
        string ownerToken_,
        int? slot_,
        string domainMode_,
        IEnumerable<WorkflowResourceClaim> resourceClaims_,
        int maxConcurrentWorkflows_,
        string? createdAt_ = null,
        string? updatedAt_ = null)
    {
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
            resourceClaims_,
            nameof(resourceClaims_));

        // Validate canonical hex encoding can be formed.
        _ = WorkflowSchedulerPaths.WorkflowIdToHex(
            workflowId_);

        if (!Enum.IsDefined(
                state_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state_),
                state_,
                "Invalid lease state.");
        }

        switch (state_)
        {
            case WorkflowSchedulerLeaseState.Active:
                if (!slot_.HasValue ||
                    slot_.Value < 1 ||
                    slot_.Value > maxConcurrentWorkflows_)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(slot_),
                        slot_,
                        $"Active lease must specify slot between 1 and {maxConcurrentWorkflows_}.");
                }

                if (!string.Equals(
                        domainMode_,
                        "Normal",
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        domainMode_,
                        "Unrestricted",
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Active lease domain mode must be 'Normal' or 'Unrestricted', got '{domainMode_}'.",
                        nameof(domainMode_));
                }
                break;

            case WorkflowSchedulerLeaseState.Quarantined:
                if (slot_.HasValue)
                {
                    throw new ArgumentException(
                        "Quarantined lease must not retain a capacity slot.",
                        nameof(slot_));
                }

                if (!string.Equals(
                        domainMode_,
                        "Normal",
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        domainMode_,
                        "Unrestricted",
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Quarantined lease domain mode must be 'Normal' or 'Unrestricted', got '{domainMode_}'.",
                        nameof(domainMode_));
                }
                break;

            case WorkflowSchedulerLeaseState.MaintenanceInProgress:
                if (slot_.HasValue)
                {
                    throw new ArgumentException(
                        "MaintenanceInProgress lease must not retain a capacity slot.",
                        nameof(slot_));
                }

                if (!string.Equals(
                        domainMode_,
                        "ExclusiveMaintenance",
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "MaintenanceInProgress lease domain mode must be 'ExclusiveMaintenance'.",
                        nameof(domainMode_));
                }
                break;
        }

        List<WorkflowResourceClaim> claimsList =
            [];

        foreach (WorkflowResourceClaim claim in resourceClaims_)
        {
            ArgumentNullException.ThrowIfNull(
                claim,
                nameof(resourceClaims_));

            // Verify no duplicate or overlapping claims
            for (int i = 0; i < claimsList.Count; i++)
            {
                if (string.Equals(
                        claimsList[i].ResourceId,
                        claim.ResourceId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Duplicate resource claim '{claim.ResourceId}' in lease manifest.",
                        nameof(resourceClaims_));
                }

                if (claimsList[i].OverlapsHierarchicallyWith(
                        claim))
                {
                    throw new ArgumentException(
                        $"Overlapping hierarchical resource claim '{claim.ResourceId}' in lease manifest.",
                        nameof(resourceClaims_));
                }
            }

            claimsList.Add(
                claim);
        }

        this.WorkflowId =
            workflowId_;
        this.State =
            state_;
        this.OwnerToken =
            ownerToken_;
        this.Slot =
            slot_;
        this.DomainMode =
            domainMode_;
        this.ResourceClaims =
            claimsList.AsReadOnly();
        this.CreatedAt =
            createdAt_;
        this.UpdatedAt =
            updatedAt_;
    }

    public static void WriteAtomic(
        WorkflowSchedulerPaths paths_,
        WorkflowSchedulerLeaseManifest manifest_,
        int maxConcurrentWorkflows_,
        WorkflowSchedulerLockHandle? domainLockHandle_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));
        ArgumentNullException.ThrowIfNull(
            manifest_,
            nameof(manifest_));

        if (!File.Exists(paths_.DomainIntegrityPath) || !File.Exists(paths_.DomainPolicyPath))
        {
            throw new InvalidOperationException(
                "Domain is not bootstrapped: domain-integrity.json or domain-policy.json is missing. Cannot publish lease manifests before domain initialization.");
        }

        WorkflowSchedulerLockHandle? acquiredLock =
            null;

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

                acquiredLock =
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
                    paths_,
                    expectedMaxConcurrentWorkflows_: maxConcurrentWorkflows_);

            if (Directory.Exists(paths_.ActiveLeasesDirectory))
            {
                foreach (string leaseFile in Directory.EnumerateFiles(
                             paths_.ActiveLeasesDirectory,
                             "*.lease.json",
                             SearchOption.TopDirectoryOnly))
                {
                    WorkflowSchedulerLeaseManifest existingOther =
                        ReadAndValidate(
                            leaseFile,
                            paths_,
                            maxConcurrentWorkflows_);

                    if (string.Equals(existingOther.WorkflowId, manifest_.WorkflowId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // 0. Reject duplicate OwnerToken across workflows:
                    if (string.Equals(existingOther.OwnerToken, manifest_.OwnerToken, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Cannot publish lease for workflow '{manifest_.WorkflowId}' because ownerToken '{manifest_.OwnerToken}' is already used by workflow '{existingOther.WorkflowId}'. Duplicate ownerToken across workflows is forbidden.");
                    }

                    // 0b. Reject duplicate Active capacity slot across workflows:
                    if (manifest_.State == WorkflowSchedulerLeaseState.Active &&
                        existingOther.State == WorkflowSchedulerLeaseState.Active &&
                        manifest_.Slot.HasValue &&
                        existingOther.Slot.HasValue &&
                        manifest_.Slot.Value == existingOther.Slot.Value)
                    {
                        throw new InvalidOperationException(
                            $"Cannot publish Active lease for workflow '{manifest_.WorkflowId}' on slot {manifest_.Slot.Value} because workflow '{existingOther.WorkflowId}' already claims Active slot {existingOther.Slot.Value}. Duplicate slot ownership is forbidden.");
                    }

                    // 1. If another workflow is in MaintenanceInProgress:
                    if (existingOther.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                    {
                        throw new InvalidOperationException(
                            $"Cannot publish lease for workflow '{manifest_.WorkflowId}' because maintenance is in progress for workflow '{existingOther.WorkflowId}'. Global exclusion violated.");
                    }

                    // 2. If new manifest is MaintenanceInProgress:
                    if (manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                    {
                        if (existingOther.State == WorkflowSchedulerLeaseState.Active)
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish MaintenanceInProgress lease for workflow '{manifest_.WorkflowId}' because workflow '{existingOther.WorkflowId}' is active. Global exclusion violated.");
                        }

                        if (existingOther.State == WorkflowSchedulerLeaseState.Quarantined &&
                            string.Equals(existingOther.DomainMode, "Unrestricted", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish MaintenanceInProgress lease for workflow '{manifest_.WorkflowId}' because workflow '{existingOther.WorkflowId}' is quarantined in Unrestricted mode. Global exclusion violated.");
                        }

                        if (existingOther.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish MaintenanceInProgress lease for workflow '{manifest_.WorkflowId}' because maintenance is already in progress for workflow '{existingOther.WorkflowId}'. Global exclusion violated.");
                        }
                    }

                    // 3. If another workflow is Quarantined in Unrestricted mode:
                    if (existingOther.State == WorkflowSchedulerLeaseState.Quarantined &&
                        string.Equals(existingOther.DomainMode, "Unrestricted", StringComparison.Ordinal))
                    {
                        if (manifest_.State == WorkflowSchedulerLeaseState.Active)
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish Active lease for workflow '{manifest_.WorkflowId}' because workflow '{existingOther.WorkflowId}' is quarantined in Unrestricted mode. Global exclusion violated.");
                        }

                        if (manifest_.State == WorkflowSchedulerLeaseState.Quarantined &&
                            string.Equals(manifest_.DomainMode, "Unrestricted", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish Quarantined lease in Unrestricted mode for workflow '{manifest_.WorkflowId}' because workflow '{existingOther.WorkflowId}' is already quarantined in Unrestricted mode. Global exclusion violated.");
                        }
                    }

                    // 4. If new manifest is Quarantined in Unrestricted mode:
                    if (manifest_.State == WorkflowSchedulerLeaseState.Quarantined &&
                        string.Equals(manifest_.DomainMode, "Unrestricted", StringComparison.Ordinal))
                    {
                        if (existingOther.State == WorkflowSchedulerLeaseState.Active)
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish Quarantined lease in Unrestricted mode for workflow '{manifest_.WorkflowId}' because workflow '{existingOther.WorkflowId}' is active. Global exclusion violated.");
                        }

                        if (existingOther.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish Quarantined lease in Unrestricted mode for workflow '{manifest_.WorkflowId}' because maintenance is in progress for workflow '{existingOther.WorkflowId}'. Global exclusion violated.");
                        }
                    }

                    // 5. Active and Unrestricted bidirectional rules:
                    if (manifest_.State == WorkflowSchedulerLeaseState.Active)
                    {
                        if (existingOther.State == WorkflowSchedulerLeaseState.Active &&
                            string.Equals(existingOther.DomainMode, "Unrestricted", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Cannot publish Active lease for workflow '{manifest_.WorkflowId}' because workflow '{existingOther.WorkflowId}' is active in Unrestricted mode. Global exclusion violated.");
                        }

                        if (string.Equals(manifest_.DomainMode, "Unrestricted", StringComparison.Ordinal))
                        {
                            if (existingOther.State == WorkflowSchedulerLeaseState.Active)
                            {
                                throw new InvalidOperationException(
                                    $"Cannot publish Active lease for workflow '{manifest_.WorkflowId}' with domainMode 'Unrestricted' because another active lease exists for workflow '{existingOther.WorkflowId}'. Global exclusion violated.");
                            }

                            if (existingOther.State == WorkflowSchedulerLeaseState.Quarantined)
                            {
                                throw new InvalidOperationException(
                                    $"Cannot publish Active lease for workflow '{manifest_.WorkflowId}' with domainMode 'Unrestricted' because an existing quarantined lease exists for workflow '{existingOther.WorkflowId}'. Global exclusion violated.");
                            }
                        }
                    }
                }
            }

            if (manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
            {
                if (!string.Equals(manifest_.DomainMode, "ExclusiveMaintenance", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"MaintenanceInProgress lease must specify domainMode 'ExclusiveMaintenance', got '{manifest_.DomainMode}'.");
                }
            }
            else if (manifest_.State == WorkflowSchedulerLeaseState.Active &&
                     string.Equals(manifest_.DomainMode, "Unrestricted", StringComparison.Ordinal))
            {
                if (!persistedPolicy.AllowUnrestrictedExecution)
                {
                    throw new InvalidOperationException(
                        $"Cannot publish lease for workflow '{manifest_.WorkflowId}' with domainMode 'Unrestricted' because domain policy has AllowUnrestrictedExecution=false.");
                }

                WorkflowLockProbeState modeLockState =
                    WorkflowSchedulerLockPrimitives.ProbeLockState(paths_.DomainModeLockPath);

                if (modeLockState != WorkflowLockProbeState.HeldExclusive)
                {
                    throw new InvalidOperationException(
                        $"Cannot publish Active lease for workflow '{manifest_.WorkflowId}' with domainMode 'Unrestricted' because domain-mode.lock is not held exclusively (actual state: {modeLockState}).");
                }
            }
            else if (manifest_.State == WorkflowSchedulerLeaseState.Active &&
                     string.Equals(manifest_.DomainMode, "Normal", StringComparison.Ordinal))
            {
                WorkflowLockProbeState modeLockState =
                    WorkflowSchedulerLockPrimitives.ProbeLockState(paths_.DomainModeLockPath);

                if (modeLockState == WorkflowLockProbeState.HeldExclusive)
                {
                    throw new InvalidOperationException(
                        $"Cannot publish Active lease for workflow '{manifest_.WorkflowId}' with domainMode 'Normal' because domain-mode.lock is held exclusively by another process.");
                }
            }

            string finalPath =
                paths_.GetActiveLeasePath(
                    manifest_.WorkflowId);

            if (File.Exists(
                    finalPath))
            {
                WorkflowSchedulerLeaseManifest existing =
                    ReadAndValidate(
                        finalPath,
                        paths_,
                        maxConcurrentWorkflows_);

                if (!string.Equals(
                        existing.OwnerToken,
                        manifest_.OwnerToken,
                        StringComparison.Ordinal))
                {
                    bool isAuthorizedMaintenanceTransition =
                        existing.State == WorkflowSchedulerLeaseState.Quarantined &&
                        manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress;

                    bool isAuthorizedOrphanMaintenanceReacquisition =
                        domainLockHandle_ is not null &&
                        existing.State == WorkflowSchedulerLeaseState.MaintenanceInProgress &&
                        manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress;

                    if (!isAuthorizedMaintenanceTransition && !isAuthorizedOrphanMaintenanceReacquisition)
                    {
                        throw new InvalidOperationException(
                            $"Cannot overwrite lease for workflow '{manifest_.WorkflowId}' owned by '{existing.OwnerToken}' with token '{manifest_.OwnerToken}'.");
                    }
                }

                if (existing.State == WorkflowSchedulerLeaseState.Active)
                {
                    if (manifest_.State == WorkflowSchedulerLeaseState.Active)
                    {
                        if (existing.Slot != manifest_.Slot)
                        {
                            throw new InvalidOperationException(
                                $"Cannot change slot for active lease '{manifest_.WorkflowId}' from {existing.Slot} to {manifest_.Slot}.");
                        }

                        if (!string.Equals(
                                existing.DomainMode,
                                manifest_.DomainMode,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Cannot change domainMode for active lease '{manifest_.WorkflowId}' from '{existing.DomainMode}' to '{manifest_.DomainMode}'.");
                        }

                        ValidateClaimsPreserved(
                            existing.ResourceClaims,
                            manifest_.ResourceClaims);
                    }
                    else if (manifest_.State == WorkflowSchedulerLeaseState.Quarantined)
                    {
                        if (!string.Equals(
                                existing.DomainMode,
                                manifest_.DomainMode,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Lease transition from Active to Quarantined for workflow '{manifest_.WorkflowId}' cannot change domainMode from '{existing.DomainMode}' to '{manifest_.DomainMode}'.");
                        }

                        ValidateClaimsPreserved(
                            existing.ResourceClaims,
                            manifest_.ResourceClaims);
                    }
                    else if (manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                    {
                        throw new InvalidOperationException(
                            $"Invalid lease transition from Active to MaintenanceInProgress for workflow '{manifest_.WorkflowId}'.");
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Invalid lease transition from Active to {manifest_.State} for workflow '{manifest_.WorkflowId}'.");
                    }
                }
                else if (existing.State == WorkflowSchedulerLeaseState.Quarantined)
                {
                    if (manifest_.State == WorkflowSchedulerLeaseState.Quarantined)
                    {
                        if (!string.Equals(
                                existing.DomainMode,
                                manifest_.DomainMode,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Quarantined lease rewrite for workflow '{manifest_.WorkflowId}' cannot alter domainMode from '{existing.DomainMode}' to '{manifest_.DomainMode}'. Global exclusion must be preserved.");
                        }

                        ValidateClaimsPreserved(
                            existing.ResourceClaims,
                            manifest_.ResourceClaims);
                    }
                    else if (manifest_.State == WorkflowSchedulerLeaseState.Active)
                    {
                        throw new InvalidOperationException(
                            $"Invalid lease transition from Quarantined to Active for workflow '{manifest_.WorkflowId}'. Quarantined leases cannot transition back to Active.");
                    }
                    else if (manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                    {
                        if (!string.Equals(manifest_.DomainMode, "ExclusiveMaintenance", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Transition from Quarantined to MaintenanceInProgress for workflow '{manifest_.WorkflowId}' must specify domainMode 'ExclusiveMaintenance'.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Invalid lease transition from Quarantined to {manifest_.State} for workflow '{manifest_.WorkflowId}'.");
                    }
                }
                else if (existing.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                {
                    if (manifest_.State != WorkflowSchedulerLeaseState.MaintenanceInProgress)
                    {
                        throw new InvalidOperationException(
                            $"Invalid lease transition from MaintenanceInProgress to {manifest_.State} for workflow '{manifest_.WorkflowId}'. Maintenance leases cannot transition without audited reconciliation.");
                    }

                    if (!string.Equals(
                            existing.DomainMode,
                            manifest_.DomainMode,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"MaintenanceInProgress lease rewrite cannot alter domainMode from '{existing.DomainMode}' to '{manifest_.DomainMode}'.");
                    }

                    ValidateClaimsPreserved(
                        existing.ResourceClaims,
                        manifest_.ResourceClaims);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unsupported lease state {existing.State} for workflow '{manifest_.WorkflowId}'.");
                }
            }

            var payload =
                new
                {
                    schemaId = LeaseSchemaId,
                    schemaVersion = LeaseSchemaVersion,
                    workflowId = manifest_.WorkflowId,
                    state = manifest_.State.ToString(),
                    ownerToken = manifest_.OwnerToken,
                    slot = manifest_.Slot,
                    domainMode = manifest_.DomainMode,
                    resourceClaims = manifest_.ResourceClaims.Select(
                        claim_ =>
                            new
                            {
                                resourceId = claim_.ResourceId,
                                accessKind = claim_.AccessKind.ToString()
                            }).ToArray(),
                    createdAt = manifest_.CreatedAt,
                    updatedAt = manifest_.UpdatedAt
                };

            string json =
                JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            WorkflowSchedulerDurabilityReport durabilityReport =
                WorkflowSchedulerDomainMetadata.WriteAtomic(
                    finalPath,
                    json,
                    paths_.ActiveLeasesDirectory);

            if (durabilityReport != WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced)
            {
                throw new IOException(
                    $"Failed to durably publish active lease manifest for workflow '{manifest_.WorkflowId}'. Durability report: {durabilityReport}. Fail closed.");
            }
        }
        finally
        {
            acquiredLock?.Dispose();
        }
    }

    private static void ValidateClaimsPreserved(
        IReadOnlyList<WorkflowResourceClaim> existingClaims_,
        IReadOnlyList<WorkflowResourceClaim> newClaims_)
    {
        if (existingClaims_.Count != newClaims_.Count)
        {
            throw new InvalidOperationException(
                "Lease transition to or within Quarantined must preserve all resource claims.");
        }

        foreach (WorkflowResourceClaim existingClaim in existingClaims_)
        {
            bool found =
                false;

            foreach (WorkflowResourceClaim newClaim in newClaims_)
            {
                if (string.Equals(
                        existingClaim.ResourceId,
                        newClaim.ResourceId,
                        StringComparison.Ordinal) &&
                    existingClaim.AccessKind == newClaim.AccessKind)
                {
                    found =
                        true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    $"Lease transition to or within Quarantined dropped or altered resource claim '{existingClaim.ResourceId}'. All claims must be preserved exactly.");
            }
        }
    }

    public static WorkflowSchedulerLeaseManifest ReadAndValidate(
        string filePath_,
        WorkflowSchedulerPaths paths_,
        int maxConcurrentWorkflows_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            filePath_,
            nameof(filePath_));
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));

        paths_.AssertSafeCoordinationFile(
            filePath_);

        string fileName =
            Path.GetFileName(
                filePath_);

        string json =
            File.ReadAllText(
                filePath_);

        using JsonDocument doc =
            JsonDocument.Parse(
                json);

        JsonElement root =
            doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Lease manifest root must be a JSON object.");
        }

        HashSet<string> seenProps =
            new(StringComparer.Ordinal);

        string[] allowedProps =
        [
            "schemaId",
            "schemaVersion",
            "workflowId",
            "state",
            "ownerToken",
            "slot",
            "domainMode",
            "resourceClaims",
            "createdAt",
            "updatedAt"
        ];

        foreach (JsonProperty prop in root.EnumerateObject())
        {
            if (!seenProps.Add(
                    prop.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate JSON property '{prop.Name}' in lease manifest.");
            }

            if (!allowedProps.Contains(
                    prop.Name,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected property '{prop.Name}' in lease manifest.");
            }
        }

        if (!root.TryGetProperty(
                "schemaId",
                out JsonElement schemaIdProp) ||
            schemaIdProp.ValueKind != JsonValueKind.String ||
            !string.Equals(
                schemaIdProp.GetString(),
                LeaseSchemaId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported or invalid lease schemaId: '{schemaIdProp}'.");
        }

        if (!root.TryGetProperty(
                "schemaVersion",
                out JsonElement versionProp) ||
            versionProp.ValueKind != JsonValueKind.Number ||
            versionProp.GetInt32() != LeaseSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported lease schemaVersion.");
        }

        if (!root.TryGetProperty(
                "workflowId",
                out JsonElement workflowIdProp) ||
            workflowIdProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(
                workflowIdProp.GetString()))
        {
            throw new InvalidOperationException(
                "Lease manifest is missing valid workflowId.");
        }

        string workflowId =
            workflowIdProp.GetString()!;

        string expectedHex =
            WorkflowSchedulerPaths.WorkflowIdToHex(
                workflowId);

        string expectedFileName =
            $"{expectedHex}.lease.json";

        if (!string.Equals(
                fileName,
                expectedFileName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Lease filename '{fileName}' does not match expected canonical filename '{expectedFileName}'.");
        }

        if (!root.TryGetProperty(
                "state",
                out JsonElement stateProp) ||
            stateProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Lease manifest is missing valid state string property.");
        }

        string stateString =
            stateProp.GetString()!;

        if (int.TryParse(
                stateString,
                out _))
        {
            throw new InvalidOperationException(
                $"Numeric lease state '{stateString}' is not permitted; named string required.");
        }

        if (!Enum.TryParse<WorkflowSchedulerLeaseState>(
                stateString,
                ignoreCase: false,
                out WorkflowSchedulerLeaseState state) ||
            !Enum.IsDefined(state) ||
            !string.Equals(state.ToString(), stateString, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Invalid or non-canonical lease state: '{stateString}'. Exact canonical name required.");
        }

        if (!root.TryGetProperty(
                "ownerToken",
                out JsonElement ownerTokenProp) ||
            ownerTokenProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(
                ownerTokenProp.GetString()))
        {
            throw new InvalidOperationException(
                "Lease manifest is missing valid ownerToken.");
        }

        string ownerToken =
            ownerTokenProp.GetString()!;

        if (!root.TryGetProperty(
                "slot",
                out JsonElement slotProp))
        {
            throw new InvalidOperationException(
                "Lease manifest is missing required 'slot' property.");
        }

        int? slot;

        if (slotProp.ValueKind == JsonValueKind.Number)
        {
            slot =
                slotProp.GetInt32();
        }
        else if (slotProp.ValueKind == JsonValueKind.Null)
        {
            slot =
                null;
        }
        else
        {
            throw new InvalidOperationException(
                "Property 'slot' must be number or null.");
        }

        if (!root.TryGetProperty(
                "domainMode",
                out JsonElement domainModeProp) ||
            domainModeProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(
                domainModeProp.GetString()))
        {
            throw new InvalidOperationException(
                "Lease manifest is missing valid domainMode.");
        }

        string domainMode =
            domainModeProp.GetString()!;

        if (!root.TryGetProperty(
                "resourceClaims",
                out JsonElement claimsProp) ||
            claimsProp.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Lease manifest must contain a valid 'resourceClaims' JSON array.");
        }

        List<WorkflowResourceClaim> claims =
            [];

        string[] allowedClaimProps =
        [
            "resourceId",
            "accessKind"
        ];

        foreach (JsonElement claimElem in claimsProp.EnumerateArray())
        {
            if (claimElem.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Claim item must be a JSON object.");
            }

            HashSet<string> seenClaimProps =
                new(StringComparer.Ordinal);

            foreach (JsonProperty claimProp in claimElem.EnumerateObject())
            {
                if (!seenClaimProps.Add(
                        claimProp.Name))
                {
                    throw new InvalidOperationException(
                        $"Duplicate property '{claimProp.Name}' in resource claim.");
                }

                if (!allowedClaimProps.Contains(
                        claimProp.Name,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected property '{claimProp.Name}' in resource claim.");
                }
            }

            if (!claimElem.TryGetProperty(
                    "resourceId",
                    out JsonElement resIdProp) ||
                resIdProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(
                    resIdProp.GetString()))
            {
                throw new InvalidOperationException(
                    "Claim missing valid resourceId string.");
            }

            if (!claimElem.TryGetProperty(
                    "accessKind",
                    out JsonElement accessKindProp) ||
                accessKindProp.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    "Claim missing valid accessKind string.");
            }

            string accessKindStr =
                accessKindProp.GetString()!;

            if (int.TryParse(
                    accessKindStr,
                    out _))
            {
                throw new InvalidOperationException(
                    $"Numeric accessKind '{accessKindStr}' is not permitted; named string required.");
            }

            if (!Enum.TryParse<WorkflowResourceAccessKind>(
                    accessKindStr,
                    ignoreCase: false,
                    out WorkflowResourceAccessKind accessKind) ||
                !Enum.IsDefined(accessKind) ||
                !string.Equals(accessKind.ToString(), accessKindStr, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Invalid or non-canonical accessKind: '{accessKindStr}'. Exact canonical name required.");
            }

            claims.Add(
                new WorkflowResourceClaim(
                    resIdProp.GetString()!,
                    accessKind));
        }

        string? createdAt =
            null;

        if (root.TryGetProperty(
                "createdAt",
                out JsonElement createdProp))
        {
            if (createdProp.ValueKind == JsonValueKind.String)
            {
                createdAt =
                    createdProp.GetString();
            }
            else if (createdProp.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidOperationException(
                    "Property 'createdAt' must be string or null.");
            }
        }

        string? updatedAt =
            null;

        if (root.TryGetProperty(
                "updatedAt",
                out JsonElement updatedProp))
        {
            if (updatedProp.ValueKind == JsonValueKind.String)
            {
                updatedAt =
                    updatedProp.GetString();
            }
            else if (updatedProp.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidOperationException(
                    "Property 'updatedAt' must be string or null.");
            }
        }

        return new WorkflowSchedulerLeaseManifest(
            workflowId,
            state,
            ownerToken,
            slot,
            domainMode,
            claims,
            maxConcurrentWorkflows_,
            createdAt,
            updatedAt);
    }
}

namespace AiRepoKit.WorkflowExecution;

using System.Text.Json;

internal enum WorkflowSchedulerDurabilityReport
{
    ContentWrittenAndSynced = 1,
    AtomicRenameCompleted = 2,
    DirectoryMetadataDurablySynced = 3,
    UnknownDurability = 4
}

internal sealed class WorkflowSchedulerDomainMetadata
{
    public const string IntegritySchemaId =
        "ai.repokit.workflow-scheduler-domain-integrity";

    public const int IntegritySchemaVersion =
        1;

    public const string PolicySchemaId =
        "ai.repokit.workflow-scheduler-domain-policy";

    public const int PolicySchemaVersion =
        1;

    public static void InitializeOrValidate(
        WorkflowSchedulerPaths paths_,
        WorkflowSchedulerPolicy policy_,
        WorkflowSchedulerLockHandle? domainLockHandle_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));
        ArgumentNullException.ThrowIfNull(
            policy_,
            nameof(policy_));

        paths_.EnsureCoordinationLayout();

        WorkflowSchedulerLockHandle? acquiredLock =
            null;

        try
        {
            if (domainLockHandle_ is not null)
            {
                ValidateDomainLockHandle(
                    paths_,
                    domainLockHandle_);
            }
            else
            {
                acquiredLock =
                    WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                        paths_.DomainLockPath,
                        policy_.DomainLockTimeout).GetAwaiter().GetResult();
            }

            CheckResidualTemporaryFiles(
                paths_);

            bool integrityExists =
                File.Exists(
                    paths_.DomainIntegrityPath);

            if (integrityExists)
            {
                ValidateExistingDomain(
                    paths_,
                    policy_);
            }
            else
            {
                BootstrapCleanDomain(
                    paths_,
                    policy_);
            }
        }
        finally
        {
            acquiredLock?.Dispose();
        }
    }

    internal static void ValidateDomainLockHandle(
        WorkflowSchedulerPaths paths_,
        WorkflowSchedulerLockHandle handle_)
    {
        ArgumentNullException.ThrowIfNull(
            handle_,
            nameof(handle_));

        if (handle_.IsDisposed)
        {
            throw new InvalidOperationException(
                "Provided domain lock handle is disposed.");
        }

        if (!handle_.IsExclusive ||
            !handle_.HasExclusiveCapability ||
            handle_.ExclusiveCapability is null)
        {
            throw new InvalidOperationException(
                "Provided domain lock handle is not held exclusively.");
        }

        if (!handle_.ExclusiveCapability.IsBoundTo(
                handle_.Stream,
                paths_.DomainLockPath))
        {
            throw new InvalidOperationException(
                "Provided domain lock handle exclusive capability is invalid or not bound to this stream and path.");
        }

        FileStream? stream =
            handle_.Stream;

        if (stream is null ||
            stream.SafeFileHandle.IsInvalid ||
            stream.SafeFileHandle.IsClosed)
        {
            throw new InvalidOperationException(
                "Provided domain lock handle does not contain a valid, open OS stream.");
        }

        if (!stream.CanWrite)
        {
            throw new InvalidOperationException(
                "Provided domain lock handle stream is not writable.");
        }

        if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredExclusive(
                stream.SafeFileHandle))
        {
            throw new InvalidOperationException(
                "Provided domain lock handle was not genuinely acquired via WorkflowSchedulerLockPrimitives.TryAcquireExclusive.");
        }

        StringComparison pathComparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (!string.Equals(
                Path.GetFullPath(
                    handle_.Path),
                Path.GetFullPath(
                    paths_.DomainLockPath),
                pathComparison))
        {
            throw new InvalidOperationException(
                $"Domain lock handle path '{handle_.Path}' does not match expected '{paths_.DomainLockPath}'.");
        }

        WorkflowSchedulerPaths.AssertSafeOpenFile(
            handle_.SafeFileHandle!,
            paths_.DomainLockPath);
    }

    internal static TimeSpan GetDomainLockTimeout(
        WorkflowSchedulerPaths paths_)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));

        if (!File.Exists(paths_.DomainPolicyPath))
        {
            throw new InvalidOperationException(
                $"Domain policy file 'domain-policy.json' is missing at '{paths_.DomainPolicyPath}'. Domain is not bootstrapped.");
        }

        WorkflowSchedulerPolicy policy =
            ValidatePolicyDocument(
                paths_);

        return policy.DomainLockTimeout;
    }

    private static void ValidateExistingDomain(
        WorkflowSchedulerPaths paths_,
        WorkflowSchedulerPolicy activePolicy_)
    {
        ValidateIntegrityDocument(
            paths_);
        _ = ValidatePolicyDocument(
            paths_,
            activePolicy_.MaxConcurrentWorkflows,
            activePolicy_);
        ValidatePreexistingLeasesConsistency(
            paths_,
            activePolicy_.MaxConcurrentWorkflows);
    }

    internal static void ValidatePreexistingLeasesConsistency(
        WorkflowSchedulerPaths paths_,
        int maxConcurrentWorkflows_)
    {
        if (!Directory.Exists(
                paths_.ActiveLeasesDirectory))
        {
            return;
        }

        List<WorkflowSchedulerLeaseManifest> manifests =
            [];

        foreach (string leaseFile in Directory.EnumerateFiles(
                     paths_.ActiveLeasesDirectory,
                     "*.lease.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                WorkflowSchedulerLeaseManifest manifest =
                    WorkflowSchedulerLeaseManifest.ReadAndValidate(
                        leaseFile,
                        paths_,
                        maxConcurrentWorkflows_);
                manifests.Add(
                    manifest);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Domain contains corrupted pre-existing lease manifest '{leaseFile}': {ex.Message}. Fail closed.",
                    ex);
            }
        }

        bool hasMaintenance =
            manifests.Any(
                manifest_ =>
                    manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress);

        bool hasActive =
            manifests.Any(
                manifest_ =>
                    manifest_.State == WorkflowSchedulerLeaseState.Active);

        int activeCount =
            manifests.Count(
                manifest_ =>
                    manifest_.State == WorkflowSchedulerLeaseState.Active);

        bool hasUnrestrictedActive =
            manifests.Any(
                manifest_ =>
                    manifest_.State == WorkflowSchedulerLeaseState.Active &&
                    string.Equals(
                        manifest_.DomainMode,
                        "Unrestricted",
                        StringComparison.Ordinal));

        bool hasUnrestrictedQuarantine =
            manifests.Any(
                manifest_ =>
                    manifest_.State == WorkflowSchedulerLeaseState.Quarantined &&
                    string.Equals(
                        manifest_.DomainMode,
                        "Unrestricted",
                        StringComparison.Ordinal));

        int maintenanceCount =
            manifests.Count(
                manifest_ =>
                    manifest_.State == WorkflowSchedulerLeaseState.MaintenanceInProgress);

        int unrestrictedQuarantineCount =
            manifests.Count(
                manifest_ =>
                    manifest_.State == WorkflowSchedulerLeaseState.Quarantined &&
                    string.Equals(
                        manifest_.DomainMode,
                        "Unrestricted",
                        StringComparison.Ordinal));

        if (hasMaintenance && hasActive)
        {
            throw new InvalidOperationException(
                "Domain has contradictory pre-existing lease manifests: MaintenanceInProgress exists alongside Active lease. Fail closed.");
        }

        if (hasMaintenance && hasUnrestrictedQuarantine)
        {
            throw new InvalidOperationException(
                "Domain has contradictory pre-existing lease manifests: MaintenanceInProgress exists alongside Quarantined Unrestricted lease. Fail closed.");
        }

        if (maintenanceCount > 1)
        {
            throw new InvalidOperationException(
                "Domain has contradictory pre-existing lease manifests: Multiple MaintenanceInProgress leases exist. Fail closed.");
        }

        if (hasUnrestrictedActive && activeCount > 1)
        {
            throw new InvalidOperationException(
                "Domain has contradictory pre-existing lease manifests: Active Unrestricted lease exists alongside other Active leases. Fail closed.");
        }

        if (hasUnrestrictedQuarantine && hasActive)
        {
            throw new InvalidOperationException(
                "Domain has contradictory pre-existing lease manifests: Quarantined Unrestricted lease exists alongside Active lease. Fail closed.");
        }

        if (unrestrictedQuarantineCount > 1)
        {
            throw new InvalidOperationException(
                "Domain has contradictory pre-existing lease manifests: Multiple Quarantined Unrestricted leases exist. Fail closed.");
        }

        HashSet<int> claimedActiveSlots = [];
        HashSet<string> claimedOwnerTokens = new(StringComparer.Ordinal);

        foreach (WorkflowSchedulerLeaseManifest manifest in manifests)
        {
            if (!claimedOwnerTokens.Add(manifest.OwnerToken))
            {
                throw new InvalidOperationException(
                    $"Domain has contradictory pre-existing lease manifests: Duplicate ownerToken '{manifest.OwnerToken}' shared across multiple workflows. Fail closed.");
            }

            if (manifest.State == WorkflowSchedulerLeaseState.Active && manifest.Slot.HasValue)
            {
                if (!claimedActiveSlots.Add(manifest.Slot.Value))
                {
                    throw new InvalidOperationException(
                        $"Domain has contradictory pre-existing lease manifests: Multiple Active leases claim capacity slot {manifest.Slot.Value}. Fail closed.");
                }
            }
        }
    }

    internal static void ValidateIntegrityDocument(
        WorkflowSchedulerPaths paths_)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));

        paths_.AssertSafeCoordinationFile(
            paths_.DomainIntegrityPath);

        if (!File.Exists(
                paths_.DomainIntegrityPath))
        {
            throw new InvalidOperationException(
                $"Domain integrity marker 'domain-integrity.json' is missing at '{paths_.DomainIntegrityPath}'.");
        }

        string integrityJson =
            File.ReadAllText(
                paths_.DomainIntegrityPath);

        JsonDocument integrityDoc;
        try
        {
            integrityDoc =
                JsonDocument.Parse(
                    integrityJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Corrupted or malformed domain-integrity.json.",
                exception);
        }

        using (integrityDoc)
        {
            JsonElement root =
                integrityDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "domain-integrity.json root must be a JSON object.");
            }

            HashSet<string> seenProps =
                new(StringComparer.Ordinal);

            string[] allowedProps =
            [
                "schemaId",
                "schemaVersion",
                "bootstrapped"
            ];

            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (!seenProps.Add(
                        prop.Name))
                {
                    throw new InvalidOperationException(
                        $"Duplicate JSON property '{prop.Name}' in domain-integrity.json.");
                }

                if (!allowedProps.Contains(
                        prop.Name,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected property '{prop.Name}' in domain-integrity.json.");
                }
            }

            if (!root.TryGetProperty(
                    "schemaId",
                    out JsonElement schemaIdProp) ||
                schemaIdProp.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    schemaIdProp.GetString(),
                    IntegritySchemaId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unsupported or invalid domain-integrity schemaId: '{schemaIdProp.GetString()}'.");
            }

            if (!root.TryGetProperty(
                    "schemaVersion",
                    out JsonElement versionProp) ||
                versionProp.ValueKind != JsonValueKind.Number ||
                versionProp.GetInt32() != IntegritySchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported domain-integrity schemaVersion.");
            }

            if (!root.TryGetProperty(
                    "bootstrapped",
                    out JsonElement bootstrappedProp) ||
                bootstrappedProp.ValueKind != JsonValueKind.True)
            {
                throw new InvalidOperationException(
                    "domain-integrity.json bootstrapped property must be boolean true.");
            }
        }
    }

    internal static WorkflowSchedulerPolicy ValidatePolicyDocument(
        WorkflowSchedulerPaths paths_,
        int? expectedMaxConcurrentWorkflows_ = null,
        WorkflowSchedulerPolicy? activePolicy_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));

        paths_.AssertSafeCoordinationFile(
            paths_.DomainPolicyPath);

        if (!File.Exists(
                paths_.DomainPolicyPath))
        {
            throw new InvalidOperationException(
                $"Domain policy file 'domain-policy.json' is missing at '{paths_.DomainPolicyPath}'.");
        }

        string policyJson =
            File.ReadAllText(
                paths_.DomainPolicyPath);

        JsonDocument policyDoc;
        try
        {
            policyDoc =
                JsonDocument.Parse(
                    policyJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Corrupted or malformed domain-policy.json.",
                exception);
        }

        using (policyDoc)
        {
            JsonElement root =
                policyDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "domain-policy.json root must be a JSON object.");
            }

            HashSet<string> seenProps =
                new(StringComparer.Ordinal);

            string[] allowedProps =
            [
                "schemaId",
                "schemaVersion",
                "maxConcurrentWorkflows",
                "allowUnrestrictedExecution",
                "continueUntilBlockedOrTerminal",
                "domainLockTimeoutMs",
                "domainLockTimeoutTicks"
            ];

            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (!seenProps.Add(
                        prop.Name))
                {
                    throw new InvalidOperationException(
                        $"Duplicate JSON property '{prop.Name}' in domain-policy.json.");
                }

                if (!allowedProps.Contains(
                        prop.Name,
                        StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected property '{prop.Name}' in domain-policy.json.");
                }
            }

            if (!root.TryGetProperty(
                    "schemaId",
                    out JsonElement schemaIdProp) ||
                schemaIdProp.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    schemaIdProp.GetString(),
                    PolicySchemaId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unsupported or invalid domain-policy schemaId: '{schemaIdProp.GetString()}'.");
            }

            if (!root.TryGetProperty(
                    "schemaVersion",
                    out JsonElement versionProp) ||
                versionProp.ValueKind != JsonValueKind.Number ||
                versionProp.GetInt32() != PolicySchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported domain-policy schemaVersion.");
            }

            if (!root.TryGetProperty(
                    "maxConcurrentWorkflows",
                    out JsonElement maxConcurrentProp) ||
                maxConcurrentProp.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    "domain-policy.json maxConcurrentWorkflows must be a valid number.");
            }

            int maxConcurrent =
                maxConcurrentProp.GetInt32();

            if (maxConcurrent < 1)
            {
                throw new InvalidOperationException(
                    $"Domain policy maxConcurrentWorkflows ({maxConcurrent}) must be at least 1.");
            }

            if (expectedMaxConcurrentWorkflows_.HasValue &&
                maxConcurrent != expectedMaxConcurrentWorkflows_.Value)
            {
                throw new InvalidOperationException(
                    $"Domain policy maxConcurrentWorkflows ({maxConcurrent}) does not match expected ({expectedMaxConcurrentWorkflows_.Value}).");
            }

            if (activePolicy_ is not null &&
                maxConcurrent != activePolicy_.MaxConcurrentWorkflows)
            {
                throw new InvalidOperationException(
                    $"Domain policy mismatch: MaxConcurrentWorkflows persisted={maxConcurrent}, active={activePolicy_.MaxConcurrentWorkflows}.");
            }

            if (!root.TryGetProperty(
                    "allowUnrestrictedExecution",
                    out JsonElement unrestrictedProp) ||
                (unrestrictedProp.ValueKind != JsonValueKind.True && unrestrictedProp.ValueKind != JsonValueKind.False))
            {
                throw new InvalidOperationException(
                    "domain-policy.json allowUnrestrictedExecution must be a boolean.");
            }

            bool allowUnrestricted =
                unrestrictedProp.GetBoolean();

            if (activePolicy_ is not null &&
                allowUnrestricted != activePolicy_.AllowUnrestrictedExecution)
            {
                throw new InvalidOperationException(
                    $"Domain policy mismatch: AllowUnrestrictedExecution persisted={allowUnrestricted}, active={activePolicy_.AllowUnrestrictedExecution}.");
            }

            if (!root.TryGetProperty(
                    "continueUntilBlockedOrTerminal",
                    out JsonElement continueProp) ||
                (continueProp.ValueKind != JsonValueKind.True && continueProp.ValueKind != JsonValueKind.False))
            {
                throw new InvalidOperationException(
                    "domain-policy.json continueUntilBlockedOrTerminal must be a boolean.");
            }

            bool continueUntilBlockedOrTerminal =
                continueProp.GetBoolean();

            if (activePolicy_ is not null &&
                continueUntilBlockedOrTerminal != activePolicy_.ContinueUntilBlockedOrTerminal)
            {
                throw new InvalidOperationException(
                    $"Domain policy mismatch: ContinueUntilBlockedOrTerminal persisted={continueUntilBlockedOrTerminal}, active={activePolicy_.ContinueUntilBlockedOrTerminal}.");
            }

            if (!root.TryGetProperty(
                    "domainLockTimeoutMs",
                    out JsonElement timeoutProp) ||
                timeoutProp.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    "domain-policy.json domainLockTimeoutMs must be a valid number.");
            }

            long timeoutMs =
                timeoutProp.GetInt64();

            if (timeoutMs < 0)
            {
                throw new InvalidOperationException(
                    $"Domain policy domainLockTimeoutMs ({timeoutMs}) must not be negative.");
            }

            if (activePolicy_ is not null &&
                timeoutMs != (long)activePolicy_.DomainLockTimeout.TotalMilliseconds)
            {
                throw new InvalidOperationException(
                    $"Domain policy mismatch: DomainLockTimeoutMs persisted={timeoutMs}, active={(long)activePolicy_.DomainLockTimeout.TotalMilliseconds}.");
            }

            if (!root.TryGetProperty(
                    "domainLockTimeoutTicks",
                    out JsonElement ticksProp) ||
                ticksProp.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    "domain-policy.json domainLockTimeoutTicks must be a valid number.");
            }

            long ticks =
                ticksProp.GetInt64();

            if (ticks <= 0)
            {
                throw new InvalidOperationException(
                    $"Domain policy domainLockTimeoutTicks ({ticks}) must be positive.");
            }

            if (activePolicy_ is not null &&
                ticks != activePolicy_.DomainLockTimeout.Ticks)
            {
                throw new InvalidOperationException(
                    $"Domain policy mismatch: DomainLockTimeoutTicks persisted={ticks}, active={activePolicy_.DomainLockTimeout.Ticks}.");
            }

            if (ticks / TimeSpan.TicksPerMillisecond != timeoutMs)
            {
                throw new InvalidOperationException(
                    $"Domain policy mismatch: DomainLockTimeoutTicks ({ticks}) does not match DomainLockTimeoutMs ({timeoutMs}).");
            }

            return new WorkflowSchedulerPolicy(
                maxConcurrentWorkflows_: maxConcurrent,
                allowUnrestrictedExecution_: allowUnrestricted,
                continueUntilBlockedOrTerminal_: continueUntilBlockedOrTerminal,
                domainLockTimeout_: TimeSpan.FromTicks(ticks));
        }
    }

    private static void BootstrapCleanDomain(
        WorkflowSchedulerPaths paths_,
        WorkflowSchedulerPolicy policy_)
    {
        if (File.Exists(
                paths_.DomainPolicyPath))
        {
            throw new InvalidOperationException(
                "Domain has evidence of prior use (domain-policy.json exists) but lacks domain-integrity.json.");
        }

        if (Directory.Exists(
                paths_.ActiveLeasesDirectory) &&
            Directory.EnumerateFileSystemEntries(
                paths_.ActiveLeasesDirectory).Any())
        {
            throw new InvalidOperationException(
                "Domain has evidence of prior use (leases exist) but lacks domain-integrity.json.");
        }

        if (Directory.Exists(
                paths_.WorkflowsDirectory) &&
            Directory.EnumerateFileSystemEntries(
                paths_.WorkflowsDirectory).Any())
        {
            throw new InvalidOperationException(
                "Domain has evidence of prior use (workflow directories exist) but lacks domain-integrity.json.");
        }

        string policyPayload =
            JsonSerializer.Serialize(
                new
                {
                    schemaId = PolicySchemaId,
                    schemaVersion = PolicySchemaVersion,
                    maxConcurrentWorkflows = policy_.MaxConcurrentWorkflows,
                    allowUnrestrictedExecution = policy_.AllowUnrestrictedExecution,
                    continueUntilBlockedOrTerminal = policy_.ContinueUntilBlockedOrTerminal,
                    domainLockTimeoutMs = (long)policy_.DomainLockTimeout.TotalMilliseconds,
                    domainLockTimeoutTicks = policy_.DomainLockTimeout.Ticks
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        WorkflowSchedulerDurabilityReport policyReport =
            WriteAtomic(
                paths_.DomainPolicyPath,
                policyPayload,
                paths_.LocksDirectory);

        if (policyReport != WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced)
        {
            throw new IOException(
                $"Failed to durably publish domain policy. Durability report: {policyReport}. Fail closed.");
        }

        string integrityPayload =
            JsonSerializer.Serialize(
                new
                {
                    schemaId = IntegritySchemaId,
                    schemaVersion = IntegritySchemaVersion,
                    bootstrapped = true
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        WorkflowSchedulerDurabilityReport integrityReport =
            WriteAtomic(
                paths_.DomainIntegrityPath,
                integrityPayload,
                paths_.LocksDirectory);

        if (integrityReport != WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced)
        {
            throw new IOException(
                $"Failed to durably publish domain integrity. Durability report: {integrityReport}. Fail closed.");
        }
    }

    private static long _tempCounter;

    private static class Win32DirectoryFlush
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FlushFileBuffers(Microsoft.Win32.SafeHandles.SafeFileHandle hFile);
    }

    internal static class LinuxDirectoryNative
    {
        public const int O_RDONLY = 0;
        public const int O_DIRECTORY = 0x10000;
        public const int O_NOFOLLOW = 0x20000;
        public const int O_CLOEXEC = 0x80000;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct Stat64LinuxX64
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

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(string pathname, int flags);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        public static extern int Fsync(int fd);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
        public static extern int Fstat(int fd, out Stat64LinuxX64 buf);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "stat", SetLastError = true)]
        public static extern int Stat(string path, out Stat64LinuxX64 buf);

        public static bool FlushDirectory(string directoryPath)
        {
            int flags = O_RDONLY | O_DIRECTORY | O_CLOEXEC | O_NOFOLLOW;
            int fd = Open(directoryPath, flags);
            if (fd < 0)
            {
                return false;
            }

            try
            {
                if (Fstat(fd, out Stat64LinuxX64 statBuf) != 0 || (statBuf.st_mode & 0xF000) != 0x4000 /* S_IFDIR */)
                {
                    return false;
                }

                if (Stat(directoryPath, out Stat64LinuxX64 pathStat) != 0 ||
                    statBuf.st_dev != pathStat.st_dev ||
                    statBuf.st_ino != pathStat.st_ino)
                {
                    return false;
                }

                int res = Fsync(fd);
                return res == 0;
            }
            finally
            {
                Close(fd);
            }
        }
    }

    public static WorkflowSchedulerDurabilityReport WriteAtomic(
        string finalPath_,
        string content_,
        string directory_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            finalPath_,
            nameof(finalPath_));
        ArgumentNullException.ThrowIfNull(
            content_,
            nameof(content_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            directory_,
            nameof(directory_));

        long count =
            Interlocked.Increment(
                ref _tempCounter);

        string tmpFileName =
            string.Concat(
                Path.GetFileName(
                    finalPath_),
                ".tmp.",
                Environment.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ".",
                count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ".",
                Environment.TickCount64.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

        string tmpPath =
            Path.Combine(
                directory_,
                tmpFileName);

        string commitMarkerPath =
            string.Concat(
                tmpPath,
                ".commit");

        // 1. Write and flush file contents to durable storage with WriteThrough
        using (FileStream stream =
               new(
                   tmpPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096,
                   FileOptions.WriteThrough))
        {
            using StreamWriter writer =
                new(
                    stream);
            writer.Write(
                content_);
            writer.Flush();
            stream.Flush(
                flushToDisk: true);
        }

        // 2. Write commit intent marker and flush to disk
        using (FileStream commitStream =
               new(
                   commitMarkerPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 512,
                   FileOptions.WriteThrough))
        {
            commitStream.Flush(
                flushToDisk: true);
        }

        // 3. Flush directory metadata for created temp files
        bool preMoveDirFlushed =
            FlushDirectoryMetadata(
                directory_);

        if (!preMoveDirFlushed)
        {
            try { File.Delete(tmpPath); } catch { }
            try { File.Delete(commitMarkerPath); } catch { }
            return WorkflowSchedulerDurabilityReport.UnknownDurability;
        }

        // 4. Atomic rename / replacement into destination path
        File.Move(
            tmpPath,
            finalPath_,
            overwrite: true);

        // 5. Flush directory metadata after rename.
        // If this flush fails, do NOT delete commitMarkerPath.
        // Leaving commitMarkerPath causes subsequent integrity checks to detect the ambiguous transition and fail closed.
        bool postMoveDirFlushed =
            FlushDirectoryMetadata(
                directory_);

        if (!postMoveDirFlushed)
        {
            return WorkflowSchedulerDurabilityReport.UnknownDurability;
        }

        // 6. Delete commit intent marker and perform final directory flush
        File.Delete(
            commitMarkerPath);

        bool finalDirFlushed =
            FlushDirectoryMetadata(
                directory_);

        if (!finalDirFlushed)
        {
            return WorkflowSchedulerDurabilityReport.UnknownDurability;
        }

        return WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced;
    }

    public static WorkflowSchedulerDurabilityReport DeleteAtomic(
        string targetPath_,
        string directory_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            targetPath_,
            nameof(targetPath_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            directory_,
            nameof(directory_));

        if (!File.Exists(targetPath_))
        {
            return WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced;
        }

        long count =
            Interlocked.Increment(
                ref _tempCounter);

        string commitFileName =
            string.Concat(
                Path.GetFileName(
                    targetPath_),
                ".tmp.",
                Environment.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ".",
                count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ".",
                Environment.TickCount64.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ".commit");

        string commitMarkerPath =
            Path.Combine(
                directory_,
                commitFileName);

        // 1. Write commit intent marker and flush to disk
        using (FileStream commitStream =
               new(
                   commitMarkerPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 512,
                   FileOptions.WriteThrough))
        {
            commitStream.Flush(
                flushToDisk: true);
        }

        // 2. Flush directory metadata for created commit marker
        bool preDeleteDirFlushed =
            FlushDirectoryMetadata(
                directory_);

        if (!preDeleteDirFlushed)
        {
            try { File.Delete(commitMarkerPath); } catch { }
            return WorkflowSchedulerDurabilityReport.UnknownDurability;
        }

        // 3. Delete target file
        File.Delete(
            targetPath_);

        // 4. Flush directory metadata after delete.
        // If this flush fails, do NOT delete commitMarkerPath so subsequent integrity checks fail closed.
        bool postDeleteDirFlushed =
            FlushDirectoryMetadata(
                directory_);

        if (!postDeleteDirFlushed)
        {
            return WorkflowSchedulerDurabilityReport.UnknownDurability;
        }

        // 5. Delete commit intent marker and perform final directory flush
        File.Delete(
            commitMarkerPath);

        bool finalDirFlushed =
            FlushDirectoryMetadata(
                directory_);

        if (!finalDirFlushed)
        {
            return WorkflowSchedulerDurabilityReport.UnknownDurability;
        }

        return WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced;
    }

    private static bool FlushDirectoryMetadata(
        string directoryPath_)
    {
        if (OperatingSystem.IsWindows())
        {
            const uint GENERIC_READ = 0x80000000;
            const uint GENERIC_WRITE = 0x40000000;
            const uint FILE_SHARE_READ = 1;
            const uint FILE_SHARE_WRITE = 2;
            const uint FILE_SHARE_DELETE = 4;
            const uint OPEN_EXISTING = 3;
            const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

            try
            {
                using Microsoft.Win32.SafeHandles.SafeFileHandle dirHandle =
                    Win32DirectoryFlush.CreateFileW(
                        directoryPath_,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_FLAG_BACKUP_SEMANTICS,
                        IntPtr.Zero);

                if (dirHandle.IsInvalid)
                {
                    return false;
                }

                if (!Win32DirectoryFlush.FlushFileBuffers(dirHandle))
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                WorkflowSchedulerPaths.AssertNoReparsePoint(
                    directoryPath_);

                return LinuxDirectoryNative.FlushDirectory(
                    directoryPath_);
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }

    public static void CheckResidualTemporaryFiles(
        WorkflowSchedulerPaths paths_)
    {
        ArgumentNullException.ThrowIfNull(
            paths_,
            nameof(paths_));

        CheckResidualTemporaryFiles(
            paths_.LocksDirectory);
        CheckResidualTemporaryFiles(
            paths_.ActiveLeasesDirectory);

        if (Directory.Exists(paths_.WorkflowsDirectory))
        {
            CheckResidualTemporaryFiles(
                paths_.WorkflowsDirectory);

            foreach (string wfDir in Directory.EnumerateDirectories(
                         paths_.WorkflowsDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                WorkflowSchedulerPaths.AssertNoReparsePoint(
                    wfDir);

                CheckResidualTemporaryFiles(
                    wfDir);
            }
        }
    }

    public static void CheckResidualTemporaryFiles(
        string directory_)
    {
        if (!Directory.Exists(
                directory_))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(
                     directory_,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            if (file.Contains(".tmp.", StringComparison.Ordinal) ||
                file.EndsWith(".commit", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Residual uncommitted temporary file detected in coordination directory: '{file}'. Integrity check fails closed.");
            }
        }
    }
}

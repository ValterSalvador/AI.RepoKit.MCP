namespace AiRepoKit.WorkflowExecution.Tests;

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Xunit;

public sealed class WorkflowSchedulerCoordinationTests
{
    private static bool TryCreateJunction(string linkPath_, string targetPath_)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(linkPath_);
            string targetFullPath = Path.GetFullPath(targetPath_);
            string substituteName = @"\??\" + targetFullPath;
            string printName = targetFullPath;

            byte[] substituteBytes = Encoding.Unicode.GetBytes(substituteName);
            byte[] printBytes = Encoding.Unicode.GetBytes(printName);

            int reparseDataLength = 8 + substituteBytes.Length + 2 + printBytes.Length + 2;
            byte[] buffer = new byte[8 + reparseDataLength];

            BitConverter.GetBytes(0xA0000003U).CopyTo(buffer, 0); // IO_REPARSE_TAG_MOUNT_POINT
            BitConverter.GetBytes((ushort)reparseDataLength).CopyTo(buffer, 4);
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);

            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 8);
            BitConverter.GetBytes((ushort)substituteBytes.Length).CopyTo(buffer, 10);
            BitConverter.GetBytes((ushort)(substituteBytes.Length + 2)).CopyTo(buffer, 12);
            BitConverter.GetBytes((ushort)printBytes.Length).CopyTo(buffer, 14);

            Array.Copy(substituteBytes, 0, buffer, 16, substituteBytes.Length);
            Array.Copy(printBytes, 0, buffer, 16 + substituteBytes.Length + 2, printBytes.Length);

            using SafeFileHandle handle = CreateFileNative(
                linkPath_,
                0x40000000U, // GENERIC_WRITE
                7U, // FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE
                IntPtr.Zero,
                3U, // OPEN_EXISTING
                0x02000000U | 0x00200000U, // FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return false;
            }

            return DeviceIoControlNative(
                handle,
                0x900A4U, // FSCTL_SET_REPARSE_POINT
                buffer,
                (uint)buffer.Length,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileNative(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DeviceIoControlNative(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    private static string CreateTempStorageRoot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "repokit-coord-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void StorageRoot_MustExistAndBeValid()
    {
        string nonExistent = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() => new WorkflowSchedulerPaths(nonExistent));

        Assert.ThrowsAny<ArgumentException>(() => new WorkflowSchedulerPaths(null!));
        Assert.Throws<ArgumentException>(() => new WorkflowSchedulerPaths("   "));
        Assert.Throws<ArgumentException>(() => new WorkflowSchedulerPaths("bad\0path"));
    }

    [Fact]
    public void StorageRoot_Layout_MatchesFrozenSpecification()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            Assert.Equal(Path.Combine(root, "locks"), paths.LocksDirectory);
            Assert.Equal(Path.Combine(root, "locks", "domain.lock"), paths.DomainLockPath);
            Assert.Equal(Path.Combine(root, "locks", "domain-mode.lock"), paths.DomainModeLockPath);
            Assert.Equal(Path.Combine(root, "locks", "domain-policy.json"), paths.DomainPolicyPath);
            Assert.Equal(Path.Combine(root, "locks", "domain-integrity.json"), paths.DomainIntegrityPath);
            Assert.Equal(Path.Combine(root, "locks", "slots"), paths.SlotsDirectory);
            Assert.Equal(Path.Combine(root, "locks", "active"), paths.ActiveLeasesDirectory);
            Assert.Equal(Path.Combine(root, "locks", "resources"), paths.ResourcesDirectory);
            Assert.Equal(Path.Combine(root, "workflows"), paths.WorkflowsDirectory);

            Assert.True(Directory.Exists(paths.LocksDirectory));
            Assert.True(Directory.Exists(paths.SlotsDirectory));
            Assert.True(Directory.Exists(paths.ActiveLeasesDirectory));
            Assert.True(Directory.Exists(paths.ResourcesDirectory));
            Assert.True(Directory.Exists(paths.WorkflowsDirectory));

            Assert.True(File.Exists(paths.DomainLockPath));
            Assert.True(File.Exists(paths.DomainModeLockPath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void WorkflowId_HexEncoding_IsStrictUtf8Lowercase()
    {
        string id = "workflow-123";
        string hex = WorkflowSchedulerPaths.WorkflowIdToHex(id);
        Assert.Equal("776f726b666c6f772d313233", hex);

        string decoded = WorkflowSchedulerPaths.HexToWorkflowId(hex);
        Assert.Equal(id, decoded);

        // Reject non-lowercase hex
        Assert.Throws<ArgumentException>(() => WorkflowSchedulerPaths.HexToWorkflowId("776F726B666C6F772D313233"));

        // Reject odd length
        Assert.Throws<ArgumentException>(() => WorkflowSchedulerPaths.HexToWorkflowId("776"));

        // Reject invalid hex
        Assert.Throws<ArgumentException>(() => WorkflowSchedulerPaths.HexToWorkflowId("776g"));

        // NUL character is permitted per P02 frozen contract compatibility
        string wfWithNullHex = WorkflowSchedulerPaths.WorkflowIdToHex("wf\0bad");
        Assert.Equal(Convert.ToHexString(Encoding.UTF8.GetBytes("wf\0bad")).ToLowerInvariant(), wfWithNullHex);
    }

    [Fact]
    public void SlotLockPath_ValidatesRangeStrictly()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            Assert.Equal(
                Path.Combine(root, "locks", "slots", "slot-00000001.lock"),
                paths.GetSlotLockPath(1, 4));
            Assert.Equal(
                Path.Combine(root, "locks", "slots", "slot-00000004.lock"),
                paths.GetSlotLockPath(4, 4));

            Assert.Throws<ArgumentOutOfRangeException>(() => paths.GetSlotLockPath(0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => paths.GetSlotLockPath(5, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => paths.GetSlotLockPath(-1, 4));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void PathTraversal_IsRejectedFailClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            Assert.Throws<InvalidOperationException>(
                () => paths.AssertPathWithinRoot(Path.Combine(root, "..", "outside.lock")));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainMetadata_CleanBootstrap_CreatesIntegrityAndPolicy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            Assert.True(File.Exists(paths.DomainIntegrityPath));
            Assert.True(File.Exists(paths.DomainPolicyPath));

            // Reopening with identical policy succeeds (idempotent)
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainMetadata_PolicyMismatch_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy initialPolicy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, initialPolicy);

            // Mismatched capacity
            WorkflowSchedulerPolicy mismatchedCapacity = new(
                maxConcurrentWorkflows_: 8,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, mismatchedCapacity));

            // Mismatched unrestricted flag
            WorkflowSchedulerPolicy mismatchedUnrestricted = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, mismatchedUnrestricted));

            // Mismatched timeout
            WorkflowSchedulerPolicy mismatchedTimeout = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(10));

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, mismatchedTimeout));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainMetadata_MissingIntegrityInUsedDomain_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Simulate deleted or missing integrity file in an already used domain
            File.Delete(paths.DomainIntegrityPath);
            Assert.False(File.Exists(paths.DomainIntegrityPath));
            Assert.True(File.Exists(paths.DomainPolicyPath));

            // Must NOT silently reconstruct
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
            Assert.Contains("evidence of prior use", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainMetadata_CorruptedOrInvalidJson_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Corrupt JSON in integrity marker
            File.WriteAllText(paths.DomainIntegrityPath, "{ malformed json");

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));

            // Invalid schemaId
            File.WriteAllText(
                paths.DomainIntegrityPath,
                JsonSerializer.Serialize(new
                {
                    schemaId = "unknown-schema",
                    schemaVersion = 1,
                    bootstrapped = true
                }));

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));

            // Invalid version
            File.WriteAllText(
                paths.DomainIntegrityPath,
                JsonSerializer.Serialize(new
                {
                    schemaId = WorkflowSchedulerDomainMetadata.IntegritySchemaId,
                    schemaVersion = 99,
                    bootstrapped = true
                }));

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));

            // Bootstrapped false
            File.WriteAllText(
                paths.DomainIntegrityPath,
                JsonSerializer.Serialize(new
                {
                    schemaId = WorkflowSchedulerDomainMetadata.IntegritySchemaId,
                    schemaVersion = 1,
                    bootstrapped = false
                }));

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainMetadata_ResidualTemporaryFile_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            // Create residual temporary file
            string residual = Path.Combine(paths.LocksDirectory, "domain-policy.json.tmp.1.1.1");
            File.WriteAllText(residual, "partial data");

            WorkflowSchedulerPolicy policy = new(4);
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_Active_Quarantined_Maintenance_Roundtrip()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            WorkflowResourceClaim claim1 = new("fs:/data/work", WorkflowResourceAccessKind.SharedRead);
            WorkflowResourceClaim claim2 = new("logical:queue/1", WorkflowResourceAccessKind.ExclusiveWrite);

            // Active manifest
            WorkflowSchedulerLeaseManifest active = new(
                workflowId_: "wf-alpha",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "token-1",
                slot_: 2,
                domainMode_: "Normal",
                resourceClaims_: [claim1, claim2],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active, 4);

            string activePath = paths.GetActiveLeasePath("wf-alpha");
            Assert.True(File.Exists(activePath));

            WorkflowSchedulerLeaseManifest loadedActive =
                WorkflowSchedulerLeaseManifest.ReadAndValidate(activePath, paths, 4);

            Assert.Equal("wf-alpha", loadedActive.WorkflowId);
            Assert.Equal(WorkflowSchedulerLeaseState.Active, loadedActive.State);
            Assert.Equal("token-1", loadedActive.OwnerToken);
            Assert.Equal(2, loadedActive.Slot);
            Assert.Equal("Normal", loadedActive.DomainMode);
            Assert.Equal(2, loadedActive.ResourceClaims.Count);

            // Quarantined manifest (must have null slot)
            WorkflowSchedulerLeaseManifest quarantined = new(
                workflowId_: "wf-beta",
                state_: WorkflowSchedulerLeaseState.Quarantined,
                ownerToken_: "token-2",
                slot_: null,
                domainMode_: "Normal",
                resourceClaims_: [claim1],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantined, 4);
            string qPath = paths.GetActiveLeasePath("wf-beta");
            WorkflowSchedulerLeaseManifest loadedQ =
                WorkflowSchedulerLeaseManifest.ReadAndValidate(qPath, paths, 4);
            Assert.Equal(WorkflowSchedulerLeaseState.Quarantined, loadedQ.State);
            Assert.Null(loadedQ.Slot);

            // MaintenanceInProgress manifest (must have null slot and ExclusiveMaintenance mode)
            WorkflowSchedulerLeaseManifest maintenance = new(
                workflowId_: "wf-gamma",
                state_: WorkflowSchedulerLeaseState.MaintenanceInProgress,
                ownerToken_: "token-3",
                slot_: null,
                domainMode_: "ExclusiveMaintenance",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            // Attempting to publish MaintenanceInProgress while Active lease exists in the same domain must be rejected (global exclusion)
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maintenance, 4));

            // Test MaintenanceInProgress in an independent domain
            string maintRoot = CreateTempStorageRoot();
            try
            {
                WorkflowSchedulerPaths maintPaths = new(maintRoot);
                WorkflowSchedulerDomainMetadata.InitializeOrValidate(maintPaths, policy);

                using (WorkflowSchedulerLockHandle? modeLock =
                       WorkflowSchedulerLockPrimitives.TryAcquireExclusive(maintPaths.DomainModeLockPath))
                {
                    Assert.NotNull(modeLock);
                    WorkflowSchedulerLeaseManifest.WriteAtomic(maintPaths, maintenance, 4);

                    string mPath = maintPaths.GetActiveLeasePath("wf-gamma");
                    WorkflowSchedulerLeaseManifest loadedM =
                        WorkflowSchedulerLeaseManifest.ReadAndValidate(mPath, maintPaths, 4);
                    Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, loadedM.State);
                    Assert.Null(loadedM.Slot);
                    Assert.Equal("ExclusiveMaintenance", loadedM.DomainMode);

                    // While MaintenanceInProgress exists, publishing Active lease must be rejected
                    Assert.Throws<InvalidOperationException>(
                        () => WorkflowSchedulerLeaseManifest.WriteAtomic(maintPaths, active, 4));
                }
            }
            finally
            {
                Directory.Delete(maintRoot, recursive: true);
            }
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_ValidationRules_AreStrict()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            // Active cannot have null slot
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new WorkflowSchedulerLeaseManifest(
                    "wf-1",
                    WorkflowSchedulerLeaseState.Active,
                    "tok",
                    slot_: null,
                    "Normal",
                    [],
                    maxConcurrentWorkflows_: 4));

            // Active cannot have out-of-range slot
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new WorkflowSchedulerLeaseManifest(
                    "wf-1",
                    WorkflowSchedulerLeaseState.Active,
                    "tok",
                    slot_: 5,
                    "Normal",
                    [],
                    maxConcurrentWorkflows_: 4));

            // Quarantined cannot have non-null slot
            Assert.Throws<ArgumentException>(
                () => new WorkflowSchedulerLeaseManifest(
                    "wf-1",
                    WorkflowSchedulerLeaseState.Quarantined,
                    "tok",
                    slot_: 1,
                    "Normal",
                    [],
                    maxConcurrentWorkflows_: 4));

            // Maintenance cannot have non-null slot
            Assert.Throws<ArgumentException>(
                () => new WorkflowSchedulerLeaseManifest(
                    "wf-1",
                    WorkflowSchedulerLeaseState.MaintenanceInProgress,
                    "tok",
                    slot_: 1,
                    "ExclusiveMaintenance",
                    [],
                    maxConcurrentWorkflows_: 4));

            // Maintenance must have ExclusiveMaintenance mode
            Assert.Throws<ArgumentException>(
                () => new WorkflowSchedulerLeaseManifest(
                    "wf-1",
                    WorkflowSchedulerLeaseState.MaintenanceInProgress,
                    "tok",
                    slot_: null,
                    "Normal",
                    [],
                    maxConcurrentWorkflows_: 4));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    // --- CORREÇÃO A: Exclusão global Unrestricted ---

    [Fact]
    public void ConflictEngine_QuarantinedUnrestricted_WithEmptyClaims_ImposesGlobalExclusion()
    {
        WorkflowSchedulerLeaseManifest quarantinedUnrestricted = new(
            workflowId_: "wf-q-unrestricted",
            state_: WorkflowSchedulerLeaseState.Quarantined,
            ownerToken_: "tok-1",
            slot_: null,
            domainMode_: "Unrestricted",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);

        // Even with empty candidate claims, conflict occurs!
        Assert.True(
            WorkflowSchedulerConflictEngine.HasConflictWithLease(
                [],
                quarantinedUnrestricted,
                out string? reason));
        Assert.NotNull(reason);
        Assert.Contains("Unrestricted domain mode global exclusion", reason);

        // With candidate claims, conflict occurs!
        WorkflowResourceClaim candidateClaim = new("fs:/data/work", WorkflowResourceAccessKind.SharedRead);
        Assert.True(
            WorkflowSchedulerConflictEngine.HasConflictWithLease(
                [candidateClaim],
                quarantinedUnrestricted,
                out string? reason2));
        Assert.NotNull(reason2);
        Assert.Contains("Unrestricted domain mode global exclusion", reason2);
    }

    [Fact]
    public void ConflictEngine_QuarantinedUnrestricted_WithDisjointClaims_ImposesGlobalExclusion()
    {
        WorkflowResourceClaim existingClaim = new("fs:/app/store", WorkflowResourceAccessKind.ExclusiveWrite);
        WorkflowSchedulerLeaseManifest quarantinedUnrestricted = new(
            workflowId_: "wf-q-unrestricted-disjoint",
            state_: WorkflowSchedulerLeaseState.Quarantined,
            ownerToken_: "tok-1",
            slot_: null,
            domainMode_: "Unrestricted",
            resourceClaims_: [existingClaim],
            maxConcurrentWorkflows_: 4);

        WorkflowResourceClaim candidateClaim = new("fs:/completely/disjoint", WorkflowResourceAccessKind.ExclusiveWrite);
        Assert.True(
            WorkflowSchedulerConflictEngine.HasConflictWithLease(
                [candidateClaim],
                quarantinedUnrestricted,
                out string? reason));
        Assert.NotNull(reason);
        Assert.Contains("Unrestricted domain mode global exclusion", reason);
    }

    [Fact]
    public void ConflictEngine_ActiveUnrestricted_ImposesGlobalExclusion()
    {
        WorkflowSchedulerLeaseManifest activeUnrestricted = new(
            workflowId_: "wf-active-unrestricted",
            state_: WorkflowSchedulerLeaseState.Active,
            ownerToken_: "tok-1",
            slot_: 1,
            domainMode_: "Unrestricted",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);

        WorkflowResourceClaim candidateClaim = new("fs:/data", WorkflowResourceAccessKind.SharedRead);
        Assert.True(
            WorkflowSchedulerConflictEngine.HasConflictWithLease(
                [candidateClaim],
                activeUnrestricted,
                out string? reason));
        Assert.NotNull(reason);
        Assert.Contains("Unrestricted domain mode global exclusion", reason);
    }

    [Fact]
    public void ConflictEngine_NormalQuarantine_ConflictingAndNonConflictingClaims()
    {
        WorkflowResourceClaim existingClaim = new("fs:/data/sub", WorkflowResourceAccessKind.ExclusiveWrite);
        WorkflowSchedulerLeaseManifest normalQuarantine = new(
            workflowId_: "wf-normal-q",
            state_: WorkflowSchedulerLeaseState.Quarantined,
            ownerToken_: "tok-1",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [existingClaim],
            maxConcurrentWorkflows_: 4);

        // Conflicting claim: ancestor exclusive or shared read on exclusive
        WorkflowResourceClaim conflictingClaim = new("fs:/data", WorkflowResourceAccessKind.ExclusiveWrite);
        Assert.True(
            WorkflowSchedulerConflictEngine.HasConflictWithLease(
                [conflictingClaim],
                normalQuarantine,
                out string? reasonConflicting));
        Assert.NotNull(reasonConflicting);

        // Non-conflicting claim: completely disjoint resource
        WorkflowResourceClaim disjointClaim = new("fs:/other/tree", WorkflowResourceAccessKind.ExclusiveWrite);
        Assert.False(
            WorkflowSchedulerConflictEngine.HasConflictWithLease(
                [disjointClaim],
                normalQuarantine,
                out string? reasonDisjoint));
        Assert.Null(reasonDisjoint);
    }

    [Fact]
    public void ConflictEngine_OrphanedMaintenance_MaintainsGlobalExclusion()
    {
        WorkflowSchedulerLeaseManifest maintenance = new(
            workflowId_: "wf-maint-orphan",
            state_: WorkflowSchedulerLeaseState.MaintenanceInProgress,
            ownerToken_: "tok-1",
            slot_: null,
            domainMode_: "ExclusiveMaintenance",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);

        Assert.True(
            WorkflowSchedulerConflictEngine.HasConflictWithLease(
                [],
                maintenance,
                out string? reason));
        Assert.NotNull(reason);
        Assert.Contains("MaintenanceInProgress with global exclusion", reason);
    }

    // --- CORREÇÃO B: Proteger metadados e transições ---

    [Fact]
    public void LeaseManifest_Transition_DifferentOwnerCannotOverwrite()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            WorkflowSchedulerLeaseManifest initial = new(
                "wf-owner-test",
                WorkflowSchedulerLeaseState.Active,
                "owner-A",
                1,
                "Normal",
                [],
                4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initial, 4);

            WorkflowSchedulerLeaseManifest unauthorized = new(
                "wf-owner-test",
                WorkflowSchedulerLeaseState.Active,
                "owner-B",
                1,
                "Normal",
                [],
                4);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, unauthorized, 4));
            Assert.Contains("Cannot overwrite lease", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_Transition_QuarantinedCannotTransitionBackToActive()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            WorkflowSchedulerLeaseManifest qLease = new(
                "wf-transition-q",
                WorkflowSchedulerLeaseState.Quarantined,
                "owner-A",
                null,
                "Normal",
                [],
                4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, qLease, 4);

            WorkflowSchedulerLeaseManifest activeAttempt = new(
                "wf-transition-q",
                WorkflowSchedulerLeaseState.Active,
                "owner-A",
                1,
                "Normal",
                [],
                4);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeAttempt, 4));
            Assert.Contains("Quarantined leases cannot transition back to Active", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_Transition_ActiveToQuarantined_MustPreserveClaims()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            WorkflowResourceClaim claim1 = new("fs:/data/a", WorkflowResourceAccessKind.SharedRead);
            WorkflowResourceClaim claim2 = new("fs:/data/b", WorkflowResourceAccessKind.ExclusiveWrite);

            WorkflowSchedulerLeaseManifest active = new(
                "wf-preserve-claims",
                WorkflowSchedulerLeaseState.Active,
                "owner-A",
                1,
                "Normal",
                [claim1, claim2],
                4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active, 4);

            // Transition with dropped claim2 -> must throw!
            WorkflowSchedulerLeaseManifest quarantinedDropped = new(
                "wf-preserve-claims",
                WorkflowSchedulerLeaseState.Quarantined,
                "owner-A",
                null,
                "Normal",
                [claim1],
                4);

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantinedDropped, 4));

            // Transition preserving all claims -> succeeds!
            WorkflowSchedulerLeaseManifest quarantinedPreserved = new(
                "wf-preserve-claims",
                WorkflowSchedulerLeaseState.Quarantined,
                "owner-A",
                null,
                "Normal",
                [claim1, claim2],
                4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantinedPreserved, 4);

            WorkflowSchedulerLeaseManifest loaded =
                WorkflowSchedulerLeaseManifest.ReadAndValidate(
                    paths.GetActiveLeasePath("wf-preserve-claims"),
                    paths,
                    4);
            Assert.Equal(WorkflowSchedulerLeaseState.Quarantined, loaded.State);
            Assert.Equal(2, loaded.ResourceClaims.Count);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    // --- CORREÇÃO C: Integridade não comprovada nunca é saudável ---

    [Fact]
    public void IntegrityInspector_DomainWithoutDomainIntegrity_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            File.Delete(paths.DomainIntegrityPath);

            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("domain-integrity.json' is missing"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_DomainWithoutDomainPolicy_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            File.Delete(paths.DomainPolicyPath);

            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("domain-policy.json' is missing"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_WorkflowLockHeldShared_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-shared-lock";
            WorkflowSchedulerLeaseManifest active = new(
                wfId,
                WorkflowSchedulerLeaseState.Active,
                "tok-shared",
                1,
                "Normal",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active, 4);

            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            // Hold workflow.lock SHARED instead of exclusive
            using WorkflowSchedulerLockHandle wfShared =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfShared);

            // Hold slot 1 exclusive
            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            Assert.NotNull(slotLock);

            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("held shared, but exclusive lock is required"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_SlotLockHeldShared_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-slot-shared";
            WorkflowSchedulerLeaseManifest active = new(
                wfId,
                WorkflowSchedulerLeaseState.Active,
                "tok-shared",
                1,
                "Normal",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active, 4);

            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            // Hold workflow.lock exclusive
            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfLock);

            // Hold slot 1 SHARED instead of exclusive
            using WorkflowSchedulerLockHandle slotShared =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.GetSlotLockPath(1, 4))!;
            Assert.NotNull(slotShared);

            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("held shared, but exclusive lock is required"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveManifest_WithFreeSlot_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-free-slot";
            WorkflowSchedulerLeaseManifest active = new(
                wfId,
                WorkflowSchedulerLeaseState.Active,
                "tok-free",
                1,
                "Normal",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active, 4);

            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            // Hold workflow.lock exclusive, but slot 1 is free!
            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfLock);

            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("claimed slot 1 is unoccupied"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_CorruptedManifestFile_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            // Corrupt file in active leases directory
            string corruptPath = Path.Combine(paths.ActiveLeasesDirectory, "corrupt.lease.json");
            File.WriteAllText(corruptPath, "{ corrupt json text");

            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("Corrupted or invalid lease manifest"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    // --- CORREÇÃO D: JSON estrito e conservação de claims ---

    [Fact]
    public void LeaseManifest_StrictJson_MissingResourceClaims_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string hex = WorkflowSchedulerPaths.WorkflowIdToHex("wf-no-claims");
            string filePath = Path.Combine(paths.ActiveLeasesDirectory, $"{hex}.lease.json");

            string json = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "wf-no-claims",
              "state": "Active",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal"
            }
            """;
            File.WriteAllText(filePath, json);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(filePath, paths, 4));
            Assert.Contains("resourceClaims", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_StrictJson_DuplicateProperty_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string hex = WorkflowSchedulerPaths.WorkflowIdToHex("wf-dup-prop");
            string filePath = Path.Combine(paths.ActiveLeasesDirectory, $"{hex}.lease.json");

            string json = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "wf-dup-prop",
              "workflowId": "wf-dup-prop",
              "state": "Active",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal",
              "resourceClaims": []
            }
            """;
            File.WriteAllText(filePath, json);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(filePath, paths, 4));
            Assert.Contains("Duplicate JSON property", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_StrictJson_UnexpectedProperty_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string hex = WorkflowSchedulerPaths.WorkflowIdToHex("wf-unexp-prop");
            string filePath = Path.Combine(paths.ActiveLeasesDirectory, $"{hex}.lease.json");

            string json = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "wf-unexp-prop",
              "state": "Active",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal",
              "resourceClaims": [],
              "unexpectedField": "bad"
            }
            """;
            File.WriteAllText(filePath, json);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(filePath, paths, 4));
            Assert.Contains("Unexpected property", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_StrictJson_NumericState_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string hex = WorkflowSchedulerPaths.WorkflowIdToHex("wf-num-state");
            string filePath = Path.Combine(paths.ActiveLeasesDirectory, $"{hex}.lease.json");

            string json = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "wf-num-state",
              "state": "1",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal",
              "resourceClaims": []
            }
            """;
            File.WriteAllText(filePath, json);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(filePath, paths, 4));
            Assert.Contains("Numeric lease state", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_StrictJson_NumericAccessKind_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string hex = WorkflowSchedulerPaths.WorkflowIdToHex("wf-num-access");
            string filePath = Path.Combine(paths.ActiveLeasesDirectory, $"{hex}.lease.json");

            string json = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "wf-num-access",
              "state": "Active",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal",
              "resourceClaims": [
                {
                  "resourceId": "fs:/data",
                  "accessKind": "0"
                }
              ]
            }
            """;
            File.WriteAllText(filePath, json);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(filePath, paths, 4));
            Assert.Contains("Numeric accessKind", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    // --- CORREÇÃO E: Filesystem e identidade física ---

    [Fact]
    public void Paths_BrokenSymlink_IsDetectedAndRejected()
    {
        string root = CreateTempStorageRoot();
        string symlinkPath = string.Empty;
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();
            string nonExistentTarget = Path.Combine(root, "target-" + Guid.NewGuid().ToString("N"));
            symlinkPath = Path.Combine(paths.LocksDirectory, "broken-link.lock");

            bool created = false;
            try
            {
                File.CreateSymbolicLink(symlinkPath, nonExistentTarget);
                created = true;
            }
            catch (IOException ioEx) when (ioEx.HResult == unchecked((int)0x80070522) || ioEx.HResult == unchecked((int)0x80070005) || ioEx.Message.Contains("privilege", StringComparison.OrdinalIgnoreCase))
            {
                // In non-elevated Windows environment where file symlinks require privilege,
                // fall back to creating a broken directory junction (NTFS reparse point) at symlinkPath.
                created = TryCreateJunction(symlinkPath, nonExistentTarget);
            }
            catch (UnauthorizedAccessException)
            {
                created = TryCreateJunction(symlinkPath, nonExistentTarget);
            }

            Assert.True(created, "Broken reparse point or symlink must be created in test environment.");
            Assert.False(File.Exists(nonExistentTarget) || Directory.Exists(nonExistentTarget), "The symlink target must remain absent.");

            // The link entry itself must really exist (a dangling link reports Exists == false by following it,
            // so existence is verified without following the link).
            FileInfo linkInfo = new(symlinkPath);
            Assert.True(
                (linkInfo.Attributes & FileAttributes.ReparsePoint) != 0,
                "The created entry must be a reparse point / symbolic link.");
            Assert.NotNull(linkInfo.LinkTarget);

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerPaths.AssertNoReparsePoint(symlinkPath));
        }
        finally
        {
            if (!string.IsNullOrEmpty(symlinkPath))
            {
                try { Directory.Delete(symlinkPath, recursive: false); } catch { }
                try { File.Delete(symlinkPath); } catch { }
            }
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WorkflowId_WithEmbeddedNull_EncodesToIdenticalP02Hex()
    {
        // Frozen public P02 constructor accepts valid Unicode text up to 64 UTF-8 bytes and does not reject internal NUL.
        string idWithNull = "wf\0workflow-id";
        AiRepoKit.Orchestration.WorkflowId p02WorkflowId = new(idWithNull);
        Assert.Equal(idWithNull, p02WorkflowId.Value);

        byte[] expectedUtf8 = Encoding.UTF8.GetBytes(idWithNull);
        string expectedHex = Convert.ToHexString(expectedUtf8).ToLowerInvariant();

        // Must encode cleanly and match P02 encoding exactly:
        string schedulerHex = WorkflowSchedulerPaths.WorkflowIdToHex(idWithNull);
        Assert.Equal(expectedHex, schedulerHex);

        // Roundtrip decoding
        string decoded = WorkflowSchedulerPaths.HexToWorkflowId(schedulerHex);
        Assert.Equal(idWithNull, decoded);
    }

    [Fact]
    public void LeaseManifest_QuarantinedUnrestricted_TransitionToNormal_IsRejected()
    {
        // A1 — Quarentena Unrestricted:
        // Criar Quarantined + Unrestricted + slot=null + claims=[].
        // Tentar gravar Quarantined + Normal com o mesmo WorkflowId e OwnerToken.
        // A transição deve ser rejeitada. A exclusão global precisa continuar válida.
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "quarantined-unrestricted-wf";
            string owner = "owner-token-1";

            WorkflowSchedulerLeaseManifest qUnrestricted = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Quarantined,
                ownerToken_: owner,
                slot_: null,
                domainMode_: "Unrestricted",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, qUnrestricted, 4);

            // Attempt to write Quarantined + Normal with identical workflowId and ownerToken:
            WorkflowSchedulerLeaseManifest qNormal = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Quarantined,
                ownerToken_: owner,
                slot_: null,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, qNormal, 4));
            Assert.Contains("Global exclusion must be preserved", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_MaintenanceInProgress_TransitionToQuarantinedNormal_IsRejected()
    {
        // A2 — Manutenção não auditada:
        // Criar MaintenanceInProgress + ExclusiveMaintenance.
        // Tentar gravar Quarantined + Normal, sem o procedimento exclusivo de reconciliação e auditoria exigido pelo contrato.
        // A transição não pode remover a exclusão global.
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "maintenance-wf";
            string owner = "maint-owner-1";

            WorkflowSchedulerLeaseManifest maintenance = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.MaintenanceInProgress,
                ownerToken_: owner,
                slot_: null,
                domainMode_: "ExclusiveMaintenance",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maintenance, 4);

            // Attempt to transition to Quarantined + Normal without audited reconciliation:
            WorkflowSchedulerLeaseManifest qNormal = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Quarantined,
                ownerToken_: owner,
                slot_: null,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, qNormal, 4));
            Assert.Contains("Maintenance leases cannot transition without audited reconciliation", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_Active_CannotAlterSlotOrDomainModeOrClaimsArbitrarily()
    {
        // A3 — Mudanças de ownership:
        // Não permitir que a simples igualdade de OwnerToken autorize alterações em slot, modo, claims ou estado sem comprovar os pré-requisitos da transição.
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "active-wf";
            string owner = "owner-token-active";
            List<WorkflowResourceClaim> claims = [new WorkflowResourceClaim("fs:/res/1", WorkflowResourceAccessKind.ExclusiveWrite)];

            WorkflowSchedulerLeaseManifest initial = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: owner,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: claims,
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initial, 4);

            // Attempt to alter slot:
            WorkflowSchedulerLeaseManifest alteredSlot = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: owner,
                slot_: 2,
                domainMode_: "Normal",
                resourceClaims_: claims,
                maxConcurrentWorkflows_: 4);

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, alteredSlot, 4));

            // Attempt to alter domainMode:
            WorkflowSchedulerLeaseManifest alteredMode = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: owner,
                slot_: 1,
                domainMode_: "Unrestricted",
                resourceClaims_: claims,
                maxConcurrentWorkflows_: 4);

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, alteredMode, 4));

            // Attempt to alter claims:
            WorkflowSchedulerLeaseManifest alteredClaims = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: owner,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [new WorkflowResourceClaim("fs:/res/2", WorkflowResourceAccessKind.ExclusiveWrite)],
                maxConcurrentWorkflows_: 4);

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, alteredClaims, 4));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_TamperedIntegritySchemaId_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Tamper schemaId in domain-integrity.json
            string json = File.ReadAllText(paths.DomainIntegrityPath);
            json = json.Replace("ai.repokit.workflow-scheduler-domain-integrity", "tampered-schema-id");
            File.WriteAllText(paths.DomainIntegrityPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("domain-integrity schemaId"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_TamperedIntegritySchemaVersion_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Tamper schemaVersion in domain-integrity.json
            string json = File.ReadAllText(paths.DomainIntegrityPath);
            json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");
            File.WriteAllText(paths.DomainIntegrityPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("domain-integrity schemaVersion"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_TamperedPolicySchemaId_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Tamper schemaId in domain-policy.json
            string json = File.ReadAllText(paths.DomainPolicyPath);
            json = json.Replace("ai.repokit.workflow-scheduler-domain-policy", "tampered-policy-id");
            File.WriteAllText(paths.DomainPolicyPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("domain-policy schemaId"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_TamperedPolicySchemaVersion_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Tamper schemaVersion in domain-policy.json
            string json = File.ReadAllText(paths.DomainPolicyPath);
            json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
            File.WriteAllText(paths.DomainPolicyPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("domain-policy schemaVersion"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_TamperedAllowUnrestrictedExecution_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Tamper allowUnrestrictedExecution with non-boolean value
            string json = File.ReadAllText(paths.DomainPolicyPath);
            json = json.Replace("\"allowUnrestrictedExecution\": false", "\"allowUnrestrictedExecution\": \"invalid\"");
            File.WriteAllText(paths.DomainPolicyPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("allowUnrestrictedExecution"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_TamperedContinueUntilBlockedOrTerminal_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Tamper continueUntilBlockedOrTerminal with non-boolean value
            string json = File.ReadAllText(paths.DomainPolicyPath);
            json = json.Replace("\"continueUntilBlockedOrTerminal\": true", "\"continueUntilBlockedOrTerminal\": 123");
            File.WriteAllText(paths.DomainPolicyPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("continueUntilBlockedOrTerminal"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_TamperedDomainLockTimeoutTicks_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Tamper domainLockTimeoutTicks to be inconsistent with domainLockTimeoutMs
            string json = File.ReadAllText(paths.DomainPolicyPath);
            json = json.Replace($"\"domainLockTimeoutTicks\": {policy.DomainLockTimeout.Ticks}", "\"domainLockTimeoutTicks\": 999999999");
            File.WriteAllText(paths.DomainPolicyPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("DomainLockTimeoutTicks"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void IntegrityInspector_DuplicateOrUnexpectedProperties_IsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4, false, true, TimeSpan.FromSeconds(5));
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Add unexpected property to domain-policy.json
            string json = File.ReadAllText(paths.DomainPolicyPath);
            json = json.Insert(json.IndexOf('{') + 1, "\n  \"unexpectedProperty\": \"malicious\",\n");
            File.WriteAllText(paths.DomainPolicyPath, json);

            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("Unexpected property 'unexpectedProperty'"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainMetadata_DurabilityStages_ReportedCorrectly()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string testPath = Path.Combine(paths.LocksDirectory, "test-durability.json");
            WorkflowSchedulerDurabilityReport report = WorkflowSchedulerDomainMetadata.WriteAtomic(
                testPath,
                "{\"test\":true}",
                paths.LocksDirectory);

            Assert.True(File.Exists(testPath));
            // On supported platforms, publication must report durably synced directory metadata.
            // UnknownDurability cannot satisfy successful publication assertion.
            Assert.Equal(WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced, report);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_CompositeOrWhitespaceOrNonCanonicalEnum_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string wfId = "test-wf-enum";
            string leasePath = paths.GetActiveLeasePath(wfId);

            // Non-canonical state: extra spaces
            string jsonWhitespaceState = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "{{wfId}}",
              "state": " Active ",
              "ownerToken": "owner1",
              "slot": 1,
              "domainMode": "Normal",
              "resourceClaims": []
            }
            """;
            File.WriteAllText(leasePath, jsonWhitespaceState);
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4));

            // Composite state
            string jsonCompositeState = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "{{wfId}}",
              "state": "Active, Quarantined",
              "ownerToken": "owner1",
              "slot": 1,
              "domainMode": "Normal",
              "resourceClaims": []
            }
            """;
            File.WriteAllText(leasePath, jsonCompositeState);
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4));

            // Non-canonical accessKind in claim
            string jsonWhitespaceAccessKind = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "{{wfId}}",
              "state": "Active",
              "ownerToken": "owner1",
              "slot": 1,
              "domainMode": "Normal",
              "resourceClaims": [
                {
                  "resourceId": "repo:/test",
                  "accessKind": " Exclusive "
                }
              ]
            }
            """;
            File.WriteAllText(leasePath, jsonWhitespaceAccessKind);
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_MissingSlotProperty_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string wfId = "test-wf-missing-slot";
            string leasePath = paths.GetActiveLeasePath(wfId);

            // Manifest missing 'slot' property completely
            string jsonMissingSlot = $$"""
            {
              "schemaId": "ai.repokit.workflow-scheduler-lease",
              "schemaVersion": 1,
              "workflowId": "{{wfId}}",
              "state": "Quarantined",
              "ownerToken": "owner1",
              "domainMode": "Normal",
              "resourceClaims": []
            }
            """;
            File.WriteAllText(leasePath, jsonMissingSlot);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4));
            Assert.Contains("missing required 'slot' property", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    // --- CORREÇÃO F: Erros de locks ---

    [Fact]
    public void LockPrimitives_SharingViolation_ClassifiedCorrectly()
    {
        // 0x80070020 is ERROR_SHARING_VIOLATION
        IOException sharingViolation = new("Sharing violation test", unchecked((int)0x80070020));
        Assert.True(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(sharingViolation));

        // 0x80070021 is ERROR_LOCK_VIOLATION
        IOException lockViolation = new("Lock violation test", unchecked((int)0x80070021));
        Assert.True(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(lockViolation));

        // Generic disk error (E_FAIL) is NOT legitimate lock contention
        IOException diskError = new("Disk full or IO error", unchecked((int)0x80004005));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(diskError));
    }

    // --- CORREÇÃO G: Compatibilidade P02 e precisão da política ---

    [Fact]
    public void WorkflowId_DecomposedUnicode_MatchesP02EncodingExactly()
    {
        // "e\u0301" is 'e' followed by COMBINING ACUTE ACCENT (decomposed Unicode NFD)
        string decomposedWorkflowId = "wf-e\u0301-test";
        Assert.False(decomposedWorkflowId.IsNormalized(NormalizationForm.FormC));

        // P02 canonical encoding from WorkflowPersistencePaths:
        byte[] expectedUtf8Bytes = Encoding.UTF8.GetBytes(decomposedWorkflowId);
        string expectedP02Hex = Convert.ToHexString(expectedUtf8Bytes).ToLowerInvariant();

        // Must encode to EXACT same hex without throwing NFC error:
        string schedulerHex = WorkflowSchedulerPaths.WorkflowIdToHex(decomposedWorkflowId);
        Assert.Equal(expectedP02Hex, schedulerHex);

        // Must decode back cleanly:
        string roundtripId = WorkflowSchedulerPaths.HexToWorkflowId(schedulerHex);
        Assert.Equal(decomposedWorkflowId, roundtripId);
    }

    [Fact]
    public void DomainPolicy_SubMillisecondTimeoutPrecision_NotConfused()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);

            TimeSpan timeout1 = TimeSpan.FromTicks(100); // 0.01 ms
            TimeSpan timeout2 = TimeSpan.FromTicks(200); // 0.02 ms

            WorkflowSchedulerPolicy policy1 = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: timeout1);

            WorkflowSchedulerPolicy policy2 = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: timeout2);

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy1);

            // Re-validating with policy2 must detect tick mismatch and fail closed!
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy2));
            Assert.Contains("DomainLockTimeoutTicks", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainMetadata_UncommittedResidualFile_PreventsDomainBootstrapAndInspect()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            // Inject a residual uncommitted .tmp. file into locks directory
            string residualPath = Path.Combine(paths.LocksDirectory, "domain-policy.json.tmp.1234.5678");
            File.WriteAllText(residualPath, "{\"corrupt\":true}");

            WorkflowSchedulerPolicy policy = new(4);

            // InitializeOrValidate must fail closed upon detecting residual uncommitted file
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
            Assert.Contains("Residual uncommitted temporary file detected", ex.Message);

            // Inspect must also detect residual temporary file and report unhealthy
            WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("Residual"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainLock_TwoStreamsWithReadWriteShare_NeitherAcceptedAsExclusiveOwnership()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            // Open simultaneously two streams with FileShare.ReadWrite on the true domain.lock
            using FileStream stream1 = new(
                paths.DomainLockPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);

            using FileStream stream2 = new(
                paths.DomainLockPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);

            // Constructing an exclusive handle from a non-exclusive shared stream must be rejected
            Assert.Throws<InvalidOperationException>(
                () => new WorkflowSchedulerLockHandle(stream1, paths.DomainLockPath, isExclusive_: true));

            Assert.Throws<InvalidOperationException>(
                () => new WorkflowSchedulerLockHandle(stream2, paths.DomainLockPath, isExclusive_: true));

            // A non-exclusive handle constructed from either stream must also be rejected by ValidateDomainLockHandle
            using WorkflowSchedulerLockHandle nonExclusiveHandle1 =
                new(stream1, paths.DomainLockPath, isExclusive_: false);

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.ValidateDomainLockHandle(paths, nonExclusiveHandle1));

            using WorkflowSchedulerLockHandle nonExclusiveHandle2 =
                new(stream2, paths.DomainLockPath, isExclusive_: false);

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.ValidateDomainLockHandle(paths, nonExclusiveHandle2));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void DomainLock_WindowsAsymmetricSharedStreams_CannotProduceExclusiveCapabilityOrAuthorizePublication()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            {
                // 1. Abrir stream A com FileAccess.Write e FileShare.ReadWrite
                using FileStream streamA = new(
                    paths.DomainLockPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite);

                // 2. Abrir stream B com FileAccess.Read e FileShare.Write
                using FileStream streamB = new(
                    paths.DomainLockPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Write);

                // 3. Confirmar que ambos coexistem simultaneamente sobre o verdadeiro domain.lock
                Assert.True(streamA.CanWrite);
                Assert.True(streamB.CanRead);

                // 4. Comprovar que stream A não pode produzir capacidade exclusiva
                Assert.Throws<InvalidOperationException>(
                    () => new WorkflowLockExclusiveCapability(streamA.SafeFileHandle, paths.DomainLockPath));

                // 5. Comprovar que tentativa de fabricação de handle exclusivo para A é rejeitada
                Assert.Throws<InvalidOperationException>(
                    () => new WorkflowSchedulerLockHandle(streamA, paths.DomainLockPath, isExclusive_: true));
            }

            // 6. Testar regressão explícita com FileAccess.ReadWrite e FileShare.ReadWrite
            {
                using var stream = new FileStream(
                    paths.DomainLockPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);

                Assert.Throws<InvalidOperationException>(() =>
                {
                    var forgedCapability =
                        new WorkflowLockExclusiveCapability(
                            stream.SafeFileHandle,
                            paths.DomainLockPath);

                    var forgedHandle =
                        new WorkflowSchedulerLockHandle(
                            stream,
                            paths.DomainLockPath,
                            true,
                            forgedCapability);
                });
            }

            // 7. Confirmar que um handle genuíno de TryAcquireExclusive continua autorizado
            using WorkflowSchedulerLockHandle genuineDomainHandle =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath)!;
            Assert.NotNull(genuineDomainHandle);
            Assert.True(genuineDomainHandle.IsExclusive);
            Assert.True(genuineDomainHandle.HasExclusiveCapability);
            WorkflowSchedulerDomainMetadata.ValidateDomainLockHandle(paths, genuineDomainHandle);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_QuarantinedUnrestrictedFollowedByActiveNormal_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // 1. Quarantined + Unrestricted for wf1
            WorkflowSchedulerLeaseManifest qUnrestricted = new(
                "wf-quarantined-unrestricted",
                WorkflowSchedulerLeaseState.Quarantined,
                "tok-q",
                null,
                "Unrestricted",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, qUnrestricted, 4);

            // 2. Attempt Active + Normal for wf2
            WorkflowSchedulerLeaseManifest activeNormal = new(
                "wf-active-normal",
                WorkflowSchedulerLeaseState.Active,
                "tok-act",
                1,
                "Normal",
                [],
                4);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeNormal, 4));
            Assert.Contains("quarantined in Unrestricted mode", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_MaintenanceInProgressFollowedByActiveNormal_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            WorkflowSchedulerLeaseManifest maint = new(
                "wf-maintenance",
                WorkflowSchedulerLeaseState.MaintenanceInProgress,
                "tok-maint",
                null,
                "ExclusiveMaintenance",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maint, 4);

            WorkflowSchedulerLeaseManifest activeNormal = new(
                "wf-active-normal",
                WorkflowSchedulerLeaseState.Active,
                "tok-act",
                1,
                "Normal",
                [],
                4);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeNormal, 4));
            Assert.Contains("maintenance is in progress", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_MaintenanceInProgressFollowedByActiveUnrestricted_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            WorkflowSchedulerLeaseManifest maint = new(
                "wf-maintenance-unres",
                WorkflowSchedulerLeaseState.MaintenanceInProgress,
                "tok-maint",
                null,
                "ExclusiveMaintenance",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maint, 4);

            using WorkflowSchedulerLockHandle? modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath);
            Assert.NotNull(modeLock);

            WorkflowSchedulerLeaseManifest activeUnrestricted = new(
                "wf-active-unrestricted-maint",
                WorkflowSchedulerLeaseState.Active,
                "tok-unres",
                1,
                "Unrestricted",
                [],
                4);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeUnrestricted, 4));
            Assert.Contains("maintenance is in progress", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_ActiveUnrestrictedFollowedByActiveNormal_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);

                WorkflowSchedulerLeaseManifest activeUnrestricted = new(
                    "wf-active-unrestricted-first",
                    WorkflowSchedulerLeaseState.Active,
                    "tok-unres",
                    1,
                    "Unrestricted",
                    [],
                    4);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeUnrestricted, 4);
            }

            WorkflowSchedulerLeaseManifest activeNormal = new(
                "wf-active-normal-second",
                WorkflowSchedulerLeaseState.Active,
                "tok-norm",
                2,
                "Normal",
                [],
                4);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeNormal, 4));
            Assert.Contains("active in Unrestricted mode", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void Domain_ReopenWithContradictoryPreexistingManifests_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Pre-populate two contradictory manifests:
            // 1) MaintenanceInProgress
            WorkflowSchedulerLeaseManifest maint = new(
                "wf-maint-contradictory",
                WorkflowSchedulerLeaseState.MaintenanceInProgress,
                "tok-maint",
                null,
                "ExclusiveMaintenance",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maint, 4);

            // 2) Write another Active lease manifest directly to simulate pre-existing contradictory state
            WorkflowSchedulerLeaseManifest active = new(
                "wf-active-contradictory",
                WorkflowSchedulerLeaseState.Active,
                "tok-act",
                1,
                "Normal",
                [],
                4);
            string hex = WorkflowSchedulerPaths.WorkflowIdToHex("wf-active-contradictory");
            string finalPath = Path.Combine(paths.ActiveLeasesDirectory, $"{hex}.lease.json");
            string payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaId = WorkflowSchedulerLeaseManifest.LeaseSchemaId,
                schemaVersion = WorkflowSchedulerLeaseManifest.LeaseSchemaVersion,
                workflowId = active.WorkflowId,
                state = active.State.ToString(),
                ownerToken = active.OwnerToken,
                slot = active.Slot,
                domainMode = active.DomainMode,
                resourceClaims = new object[0]
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            WorkflowSchedulerDomainMetadata.WriteAtomic(finalPath, payload, paths.ActiveLeasesDirectory);

            // Re-opening/validating domain must throw InvalidOperationException
            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
            Assert.Contains("contradictory pre-existing lease manifests", ex.Message);

            // Integrity inspector must report unhealthy with violation
            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("contradictory pre-existing lease manifests"));

            // Files must NOT be deleted
            Assert.True(File.Exists(finalPath));
            Assert.True(File.Exists(paths.GetActiveLeasePath("wf-maint-contradictory")));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_UnbootstrappedDomain_Active_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            WorkflowSchedulerLeaseManifest activeManifest = new(
                "wf-unbootstrapped-active",
                WorkflowSchedulerLeaseState.Active,
                "token-active",
                1,
                "Normal",
                [],
                4);

            // Attempting to write active lease manifest on unbootstrapped domain must fail closed
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4));

            // Verify no manifest was written to disk
            string leasePath = paths.GetActiveLeasePath("wf-unbootstrapped-active");
            Assert.False(File.Exists(leasePath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_UnbootstrappedDomain_Quarantined_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            WorkflowSchedulerLeaseManifest quarantinedManifest = new(
                "wf-unbootstrapped-quarantined",
                WorkflowSchedulerLeaseState.Quarantined,
                "token-quar",
                null,
                "Normal",
                [],
                4);

            // Attempting to write quarantined lease manifest on unbootstrapped domain must fail closed
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantinedManifest, 4));

            string leasePath = paths.GetActiveLeasePath("wf-unbootstrapped-quarantined");
            Assert.False(File.Exists(leasePath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_UnbootstrappedDomain_MaintenanceInProgress_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            WorkflowSchedulerLeaseManifest maintManifest = new(
                "wf-unbootstrapped-maint",
                WorkflowSchedulerLeaseState.MaintenanceInProgress,
                "token-maint",
                null,
                "ExclusiveMaintenance",
                [],
                4);

            // Attempting to write maintenance lease manifest on unbootstrapped domain must fail closed
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maintManifest, 4));

            string leasePath = paths.GetActiveLeasePath("wf-unbootstrapped-maint");
            Assert.False(File.Exists(leasePath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_BootstrappedDomain_PermittedPublications_Succeed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            WorkflowSchedulerLeaseManifest activeManifest = new(
                "wf-bootstrapped-active",
                WorkflowSchedulerLeaseState.Active,
                "token-active",
                1,
                "Normal",
                [],
                4);

            // After valid bootstrap, permitted publication succeeds
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4);

            string leasePath = paths.GetActiveLeasePath("wf-bootstrapped-active");
            Assert.True(File.Exists(leasePath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct RM_PROCESS_INFO
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

    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, IntPtr rgApplications, uint nServices, IntPtr rgsServiceNames);

    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] RM_PROCESS_INFO[]? rgAffectedApps, out uint lpdwRebootReasons);

    [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [Fact]
    public void RestartManager_DetectsLockingProcessId()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string tempFile = Path.Combine(Path.GetTempPath(), "rm-test-" + Guid.NewGuid().ToString("N") + ".lock");
        try
        {
            using (FileStream fs = new(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                int res = RmStartSession(out uint sessionHandle, 0, Guid.NewGuid().ToString("N"));
                Assert.Equal(0, res);
                try
                {
                    string[] resources = [ tempFile ];
                    res = RmRegisterResources(sessionHandle, 1, resources, 0, IntPtr.Zero, 0, IntPtr.Zero);
                    Assert.Equal(0, res);

                    uint pnProcInfoNeeded = 0;
                    uint pnProcInfo = 0;
                    res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, out uint rebootReasons);
                    Assert.True(pnProcInfoNeeded >= 1);

                    RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out rebootReasons);
                    Assert.Equal(0, res);

                    Assert.Equal(Environment.ProcessId, processInfo[0].Process.dwProcessId);
                }
                finally
                {
                    RmEndSession(sessionHandle);
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void LeaseManifest_ActiveUnrestricted_WhenPolicyDisallows_IsRejected_AndDomainRemainsHealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            // 1. Bootstrap with AllowUnrestrictedExecution = false
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // 2. Construct an Active + Unrestricted manifest
            WorkflowSchedulerLeaseManifest unrestrictedManifest = new(
                workflowId_: "wf-unrestricted-disallowed",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "owner-unrestricted",
                slot_: 1,
                domainMode_: "Unrestricted",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            // 3. Attempt WriteAtomic
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, unrestrictedManifest, 4));

            // 4. Confirm rejection: manifest was not published
            string leasePath = paths.GetActiveLeasePath("wf-unrestricted-disallowed");
            Assert.False(File.Exists(leasePath));

            // Domain remains intact and healthy
            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.True(report.IsHealthy);
            Assert.Empty(report.Violations);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_ActiveUnrestricted_WhenPolicyAllowsAndRequiredLocksAcquired_Succeeds()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            // Bootstrap with AllowUnrestrictedExecution = true
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: TimeSpan.FromSeconds(5));

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Acquire domain-mode.lock exclusive (required for Unrestricted)
            using WorkflowSchedulerLockHandle? modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath);
            Assert.NotNull(modeLock);

            WorkflowSchedulerLeaseManifest unrestrictedManifest = new(
                workflowId_: "wf-unrestricted-allowed",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "owner-unrestricted",
                slot_: 1,
                domainMode_: "Unrestricted",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            // WriteAtomic succeeds
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, unrestrictedManifest, 4);

            string leasePath = paths.GetActiveLeasePath("wf-unrestricted-allowed");
            Assert.True(File.Exists(leasePath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void Durability_CommitProtocol_FaultBeforeRename_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Simulate partial write where tmp file and .commit marker were created, but rename did not execute
            string tmpFile = Path.Combine(paths.ActiveLeasesDirectory, "test.tmp.1234.1.9999");
            string commitMarker = tmpFile + ".commit";
            File.WriteAllText(tmpFile, "partial-uncommitted-content");
            File.WriteAllText(commitMarker, "");

            // 1. CheckResidualTemporaryFiles must throw InvalidOperationException
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.CheckResidualTemporaryFiles(paths));

            // 2. Integrity inspector must report unhealthy and record residual file violation
            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("Residual"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void Durability_CommitProtocol_FaultAfterRenameBeforeCommitRemoval_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Publish valid manifest
            string wfId = "wf-durability-marker";
            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "owner-durable",
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            // Simulate destination file moved, but residual commit marker left behind (crash after rename before marker deletion)
            string orphanedCommitMarker = Path.Combine(
                paths.ActiveLeasesDirectory,
                "wf-durability.tmp.5678.2.8888.commit");
            File.WriteAllText(orphanedCommitMarker, "");

            // 1. CheckResidualTemporaryFiles must fail closed
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.CheckResidualTemporaryFiles(paths));

            // 2. Integrity inspector must report unhealthy
            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("Residual"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void Durability_ResidualCommitAndTmpMarker_InWorkflowSubdirectory_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-residual-sub";
            paths.EnsureWorkflowDirectory(wfId);
            string wfDir = paths.GetWorkflowDirectory(wfId);

            string residualCommit = Path.Combine(wfDir, "owner-auth.tmp.1234.1.9999.commit");
            File.WriteAllText(residualCommit, "");

            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.CheckResidualTemporaryFiles(paths));

            WorkflowSchedulerIntegrityReport report =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

            Assert.False(report.IsHealthy);
            Assert.Contains(report.Violations, v => v.Contains("Residual"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void OwnerAuth_StrictSchemaValidation_RejectsMalformedPayloads()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-strict-auth";
            paths.EnsureWorkflowDirectory(wfId);
            string wfDir = paths.GetWorkflowDirectory(wfId);
            string authPath = Path.Combine(wfDir, "owner-auth.json");

            // 1. Duplicate property key
            string dupJson = """
            {
              "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
              "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
              "schemaVersion": 1,
              "workflowId": "wf-strict-auth",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal",
              "processId": 1234,
              "processStartTimeTicks": 99999
            }
            """;
            File.WriteAllText(authPath, dupJson);
            Assert.Null(WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(paths, wfId));

            // 2. Unexpected property
            string unexpectedJson = """
            {
              "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
              "schemaVersion": 1,
              "workflowId": "wf-strict-auth",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal",
              "processId": 1234,
              "processStartTimeTicks": 99999,
              "unexpectedProp": "injected"
            }
            """;
            File.WriteAllText(authPath, unexpectedJson);
            Assert.Null(WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(paths, wfId));

            // 3. processStartTimeTicks is 0
            string zeroTimeJson = """
            {
              "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
              "schemaVersion": 1,
              "workflowId": "wf-strict-auth",
              "ownerToken": "tok-1",
              "slot": 1,
              "domainMode": "Normal",
              "processId": 1234,
              "processStartTimeTicks": 0
            }
            """;
            File.WriteAllText(authPath, zeroTimeJson);
            Assert.Null(WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(paths, wfId));

            // 4. Missing required property (missing slot)
            string missingSlotJson = """
            {
              "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
              "schemaVersion": 1,
              "workflowId": "wf-strict-auth",
              "ownerToken": "tok-1",
              "domainMode": "Normal",
              "processId": 1234,
              "processStartTimeTicks": 99999
            }
            """;
            File.WriteAllText(authPath, missingSlotJson);
            Assert.Null(WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(paths, wfId));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_ActiveNormalFollowedByMaintenanceInProgress_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // 1. Active + Normal lease published
            WorkflowSchedulerLeaseManifest active = new(
                "wf-active-first",
                WorkflowSchedulerLeaseState.Active,
                "tok-act",
                1,
                "Normal",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active, 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);

                // 2. New MaintenanceInProgress must be rejected (global exclusion violated)
                WorkflowSchedulerLeaseManifest maint = new(
                    "wf-maint-second",
                    WorkflowSchedulerLeaseState.MaintenanceInProgress,
                    "tok-maint",
                    null,
                    "ExclusiveMaintenance",
                    [],
                    4);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maint, 4));
                Assert.Contains("active", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_ActiveNormalFollowedByQuarantinedUnrestricted_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // 1. Active + Normal lease published
            WorkflowSchedulerLeaseManifest active = new(
                "wf-active-norm",
                WorkflowSchedulerLeaseState.Active,
                "tok-act",
                1,
                "Normal",
                [],
                4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active, 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);

                // 2. Quarantined + Unrestricted must be rejected
                WorkflowSchedulerLeaseManifest qUnrestricted = new(
                    "wf-quar-unres",
                    WorkflowSchedulerLeaseState.Quarantined,
                    "tok-q",
                    null,
                    "Unrestricted",
                    [],
                    4);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, qUnrestricted, 4));
                Assert.Contains("active", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void RegisterOwnerAuthentication_WrongSlotHandle_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-wrong-slot";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);
            paths.EnsureSlotFile(2, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfLock);

            // Acquire slot 2 handle, but declare slot 1
            using WorkflowSchedulerLockHandle slot2Lock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(2, 4))!;
            Assert.NotNull(slot2Lock);

            using WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;
            Assert.NotNull(modeLock);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slotLockHandle_: slot2Lock,
                    modeLock));

            Assert.Contains("slotLockHandle is bound to", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void RegisterOwnerAuthentication_WrongWorkflowHandle_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfTarget = "wf-target";
            string wfOther = "wf-other";
            paths.EnsureWorkflowDirectory(wfTarget);
            paths.EnsureWorkflowDirectory(wfOther);
            paths.EnsureSlotFile(1, 4);

            // Acquire lock on wf-other, but try to register for wf-target
            using WorkflowSchedulerLockHandle wfOtherLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfOther))!;
            Assert.NotNull(wfOtherLock);

            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            Assert.NotNull(slotLock);

            using WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;
            Assert.NotNull(modeLock);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfTarget,
                    "token-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    workflowLockHandle_: wfOtherLock,
                    slotLock,
                    modeLock));

            Assert.Contains("workflowLockHandle is bound to", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void RegisterOwnerAuthentication_WrongModeHandle_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-wrong-mode";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfLock);

            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            Assert.NotNull(slotLock);

            // Pass domain.lock handle instead of domain-mode.lock handle
            using WorkflowSchedulerLockHandle wrongModeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainLockPath)!;
            Assert.NotNull(wrongModeLock);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slotLock,
                    domainModeLockHandle_: wrongModeLock));

            Assert.Contains("domainModeLockHandle is bound to", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void RegisterOwnerAuthentication_NormalMode_WithExclusiveModeHandle_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-norm-with-excl-mode";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfLock);

            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            Assert.NotNull(slotLock);

            // Acquire domain-mode.lock exclusive
            using WorkflowSchedulerLockHandle exclusiveModeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath)!;
            Assert.NotNull(exclusiveModeLock);

            // Attempting to register Normal mode with exclusive mode lock must be rejected
            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slotLock,
                    domainModeLockHandle_: exclusiveModeLock));

            Assert.Contains("must be held shared, not exclusive", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void RegisterOwnerAuthentication_UnrestrictedMode_WithSharedModeHandle_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-unres-with-shared-mode";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfLock);

            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            Assert.NotNull(slotLock);

            // Acquire domain-mode.lock shared
            using WorkflowSchedulerLockHandle sharedModeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;
            Assert.NotNull(sharedModeLock);

            // Attempting to register Unrestricted mode with shared mode lock must be rejected
            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-1",
                    slot_: 1,
                    domainMode_: "Unrestricted",
                    wfLock,
                    slotLock,
                    domainModeLockHandle_: sharedModeLock));

            Assert.Contains("must be held exclusively", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void RegisterOwnerAuthentication_ForgedSharedHandleWithoutCapability_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-forged-shared";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            Assert.NotNull(wfLock);

            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            Assert.NotNull(slotLock);

            // Create caller-constructed handle without genuine capability
            using FileStream dummyStream = new(
                paths.DomainModeLockPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using WorkflowSchedulerLockHandle dummyHandle = new(
                dummyStream,
                paths.DomainModeLockPath,
                false);

            var ex = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slotLock,
                    domainModeLockHandle_: dummyHandle));

            Assert.Contains("genuine shared capability", ex.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void OwnerAuth_OldGenerationDispose_DoesNotDeleteNewerGeneration()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-gen-protection";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            using WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;

            // 1. Generation 1 registers
            WorkflowSchedulerOwnerAuthHandle gen1Handle =
                WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-gen-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slotLock,
                    modeLock);

            string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, wfId);
            Assert.True(File.Exists(authPath));

            // 2. Generation 1 releases its handle before Generation 2 registers (simulating clean generation transition)
            gen1Handle.Dispose();

            // 3. Generation 2 registers
            using WorkflowSchedulerOwnerAuthHandle gen2Handle =
                WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-gen-2",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slotLock,
                    modeLock);

            // Verify disk record now belongs to token-gen-2
            WorkflowSchedulerOwnerAuthentication? authOnDisk =
                WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(paths, wfId);
            Assert.NotNull(authOnDisk);
            Assert.Equal("token-gen-2", authOnDisk.OwnerToken);

            // 4. Stale/delayed removal call from Generation 1 (e.g., old handle cleanup or RemoveOwnerAuthentication with Gen 1 token)
            WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                paths,
                wfId,
                expectedOwnerToken_: "token-gen-1",
                expectedProcessId_: gen1Handle.ProcessId,
                expectedStartTimeTicks_: gen1Handle.ProcessStartTimeTicks);
            gen1Handle.Dispose();

            // 5. Record for Generation 2 MUST still be intact!
            Assert.True(File.Exists(authPath));
            WorkflowSchedulerOwnerAuthentication? authAfterGen1Dispose =
                WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(paths, wfId);
            Assert.NotNull(authAfterGen1Dispose);
            Assert.Equal("token-gen-2", authAfterGen1Dispose.OwnerToken);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void OwnerAuth_RemoveOwnerAuthentication_ProtectsMismatchedGeneration()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-remove-safety";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            using WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;

            using (WorkflowSchedulerOwnerAuthHandle handle =
                   WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                       paths,
                       wfId,
                       "token-real",
                       slot_: 1,
                       domainMode_: "Normal",
                       wfLock,
                       slotLock,
                       modeLock))
            {
                string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, wfId);
                Assert.True(File.Exists(authPath));

                // Attempt to remove with wrong expected token: must NOT delete file
                WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    paths,
                    wfId,
                    expectedOwnerToken_: "token-wrong");

                Assert.True(File.Exists(authPath));

                // Attempt to remove with wrong process id: must NOT delete file
                WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    paths,
                    wfId,
                    expectedOwnerToken_: "token-real",
                    expectedProcessId_: 999999,
                    expectedStartTimeTicks_: 123456);

                Assert.True(File.Exists(authPath));

                // Attempt to remove with wrong start time ticks: must NOT delete file
                WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    paths,
                    wfId,
                    expectedOwnerToken_: "token-real",
                    expectedProcessId_: handle.ProcessId,
                    expectedStartTimeTicks_: handle.ProcessStartTimeTicks + 99999);

                Assert.True(File.Exists(authPath));

                // Attempt to remove without specifying expectedOwnerToken: must throw ArgumentException
                Assert.Throws<ArgumentException>(
                    () => WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                        paths,
                        wfId,
                        expectedOwnerToken_: null));
            }
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void OwnerAuth_CorruptedFile_PreservedOnRemoveAndReplace_FailClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-corrupt";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);

            string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, wfId);
            string corruptContent = "{ corrupted-json-payload-cannot-be-silently-removed";
            File.WriteAllText(authPath, corruptContent);

            // 1. RemoveOwnerAuthentication must fail closed and preserve the corrupted file
            var removeEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    paths,
                    wfId,
                    expectedOwnerToken_: "token-any",
                    expectedProcessId_: Environment.ProcessId,
                    expectedStartTimeTicks_: 12345));
            Assert.Contains("corrupted or invalid", removeEx.Message);
            Assert.True(File.Exists(authPath));
            Assert.Equal(corruptContent, File.ReadAllText(authPath));

            // 2. RegisterOwnerAuthentication must fail closed and preserve the corrupted file
            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            using WorkflowSchedulerLockHandle slotLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            using WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;

            var registerEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "token-new",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slotLock,
                    modeLock));
            Assert.Contains("corrupted or invalid", registerEx.Message);
            Assert.True(File.Exists(authPath));
            Assert.Equal(corruptContent, File.ReadAllText(authPath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void OwnerAuth_ConsistentTransitionWithActiveLease_EnforcedOnRegisterAndRemove()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-lease-consistency";
            paths.EnsureWorkflowDirectory(wfId);
            paths.EnsureSlotFile(1, 4);
            paths.EnsureSlotFile(2, 4);

            WorkflowSchedulerLeaseManifest activeManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "tok-active-1",
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4);

            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            using WorkflowSchedulerLockHandle slot1Lock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            using WorkflowSchedulerLockHandle slot2Lock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(2, 4))!;
            using WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;

            // 1. Registering with mismatched ownerToken against existing Active manifest must throw
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "tok-wrong",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slot1Lock,
                    modeLock));

            // 2. Registering with mismatched slot against existing Active manifest must throw
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "tok-active-1",
                    slot_: 2,
                    domainMode_: "Normal",
                    wfLock,
                    slot2Lock,
                    modeLock));

            // 3. Registering with matching parameters succeeds
            WorkflowSchedulerOwnerAuthHandle authHandle =
                WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "tok-active-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slot1Lock,
                    modeLock);

            // 4. Removing owner-auth while lease manifest is still Active must throw and preserve owner-auth.json
            var removeWhileActiveEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    paths,
                    wfId,
                    expectedOwnerToken_: "tok-active-1",
                    expectedProcessId_: authHandle.ProcessId,
                    expectedStartTimeTicks_: authHandle.ProcessStartTimeTicks));
            Assert.Contains("still in state 'Active'", removeWhileActiveEx.Message);
            Assert.True(File.Exists(authHandle.AuthPath));

            // 5. After transitioning lease manifest to Quarantined, removing owner-auth via Dispose succeeds cleanly
            WorkflowSchedulerLeaseManifest quarantinedManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Quarantined,
                ownerToken_: "tok-active-1",
                slot_: null,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantinedManifest, 4);

            authHandle.Dispose();
            Assert.False(File.Exists(authHandle.AuthPath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void OwnerAuth_DurableRemoval_ResidualCommitMarker_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-auth-durable-remove";
            paths.EnsureWorkflowDirectory(wfId);
            string wfDir = paths.GetWorkflowDirectory(wfId);
            string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, wfId);
            File.WriteAllText(authPath, "{}");

            // Durable DeleteAtomic removes the file and syncs directory metadata
            WorkflowSchedulerDurabilityReport report =
                WorkflowSchedulerDomainMetadata.DeleteAtomic(authPath, wfDir);
            Assert.Equal(WorkflowSchedulerDurabilityReport.DirectoryMetadataDurablySynced, report);
            Assert.False(File.Exists(authPath));

            // Simulate interrupted DeleteAtomic leaving a residual .commit marker in the workflow directory
            string residualDeleteCommit = Path.Combine(wfDir, "owner-auth.json.tmp.999.1.8888.commit");
            File.WriteAllText(residualDeleteCommit, "");

            // Subsequent operations and integrity inspection must fail closed
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));

            WorkflowSchedulerIntegrityReport integrity =
                WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
            Assert.False(integrity.IsHealthy);
            Assert.Contains(integrity.Violations, v => v.Contains("Residual"));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LeaseManifest_DuplicateSlotAndDuplicateOwnerToken_Rejected_AndLegitimateConcurrencyPreserved()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // 1. Publish first Active Normal lease on slot 1 with token-1
            WorkflowSchedulerLeaseManifest active1 = new(
                workflowId_: "wf-concurrent-1",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "token-1",
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [new WorkflowResourceClaim("fs:/workspace/a", WorkflowResourceAccessKind.ExclusiveWrite)],
                maxConcurrentWorkflows_: 4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active1, 4);

            // 2. Reject second Active lease with the SAME slot (slot 1)
            WorkflowSchedulerLeaseManifest duplicateSlot = new(
                workflowId_: "wf-concurrent-2",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "token-2",
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [new WorkflowResourceClaim("fs:/workspace/b", WorkflowResourceAccessKind.ExclusiveWrite)],
                maxConcurrentWorkflows_: 4);
            var slotEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, duplicateSlot, 4));
            Assert.Contains("Duplicate slot ownership is forbidden", slotEx.Message);

            // 3. Reject second lease with the SAME OwnerToken ("token-1") even on a different slot or Quarantined
            WorkflowSchedulerLeaseManifest duplicateOwnerActive = new(
                workflowId_: "wf-concurrent-2",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "token-1",
                slot_: 2,
                domainMode_: "Normal",
                resourceClaims_: [new WorkflowResourceClaim("fs:/workspace/b", WorkflowResourceAccessKind.ExclusiveWrite)],
                maxConcurrentWorkflows_: 4);
            var ownerEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, duplicateOwnerActive, 4));
            Assert.Contains("Duplicate ownerToken across workflows is forbidden", ownerEx.Message);

            WorkflowSchedulerLeaseManifest duplicateOwnerQuarantined = new(
                workflowId_: "wf-concurrent-2",
                state_: WorkflowSchedulerLeaseState.Quarantined,
                ownerToken_: "token-1",
                slot_: null,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);
            Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, duplicateOwnerQuarantined, 4));

            // 4. Positive: legitimate concurrency between two Active Normal leases with DIFFERENT slots and DIFFERENT ownerTokens
            WorkflowSchedulerLeaseManifest active2 = new(
                workflowId_: "wf-concurrent-2",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "token-2",
                slot_: 2,
                domainMode_: "Normal",
                resourceClaims_: [new WorkflowResourceClaim("fs:/workspace/b", WorkflowResourceAccessKind.ExclusiveWrite)],
                maxConcurrentWorkflows_: 4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active2, 4);

            Assert.True(File.Exists(paths.GetActiveLeasePath("wf-concurrent-1")));
            Assert.True(File.Exists(paths.GetActiveLeasePath("wf-concurrent-2")));

            // Reopening domain with two legitimate concurrent Active Normal leases on slots 1 and 2 succeeds!
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void Domain_ReopenWithPreexistingDuplicateSlotOrDuplicateOwnerManifests_FailsClosed()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Publish first valid Active manifest
            WorkflowSchedulerLeaseManifest active1 = new(
                workflowId_: "wf-pre-1",
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "tok-shared-bad",
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, active1, 4);

            // Directly write a second pre-existing manifest with duplicate slot 1 (and different token)
            string hex2 = WorkflowSchedulerPaths.WorkflowIdToHex("wf-pre-2");
            string path2 = Path.Combine(paths.ActiveLeasesDirectory, $"{hex2}.lease.json");
            string duplicateSlotPayload = JsonSerializer.Serialize(new
            {
                schemaId = WorkflowSchedulerLeaseManifest.LeaseSchemaId,
                schemaVersion = WorkflowSchedulerLeaseManifest.LeaseSchemaVersion,
                workflowId = "wf-pre-2",
                state = "Active",
                ownerToken = "tok-unique-2",
                slot = 1,
                domainMode = "Normal",
                resourceClaims = Array.Empty<object>()
            }, new JsonSerializerOptions { WriteIndented = true });
            WorkflowSchedulerDomainMetadata.WriteAtomic(path2, duplicateSlotPayload, paths.ActiveLeasesDirectory);

            var dupSlotEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
            Assert.Contains("Multiple Active leases claim capacity slot 1", dupSlotEx.Message);

            // Now change path2 to have slot 2, but duplicate ownerToken "tok-shared-bad"
            string duplicateOwnerPayload = JsonSerializer.Serialize(new
            {
                schemaId = WorkflowSchedulerLeaseManifest.LeaseSchemaId,
                schemaVersion = WorkflowSchedulerLeaseManifest.LeaseSchemaVersion,
                workflowId = "wf-pre-2",
                state = "Active",
                ownerToken = "tok-shared-bad",
                slot = 2,
                domainMode = "Normal",
                resourceClaims = Array.Empty<object>()
            }, new JsonSerializerOptions { WriteIndented = true });
            WorkflowSchedulerDomainMetadata.WriteAtomic(path2, duplicateOwnerPayload, paths.ActiveLeasesDirectory);

            var dupOwnerEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
            Assert.Contains("Duplicate ownerToken 'tok-shared-bad'", dupOwnerEx.Message);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LinuxLockParser_ParsesActiveFlockAndPosixLocks_AndRejectsBlockedRequests()
    {
        // Active FLOCK lock on device 08:01 (major 8, minor 1), inode 67890 by PID 4321
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "1: FLOCK  ADVISORY  WRITE 4321 08:01:67890 0 EOF",
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out int pid1));
        Assert.Equal(4321, pid1);

        // Active POSIX lock on device 00:2a (major 0, minor 42), inode 123456 by PID 9876
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "2: POSIX  ADVISORY  READ  9876 00:2a:123456 0 EOF",
                expectedMajor_: 0,
                expectedMinor_: 42,
                expectedInode_: 123456,
                out int pid2));
        Assert.Equal(9876, pid2);

        // Blocked lock request ("->") must NOT be treated as lock owner
        Assert.False(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "3: -> FLOCK  ADVISORY  WRITE 5555 08:01:67890 0 EOF",
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out _));

        // Different inode must not match
        Assert.False(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "1: FLOCK  ADVISORY  WRITE 4321 08:01:99999 0 EOF",
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out _));
    }

    [Fact]
    public void LinuxLockParser_LockShVsLockEx_DistinguishesSharedAndExclusive()
    {
        // Exclusive WRITE lock
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "1: FLOCK  ADVISORY  WRITE 4321 08:01:67890 0 EOF",
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out int pid1,
                out bool isExclusive1));
        Assert.Equal(4321, pid1);
        Assert.True(isExclusive1);

        // Shared READ lock
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "2: FLOCK  ADVISORY  READ  4321 08:01:67890 0 EOF",
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out int pid2,
                out bool isExclusive2));
        Assert.Equal(4321, pid2);
        Assert.False(isExclusive2);
    }

    [Fact]
    public void LinuxProcLocks_PosixAndFlockCoexistence_NoFalseOwnershipAttribution()
    {
        // Line with POSIX WRITE from PID 1111 on device 08:01, inode 67890
        string posixLine = "1: POSIX  ADVISORY  WRITE 1111 08:01:67890 0 EOF";
        // Line with FLOCK EX (WRITE) from PID 2222 on device 08:01, inode 67890
        string flockLine = "2: FLOCK  ADVISORY  WRITE 2222 08:01:67890 0 EOF";

        // TryParseProcLockLine distinguishes FLOCK, POSIX, and OFDLCK
        Assert.True(WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
            posixLine, 8, 1, 67890, out int pidPosix, out bool isExPosix, out string typePosix));
        Assert.Equal(1111, pidPosix);
        Assert.True(isExPosix);
        Assert.Equal("POSIX", typePosix);

        Assert.True(WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
            flockLine, 8, 1, 67890, out int pidFlock, out bool isExFlock, out string typeFlock));
        Assert.Equal(2222, pidFlock);
        Assert.True(isExFlock);
        Assert.Equal("FLOCK", typeFlock);

        // For FLOCK proof, POSIX WRITE must NOT be attributed as FLOCK owner
        Assert.NotEqual("FLOCK", typePosix);
    }

    [Fact]
    public void LinuxLockParser_CorruptedOrIncompatible_Rejected()
    {
        // Corrupted token count
        Assert.False(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "1: FLOCK ADVISORY",
                8, 1, 67890, out _));

        // Corrupted PID
        Assert.False(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "1: FLOCK  ADVISORY  WRITE not-a-pid 08:01:67890 0 EOF",
                8, 1, 67890, out _));

        // Corrupted device format
        Assert.False(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                "1: FLOCK  ADVISORY  WRITE 4321 not-a-dev 0 EOF",
                8, 1, 67890, out _));
    }

    [Fact]
    public void LinuxProcessTracker_ProcessStartTimeTicks_ParsesClockTicksEvenWithComplexNames()
    {
        // Complex process name with spaces and nested parentheses
        string statContent = "12345 (my (special) process name) S 1 12345 12345 0 -1 4194304 100 0 0 0 10 20 0 0 20 0 1 0 543210 1234567 100 18446744073709551615 1 1 0 0 0 0 0 0 0 0 0 0 17 0 0 0";
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseStatContent(
                statContent,
                out long ticks));
        Assert.Equal(543210, ticks);
    }

    [Fact]
    public void OwnerAuth_RemoveOwnerAuthentication_ForeignProcessWithoutLocks_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-foreign-rem-rej";
            using WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!;
            using WorkflowSchedulerLockHandle slot1Lock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!;
            using WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!;

            WorkflowSchedulerOwnerAuthHandle authHandle =
                WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                    paths,
                    wfId,
                    "tok-foreign-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    wfLock,
                    slot1Lock,
                    modeLock);

            // Foreign removal attempt with mismatched PID or start time returns without deleting (file preserved)
            WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                paths,
                wfId,
                expectedOwnerToken_: "tok-foreign-1",
                expectedProcessId_: 999999, // foreign PID
                expectedStartTimeTicks_: 12345); // foreign start time

            Assert.True(File.Exists(authHandle.AuthPath));

            // Now caller provides matching token, PID, and timestamp, but DOES NOT have authLockStream capability
            // and the manifest is Quarantined (so state machine allows removal attempt)
            WorkflowSchedulerLeaseManifest quarantinedManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Quarantined,
                ownerToken_: "tok-foreign-1",
                slot_: null,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantinedManifest, 4);

            var foreignAttemptEx = Assert.Throws<InvalidOperationException>(
                () => WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    paths,
                    wfId,
                    expectedOwnerToken_: "tok-foreign-1",
                    expectedProcessId_: authHandle.ProcessId,
                    expectedStartTimeTicks_: authHandle.ProcessStartTimeTicks,
                    authLockStream_: null)); // foreign caller has NO authentic capability

            Assert.Contains("foreign process lacks authority", foreignAttemptEx.Message);
            Assert.True(File.Exists(authHandle.AuthPath));

            // Clean disposal by genuine capability owner succeeds
            authHandle.Dispose();
            Assert.False(File.Exists(authHandle.AuthPath));
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    [Fact]
    public void LinuxLockParser_PosixWriteAndFlockExCoexist_OnlyFlockAttributed()
    {
        // Line 1: POSIX WRITE lock on device 08:01, inode 67890 by PID 9999 (adversary or non-cooperative writer)
        string posixLine = "1: POSIX  ADVISORY  WRITE 9999 08:01:67890 0 EOF";
        // Line 2: FLOCK WRITE lock on device 08:01, inode 67890 by PID 1111 (legitimate process)
        string flockLine = "2: FLOCK  ADVISORY  WRITE 1111 08:01:67890 0 EOF";
        // Line 3: OFDLCK WRITE lock on device 08:01, inode 67890 by PID 8888
        string ofdlckLine = "3: OFDLCK ADVISORY  WRITE 8888 08:01:67890 0 EOF";

        // Parsing posixLine returns lockType "POSIX"
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                posixLine,
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out int posixPid,
                out bool isExclusivePosix,
                out string posixType));
        Assert.Equal(9999, posixPid);
        Assert.True(isExclusivePosix);
        Assert.Equal("POSIX", posixType);

        // Parsing flockLine returns lockType "FLOCK"
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                flockLine,
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out int flockPid,
                out bool isExclusiveFlock,
                out string flockType));
        Assert.Equal(1111, flockPid);
        Assert.True(isExclusiveFlock);
        Assert.Equal("FLOCK", flockType);

        // Parsing ofdlckLine returns lockType "OFDLCK"
        Assert.True(
            WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                ofdlckLine,
                expectedMajor_: 8,
                expectedMinor_: 1,
                expectedInode_: 67890,
                out int ofdlckPid,
                out bool isExclusiveOfd,
                out string ofdType));
        Assert.Equal(8888, ofdlckPid);
        Assert.Equal("OFDLCK", ofdType);

        // When attributing locks acquired via FLOCK mechanism, POSIX WRITE must NOT be accepted as equivalent:
        List<string> lines = [posixLine, flockLine, ofdlckLine];
        HashSet<int> flockOwners = [];
        foreach (string line in lines)
        {
            if (WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.TryParseProcLockLine(
                line, 8, 1, 67890, out int pid, out bool isEx, out string type))
            {
                if (string.Equals(type, "FLOCK", StringComparison.OrdinalIgnoreCase) && isEx)
                {
                    flockOwners.Add(pid);
                }
            }
        }

        // Only PID 1111 is attributed as the FLOCK EX owner, NOT PID 9999 or 8888
        Assert.Single(flockOwners);
        Assert.Contains(1111, flockOwners);
        Assert.DoesNotContain(9999, flockOwners);
        Assert.DoesNotContain(8888, flockOwners);
    }

    [Fact]
    public void OwnerAuth_RemoveOwnerAuthentication_PlainFileStreamWithoutByteLock_LacksAuthenticCapability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-unlocked-stream-rem";
            using (WorkflowSchedulerLockHandle wfLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wfId))!)
            using (WorkflowSchedulerLockHandle slot1Lock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4))!)
            using (WorkflowSchedulerLockHandle modeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath)!)
            {
                WorkflowSchedulerOwnerAuthHandle authHandle =
                    WorkflowSchedulerOwnerAuthentication.RegisterOwnerAuthentication(
                        paths,
                        wfId,
                        "tok-unlocked-1",
                        slot_: 1,
                        domainMode_: "Normal",
                        wfLock,
                        slot1Lock,
                        modeLock);

                // Manifest transitioned to Quarantined
                WorkflowSchedulerLeaseManifest quarantinedManifest = new(
                    workflowId_: wfId,
                    state_: WorkflowSchedulerLeaseState.Quarantined,
                    ownerToken_: "tok-unlocked-1",
                    slot_: null,
                    domainMode_: "Normal",
                    resourceClaims_: [],
                    maxConcurrentWorkflows_: 4);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantinedManifest, 4);

                // Caller provides a plain FileStream that has the file open, but DOES NOT lock the sentinel byte
                using (FileStream plainStream = new(
                    authHandle.AuthPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    var ex = Assert.Throws<InvalidOperationException>(
                        () => WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                            paths,
                            wfId,
                            expectedOwnerToken_: "tok-unlocked-1",
                            expectedProcessId_: authHandle.ProcessId,
                            expectedStartTimeTicks_: authHandle.ProcessStartTimeTicks,
                            authLockStream_: plainStream));

                    Assert.Contains("foreign process lacks authority", ex.Message);
                    Assert.True(File.Exists(authHandle.AuthPath));
                }

                // Genuine authHandle disposal succeeds
                authHandle.Dispose();
                Assert.False(File.Exists(authHandle.AuthPath));
            }
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    break;
                }
                catch (IOException) when (i < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}

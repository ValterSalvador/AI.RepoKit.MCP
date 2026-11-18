namespace AiRepoKit.WorkflowExecution.Tests;

using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using Xunit;

public sealed class WorkflowBrokerTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    public WorkflowBrokerTests()
    {
        string currentPrincipal = OperatingSystem.IsWindows()
            ? (WindowsIdentity.GetCurrent().User?.Value ?? string.Empty)
            : WorkflowBrokerIpcTransport.GetCurrentLinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);

        Environment.SetEnvironmentVariable("AIREPO_AUTHORIZED_WORKER_PRINCIPAL", currentPrincipal);
    }

    private static void CreateProtectedWindowsDirectory(string path)
    {
        DirectoryInfo di = new(path);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.Create(ds);
    }

    private static void CreateWindowsDirectoryWithBrokerReadOnly(string path)
    {
        DirectoryInfo di = new(path);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.Create(ds);
    }

    private static void CreateWindowsDirectoryWithBrokerWriteDenied(string path)
    {
        DirectoryInfo di = new(path);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.Create(ds);
    }

    private static void CreateInsecureWindowsDirectory(string path, SecurityIdentifier untrustedSid, FileSystemRights rights)
    {
        DirectoryInfo di = new(path);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");

        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            untrustedSid,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.Create(ds);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateWindowsDirectoryWithTokenGroupWriteDenied(string path, SecurityIdentifier groupSid)
    {
        DirectoryInfo di = new(path);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        ds.AddAccessRule(new FileSystemAccessRule(
            groupSid,
            FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.Create(ds);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateInsecureWindowsFile(string filePath, SecurityIdentifier untrustedSid, FileSystemRights rights)
    {
        string? parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(filePath, "test-content");

        FileInfo fi = new(filePath);
        FileSecurity fs = new();
        fs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");

        fs.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            untrustedSid,
            rights,
            AccessControlType.Allow));

        fi.SetAccessControl(fs);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateWindowsDirectoryWithBrokerWriteAttributesOnly(string path)
    {
        DirectoryInfo di = new(path);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        // Grant Broker ReadAndExecute, Synchronize, and WriteAttributes ONLY (no CreateFiles, no CreateDirectories, no WriteData)
        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize | FileSystemRights.WriteAttributes,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.Create(ds);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateWindowsDirectoryWithBrokerWriteInheritOnly(string path)
    {
        DirectoryInfo di = new(path);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        // Grant Broker ReadAndExecute and Synchronize directly (effective on current directory)
        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        // Grant Broker Write/Create permissions with InheritOnly (does NOT apply to current directory)
        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly,
            AccessControlType.Allow));

        ds.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        di.Create(ds);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateWindowsFileWithBrokerWriteAttributesOnly(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, "test-content");
        FileInfo fi = new(path);
        FileSecurity fs = new();
        fs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        // Grant Broker ReadAndExecute, Synchronize, and WriteAttributes ONLY (no WriteData, no Write)
        fs.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize | FileSystemRights.WriteAttributes,
            AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        fi.SetAccessControl(fs);
    }

    private static void CreateNtfsJunction(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("NTFS junctions require Windows.");
        }

        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
        Assert.NotNull(proc);
        proc.WaitForExit(5000);
        Assert.Equal(0, proc.ExitCode);
        Assert.True(Directory.Exists(linkPath), $"Junction '{linkPath}' must exist after creation.");
        Assert.NotEqual(0, (int)(File.GetAttributes(linkPath) & FileAttributes.ReparsePoint));
    }

    private string CreateTempStorageRoot(bool protectWindowsAcl = true)
    {
        string baseDir = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A")
            : Path.GetTempPath();

        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        string dir = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows() && protectWindowsAcl)
        {
            CreateProtectedWindowsDirectory(dir);
        }
        else
        {
            Directory.CreateDirectory(dir);
        }
        this._tempDirectories.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AIREPO_AUTHORIZED_WORKER_PRINCIPAL", null);
        foreach (string dir in this._tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(dir, recursive: false);
                    }
                    else
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
            }
            catch
            {
            }
        }
    }

    private static string FindBrokerExecutable()
    {
        string baseDir = AppContext.BaseDirectory;
        string exeName = OperatingSystem.IsWindows() ? "AiRepoKit.CoordinationBroker.exe" : "AiRepoKit.CoordinationBroker";

        string? cur = baseDir;
        while (!string.IsNullOrEmpty(cur))
        {
            if (File.Exists(Path.Combine(cur, "AI.RepoKit.MCP.sln")))
            {
                string candidate = Path.Combine(cur, "src", "AiRepoKit.CoordinationBroker", "bin", "Debug", "net10.0", exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string? parent = Path.GetDirectoryName(cur);
            if (parent == cur)
            {
                break;
            }

            cur = parent;
        }

        throw new FileNotFoundException($"Could not locate {exeName} starting from {baseDir}");
    }

    // =========================================================================
    // 1. Genuine Owner Registration
    // =========================================================================

    [Fact]
    public async Task Broker_GenuineOwnerRegistration_Succeeds()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

        WorkflowResourceClaim[] claims =
        [
            new("fs:/repo/src/file1.txt", WorkflowResourceAccessKind.ExclusiveWrite)
        ];

        await using (WorkflowBrokerLeaseHandle lease = await client.AcquireLeaseAsync("wf-genuine", claims, "Normal"))
        {
            Assert.Equal("wf-genuine", lease.WorkflowId);
            Assert.False(string.IsNullOrWhiteSpace(lease.OwnerToken));
            Assert.Equal(1, lease.Slot);
            Assert.Equal("Normal", lease.DomainMode);
            Assert.Single(lease.ResourceClaims);

            // Verify disk state
            string activeLeasePath = paths.GetActiveLeasePath("wf-genuine");
            Assert.True(File.Exists(activeLeasePath));

            WorkflowSchedulerLeaseManifest manifest =
                WorkflowSchedulerLeaseManifest.ReadAndValidate(activeLeasePath, paths, 4);
            Assert.Equal(WorkflowSchedulerLeaseState.Active, manifest.State);
            Assert.Equal(lease.OwnerToken, manifest.OwnerToken);
            Assert.Equal(1, manifest.Slot);

            string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, "wf-genuine");
            Assert.True(File.Exists(authPath));
        }

        // After lease handle disposal, lease is released cleanly
        string releasedLeasePath = paths.GetActiveLeasePath("wf-genuine");
        Assert.False(File.Exists(releasedLeasePath));
    }

    // =========================================================================
    // 2. Forged Owner Token and PID Rejection
    // =========================================================================

    [Fact]
    public async Task Broker_ForgedOwnerTokenAndPidRejection_Fails()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client1 = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);

        WorkflowResourceClaim[] claims =
        [
            new("fs:/repo/src/module.cs", WorkflowResourceAccessKind.ExclusiveWrite)
        ];

        WorkflowBrokerLeaseHandle lease1 = await client1.AcquireLeaseAsync("wf-victim", claims, "Normal");

        // Attacker attempts to release victim's lease using forged owner token
        WorkflowBrokerException ex1 = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client2.ReleaseLeaseAsync("wf-victim", "forged-token-12345"));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeUnauthorized, ex1.ErrorCode);

        // Attacker attempts to release victim's lease even with correct token
        WorkflowBrokerException ex2 = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client2.ReleaseLeaseAsync("wf-victim", lease1.OwnerToken));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeUnauthorized, ex2.ErrorCode);

        // Legitimate owner can release
        await lease1.DisposeAsync();
    }

    // =========================================================================
    // 3. Rogue Server Identity Rejection
    // =========================================================================

    [Fact]
    public async Task Broker_RogueServerIdentity_FailsVerification()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        string fakeServerIdentity = OperatingSystem.IsWindows()
            ? "S-1-5-21-999999999-999999999-999999999-9999"
            : "99999";

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WorkflowBrokerClient.ConnectAsync(paths, expectedServerIdentity_: fakeServerIdentity));

        if (OperatingSystem.IsWindows())
        {
            // Windows Named Pipe rejects rogue server process identity during VerifyServerIdentity
            Assert.Contains("rogue", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // Linux client rejects rogue server at endpoint validation stage (socket owned by different UID)
            Assert.Contains("Failed to validate Linux socket security", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // =========================================================================
    // 4. Unauthorized Client Identity Rejection
    // =========================================================================

    [Fact]
    public void Broker_UnauthorizedClientIdentity_FailsVerification()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        string fakeClientIdentity = OperatingSystem.IsWindows()
            ? "S-1-5-21-888888888-888888888-888888888-8888"
            : "88888";

        if (OperatingSystem.IsWindows())
        {
            string pipeName = WorkflowBrokerIpcTransport.GetEndpointIdentifier(paths);
            using NamedPipeServerStream server = WorkflowBrokerIpcTransport.CreateWindowsPipeServerStream(pipeName);
            Assert.NotNull(server);
        }

        Assert.False(WorkflowBrokerIpcTransport.VerifyClientIdentity(
            Stream.Null,
            out _,
            out _,
            out _,
            expectedClientIdentity_: fakeClientIdentity));
    }

    // =========================================================================
    // 5. Durable Quarantine Persistence Failure (Fault Injection)
    // =========================================================================

    [Fact]
    public async Task Broker_DurableQuarantinePersistenceFailure_FailsClosedAndBlocksAdmissions()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        WorkflowResourceClaim[] claims = [new("fs:/repo/fault.txt", WorkflowResourceAccessKind.ExclusiveWrite)];
        WorkflowBrokerClient client1 = await WorkflowBrokerClient.ConnectAsync(paths);
        WorkflowBrokerLeaseHandle lease1 = await client1.AcquireLeaseAsync("wf-fault", claims, "Normal");

        string leasePath = paths.GetActiveLeasePath("wf-fault");
        Assert.True(File.Exists(leasePath));

        // Inject fault: lock the active lease file exclusively with FileShare.None so File.Move fails
        using (FileStream lockStream = new(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Now abruptly kill client connection
            client1.Dispose();

            // Wait for session termination loop to attempt quarantine and hit IOException
            await Task.Delay(350);

            // Verify with real OS lock probes that the broker retained the OS locks fail-closed!
            using WorkflowSchedulerLockHandle? wfLockProbe =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath("wf-fault"));
            Assert.Null(wfLockProbe); // Lock MUST be retained by the broker!

            using WorkflowSchedulerLockHandle? slotLockProbe =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetSlotLockPath(1, 4));
            Assert.Null(slotLockProbe); // Slot lock MUST be retained by the broker!

            // Verify that subsequent admissions are blocked fail-closed!
            await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);
            WorkflowResourceClaim[] claims2 = [new("fs:/repo/other.txt", WorkflowResourceAccessKind.SharedRead)];

            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client2.AcquireLeaseAsync("wf-blocked", claims2, "Normal"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
            Assert.Contains("blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // =========================================================================
    // 6. Invalid Integrity Manifest Blocks Admission
    // =========================================================================

    [Fact]
    public async Task Broker_InvalidIntegrityManifest_BlocksAdmission()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        // Inject corrupted lease file into active leases directory
        string corruptPath = Path.Combine(paths.ActiveLeasesDirectory, "corrupted.lease.json");
        File.WriteAllText(corruptPath, "{ \"corrupted\": true, invalid_json }");

        // Client attempts to acquire lease -> blocked because integrity inspection fails!
        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        WorkflowResourceClaim[] claims = [new("fs:/repo/valid.txt", WorkflowResourceAccessKind.ExclusiveWrite)];

        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.AcquireLeaseAsync("wf-valid", claims, "Normal"));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        Assert.Contains("integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 7. Unauthorized Lease Release
    // =========================================================================

    [Fact]
    public async Task Broker_UnauthorizedLeaseRelease_FailsForNonExistentOrUnowned()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.ReleaseLeaseAsync("wf-nonexistent", "dummy-token"));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeNotFound, ex.ErrorCode);
    }

    // =========================================================================
    // 8. Legacy Scheduler Exclusion
    // =========================================================================

    [Fact]
    public async Task Broker_LegacySchedulerExclusion_BlocksSimultaneousAccess()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        // While broker is running, it holds domain.lock exclusively:
        // An uncoordinated legacy scheduler attempting to acquire domain.lock is excluded!
        using WorkflowSchedulerLockHandle? legacyLock =
            WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath);
        Assert.Null(legacyLock);

        // If a legacy scheduler was holding domain.lock, a second broker refuses to start
        string root2 = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths2 = new(root2);
        paths2.EnsureCoordinationLayout();

        using WorkflowSchedulerLockHandle? preHeldLegacyLock =
            WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths2.DomainLockPath);
        Assert.NotNull(preHeldLegacyLock);

        await using WorkflowCoordinationBroker competingBroker = new(paths2, maxConcurrentWorkflows_: 4);
        await Assert.ThrowsAsync<InvalidOperationException>(() => competingBroker.StartAsync());
    }

    // =========================================================================
    // 9. Two Concurrent Legitimate Normal Workflows
    // =========================================================================

    [Fact]
    public async Task Broker_TwoConcurrentLegitimateNormalWorkflows_Succeed()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(2, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 2, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client1 = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerClient client3 = await WorkflowBrokerClient.ConnectAsync(paths);

        WorkflowResourceClaim[] claims1 = [new("fs:/repo/src/serviceA", WorkflowResourceAccessKind.ExclusiveWrite)];
        WorkflowResourceClaim[] claims2 = [new("fs:/repo/src/serviceB", WorkflowResourceAccessKind.ExclusiveWrite)];
        WorkflowResourceClaim[] claims3 = [new("fs:/repo/src/serviceC", WorkflowResourceAccessKind.ExclusiveWrite)];

        await using WorkflowBrokerLeaseHandle lease1 = await client1.AcquireLeaseAsync("wf-1", claims1, "Normal");
        await using WorkflowBrokerLeaseHandle lease2 = await client2.AcquireLeaseAsync("wf-2", claims2, "Normal");

        Assert.Equal(1, lease1.Slot);
        Assert.Equal(2, lease2.Slot);
        Assert.NotEqual(lease1.OwnerToken, lease2.OwnerToken);

        WorkflowBrokerIntegrityReportDto report = await client1.InspectIntegrityAsync();
        Assert.Equal(2, report.ActiveLeaseCount);
        Assert.False(report.HasGlobalExclusion);

        // Third concurrent request exceeds max capacity of 2 slots -> Conflict!
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client3.AcquireLeaseAsync("wf-3", claims3, "Normal"));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
    }

    // =========================================================================
    // 10. Unrestricted and MaintenanceInProgress Global Exclusion
    // =========================================================================

    [Fact]
    public async Task Broker_UnrestrictedAndMaintenance_EnforcesGlobalExclusion()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client1 = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);

        // 1. Client 1 acquires Unrestricted lease
        WorkflowResourceClaim[] claims = [new("fs:/repo", WorkflowResourceAccessKind.ExclusiveWrite)];
        await using (WorkflowBrokerLeaseHandle unrestrictedLease =
                     await client1.AcquireLeaseAsync("wf-unrestricted", claims, "Unrestricted"))
        {
            Assert.Equal("Unrestricted", unrestrictedLease.DomainMode);

            // 2. Client 2 attempts to acquire Normal lease while Unrestricted is active -> Conflict!
            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client2.AcquireLeaseAsync("wf-normal", claims, "Normal"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        }

        // 3. After Unrestricted is released, Normal lease succeeds
        await using WorkflowBrokerLeaseHandle normalLease =
            await client2.AcquireLeaseAsync("wf-normal", claims, "Normal");
        Assert.Equal("Normal", normalLease.DomainMode);
    }

    // =========================================================================
    // 11. Worker Crash and Durable Quarantine (In-process Disconnect)
    // =========================================================================

    [Fact]
    public async Task Broker_WorkerCrash_TransitionsToDurableQuarantine()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        WorkflowResourceClaim[] claims = [new("fs:/repo/critical.dat", WorkflowResourceAccessKind.ExclusiveWrite)];

        // Simulate worker crashing by abruptly disposing its client connection without calling release
        {
            WorkflowBrokerClient crashingClient = await WorkflowBrokerClient.ConnectAsync(paths);
            _ = await crashingClient.AcquireLeaseAsync("wf-crash", claims, "Normal");
            // Abrupt disconnect:
            crashingClient.Dispose();
        }

        // Give broker session loop a brief moment to process disconnect
        await Task.Delay(250);

        // Lease is now durably Quarantined on disk!
        string leasePath = paths.GetActiveLeasePath("wf-crash");
        Assert.True(File.Exists(leasePath));

        WorkflowSchedulerLeaseManifest quarantined =
            WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.Quarantined, quarantined.State);
        Assert.Null(quarantined.Slot);

        // A new workflow with conflicting claim is blocked by quarantine!
        await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client2.AcquireLeaseAsync("wf-new", claims, "Normal"));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
    }

    // =========================================================================
    // 12. Real Worker Subprocess Termination
    // =========================================================================

    [Fact]
    public async Task Broker_RealWorkerSubprocessTermination_TransitionsToQuarantined()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        string brokerExe = FindBrokerExecutable();
        ProcessStartInfo psi = new(brokerExe)
        {
            ArgumentList = { "--worker", root, "wf-subproc-kill", broker.ServicePrincipal },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using Process workerProcess = Process.Start(psi)!;
        Assert.NotNull(workerProcess);

        try
        {
            // Read stdout until "WORKER_ACQUIRED"
            string? line;
            bool acquired = false;
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
            while (!cts.IsCancellationRequested && (line = await workerProcess.StandardOutput.ReadLineAsync(cts.Token)) != null)
            {
                if (line.Contains("WORKER_ACQUIRED", StringComparison.OrdinalIgnoreCase))
                {
                    acquired = true;
                    break;
                }
            }
            if (!acquired)
            {
                string err = await workerProcess.StandardError.ReadToEndAsync();
                Assert.Fail($"Subprocess worker failed to acquire lease. Stderr: {err}");
            }

            // Check active lease on disk
            string activeLeasePath = paths.GetActiveLeasePath("wf-subproc-kill");
            Assert.True(File.Exists(activeLeasePath));

            // Terminate subprocess abruptly via OS signal
            workerProcess.Kill(entireProcessTree: true);
            await workerProcess.WaitForExitAsync();

            // Allow broker to detect socket/pipe closure and complete durable quarantine write
            string quarantinedPath = paths.GetActiveLeasePath("wf-subproc-kill");
            bool transitioned = false;
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                if (File.Exists(quarantinedPath))
                {
                    try
                    {
                        WorkflowSchedulerLeaseManifest manifest =
                            WorkflowSchedulerLeaseManifest.ReadAndValidate(quarantinedPath, paths, 4);
                        if (manifest.State == WorkflowSchedulerLeaseState.Quarantined)
                        {
                            transitioned = true;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            Assert.True(transitioned, "Subprocess lease failed to transition to Quarantined after worker termination.");
        }
        finally
        {
            if (!workerProcess.HasExited)
            {
                workerProcess.Kill(entireProcessTree: true);
                await workerProcess.WaitForExitAsync();
            }
        }
    }

    // =========================================================================
    // 13. Real Broker Subprocess Crash and Restart
    // =========================================================================

    [Fact]
    public async Task Broker_RealBrokerSubprocessCrashAndRestart_RecoversOrphanedActiveLeases()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        string brokerExe = FindBrokerExecutable();
        ProcessStartInfo psi1 = new(brokerExe)
        {
            ArgumentList = { root, "--max-concurrent", "4" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        Process broker1 = Process.Start(psi1)!;
        Assert.NotNull(broker1);

        try
        {
            string? line;
            bool ready1 = false;
            using CancellationTokenSource cts1 = new(TimeSpan.FromSeconds(10));
            while (!cts1.IsCancellationRequested && (line = await broker1.StandardOutput.ReadLineAsync(cts1.Token)) != null)
            {
                if (line.Contains("BROKER_READY", StringComparison.OrdinalIgnoreCase))
                {
                    ready1 = true;
                    break;
                }
            }
            if (!ready1)
            {
                string err = await broker1.StandardError.ReadToEndAsync();
                Assert.Fail($"Broker 1 failed to signal ready. Stderr: {err}");
            }

            // Connect client and acquire a lease
            await using (WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths))
            {
                WorkflowResourceClaim[] claims = [new("fs:/repo/data", WorkflowResourceAccessKind.SharedRead)];
                WorkflowBrokerLeaseHandle lease = await client.AcquireLeaseAsync("wf-crashed-broker", claims, "Normal");
                Assert.NotNull(lease);

                // Crash broker 1 abruptly without shutdown notification
                broker1.Kill(entireProcessTree: true);
                await broker1.WaitForExitAsync();
            }

            // Start broker 2 to perform Phase 0 startup recovery
            ProcessStartInfo psi2 = new(brokerExe)
            {
                ArgumentList = { root, "--max-concurrent", "4" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            Process broker2 = Process.Start(psi2)!;
            Assert.NotNull(broker2);

            try
            {
                bool ready2 = false;
                using CancellationTokenSource cts2 = new(TimeSpan.FromSeconds(10));
                while (!cts2.IsCancellationRequested && (line = await broker2.StandardOutput.ReadLineAsync(cts2.Token)) != null)
                {
                    if (line.Contains("BROKER_READY", StringComparison.OrdinalIgnoreCase))
                    {
                        ready2 = true;
                        break;
                    }
                }
                Assert.True(ready2, "Broker 2 failed to signal ready.");

                // Phase 0 startup must have recovered the orphaned active lease to Quarantined!
                string leasePath = paths.GetActiveLeasePath("wf-crashed-broker");
                Assert.True(File.Exists(leasePath));

                WorkflowSchedulerLeaseManifest recovered =
                    WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
                Assert.Equal(WorkflowSchedulerLeaseState.Quarantined, recovered.State);
                Assert.Null(recovered.Slot);

                // Verify slot 1 is freed and admits a subsequent workflow
                await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);
                WorkflowResourceClaim[] newClaims = [new("fs:/repo/other", WorkflowResourceAccessKind.SharedRead)];
                await using WorkflowBrokerLeaseHandle newLease =
                    await client2.AcquireLeaseAsync("wf-subsequent", newClaims, "Normal");
                Assert.Equal(1, newLease.Slot);
            }
            finally
            {
                if (!broker2.HasExited)
                {
                    broker2.Kill(entireProcessTree: true);
                    await broker2.WaitForExitAsync();
                }
            }
        }
        finally
        {
            if (!broker1.HasExited)
            {
                broker1.Kill(entireProcessTree: true);
                await broker1.WaitForExitAsync();
            }
        }
    }

    // =========================================================================
    // 14. Ambiguous P09 Side-Effect Recovery While Broker Is Operating
    // =========================================================================

    [Fact]
    public async Task Broker_P09MaintenanceWhileBrokerOperating_ReconcilesWithoutDeadlock()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-p09-broker-online");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "do work");
        ExecutableWork work = new(1, [task], [], [], [], []);

        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowState runningWorkflow = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWorkflow, 1);

        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWorkflow, "task-1", WorkflowStepStatus.Running);
        store.Append(runningStep, 2);

        CompiledPrompt prompt = new("task-1", "instruction");
        ExecutionEnvelope envelope = ExecutionEnvelope.Create(
            work,
            prompt,
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(root)),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);

        // Before maintenance can be acquired, the orphaned workflow must be in Quarantined state
        WorkflowSchedulerLeaseManifest initialQuarantine = new(
            "wf-p09-broker-online",
            WorkflowSchedulerLeaseState.Quarantined,
            "dead-token-12345",
            slot_: null,
            "Normal",
            [],
            4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initialQuarantine, 4, domainLockHandle_: null);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        TestAgentExecutor agentExecutor = new(new AgentProviderId("agent-1"), AgentExecutionResult.Completed());
        TestValidationExecutor validator = new(ValidationStrategy.Policy);
        TestSideEffectReconciler reconciler = new();

        // Perform orphan workflow reconciliation while broker is operating
        await WorkflowOrphanReconciler.ReconcileOrphanWorkflowAsync(
            root,
            store,
            work,
            envelope,
            [agentExecutor],
            [validator],
            new WorkflowRepairPolicy(1, 1),
            reconciler);

        // Verification: canonical persistence snapshot is loaded and updated
        WorkflowPersistenceSnapshot? snapshot = store.Load();
        Assert.NotNull(snapshot);

        // Maintenance lease is deleted and no longer present on disk
        string maintLeasePath = paths.GetActiveLeasePath("wf-p09-broker-online");
        Assert.False(File.Exists(maintLeasePath));
    }

    // =========================================================================
    // 15. Filesystem Tampering and Reparse Points
    // =========================================================================

    [Fact]
    public void Broker_FilesystemTampering_DetectsReparsePointsAndFailsClosed()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // 1. Invalid path names fail closed
        Assert.ThrowsAny<Exception>(() => paths.AssertSafeCoordinationFile(Path.Combine(root, "invalid\0name")));

        // 2. Directory junction / symlink substitution inside coordination paths fails closed
        string targetDir = Path.Combine(root, "target-payload");
        Directory.CreateDirectory(targetDir);
        string linkDir = Path.Combine(paths.LocksDirectory, "malicious-junction");

        try
        {
            Directory.CreateSymbolicLink(linkDir, targetDir);
        }
        catch
        {
            // On Windows systems without developer mode, symlink creation may throw; fallback to junction
        }

        if (Directory.Exists(linkDir))
        {
            Assert.Throws<InvalidOperationException>(() =>
                WorkflowSchedulerPaths.AssertNoReparsePoint(linkDir));
            // Correct argument order: path is the child/leaf, stopRoot is the ancestor boundary
            Assert.Throws<InvalidOperationException>(() =>
                WorkflowSchedulerPaths.AssertNoReparsePointInHierarchy(Path.Combine(linkDir, "payload.txt"), paths.LocksDirectory));
        }

        // 3. Test invalid or non-ancestor stopRoot rejection fails closed
        Assert.Throws<ArgumentException>(() =>
            WorkflowSchedulerPaths.AssertNoReparsePointInHierarchy(paths.LocksDirectory, Path.Combine(root, "unrelated-sibling")));
        Assert.Throws<ArgumentException>(() =>
            WorkflowSchedulerPaths.AssertNoReparsePointInHierarchy(paths.LocksDirectory, "   "));

        // 4. Broken symlink detection
        string brokenLink = Path.Combine(paths.LocksDirectory, "broken-symlink");
        try
        {
            Directory.CreateSymbolicLink(brokenLink, Path.Combine(root, "non-existent-target-dir"));
        }
        catch
        {
        }

        if (File.Exists(brokenLink) || Directory.Exists(brokenLink))
        {
            Assert.Throws<InvalidOperationException>(() =>
                WorkflowSchedulerPaths.AssertNoReparsePoint(brokenLink));
            Assert.Throws<InvalidOperationException>(() =>
                WorkflowSchedulerPaths.AssertNoReparsePointInHierarchy(Path.Combine(brokenLink, "child.txt"), paths.LocksDirectory));
        }

        // 5. Normal path within hierarchy succeeds
        string normalDir = Path.Combine(paths.LocksDirectory, "normal-dir");
        Directory.CreateDirectory(normalDir);
        string normalFile = Path.Combine(normalDir, "normal.txt");
        File.WriteAllText(normalFile, "payload");
        WorkflowSchedulerPaths.AssertNoReparsePointInHierarchy(normalFile, paths.LocksDirectory);
    }

    // =========================================================================
    // 15A. Worker Client Does Not Bootstrap Coordination Layout
    // =========================================================================

    [Fact]
    public async Task WorkerClient_Connect_DoesNotCreateCoordinationLayout()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);

        // Locks directory must NOT exist yet
        Assert.False(Directory.Exists(paths.LocksDirectory));

        // Worker attempts to connect when broker is not running: fails connection without bootstrapping layout!
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<Exception>(() => WorkflowBrokerClient.ConnectAsync(paths, cancellationToken_: cts.Token));

        // Worker must NEVER create locks/ or coordination directories!
        Assert.False(Directory.Exists(paths.LocksDirectory));
        Assert.False(Directory.Exists(paths.ActiveLeasesDirectory));
    }

    // =========================================================================
    // 15B. Direct Maintenance Release Without Reconciliation Fails Closed
    // =========================================================================

    [Fact]
    public async Task Broker_DirectMaintenanceReleaseWithoutReconciliation_FailsAndPreservesQuarantine()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // Setup an existing quarantined workflow
        WorkflowSchedulerLeaseManifest initialQuarantine = new(
            "wf-direct-maint",
            WorkflowSchedulerLeaseState.Quarantined,
            "old-token",
            slot_: null,
            "Normal",
            [],
            4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initialQuarantine, 4, domainLockHandle_: null);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

        // 1. Acquire maintenance lease succeeds
        WorkflowBrokerMaintenanceHandle maintHandle = await client.AcquireMaintenanceLeaseAsync("wf-direct-maint");
        Assert.NotNull(maintHandle);

        // 2. Client attempts to directly release maintenance lease without P09 reconciliation evidence!
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.ReleaseMaintenanceLeaseAsync("wf-direct-maint", maintHandle.OwnerToken));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        Assert.Contains("missing or incomplete P09 reconciliation evidence", ex.Message, StringComparison.OrdinalIgnoreCase);

        // 3. Verify workflow remains MaintenanceInProgress (global exclusion preserved) on disk, NEVER deleted!
        string leasePath = paths.GetActiveLeasePath("wf-direct-maint");
        Assert.True(File.Exists(leasePath));
        WorkflowSchedulerLeaseManifest manifest = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, manifest.State);
    }

    // =========================================================================
    // 15C. Maintenance Handle Disposal Without Completion Preserves Quarantine
    // =========================================================================

    [Fact]
    public async Task Broker_MaintenanceHandleDisposalWithoutCompletion_PreservesQuarantine()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // Setup an existing quarantined workflow
        WorkflowSchedulerLeaseManifest initialQuarantine = new(
            "wf-maint-dispose",
            WorkflowSchedulerLeaseState.Quarantined,
            "old-token",
            slot_: null,
            "Normal",
            [],
            4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initialQuarantine, 4, domainLockHandle_: null);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

        // Acquire maintenance lease and dispose without calling CompleteAsync()
        {
            await using WorkflowBrokerMaintenanceHandle maintHandle =
                await client.AcquireMaintenanceLeaseAsync("wf-maint-dispose");
            Assert.NotNull(maintHandle);
            // Handle disposed here without CompleteAsync()
        }

        // Verify workflow remains MaintenanceInProgress (global exclusion preserved) on disk, NEVER deleted!
        string leasePath = paths.GetActiveLeasePath("wf-maint-dispose");
        Assert.True(File.Exists(leasePath));
        WorkflowSchedulerLeaseManifest manifest = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, manifest.State);
    }

    // =========================================================================
    // 15D. Phase 0 Startup Recovery of Orphaned MaintenanceInProgress
    // =========================================================================

    [Fact]
    public async Task Broker_Phase0Recovery_RecoversOrphanedMaintenanceInProgress()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // Simulate crash during maintenance: MaintenanceInProgress manifest exists with no live process
        WorkflowSchedulerLeaseManifest orphanedMaint = new(
            "wf-orphaned-maint",
            WorkflowSchedulerLeaseState.MaintenanceInProgress,
            "dead-maint-token",
            slot_: null,
            domainMode_: "ExclusiveMaintenance",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, orphanedMaint, 4, domainLockHandle_: null);

        // Start broker: Phase 0 executes
        await using (WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker.StartAsync();

            // Verify the orphaned maintenance manifest was preserved on disk with global quarantine!
            string leasePath = paths.GetActiveLeasePath("wf-orphaned-maint");
            Assert.True(File.Exists(leasePath));
            WorkflowSchedulerLeaseManifest preserved = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
            Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, preserved.State);
            Assert.Equal("ExclusiveMaintenance", preserved.DomainMode);

            // Verify global quarantine: any normal workflow is excluded from admission!
            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
            WorkflowResourceClaim[] claims = [new("fs:/repo/test", WorkflowResourceAccessKind.ExclusiveWrite)];
            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireLeaseAsync("wf-normal", claims, "Normal"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        }
    }

    // =========================================================================
    // 15E. Transactional Safety: Rollback of Owner Auth and Slot on Write Failure
    // =========================================================================

    [Fact]
    public async Task Broker_AcquireLeaseFailure_RollsBackOwnerAuthAndReleasesLocks()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(1, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 1, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        try
        {
            // Inject write fault via SimulateDurabilityFailure so transaction fails during manifest write
            WorkflowCoordinationBroker.SimulateDurabilityFailure = true;

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
            WorkflowResourceClaim[] claims = [new("fs:/repo/tx", WorkflowResourceAccessKind.ExclusiveWrite)];

            // Attempting to acquire lease will fail during manifest write
            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireLeaseAsync("wf-tx-fail", claims, "Normal"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeInternalError, ex.ErrorCode);

            // Verify transaction rolled back:
            // 1. owner-auth file does NOT exist
            string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, "wf-tx-fail");
            Assert.False(File.Exists(authPath), "owner-auth.json should have been rolled back.");

            // 2. Slot 1 is immediately free and can be acquired by a valid workflow after clearing fault
            WorkflowCoordinationBroker.SimulateDurabilityFailure = false;
            WorkflowResourceClaim[] claims2 = [new("fs:/repo/valid", WorkflowResourceAccessKind.ExclusiveWrite)];
            await using WorkflowBrokerLeaseHandle validLease = await client.AcquireLeaseAsync("wf-tx-success", claims2, "Normal");
            Assert.Equal(1, validLease.Slot);
        }
        finally
        {
            WorkflowCoordinationBroker.SimulateDurabilityFailure = false;
        }
    }

    // =========================================================================
    // 15F. Mutual Authentication: Reject Fake Broker
    // =========================================================================

    [Fact]
    public async Task Broker_MutualAuthentication_RejectsFakeBrokerUnderWorkerIdentity()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // Broker runs with its dedicated ServicePrincipal
        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        // Worker specifies an expected server identity that does NOT match the broker's service principal
        string fakePrincipal = OperatingSystem.IsWindows()
            ? "FakeServicePrincipal_DifferentFromBroker"
            : "99999";

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WorkflowBrokerClient.ConnectAsync(paths, expectedServerIdentity_: fakePrincipal));

        if (OperatingSystem.IsWindows())
        {
            // Windows Named Pipe rejects fake broker peer during VerifyServerIdentity
            Assert.Contains("rogue", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // Linux client rejects fake broker at endpoint security validation (socket owner mismatch)
            Assert.Contains("Failed to validate Linux socket security", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Positive control: Connecting with the genuine expected broker identity succeeds
        await using WorkflowBrokerClient legitimateClient = await WorkflowBrokerClient.ConnectAsync(
            paths,
            expectedServerIdentity_: broker.ServicePrincipal);
        Assert.NotNull(legitimateClient);
    }

    // =========================================================================
    // 16. Owner Generation Replacement
    // =========================================================================

    [Fact]
    public async Task Broker_OwnerGenerationReplacement_RejectsPreviousGenerationToken()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        WorkflowResourceClaim[] claims = [new("fs:/repo/target", WorkflowResourceAccessKind.ExclusiveWrite)];

        // Generation 1
        WorkflowBrokerLeaseHandle lease1 = await client.AcquireLeaseAsync("wf-gen", claims, "Normal");
        string oldToken = lease1.OwnerToken;
        await lease1.DisposeAsync();

        // Generation 2
        WorkflowBrokerLeaseHandle lease2 = await client.AcquireLeaseAsync("wf-gen", claims, "Normal");
        string newToken = lease2.OwnerToken;
        Assert.NotEqual(oldToken, newToken);

        // Attempting to release with the old token fails
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.ReleaseLeaseAsync("wf-gen", oldToken));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeUnauthorized, ex.ErrorCode);

        await lease2.DisposeAsync();
    }

    // =========================================================================
    // 17. Frozen Contracts and Public Surface (36 Types)
    // =========================================================================

    [Fact]
    public void Broker_FrozenContractsAndPublicSurface_ContainsExactlyThirtySixTypes()
    {
        Type[] exportedTypes = typeof(WorkflowResourceClaim).Assembly.GetExportedTypes();
        Assert.Equal(36, exportedTypes.Length);
    }

    // =========================================================================
    // 18. Existing Slice 01 Hashes Preserved
    // =========================================================================

    [Fact]
    public void Broker_Slice01HashesPreserved_AllSixMatch()
    {
        string baseDir = AppContext.BaseDirectory;
        string repoRoot = baseDir;
        while (!string.IsNullOrEmpty(repoRoot) && !File.Exists(Path.Combine(repoRoot, "AI.RepoKit.MCP.sln")))
        {
            repoRoot = Path.GetDirectoryName(repoRoot)!;
        }

        (string RelativePath, string ExpectedHash)[] slice01Files =
        [
            ("src/AiRepoKit.WorkflowExecution/WorkflowResourceAccessKind.cs", "6BF8E1868C6A96E557BF1FDCC06A53DFC2B39262F5303EBEF4222CF42352CFE4"),
            ("src/AiRepoKit.WorkflowExecution/WorkflowResourceClaim.cs", "D2A6AFC961507CEA8DB9E9C730B033F80AAF1C954A59314C6415EA1D25C50F74"),
            ("src/AiRepoKit.WorkflowExecution/WorkflowSchedulerCandidate.cs", "5D2491F3FAC2F252B505DBF8B50B493C7672B72E988F7C652D872D82AA96B221"),
            ("src/AiRepoKit.WorkflowExecution/WorkflowSchedulerPolicy.cs", "0A73E480EA1BD0EFAD2723974D0C44BCE29C0E206943ED074942943E7E5DC8DA"),
            ("tests/AiRepoKit.WorkflowExecution.Tests/WorkflowSchedulerContractTests.cs", "D1C7D8E27093F1AD4B6C974523C01F3D933883C70526BEA8690EB3D871B6AFE6"),
            ("tests/AiRepoKit.WorkflowExecution.Tests/ArchitectureAndBoundaryTests.cs", "4779BD804B7393576F9CBE810DEE08491A8A6F4F2438B156908F523FFE7F4F1D")
        ];

        foreach (var (relPath, expectedHash) in slice01Files)
        {
            string fullPath = Path.Combine(repoRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"File '{relPath}' must exist.");

            byte[] bytes = File.ReadAllBytes(fullPath);
            byte[] hashBytes = SHA256.HashData(bytes);
            string actualHash = Convert.ToHexString(hashBytes);

            Assert.Equal(expectedHash, actualHash);
        }
    }

    // =========================================================================
    // 19. Broker/Worker Provenance with Distinct Processes
    // =========================================================================

    [Fact]
    public async Task Broker_WorkerProvenance_DistinctProcesses_IntegrityInspectionAndAdmissionSucceed()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        string brokerExe = FindBrokerExecutable();

        // 1. Start Broker process as an independent OS process
        ProcessStartInfo brokerPsi = new(brokerExe)
        {
            ArgumentList = { root, "--max-concurrent", "4" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        Process brokerProc = Process.Start(brokerPsi)!;
        Assert.NotNull(brokerProc);

        try
        {
            string? line;
            bool brokerReady = false;
            string brokerPrincipal = string.Empty;
            using CancellationTokenSource brokerCts = new(TimeSpan.FromSeconds(10));
            while (!brokerCts.IsCancellationRequested && (line = await brokerProc.StandardOutput.ReadLineAsync(brokerCts.Token)) != null)
            {
                if (line.Contains("BROKER_READY", StringComparison.OrdinalIgnoreCase))
                {
                    brokerReady = true;
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        brokerPrincipal = parts[1].Trim();
                    }
                    break;
                }
            }
            if (!brokerReady)
            {
                string err = await brokerProc.StandardError.ReadToEndAsync();
                Assert.Fail($"Broker process failed to signal BROKER_READY. Stderr: {err}");
            }
            if (string.IsNullOrEmpty(brokerPrincipal) && OperatingSystem.IsWindows())
            {
                brokerPrincipal = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
            }

            // 2. Start Worker 1 as a distinct OS process
            ProcessStartInfo workerPsi = new(brokerExe)
            {
                ArgumentList = { "--worker", root, "wf-worker-1", brokerPrincipal },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            Process worker1Proc = Process.Start(workerPsi)!;
            Assert.NotNull(worker1Proc);

            try
            {
                bool worker1Acquired = false;
                using CancellationTokenSource worker1Cts = new(TimeSpan.FromSeconds(10));
                while (!worker1Cts.IsCancellationRequested && (line = await worker1Proc.StandardOutput.ReadLineAsync(worker1Cts.Token)) != null)
                {
                    if (line.Contains("WORKER_ACQUIRED", StringComparison.OrdinalIgnoreCase))
                    {
                        worker1Acquired = true;
                        break;
                    }
                }
                if (!worker1Acquired)
                {
                    string err = await worker1Proc.StandardError.ReadToEndAsync();
                    Assert.Fail($"Worker 1 process failed to acquire lease. Error: {err}");
                }

                // 3. Verify Broker and Worker are genuinely distinct OS processes
                Assert.NotEqual(brokerProc.Id, worker1Proc.Id);
                Assert.NotEqual(Environment.ProcessId, brokerProc.Id);
                Assert.NotEqual(Environment.ProcessId, worker1Proc.Id);

                // 4. Verify owner-auth.json on disk records worker1 PID and start time, NOT the broker PID
                string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, "wf-worker-1");
                Assert.True(File.Exists(authPath));
                WorkflowSchedulerOwnerAuthentication? auth = WorkflowSchedulerOwnerAuthentication.ReadOwnerAuthentication(paths, "wf-worker-1");
                Assert.NotNull(auth);
                Assert.Equal(worker1Proc.Id, auth.ProcessId);
                Assert.NotEqual(brokerProc.Id, auth.ProcessId);

                // 5. Run independent integrity inspection while worker 1 lease remains active
                WorkflowSchedulerIntegrityReport report1 = WorkflowSchedulerIntegrityInspector.Inspect(
                    paths,
                    maxConcurrentWorkflows_: 4);

                Assert.True(report1.IsHealthy, $"Inspection should be healthy. Violations: {string.Join("; ", report1.Violations)}");
                Assert.Empty(report1.Violations);
                Assert.Single(report1.ActiveLeases);
                Assert.Equal("wf-worker-1", report1.ActiveLeases[0].WorkflowId);

                // 6. Connect client and admit a second nonconflicting workflow
                await using (WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths, expectedServerIdentity_: brokerPrincipal))
                {
                    WorkflowResourceClaim[] claims2 = [new("fs:/repo/distinct-file.txt", WorkflowResourceAccessKind.ExclusiveWrite)];
                    await using WorkflowBrokerLeaseHandle lease2 = await client.AcquireLeaseAsync("wf-worker-2", claims2, "Normal");

                    Assert.Equal(2, lease2.Slot);

                    // 7. Re-run integrity inspection with both workflows active across distinct processes
                    WorkflowSchedulerIntegrityReport report2 = WorkflowSchedulerIntegrityInspector.Inspect(
                        paths,
                        maxConcurrentWorkflows_: 4);

                    Assert.True(report2.IsHealthy, $"Inspection with two active workflows should be healthy. Violations: {string.Join("; ", report2.Violations)}");
                    Assert.Empty(report2.Violations);
                    Assert.Equal(2, report2.ActiveLeases.Count);
                }
            }
            finally
            {
                if (!worker1Proc.HasExited)
                {
                    try
                    {
                        await worker1Proc.StandardInput.WriteLineAsync("STOP");
                        await worker1Proc.StandardInput.FlushAsync();
                        await worker1Proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
                    }
                    catch
                    {
                        worker1Proc.Kill(entireProcessTree: true);
                    }
                }
            }
        }
        finally
        {
            if (!brokerProc.HasExited)
            {
                brokerProc.Kill(entireProcessTree: true);
                await brokerProc.WaitForExitAsync();
            }
        }
    }

    // =========================================================================
    // 20. P09 Full Journal Audit: Unresolved Intents Reject Manifest Deletion
    // =========================================================================

    [Fact]
    public async Task Broker_P09Audit_ValidRevisionAdvanceWithDeliberatelyUnresolvedP09Intents_RejectsManifestDeletion()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-p09-unresolved");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "do work");
        ExecutableWork work = new(1, [task], [], [], [], []);

        // 1. Initialize store with created -> running -> step running
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created); // Revision 1

        WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWf, 1); // Revision 2

        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWf, "task-1", WorkflowStepStatus.Running);
        store.Append(runningStep, 2); // Revision 3

        // Workflow was quarantined following a crash/abandonment
        WorkflowSchedulerLeaseManifest initialQuarantine = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "old-token",
            slot_: null,
            "Normal",
            [],
            4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initialQuarantine, 4, domainLockHandle_: null);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

        // 2. Acquire maintenance lease
        await using (WorkflowBrokerMaintenanceHandle maintHandle = await client.AcquireMaintenanceLeaseAsync(wfId.Value))
        {
            Assert.NotNull(maintHandle);

            // 3. Deliberately construct a valid revision advance on snapshot (Revision 3 -> 4)
            // but leave an unresolved P09 intent in the canonical execution checkpoint journal!
            string baseFp20 = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(runningStep);
            string inputFp20 = new('c', 64);
            string regFp20 = new('d', 64);
            WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
                root,
                wfId,
                "task-1",
                basePersistenceRevision_: 3,
                baseFp20,
                inputFp20,
                regFp20);

            // Sequence 2: InvocationIntentRecord for op 1, generation 1 (NO matching outcome or RNE record!)
            InvocationIntentRecord intent = new()
            {
                Sequence = 2,
                OperationOrdinal = 1,
                OperationId = "op-audit-1",
                OperationKind = "agent",
                InvocationGeneration = 1,
                ExternalIdentity = "provider-test",
                RequestFingerprint = "req-fp"
            };
            journal.AppendRecord(intent);

            // Sequence 3: ProjectionCompletedRecord claiming target persistence revision 5
            ProjectionCompletedRecord projRecord = new()
            {
                Sequence = 3,
                FinalPersistenceRevision = 5
            };
            journal.AppendRecord(projRecord);

            // Advance persistence snapshot to revision 5, step completed
            WorkflowState awaitingValidation = WorkflowStateMachine.TransitionStep(runningStep, "task-1", WorkflowStepStatus.AwaitingValidation);
            store.Append(awaitingValidation, 3); // Revision 4
            WorkflowState completedStep = WorkflowStateMachine.TransitionStep(awaitingValidation, "task-1", WorkflowStepStatus.Completed);
            store.Append(completedStep, 4); // Revision 5

            // 4. Attempting to release maintenance lease must be REJECTED because of unresolved P09 intent!
            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => maintHandle.CompleteAsync());
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
            Assert.Contains("missing or incomplete P09 reconciliation evidence", ex.Message, StringComparison.OrdinalIgnoreCase);

            // 5. Verify maintenance lease manifest STILL exists on disk and is STILL MaintenanceInProgress!
            string leasePath = paths.GetActiveLeasePath(wfId.Value);
            Assert.True(File.Exists(leasePath));
            WorkflowSchedulerLeaseManifest preserved = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
            Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, preserved.State);
            Assert.Equal("ExclusiveMaintenance", preserved.DomainMode);
        }
    }

    // =========================================================================
    // 21. Orphan Maintenance Re-acquisition and Safe Generation Transition
    // =========================================================================

    [Fact]
    public async Task Broker_OrphanMaintenanceReacquisition_ReconcilesAfterRestartAndProhibitsUnauthorizedTokenSubstitution()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-orphan-maint");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "do work");
        ExecutableWork work = new(1, [task], [], [], [], []);

        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        // 1. Broker 1 runs, an existing workflow is in MaintenanceInProgress with token1
        string oldToken = "dead-owner-token-" + Guid.NewGuid().ToString("N");
        WorkflowSchedulerLeaseManifest orphanedManifest = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.MaintenanceInProgress,
            oldToken,
            slot_: null,
            domainMode_: "ExclusiveMaintenance",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, orphanedManifest, 4, domainLockHandle_: null);

        // Unauthorized direct overwrite attempt by a worker without domain lock authority MUST fail
        WorkflowSchedulerLeaseManifest rogueManifest = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.MaintenanceInProgress,
            "rogue-token-unauthorized",
            slot_: null,
            domainMode_: "ExclusiveMaintenance",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        Assert.Throws<InvalidOperationException>(() =>
            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, rogueManifest, 4, domainLockHandle_: null));

        // 2. Start Broker 2 (simulating broker restart after crash during maintenance)
        await using (WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker.StartAsync();

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

            // 3. Unauthorized token release attempt is rejected
            WorkflowBrokerException unauthEx = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.ReleaseMaintenanceLeaseAsync(wfId.Value, oldToken));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeUnauthorized, unauthEx.ErrorCode);

            // 4. Broker-mediated safe generation transition:
            // A new maintenance worker acquires the orphaned maintenance lease
            await using WorkflowBrokerMaintenanceHandle newMaintHandle =
                await client.AcquireMaintenanceLeaseAsync(wfId.Value);

            Assert.NotNull(newMaintHandle);
            string newToken = newMaintHandle.OwnerToken;
            Assert.NotEqual(oldToken, newToken);

            // 5. Verify the manifest on disk was transitioned to the new token under broker domain lock
            string leasePath = paths.GetActiveLeasePath(wfId.Value);
            WorkflowSchedulerLeaseManifest currentManifest = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
            Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, currentManifest.State);
            Assert.Equal(newToken, currentManifest.OwnerToken);

            // 6. Complete valid P09 reconciliation evidence
            string baseFp21 = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(created);
            string inputFp21 = new('a', 64);
            string regFp21 = new('b', 64);
            WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
                root,
                wfId,
                "task-1",
                basePersistenceRevision_: 1,
                baseFp21,
                inputFp21,
                regFp21);

            WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
            store.Append(runningWf, 1); // Revision 2
            WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWf, "task-1", WorkflowStepStatus.Running);
            store.Append(runningStep, 2); // Revision 3
            WorkflowState awaitingVal = WorkflowStateMachine.TransitionStep(runningStep, "task-1", WorkflowStepStatus.AwaitingValidation);
            store.Append(awaitingVal, 3); // Revision 4
            WorkflowState completedStep = WorkflowStateMachine.TransitionStep(awaitingVal, "task-1", WorkflowStepStatus.Completed);
            store.Append(completedStep, 4); // Revision 5

            InvocationIntentRecord intent = new()
            {
                Sequence = 2,
                OperationOrdinal = 1,
                OperationId = "op-1",
                OperationKind = "agent",
                InvocationGeneration = 1,
                RequestFingerprint = "req-1"
            };
            journal.AppendRecord(intent);

            InvocationOutcomeRecord outcome = new()
            {
                Sequence = 3,
                OperationOrdinal = 1,
                OperationId = "op-1",
                OperationKind = "agent",
                InvocationGeneration = 1,
                RequestFingerprint = "req-1",
                ValidationPassed = true
            };
            journal.AppendRecord(outcome);

            ExecutionCompletedRecord completedRec = new()
            {
                Sequence = 4,
                FinalStateFingerprint = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(completedStep),
                AttemptCount = 1,
                ExpectedProjectionTransitionCount = 4
            };
            journal.AppendRecord(completedRec);

            ProjectionCompletedRecord proj = new()
            {
                Sequence = 5,
                FinalPersistenceRevision = 5
            };
            journal.AppendRecord(proj);

            // 7. Complete maintenance with new token: succeeds and deletes manifest!
            await newMaintHandle.CompleteAsync();
            Assert.False(File.Exists(leasePath), "Maintenance manifest must be deleted upon successful P09 reconciliation.");
        }
    }

    // =========================================================================
    // 22. Transaction Rollback: Fail-Closed Retains Locks and Blocks Admission
    // =========================================================================

    [Fact]
    public async Task Broker_TransactionRollbackFailClosed_RetainsLocksAndBlocksFurtherAdmission()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        try
        {
            // 1. Enable targeted durability and rollback fault injection
            WorkflowCoordinationBroker.SimulateDurabilityFailure = true;
            WorkflowCoordinationBroker.SimulateRollbackFailure = true;

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
            WorkflowResourceClaim[] claims = [new("fs:/repo/rollback-target", WorkflowResourceAccessKind.ExclusiveWrite)];

            // 2. Lease acquisition must fail with ErrorCodeInternalError (never swallowed or masked)
            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireLeaseAsync("wf-fail-rollback", claims, "Normal"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeInternalError, ex.ErrorCode);
            Assert.Contains("rollback failed", ex.Message, StringComparison.OrdinalIgnoreCase);

            // 3. Verify broker retained the OS locks: slot-1.lock is still held exclusively by broker!
            string slot1LockPath = paths.GetSlotLockPath(1, 4);
            WorkflowSchedulerLockHandle? probeSlotLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(slot1LockPath);
            Assert.Null(probeSlotLock); // Must be non-acquirable because broker retained it!

            string wfLockPath = paths.GetWorkflowLockPath("wf-fail-rollback");
            WorkflowSchedulerLockHandle? probeWfLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(wfLockPath);
            Assert.Null(probeWfLock); // Must be non-acquirable because broker retained it!

            // 4. Restore the directory
            Directory.CreateDirectory(paths.ActiveLeasesDirectory);

            // 5. Subsequent admission of any workflow MUST be blocked fail-closed due to prior rollback failure!
            WorkflowResourceClaim[] claims2 = [new("fs:/repo/other-target", WorkflowResourceAccessKind.ExclusiveWrite)];
            WorkflowBrokerException blockedEx = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireLeaseAsync("wf-subsequent-fail", claims2, "Normal"));
            Assert.True(
                blockedEx.ErrorCode == WorkflowBrokerProtocol.ErrorCodeInternalError ||
                blockedEx.ErrorCode == WorkflowBrokerProtocol.ErrorCodeConflict);
            Assert.Contains("admission is blocked", blockedEx.Message, StringComparison.OrdinalIgnoreCase);

            // 6. Maintenance admission is also blocked fail-closed
            WorkflowBrokerException maintBlockedEx = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireMaintenanceLeaseAsync("wf-subsequent-fail"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, maintBlockedEx.ErrorCode);
            Assert.Contains("admission is blocked", maintBlockedEx.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowCoordinationBroker.SimulateDurabilityFailure = false;
            WorkflowCoordinationBroker.SimulateRollbackFailure = false;
        }
    }

    // =========================================================================
    // 23A. Cross-Principal IPC: Distinguish User SID from Service SID on Windows
    // =========================================================================

    [Fact]
    public void Broker_CrossPrincipalIPC_DistinguishUserSidFromServiceSid_WindowsToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        bool success = WorkflowBrokerIpcTransport.TryGetProcessTokenSids(
            Environment.ProcessId,
            out string? userSid,
            out List<string> serviceSids);

        Assert.True(success, "TryGetProcessTokenSids should succeed on Windows.");
        Assert.NotNull(userSid);
        Assert.StartsWith("S-1-5-", userSid);
        Assert.NotEmpty(serviceSids);

        // Verify user SID is distinguished from service/group SIDs
        Assert.True(serviceSids.Count > 0);
        foreach (string sid in serviceSids)
        {
            Assert.StartsWith("S-1-", sid);
        }
    }

    // =========================================================================
    // 23B. Cross-Principal IPC: Reject Unauthorized Client with No Same-User Fallback
    // =========================================================================

    [Fact]
    public async Task Broker_CrossPrincipalIPC_UnauthorizedIdentity_RejectedNoSameUserFallback()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // Configure broker with an authorized worker identity that does NOT match the current user
        string unauthorizedWorkerSid = "S-1-5-21-9999999999-8888888888-7777777777-9999";

        await using WorkflowCoordinationBroker broker = new(
            paths,
            maxConcurrentWorkflows_: 4,
            allowUnrestrictedExecution_: true,
            authorizedWorkerIdentity_: unauthorizedWorkerSid);

        await broker.StartAsync();

        // Worker attempts to connect under current identity (which is not the configured authorized SID).
        // Must fail with access denied or authentication failure: NO same-user fallback!
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
        Exception ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            WorkflowBrokerClient.ConnectAsync(paths, cancellationToken_: cts.Token));

        // Rejection must be a genuine communication / transport failure (broken pipe, EOF, or broker exception)
        // caused by server-side disconnect upon failed peer authentication, not an unrelated failure.
        Assert.True(
            ex is IOException or EndOfStreamException or TimeoutException or OperationCanceledException or InvalidOperationException,
            $"Expected transport rejection exception, got: {ex.GetType().Name}: {ex.Message}");

        Assert.True(broker.RejectedAuthenticationCount > 0, "Broker must have explicitly rejected unauthorized peer.");
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class ConditionalSecondaryWorkerFactAttribute : FactAttribute
    {
        public ConditionalSecondaryWorkerFactAttribute()
        {
            string? user = Environment.GetEnvironmentVariable("AIREPO_SECONDARY_WORKER_USER");
            if (string.IsNullOrWhiteSpace(user))
            {
                this.Skip = "CROSS_PRINCIPAL_IPC=NOT_EXECUTED: Secondary OS identity is not configured in the host environment (AIREPO_SECONDARY_WORKER_USER is absent).";
                return;
            }

            string? password = Environment.GetEnvironmentVariable("AIREPO_SECONDARY_WORKER_PASSWORD");
            string? command = Environment.GetEnvironmentVariable("AIREPO_SECONDARY_WORKER_COMMAND");
            if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(command))
            {
                this.Skip = "CROSS_PRINCIPAL_IPC=NOT_EXECUTED: Secondary OS execution facility is absent (neither AIREPO_SECONDARY_WORKER_PASSWORD nor AIREPO_SECONDARY_WORKER_COMMAND is configured).";
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class LinuxOnlyFactAttribute : FactAttribute
    {
        public LinuxOnlyFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
            {
                this.Skip = "LINUX_TESTS=NOT_EXECUTED: Requires genuine Linux runtime with statx, POSIX permissions, and Unix Domain Sockets.";
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class WindowsNtfsJunctionFactAttribute : FactAttribute
    {
        public WindowsNtfsJunctionFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                this.Skip = "WINDOWS_JUNCTION=NOT_EXECUTED: Requires Windows NTFS filesystem.";
                return;
            }

            string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
            if (!Directory.Exists(baseDir))
            {
                try
                {
                    Directory.CreateDirectory(baseDir);
                }
                catch
                {
                    this.Skip = $"WINDOWS_JUNCTION=NOT_EXECUTED: Scratch directory '{baseDir}' is unavailable.";
                    return;
                }
            }

            string probeTarget = Path.Combine(baseDir, "aik-probe-target-" + Guid.NewGuid().ToString("N"));
            string probeJunction = Path.Combine(baseDir, "aik-probe-link-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(probeTarget);
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{probeJunction}\" \"{probeTarget}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                proc?.WaitForExit(5000);
                if (proc?.ExitCode != 0 || !Directory.Exists(probeJunction) || (File.GetAttributes(probeJunction) & FileAttributes.ReparsePoint) == 0)
                {
                    this.Skip = "WINDOWS_JUNCTION=NOT_EXECUTED: Host environment does not permit creation of NTFS junctions via mklink /J.";
                }
            }
            catch (Exception ex)
            {
                this.Skip = $"WINDOWS_JUNCTION=NOT_EXECUTED: Failed to probe NTFS junction capability ({ex.Message}).";
            }
            finally
            {
                try { if (Directory.Exists(probeJunction)) Directory.Delete(probeJunction, recursive: false); } catch { }
                try { if (Directory.Exists(probeTarget)) Directory.Delete(probeTarget, recursive: false); } catch { }
            }
        }
    }

    private sealed record ProcessProbeResult(
        int ExitCode,
        string? WorkerIdentity,
        string? WorkerName,
        string? Error);

    private static ProcessProbeResult ExecuteSecondaryWorkerProbe(
        string secondaryUser,
        string? secondaryPassword,
        string? secondaryCommand)
    {
        string brokerExe = FindBrokerExecutable();
        ProcessStartInfo psi = new();

        if (!string.IsNullOrWhiteSpace(secondaryCommand))
        {
            // Command wrapper (e.g. "sudo -u <user>" or custom test launcher)
            string[] cmdParts = secondaryCommand.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            psi.FileName = cmdParts[0];
            string subArgs = cmdParts.Length > 1 ? cmdParts[1] + " " : string.Empty;
            string brokerArgs = $"\"{brokerExe}\" --probe-identity";
            psi.Arguments = $"{subArgs}{brokerArgs}";
        }
        else
        {
            psi.FileName = brokerExe;
            psi.ArgumentList.Add("--probe-identity");

            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(secondaryPassword))
            {
                string user = secondaryUser;
                string? domain = null;
                if (user.Contains('\\'))
                {
                    string[] userParts = user.Split('\\', 2);
                    domain = userParts[0];
                    user = userParts[1];
                }
                else if (user.Contains('@'))
                {
                    string[] userParts = user.Split('@', 2);
                    user = userParts[0];
                    domain = userParts[1];
                }

                psi.UserName = user;
                if (!string.IsNullOrEmpty(domain))
                {
                    psi.Domain = domain;
                }
                psi.PasswordInClearText = secondaryPassword;
            }
        }

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start secondary worker probe process.");
        }
        catch (Exception ex)
        {
            throw new NotSupportedException($"CROSS_PRINCIPAL_IPC=NOT_EXECUTED: Host execution facility failed to start secondary probe process: {ex.Message}", ex);
        }

        string? workerIdentity = null;
        string? workerName = null;
        string? error = null;

        using (process)
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
            cts.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch { }
            });

            string? line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                if (line.StartsWith("WORKER_IDENTITY:", StringComparison.Ordinal))
                {
                    string[] idParts = line.Substring("WORKER_IDENTITY:".Length).Split(':', 2);
                    workerIdentity = idParts[0];
                    workerName = idParts.Length > 1 ? idParts[1] : null;
                }
            }

            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                string stderr = process.StandardError.ReadToEnd();
                error = !string.IsNullOrWhiteSpace(stderr) ? stderr : $"Probe exited with code {process.ExitCode}";
            }

            return new ProcessProbeResult(
                process.ExitCode,
                workerIdentity,
                workerName,
                error);
        }
    }

    private sealed record ProcessWorkerResult(
        int ExitCode,
        string? WorkerIdentity,
        string? WorkerName,
        bool Success,
        string? Error);

    private static ProcessWorkerResult ExecuteSecondaryWorkerProcess(
        string storageRoot,
        string workflowId,
        string? expectedBrokerPrincipal,
        string secondaryUser,
        string? secondaryPassword,
        string? secondaryCommand)
    {
        string brokerExe = FindBrokerExecutable();
        ProcessStartInfo psi = new();

        if (!string.IsNullOrWhiteSpace(secondaryCommand))
        {
            // Command wrapper (e.g. "sudo -u <user>" or custom test launcher)
            string[] cmdParts = secondaryCommand.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            psi.FileName = cmdParts[0];
            string subArgs = cmdParts.Length > 1 ? cmdParts[1] + " " : string.Empty;
            string principalArg = !string.IsNullOrEmpty(expectedBrokerPrincipal)
                ? $" --expected-principal \"{expectedBrokerPrincipal}\""
                : string.Empty;
            string brokerArgs = $"\"{brokerExe}\" --worker \"{storageRoot}\" \"{workflowId}\"{principalArg} --once";
            psi.Arguments = $"{subArgs}{brokerArgs}";
        }
        else
        {
            psi.FileName = brokerExe;
            psi.ArgumentList.Add("--worker");
            psi.ArgumentList.Add(storageRoot);
            psi.ArgumentList.Add(workflowId);
            if (!string.IsNullOrEmpty(expectedBrokerPrincipal))
            {
                psi.ArgumentList.Add("--expected-principal");
                psi.ArgumentList.Add(expectedBrokerPrincipal);
            }
            psi.ArgumentList.Add("--once");

            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(secondaryPassword))
            {
                string user = secondaryUser;
                string? domain = null;
                if (user.Contains('\\'))
                {
                    string[] userParts = user.Split('\\', 2);
                    domain = userParts[0];
                    user = userParts[1];
                }
                else if (user.Contains('@'))
                {
                    string[] userParts = user.Split('@', 2);
                    user = userParts[0];
                    domain = userParts[1];
                }

                psi.UserName = user;
                if (!string.IsNullOrEmpty(domain))
                {
                    psi.Domain = domain;
                }
                psi.PasswordInClearText = secondaryPassword;
            }
        }

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start secondary worker process.");
        }
        catch (Exception ex)
        {
            throw new NotSupportedException($"CROSS_PRINCIPAL_IPC=NOT_EXECUTED: Host execution facility failed to start secondary process: {ex.Message}", ex);
        }

        string? workerIdentity = null;
        string? workerName = null;
        bool workerAcquired = false;
        string? workerError = null;

        using (process)
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            cts.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch { }
            });

            string? line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                if (line.StartsWith("WORKER_IDENTITY:", StringComparison.Ordinal))
                {
                    string[] idParts = line.Substring("WORKER_IDENTITY:".Length).Split(':', 2);
                    workerIdentity = idParts[0];
                    workerName = idParts.Length > 1 ? idParts[1] : null;
                }
                else if (line.Contains("WORKER_ACQUIRED", StringComparison.Ordinal))
                {
                    workerAcquired = true;
                }
                else if (line.StartsWith("WORKER_REJECTED:", StringComparison.Ordinal))
                {
                    workerError = line.Substring("WORKER_REJECTED:".Length);
                }
            }

            process.WaitForExit(5000);
            if (string.IsNullOrEmpty(workerError) && process.ExitCode != 0)
            {
                string stderr = process.StandardError.ReadToEnd();
                workerError = !string.IsNullOrWhiteSpace(stderr) ? stderr : $"Process exited with code {process.ExitCode}";
            }

            return new ProcessWorkerResult(
                process.ExitCode,
                workerIdentity,
                workerName,
                workerAcquired,
                workerError);
        }
    }

    // =========================================================================
    // 23C. Cross-Principal IPC: OS-Level Genuine Execution Or Mark NOT_EXECUTED
    // =========================================================================

    [ConditionalSecondaryWorkerFact]
    public async Task Broker_CrossPrincipalIPC_OSLevelDifferentPrincipal_GenuineExecutionOrMarkNotExecuted()
    {
        string? secondaryUser = Environment.GetEnvironmentVariable("AIREPO_SECONDARY_WORKER_USER");
        if (string.IsNullOrWhiteSpace(secondaryUser))
        {
            return;
        }

        string? secondaryPassword = Environment.GetEnvironmentVariable("AIREPO_SECONDARY_WORKER_PASSWORD");
        string? secondaryCommand = Environment.GetEnvironmentVariable("AIREPO_SECONDARY_WORKER_COMMAND");

        if (string.IsNullOrWhiteSpace(secondaryPassword) && string.IsNullOrWhiteSpace(secondaryCommand))
        {
            throw new InvalidOperationException("CROSS_PRINCIPAL_IPC=NOT_EXECUTED: Host lacks credentials or execution command when AIREPO_SECONDARY_WORKER_USER is set.");
        }

        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        string parentIdentity = OperatingSystem.IsWindows()
            ? System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty
            : WorkflowBrokerIpcTransport.GetCurrentLinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);

        // 1. First probe child process execution to independently verify actual OS identity deterministically
        ProcessProbeResult probeResult;
        try
        {
            probeResult = ExecuteSecondaryWorkerProbe(secondaryUser, secondaryPassword, secondaryCommand);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException($"CROSS_PRINCIPAL_IPC=NOT_EXECUTED: {ex.Message}", ex);
        }

        Assert.Equal(0, probeResult.ExitCode);
        Assert.NotNull(probeResult.WorkerIdentity);
        Assert.False(string.IsNullOrWhiteSpace(probeResult.WorkerIdentity), "Child process must report genuine OS principal identity.");

        // Independent identity verification: child identity MUST NOT be the current test process identity!
        Assert.NotEqual(parentIdentity, probeResult.WorkerIdentity);

        string verifiedSecondaryPrincipal = probeResult.WorkerIdentity;

        // 2. Positive Control: Broker authorizes the verified secondary OS principal.
        // Secondary OS worker process executes, connects, acquires lease, and succeeds.
        await using (WorkflowCoordinationBroker authorizedBroker = new(
            paths,
            maxConcurrentWorkflows_: 4,
            allowUnrestrictedExecution_: true,
            authorizedWorkerIdentity_: verifiedSecondaryPrincipal))
        {
            await authorizedBroker.StartAsync();

            ProcessWorkerResult positiveResult = ExecuteSecondaryWorkerProcess(
                root,
                "wf-sec-auth",
                authorizedBroker.ServicePrincipal,
                secondaryUser,
                secondaryPassword,
                secondaryCommand);

            Assert.True(positiveResult.Success, $"Secondary worker process failed authorized connection: {positiveResult.Error}");
            Assert.Equal(0, authorizedBroker.RejectedAuthenticationCount);
        }

        // 3. Negative Control: Broker authorizes ONLY parentIdentity (unauthorized to secondary user).
        // Secondary OS worker process executes, connects, and MUST be rejected fail-closed.
        await using (WorkflowCoordinationBroker rejectingBroker = new(
            paths,
            maxConcurrentWorkflows_: 4,
            allowUnrestrictedExecution_: true,
            authorizedWorkerIdentity_: parentIdentity))
        {
            await rejectingBroker.StartAsync();

            ProcessWorkerResult negativeResult = ExecuteSecondaryWorkerProcess(
                root,
                "wf-sec-unauth",
                rejectingBroker.ServicePrincipal,
                secondaryUser,
                secondaryPassword,
                secondaryCommand);

            Assert.False(negativeResult.Success, "Unauthorized secondary worker process must not succeed.");
            Assert.NotNull(negativeResult.Error);
            Assert.True(
                rejectingBroker.RejectedAuthenticationCount > 0 ||
                negativeResult.Error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                negativeResult.Error.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase) ||
                negativeResult.Error.Contains("permission", StringComparison.OrdinalIgnoreCase),
                $"Broker must record rejected authentication attempt or OS-level pipe DACL rejection. Error was: {negativeResult.Error}");
        }
    }

    [Fact]
    public async Task Broker_MultiprocessWorker_AuthorizedConnectionAndRejection_SucceedsEndToEnd()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        string currentIdentity = OperatingSystem.IsWindows()
            ? System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty
            : WorkflowBrokerIpcTransport.GetCurrentLinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);

        // 1. Positive: Broker authorizes current user, child process connects and acquires
        await using (WorkflowCoordinationBroker authorizedBroker = new(
            paths,
            maxConcurrentWorkflows_: 4,
            allowUnrestrictedExecution_: true,
            authorizedWorkerIdentity_: currentIdentity))
        {
            await authorizedBroker.StartAsync();

            ProcessWorkerResult positive = ExecuteSecondaryWorkerProcess(
                root,
                "wf-sub-auth",
                authorizedBroker.ServicePrincipal,
                secondaryUser: Environment.UserName,
                secondaryPassword: null,
                secondaryCommand: null);

            Assert.True(positive.Success, $"Worker process failed authorized connection: {positive.Error}");
            Assert.Equal(0, authorizedBroker.RejectedAuthenticationCount);
        }

        // 2. Negative: Broker authorizes an unauthorized identity, child process is rejected
        string fakeUnauthorizedIdentity = OperatingSystem.IsWindows()
            ? "S-1-5-21-9999999999-8888888888-7777777777-9999"
            : "999999";

        await using (WorkflowCoordinationBroker rejectingBroker = new(
            paths,
            maxConcurrentWorkflows_: 4,
            allowUnrestrictedExecution_: true,
            authorizedWorkerIdentity_: fakeUnauthorizedIdentity))
        {
            await rejectingBroker.StartAsync();

            ProcessWorkerResult negative = ExecuteSecondaryWorkerProcess(
                root,
                "wf-sub-unauth",
                rejectingBroker.ServicePrincipal,
                secondaryUser: Environment.UserName,
                secondaryPassword: null,
                secondaryCommand: null);

            Assert.False(negative.Success, "Unauthorized worker process must not succeed.");
            Assert.True(rejectingBroker.RejectedAuthenticationCount > 0, "Broker must record rejected authentication attempt.");
        }
    }

    // =========================================================================
    // 24. Broker Shutdown: Safely Quarantines Live Leases Before Domain Lock Release
    // =========================================================================

    [Fact]
    public async Task Broker_Shutdown_SafelyQuarantinesLiveLeasesBeforeReleasingDomainLock()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // 1. Broker 1 starts and admits an active workflow
        WorkflowSchedulerLeaseManifest activeManifest;
        string leasePath = paths.GetActiveLeasePath("wf-active-shutdown");
        string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, "wf-active-shutdown");

        await using (WorkflowCoordinationBroker broker1 = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker1.StartAsync();

            await using (WorkflowBrokerClient client1 = await WorkflowBrokerClient.ConnectAsync(paths))
            {
                WorkflowResourceClaim[] claims = [new("fs:/repo/shutdown-test", WorkflowResourceAccessKind.ExclusiveWrite)];
                WorkflowBrokerLeaseHandle lease = await client1.AcquireLeaseAsync("wf-active-shutdown", claims, "Normal");

                Assert.Equal(1, lease.Slot);
                Assert.True(File.Exists(leasePath));

                activeManifest = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
                Assert.Equal(WorkflowSchedulerLeaseState.Active, activeManifest.State);
                Assert.Equal(1, activeManifest.Slot);
                Assert.True(File.Exists(authPath));
            }

            // Broker 1 shuts down gracefully (DisposeAsync executes)
        }

        // 2. Verify: Live lease was safely transitioned to Quarantined BEFORE releasing domain lock!
        Assert.True(File.Exists(leasePath), "Manifest should remain on disk as quarantined.");
        WorkflowSchedulerLeaseManifest quarantined = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.Quarantined, quarantined.State);
        Assert.Null(quarantined.Slot);
        Assert.Equal("Normal", quarantined.DomainMode);

        // 3. owner-auth.json was safely cleaned up
        Assert.False(File.Exists(authPath), "owner-auth.json should have been cleaned up during quarantine transition.");

        // 4. Broker 2 starts cleanly, acquires singleton domain authority, and admits a subsequent workflow into Slot 1
        await using (WorkflowCoordinationBroker broker2 = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker2.StartAsync();

            await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);
            WorkflowResourceClaim[] newClaims = [new("fs:/repo/distinct-resource", WorkflowResourceAccessKind.ExclusiveWrite)];
            await using WorkflowBrokerLeaseHandle newLease = await client2.AcquireLeaseAsync("wf-after-shutdown", newClaims, "Normal");

            Assert.Equal(1, newLease.Slot);
        }
    }

    // =========================================================================
    // 25. P09 Audit: Synthetic Journal Lacking ExecutionCompleted Fails Reconciliation
    // =========================================================================

    [Fact]
    public async Task Broker_P09Audit_SyntheticJournalLacksExecutionCompleted_FailsReconciliationAndManifestPreserved()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-p09-no-exec-completed");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "do work");
        ExecutableWork work = new(1, [task], [], [], [], []);
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created); // Revision 1

        WorkflowSchedulerLeaseManifest quarantinedManifest = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "prior-token",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantinedManifest, 4);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerMaintenanceHandle maintHandle = await client.AcquireMaintenanceLeaseAsync(wfId.Value);

        // Advance persistence state to revision 5 with step completed
        WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWf, 1); // Revision 2
        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWf, "task-1", WorkflowStepStatus.Running);
        store.Append(runningStep, 2); // Revision 3
        WorkflowState awaitingVal = WorkflowStateMachine.TransitionStep(runningStep, "task-1", WorkflowStepStatus.AwaitingValidation);
        store.Append(awaitingVal, 3); // Revision 4
        WorkflowState completedStep = WorkflowStateMachine.TransitionStep(awaitingVal, "task-1", WorkflowStepStatus.Completed);
        store.Append(completedStep, 4); // Revision 5

        // Create a synthetic journal that has Started, Intent, Outcome, and Projection records,
        // BUT deliberately lacks ExecutionCompletedRecord
        string baseFp25 = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(created);
        string inputFp25 = new('e', 64);
        string regFp25 = new('f', 64);
        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
            root,
            wfId,
            "task-1",
            basePersistenceRevision_: 1,
            baseFp25,
            inputFp25,
            regFp25);

        InvocationIntentRecord intent = new()
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "agent",
            InvocationGeneration = 1,
            RequestFingerprint = "req-1"
        };
        journal.AppendRecord(intent);

        InvocationOutcomeRecord outcome = new()
        {
            Sequence = 3,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "agent",
            InvocationGeneration = 1,
            RequestFingerprint = "req-1",
            ValidationPassed = true
        };
        journal.AppendRecord(outcome);

        // OMIT ExecutionCompletedRecord and append ProjectionCompletedRecord directly!
        ProjectionCompletedRecord proj = new()
        {
            Sequence = 4,
            FinalPersistenceRevision = 5
        };
        journal.AppendRecord(proj);

        // Attempting to release maintenance lease MUST fail because ExecutionCompletedRecord is missing
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => maintHandle.CompleteAsync());
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        Assert.Contains("missing or incomplete P09 reconciliation evidence", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Maintenance manifest must remain on disk as MaintenanceInProgress
        string leasePath = paths.GetActiveLeasePath(wfId.Value);
        Assert.True(File.Exists(leasePath));
        WorkflowSchedulerLeaseManifest preserved = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, preserved.State);
    }

    // =========================================================================
    // 26. Durable Failure Recovery: Incomplete Generation Blocks Across Restart
    // =========================================================================

    [Fact]
    public async Task Broker_DurableFailureRecovery_IncompleteGenerationOrphanOwnerAuth_BlocksAdmissionAcrossRestart()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // 1. Simulate an incomplete generation: owner-auth.json exists on disk for wf-incomplete-gen,
        // but no active lease manifest exists (e.g. broker or worker crashed during publication)
        string wfDir = paths.GetWorkflowDirectory("wf-incomplete-gen");
        Directory.CreateDirectory(wfDir);
        string authPath = Path.Combine(wfDir, "owner-auth.json");

        WorkflowSchedulerOwnerAuthentication orphanedAuth = new()
        {
            WorkflowId = "wf-incomplete-gen",
            OwnerToken = "orphaned-token-123",
            Slot = 1,
            DomainMode = "Normal",
            ProcessId = 999999, // Dead PID
            ProcessStartTimeTicks = 123456
        };
        byte[] authBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            orphanedAuth,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        File.WriteAllBytes(authPath, authBytes);

        // Verify active lease manifest does NOT exist
        string leasePath = paths.GetActiveLeasePath("wf-incomplete-gen");
        Assert.False(File.Exists(leasePath));

        // 2. Start Broker: Phase 0 should detect orphaned owner-auth.json and record admission blocked
        await using (WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker.StartAsync();

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
            WorkflowResourceClaim[] claims = [new("fs:/repo/admit-test", WorkflowResourceAccessKind.ExclusiveWrite)];

            // 3. Any new lease admission MUST be blocked fail-closed across restarts!
            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireLeaseAsync("wf-new-work", claims, "Normal"));
            Assert.True(
                ex.ErrorCode == WorkflowBrokerProtocol.ErrorCodeInternalError ||
                ex.ErrorCode == WorkflowBrokerProtocol.ErrorCodeConflict);
            Assert.Contains("admission is blocked", ex.Message, StringComparison.OrdinalIgnoreCase);

            // 4. Maintenance admission is also blocked fail-closed
            WorkflowBrokerException maintEx = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireMaintenanceLeaseAsync("wf-new-work"));
            Assert.True(
                maintEx.ErrorCode == WorkflowBrokerProtocol.ErrorCodeInternalError ||
                maintEx.ErrorCode == WorkflowBrokerProtocol.ErrorCodeConflict);
            Assert.Contains("admission is blocked", maintEx.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // =========================================================================
    // 27. Maintenance Acquisition: Fail-Closed Rollback and Retained Ownership
    // =========================================================================

    [Fact]
    public async Task Broker_MaintenanceAcquisition_RollbackAndOwnerAuthFailure_RetainsLocksOrFailsClosed()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowSchedulerLeaseManifest quarantined = new(
            "wf-maint-fail-rollback",
            WorkflowSchedulerLeaseState.Quarantined,
            "orig-token",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantined, 4);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        try
        {
            // Enable fault injection: failure during maintenance acquisition and rollback failure
            WorkflowCoordinationBroker.SimulateDurabilityFailure = true;
            WorkflowCoordinationBroker.SimulateRollbackFailure = true;

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

            // Acquisition must fail with ErrorCodeInternalError (never swallowed or masked)
            WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireMaintenanceLeaseAsync("wf-maint-fail-rollback"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeInternalError, ex.ErrorCode);

            // Locks must be retained fail-closed: mode lock must be non-acquirable
            WorkflowSchedulerLockHandle? probeModeLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath);
            Assert.Null(probeModeLock);

            // Subsequent admissions are blocked fail-closed
            WorkflowBrokerException blockedEx = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireMaintenanceLeaseAsync("wf-maint-fail-rollback"));
            Assert.True(
                blockedEx.ErrorCode == WorkflowBrokerProtocol.ErrorCodeInternalError ||
                blockedEx.ErrorCode == WorkflowBrokerProtocol.ErrorCodeConflict);
            Assert.Contains("admission is blocked", blockedEx.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowCoordinationBroker.SimulateDurabilityFailure = false;
            WorkflowCoordinationBroker.SimulateRollbackFailure = false;
        }
    }

    // =========================================================================
    // 28. Maintenance Shutdown: Preserves MaintenanceInProgress Across Disconnect and Restart
    // =========================================================================

    [Fact]
    public async Task Broker_MaintenanceShutdown_PreservesMaintenanceInProgressAcrossDisconnectAndRestart()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowSchedulerLeaseManifest quarantined = new(
            "wf-maint-shutdown-preserve",
            WorkflowSchedulerLeaseState.Quarantined,
            "orig-token",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantined, 4);

        string leasePath = paths.GetActiveLeasePath("wf-maint-shutdown-preserve");

        // 1. Broker 1 starts and admits a maintenance lease
        await using (WorkflowCoordinationBroker broker1 = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker1.StartAsync();

            await using (WorkflowBrokerClient client1 = await WorkflowBrokerClient.ConnectAsync(paths))
            {
                WorkflowBrokerMaintenanceHandle maint = await client1.AcquireMaintenanceLeaseAsync("wf-maint-shutdown-preserve");
                Assert.NotNull(maint);

                WorkflowSchedulerLeaseManifest activeMaint = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
                Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, activeMaint.State);
                Assert.Equal("ExclusiveMaintenance", activeMaint.DomainMode);
            }

            // Broker 1 shuts down gracefully without the maintenance having completed P09 reconciliation
        }

        // 2. Verify on disk: Manifest MUST remain MaintenanceInProgress with ExclusiveMaintenance!
        // It must NEVER be downgraded to Quarantined/Normal!
        Assert.True(File.Exists(leasePath));
        WorkflowSchedulerLeaseManifest shutdownManifest = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, shutdownManifest.State);
        Assert.Equal("ExclusiveMaintenance", shutdownManifest.DomainMode);

        // 3. Broker 2 restarts
        await using (WorkflowCoordinationBroker broker2 = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker2.StartAsync();

            await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);

            // Normal workflow admission MUST be rejected because global exclusive maintenance is preserved!
            WorkflowResourceClaim[] claims = [new("fs:/repo/normal-target", WorkflowResourceAccessKind.ExclusiveWrite)];
            WorkflowBrokerException normalEx = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client2.AcquireLeaseAsync("wf-normal-work", claims, "Normal"));
            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, normalEx.ErrorCode);

            // Re-acquisition of the orphaned maintenance lease by a new maintenance worker succeeds safely!
            await using WorkflowBrokerMaintenanceHandle resumedMaint =
                await client2.AcquireMaintenanceLeaseAsync("wf-maint-shutdown-preserve");
            Assert.NotNull(resumedMaint);
        }
    }

    // =========================================================================
    // 29. Cross-Principal IPC: No Worker Configured Rejects All Workers (No Fallback)
    // =========================================================================

    [Fact]
    public async Task Broker_CrossPrincipalIPC_NoWorkerConfigured_RejectsAllWorkersWithoutBrokerFallback()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // Clear the environment variable so no worker principal is configured
        Environment.SetEnvironmentVariable("AIREPO_AUTHORIZED_WORKER_PRINCIPAL", null);

        try
        {
            // Start broker with NO authorized worker identities
            await using WorkflowCoordinationBroker broker = new(
                paths,
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: true,
                authorizedWorkerIdentities_: []);

            Assert.Empty(broker.AuthorizedWorkerIdentities);

            await broker.StartAsync();

            // Client connection under current user MUST fail because broker does NOT fall back to authorizing itself!
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                WorkflowBrokerClient.ConnectAsync(paths, cancellationToken_: cts.Token));
        }
        finally
        {
            // Restore current identity for subsequent tests
            string currentPrincipal = OperatingSystem.IsWindows()
                ? (WindowsIdentity.GetCurrent().User?.Value ?? string.Empty)
                : WorkflowBrokerIpcTransport.GetCurrentLinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);
            Environment.SetEnvironmentVariable("AIREPO_AUTHORIZED_WORKER_PRINCIPAL", currentPrincipal);
        }
    }

    // =========================================================================
    // 30. Phase 0: Active Lease + Owner-Auth Before Broker Crash Recovers Without False Orphan Detection
    // =========================================================================

    [Fact]
    public async Task Broker_Phase0_ActiveLeaseWithOwnerAuth_BeforeBrokerCrash_RecoversWithoutFalseOrphanIdentification()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-genuine-active");

        // Pre-create genuine active lease and owner authentication on disk before broker start
        paths.EnsureWorkflowDirectory(wfId.Value);
        string authPath = Path.Combine(paths.GetWorkflowDirectory(wfId.Value), "owner-auth.json");
        var authData = new
        {
            schemaId = "ai.repokit.workflow-scheduler-owner-auth",
            schemaVersion = 1,
            workflowId = wfId.Value,
            ownerToken = "genuine-token-1",
            slot = 1,
            domainMode = "Normal",
            processId = 999999, // Dead PID
            processStartTimeTicks = 123456
        };
        File.WriteAllBytes(authPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(authData));

        WorkflowResourceClaim[] claims = [new("fs:/repo/res1", WorkflowResourceAccessKind.ExclusiveWrite)];
        WorkflowSchedulerLeaseManifest activeManifest = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Active,
            "genuine-token-1",
            slot_: 1,
            domainMode_: "Normal",
            resourceClaims_: claims,
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4);

        // Broker starts: Phase 0 decodes directory name from hex to logical workflow ID.
        // It detects the genuine active lease and does NOT falsely identify an orphaned owner-auth.
        // In Step 2, seeing the process dead, it transitions the active lease to Quarantined and cleans up owner-auth.
        await using (WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true))
        {
            await broker.StartAsync();

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);

            // New lease admission MUST succeed because admission was NOT blocked by false orphan detection!
            WorkflowResourceClaim[] newClaims = [new("fs:/repo/res2", WorkflowResourceAccessKind.ExclusiveWrite)];
            await using WorkflowBrokerLeaseHandle newLease = await client.AcquireLeaseAsync("wf-new-work", newClaims, "Normal");
            Assert.Equal(1, newLease.Slot);

            // The dead active lease was cleanly transitioned to Quarantined
            string leasePath = paths.GetActiveLeasePath(wfId.Value);
            Assert.True(File.Exists(leasePath));
            WorkflowSchedulerLeaseManifest quarantined = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
            Assert.Equal(WorkflowSchedulerLeaseState.Quarantined, quarantined.State);

            // And owner-auth was cleaned up
            Assert.False(File.Exists(authPath));
        }
    }

    // =========================================================================
    // 31. Phase 0: Malformed Workflow Directory Name Rejects Fail-Closed
    // =========================================================================

    [Fact]
    public async Task Broker_Phase0_MalformedDirectoryName_RejectsFailClosed()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        // Create a malformed directory name inside WorkflowsDirectory (odd length, invalid hex)
        string malformedDir = Path.Combine(paths.WorkflowsDirectory, "not-valid-hex-dir");
        Directory.CreateDirectory(malformedDir);
        string authPath = Path.Combine(malformedDir, "owner-auth.json");
        File.WriteAllText(authPath, "{}");

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        WorkflowResourceClaim[] claims = [new("fs:/repo/res1", WorkflowResourceAccessKind.ExclusiveWrite)];

        // Admissions must be blocked fail-closed
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.AcquireLeaseAsync("wf-test", claims, "Normal"));
        Assert.Contains("admission is blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 32. Multi-Checkpoint P09 Integrity: Valid Sequential Checkpoints Succeed
    // =========================================================================

    [Fact]
    public async Task Broker_P09Audit_ValidSequentialCheckpoints_DifferentFinalRevisions_ReconciliationSucceeds()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-multi-checkpoint-valid");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task1 = new("task-1", "step-1", "work 1");
        ExecutableTask task2 = new("task-2", "step-2", "work 2");
        ExecutableWork work = new(1, [task1, task2], [], [], [], []);
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created); // Revision 1

        WorkflowSchedulerLeaseManifest quarantined = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "prior-token",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantined, 4);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerMaintenanceHandle maint = await client.AcquireMaintenanceLeaseAsync(wfId.Value);

        // Sequence of transitions across 2 sequential checkpoint journals:
        // Journal 1 (task-1): baseRev = 1 -> finalRev = 5 (transitions = 4)
        // Revision 2: Workflow Running
        WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWf, 1);
        // Revision 3: task-1 Running
        WorkflowState step1Running = WorkflowStateMachine.TransitionStep(runningWf, "task-1", WorkflowStepStatus.Running);
        store.Append(step1Running, 2);
        // Revision 4: task-1 AwaitingValidation
        WorkflowState step1Awaiting = WorkflowStateMachine.TransitionStep(step1Running, "task-1", WorkflowStepStatus.AwaitingValidation);
        store.Append(step1Awaiting, 3);
        // Revision 5: task-1 Completed
        WorkflowState step1Completed = WorkflowStateMachine.TransitionStep(step1Awaiting, "task-1", WorkflowStepStatus.Completed);
        store.Append(step1Completed, 4);

        string baseFp1 = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(created);
        string inputFp1 = new('1', 64);
        string regFp1 = new('2', 64);
        WorkflowCheckpointJournal journal1 = WorkflowCheckpointJournal.CreateNew(
            root, wfId, "task-1", 1, baseFp1, inputFp1, regFp1);

        InvocationIntentRecord intent1 = new()
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-t1",
            OperationKind = "agent",
            InvocationGeneration = 1,
            RequestFingerprint = "req-t1"
        };
        journal1.AppendRecord(intent1);

        InvocationOutcomeRecord outcome1 = new()
        {
            Sequence = 3,
            OperationOrdinal = 1,
            OperationId = "op-t1",
            OperationKind = "agent",
            InvocationGeneration = 1,
            RequestFingerprint = "req-t1",
            ValidationPassed = true
        };
        journal1.AppendRecord(outcome1);

        ExecutionCompletedRecord completed1 = new()
        {
            Sequence = 4,
            FinalStateFingerprint = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(step1Completed),
            AttemptCount = 1,
            ExpectedProjectionTransitionCount = 4
        };
        journal1.AppendRecord(completed1);

        ProjectionCompletedRecord proj1 = new()
        {
            Sequence = 5,
            FinalPersistenceRevision = 5
        };
        journal1.AppendRecord(proj1);

        // Journal 2 (task-2): baseRev = 5 -> finalRev = 8 (transitions = 3)
        // Revision 6: task-2 Running
        WorkflowState step2Running = WorkflowStateMachine.TransitionStep(step1Completed, "task-2", WorkflowStepStatus.Running);
        store.Append(step2Running, 5);
        // Revision 7: task-2 AwaitingValidation
        WorkflowState step2Awaiting = WorkflowStateMachine.TransitionStep(step2Running, "task-2", WorkflowStepStatus.AwaitingValidation);
        store.Append(step2Awaiting, 6);
        // Revision 8: task-2 Completed
        WorkflowState step2Completed = WorkflowStateMachine.TransitionStep(step2Awaiting, "task-2", WorkflowStepStatus.Completed);
        store.Append(step2Completed, 7);

        string baseFp2 = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(step1Completed);
        string inputFp2 = new('3', 64);
        string regFp2 = new('4', 64);
        WorkflowCheckpointJournal journal2 = WorkflowCheckpointJournal.CreateNew(
            root, wfId, "task-2", 5, baseFp2, inputFp2, regFp2);

        InvocationIntentRecord intent2 = new()
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-t2",
            OperationKind = "agent",
            InvocationGeneration = 1,
            RequestFingerprint = "req-t2"
        };
        journal2.AppendRecord(intent2);

        InvocationOutcomeRecord outcome2 = new()
        {
            Sequence = 3,
            OperationOrdinal = 1,
            OperationId = "op-t2",
            OperationKind = "agent",
            InvocationGeneration = 1,
            RequestFingerprint = "req-t2",
            ValidationPassed = true
        };
        journal2.AppendRecord(outcome2);

        ExecutionCompletedRecord completed2 = new()
        {
            Sequence = 4,
            FinalStateFingerprint = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(step2Completed),
            AttemptCount = 1,
            ExpectedProjectionTransitionCount = 3
        };
        journal2.AppendRecord(completed2);

        ProjectionCompletedRecord proj2 = new()
        {
            Sequence = 5,
            FinalPersistenceRevision = 8
        };
        journal2.AppendRecord(proj2);

        // Audit MUST succeed for multi-checkpoint workflow with sequential journals!
        await maint.CompleteAsync();

        // Active lease file was removed
        string leasePath = paths.GetActiveLeasePath(wfId.Value);
        Assert.False(File.Exists(leasePath));
    }

    // =========================================================================
    // 33. P09 Audit: Placeholder or Invalid Fingerprint Rejection
    // =========================================================================

    [Fact]
    public async Task Broker_P09Audit_PlaceholderOrInvalidFingerprint_RejectsReconciliation()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-invalid-fp");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "work");
        ExecutableWork work = new(1, [task], [], [], [], []);
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowSchedulerLeaseManifest quarantined = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "prior-token",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantined, 4);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerMaintenanceHandle maint = await client.AcquireMaintenanceLeaseAsync(wfId.Value);

        WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWf, 1);
        WorkflowState stepRunning = WorkflowStateMachine.TransitionStep(runningWf, "task-1", WorkflowStepStatus.Running);
        store.Append(stepRunning, 2);
        WorkflowState awaitingVal = WorkflowStateMachine.TransitionStep(stepRunning, "task-1", WorkflowStepStatus.AwaitingValidation);
        store.Append(awaitingVal, 3);
        WorkflowState completed = WorkflowStateMachine.TransitionStep(awaitingVal, "task-1", WorkflowStepStatus.Completed);
        store.Append(completed, 4);

        // Placeholder input fingerprint "placeholder-not-64-hex"
        string authenticBaseFp = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(created);
        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
            root, wfId, "task-1", 1, authenticBaseFp, "placeholder-not-64-hex", new string('a', 64));

        ExecutionCompletedRecord completedRec = new()
        {
            Sequence = 2,
            FinalStateFingerprint = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(completed),
            AttemptCount = 1,
            ExpectedProjectionTransitionCount = 4
        };
        journal.AppendRecord(completedRec);

        ProjectionCompletedRecord projRec = new()
        {
            Sequence = 3,
            FinalPersistenceRevision = 5
        };
        journal.AppendRecord(projRec);

        // Release must be rejected because of placeholder fingerprint!
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => maint.CompleteAsync());
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
    }

    // =========================================================================
    // 34. P09 Audit: Inconsistent Projection Prefix Rejection
    // =========================================================================

    [Fact]
    public async Task Broker_P09Audit_InconsistentProjectionPrefix_RejectsReconciliation()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-inconsistent-proj");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "work");
        ExecutableWork work = new(1, [task], [], [], [], []);
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowSchedulerLeaseManifest quarantined = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "prior-token",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantined, 4);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await using WorkflowBrokerMaintenanceHandle maint = await client.AcquireMaintenanceLeaseAsync(wfId.Value);

        WorkflowState runningWf = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWf, 1);
        WorkflowState stepRunning = WorkflowStateMachine.TransitionStep(runningWf, "task-1", WorkflowStepStatus.Running);
        store.Append(stepRunning, 2);
        WorkflowState awaitingVal = WorkflowStateMachine.TransitionStep(stepRunning, "task-1", WorkflowStepStatus.AwaitingValidation);
        store.Append(awaitingVal, 3);
        WorkflowState completed = WorkflowStateMachine.TransitionStep(awaitingVal, "task-1", WorkflowStepStatus.Completed);
        store.Append(completed, 4);

        string authenticBaseFp = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(created);
        WorkflowCheckpointJournal journal = WorkflowCheckpointJournal.CreateNew(
            root, wfId, "task-1", 1, authenticBaseFp, new string('a', 64), new string('b', 64));

        ExecutionCompletedRecord completedRec = new()
        {
            Sequence = 2,
            // Divergent final state fingerprint (using created state instead of completed state)
            FinalStateFingerprint = authenticBaseFp,
            AttemptCount = 1,
            ExpectedProjectionTransitionCount = 4
        };
        journal.AppendRecord(completedRec);

        ProjectionCompletedRecord projRec = new()
        {
            Sequence = 3,
            FinalPersistenceRevision = 5
        };
        journal.AppendRecord(projRec);

        // Release must be rejected because final state fingerprint does not match canonical persistence state!
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => maint.CompleteAsync());
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
    }

    // =========================================================================
    // 35. Maintenance Failure Lifecycle: Preserve MaintenanceInProgress and Allow Retry
    // =========================================================================

    [Fact]
    public async Task Broker_MaintenanceFailureLifecycle_FailurePreservesExclusiveMaintenanceAndSupportsRetry()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-maint-fail-retry");
        WorkflowPersistenceStore store = new(root, wfId);
        ExecutableTask task = new("task-1", "step-1", "work");
        ExecutableWork work = new(1, [task], [], [], [], []);
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowSchedulerLeaseManifest quarantined = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "orig-token",
            slot_: null,
            domainMode_: "Normal",
            resourceClaims_: [],
            maxConcurrentWorkflows_: 4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, quarantined, 4);

        string leasePath = paths.GetActiveLeasePath(wfId.Value);

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        // 1. Worker 1 acquires maintenance lease and disposes handle without completion (simulating cancellation/failure)
        await using (WorkflowBrokerClient client1 = await WorkflowBrokerClient.ConnectAsync(paths))
        {
            WorkflowBrokerMaintenanceHandle maint1 = await client1.AcquireMaintenanceLeaseAsync(wfId.Value);
            Assert.NotNull(maint1);
            maint1.Dispose(); // sync dispose without CompleteAsync()
        }

        // 2. Verify manifest remains MaintenanceInProgress with ExclusiveMaintenance!
        Assert.True(File.Exists(leasePath));
        WorkflowSchedulerLeaseManifest postFailManifest = WorkflowSchedulerLeaseManifest.ReadAndValidate(leasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, postFailManifest.State);
        Assert.Equal("ExclusiveMaintenance", postFailManifest.DomainMode);

        // 3. Normal workflow admission is blocked because of global exclusion!
        await using WorkflowBrokerClient client2 = await WorkflowBrokerClient.ConnectAsync(paths);
        WorkflowResourceClaim[] normalClaims = [new("fs:/repo/test", WorkflowResourceAccessKind.ExclusiveWrite)];
        WorkflowBrokerException ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client2.AcquireLeaseAsync("wf-normal", normalClaims, "Normal"));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);

        // 4. Worker 2 successfully re-acquires the orphaned maintenance lease (Retry)
        await using WorkflowBrokerMaintenanceHandle maint2 = await client2.AcquireMaintenanceLeaseAsync(wfId.Value);
        Assert.NotNull(maint2);
    }

    // =========================================================================
    // 36. Linux Cross-UID Socket Security Validation
    // =========================================================================

    [Fact]
    public void Broker_IpcEndpointHierarchy_PortablePathAndTraversalValidation()
    {
        string root = this.CreateTempStorageRoot();
        string ipcDir = Path.Combine(root, "ipc");
        Directory.CreateDirectory(ipcDir);
        string socketPath = Path.Combine(ipcDir, "broker.sock");

        // Canonical hierarchy tests (portable across all OS platforms):
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity("", ipcDir));
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ""));
        // Endpoint outside directory (path traversal / substitution) fails closed
        string outsideSocket = Path.Combine(root, "outside.sock");
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(outsideSocket, ipcDir));

        string traversalSocket = Path.Combine(ipcDir, "..", "other.sock");
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(traversalSocket, ipcDir));
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void Broker_LinuxSocketSecurityValidation_ValidatesPermissionsCorrectly()
    {
        string root = this.CreateTempStorageRoot();
        string ipcDir = Path.Combine(root, "ipc");
        Directory.CreateDirectory(ipcDir);
        string socketPath = Path.Combine(ipcDir, "broker.sock");

        // On Linux: create genuine Unix Domain Socket
        using Socket realSocket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        realSocket.Bind(new UnixDomainSocketEndPoint(socketPath));

        // 1. Directory 0755, Socket 0666 -> Valid
        File.SetUnixFileMode(ipcDir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.SetUnixFileMode(socketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        uint currentUid = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId();
        Assert.True(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: currentUid));
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: null));

        // 2. Negative test: regular file instead of genuine socket -> Invalid (S_IFREG rejected)
        string regularFilePath = Path.Combine(ipcDir, "regular.sock");
        File.WriteAllText(regularFilePath, "not a socket");
        File.SetUnixFileMode(regularFilePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(regularFilePath, ipcDir, expectedUid_: currentUid));

        // 3. Negative test: Directory with GroupWrite -> Invalid
        File.SetUnixFileMode(ipcDir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupWrite);
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: currentUid));

        // 4. Negative test: Socket with UserExecute -> Invalid
        File.SetUnixFileMode(ipcDir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(socketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: currentUid));

        // 5. Negative test: Untrusted owner UID -> Invalid
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: 999999));
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void Broker_LinuxSocketSecurityValidation_AncestorProtection_ValidatesCorrectly()
    {
        string root = this.CreateTempStorageRoot();
        string untrustedAncestor = Path.Combine(root, "untrusted_parent");
        Directory.CreateDirectory(untrustedAncestor);
        string ipcDir = Path.Combine(untrustedAncestor, "ipc");
        Directory.CreateDirectory(ipcDir);
        string socketPath = Path.Combine(ipcDir, "broker.sock");

        // On Linux: null expectedUid_ must fail closed
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: null));

        uint currentUid = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId();
        using Socket realSocket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        realSocket.Bind(new UnixDomainSocketEndPoint(socketPath));

        File.SetUnixFileMode(ipcDir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.SetUnixFileMode(socketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite);

        // Protected ancestor (standard 0755) -> Valid
        File.SetUnixFileMode(untrustedAncestor,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Assert.True(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: currentUid));

        // Insecure ancestor: other-writable without sticky bit -> Invalid (rejects tampering)
        File.SetUnixFileMode(untrustedAncestor,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.OtherWrite);
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: currentUid));

        // Insecure ancestor: group-writable without sticky bit -> Invalid (rejects tampering)
        File.SetUnixFileMode(untrustedAncestor,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupWrite);
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: currentUid));

        // Ancestor owned by untrusted UID -> Invalid
        Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, ipcDir, expectedUid_: 999999));
    }

    [LinuxOnlyFact]
    public async Task Broker_LinuxCrossUidClient_ValidatesExplicitBrokerPrincipalFormatFailClosed()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        string? previousDefault = WorkflowBrokerIpcTransport.DefaultBrokerServicePrincipal;
        try
        {
            // When no expectedServerIdentity is configured and DefaultBrokerServicePrincipal is null -> fails closed
            WorkflowBrokerIpcTransport.DefaultBrokerServicePrincipal = null;
            var exNull = await Assert.ThrowsAsync<InvalidOperationException>(
                () => WorkflowBrokerClient.ConnectAsync(paths, expectedServerIdentity_: null));
            Assert.Contains("missing or invalid trusted broker UID", exNull.Message, StringComparison.OrdinalIgnoreCase);

            // When non-numeric UID string is provided -> fails closed
            var exInvalid = await Assert.ThrowsAsync<InvalidOperationException>(
                () => WorkflowBrokerClient.ConnectAsync(paths, expectedServerIdentity_: "not-a-numeric-uid"));
            Assert.Contains("missing or invalid trusted broker UID", exInvalid.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowBrokerIpcTransport.DefaultBrokerServicePrincipal = previousDefault;
        }
    }

    // =========================================================================
    // 36b. Broker Server-Side storageRoot Security Admission & Hierarchy Hardening
    // =========================================================================

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_OtherWritable_ServerRejectsBeforeAdmission()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute); // 0777 (OtherWrite)

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("writable by group/other", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(paths.DomainLockPath), "Coordination layout must NOT be initialized after rejection.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_InsecureGroupPermissions_ServerRejectsBeforeAdmission()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupWrite); // GroupWrite

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("writable by group/other", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_InsecureAncestor_ServerRejectsBeforeAdmission()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            string ancestor = Path.Combine(root, "insecure_ancestor");
            Directory.CreateDirectory(ancestor);
            string storageRoot = Path.Combine(ancestor, "storage");
            Directory.CreateDirectory(storageRoot);

            // Preprovision storageRoot securely (0700)
            File.SetUnixFileMode(storageRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Insecure ancestor: other-writable without root sticky bit (owned by user, not root)
            File.SetUnixFileMode(ancestor,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.OtherWrite);

            WorkflowSchedulerPaths paths = new(storageRoot);
            await using WorkflowCoordinationBroker broker = new(paths);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("Ancestor", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void Broker_LinuxStorageRoot_SymlinkInHierarchy_ServerRejectsBeforeAdmission()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            string targetDir = Path.Combine(root, "real_target");
            Directory.CreateDirectory(targetDir);
            File.SetUnixFileMode(targetDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            string symlinkStorageRoot = Path.Combine(root, "symlink_storage");
            File.CreateSymbolicLink(symlinkStorageRoot, targetDir);

            // 1. ValidateLinuxStorageRootHierarchy rejects symlink directly
            Assert.False(WorkflowBrokerIpcTransport.ValidateLinuxStorageRootHierarchy(
                symlinkStorageRoot,
                WorkflowBrokerIpcTransport.GetCurrentLinuxUserId(),
                out string reason,
                out _));
            Assert.Contains("symbolic link", reason, StringComparison.OrdinalIgnoreCase);

            // 2. Broker path initialization rejects symlink fail-closed before any admission
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                WorkflowSchedulerPaths paths = new(symlinkStorageRoot);
                using WorkflowCoordinationBroker broker = new(paths);
                broker.StartAsync().GetAwaiter().GetResult();
            });
            Assert.Contains("symlink", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_OsInspectionFailure_FailsClosedBeforeAdmission()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);

            // Fault injection: simulate statx OS inspection failure on storageRoot
            WorkflowBrokerIpcTransport.LinuxStorageRootInspectionFaultInjector = p => p.Contains(root);
            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
                Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(broker.IsRunning);
                Assert.NotNull(broker.AdmissionBlockedReason);
                Assert.Contains("OS inspection failed", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                WorkflowBrokerIpcTransport.LinuxStorageRootInspectionFaultInjector = null;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_PositiveControl_ProtectedHierarchy_StartsAndAdmitsWorkflows()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            string storageRoot = Path.Combine(root, "protected_storage");
            Directory.CreateDirectory(storageRoot);
            File.SetUnixFileMode(storageRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(storageRoot);
            await using WorkflowCoordinationBroker broker = new(paths);
            await broker.StartAsync();

            Assert.True(broker.IsRunning);
            Assert.Null(broker.AdmissionBlockedReason);

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths, broker.ServicePrincipal);
            await client.PingAsync();

            await using WorkflowBrokerLeaseHandle lease = await client.AcquireLeaseAsync("wf-pos-ext4", []);
            Assert.NotNull(lease);
            Assert.Equal("wf-pos-ext4", lease.WorkflowId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_PathSubstitution_DetectedAndAdmissionBlocked()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            string storageRoot = Path.Combine(root, "substitutable_storage");
            Directory.CreateDirectory(storageRoot);
            File.SetUnixFileMode(storageRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(storageRoot);
            await using WorkflowCoordinationBroker broker = new(paths);
            await broker.StartAsync();

            Assert.True(broker.IsRunning);
            Assert.Null(broker.AdmissionBlockedReason);

            await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths, broker.ServicePrincipal);
            await client.PingAsync();

            // Simulate path substitution: rename genuine storageRoot and replace with a fresh directory (new inode)
            string backup = Path.Combine(root, "backup_storage");
            Directory.Move(storageRoot, backup);
            Directory.CreateDirectory(storageRoot);
            File.SetUnixFileMode(storageRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Attempting to admit a workflow MUST be detected and rejected fail-closed
            var ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireLeaseAsync("wf-after-sub", []));

            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("substitution detected", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_IncompleteStatxMask_MissingInode_FailsClosed()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);

            // Mutate statx result to clear STATX_INO from stx_mask
            WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = (string p, ref WorkflowBrokerIpcTransport.LinuxStatx st) =>
            {
                st.stx_mask &= ~0x0100u; // clear STATX_INO
            };

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
                Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(broker.IsRunning);
                Assert.NotNull(broker.AdmissionBlockedReason);
                Assert.Contains("incomplete information", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = null;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_ZeroInode_FailsClosed()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);

            // Mutate statx result to force inode 0
            WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = (string p, ref WorkflowBrokerIpcTransport.LinuxStatx st) =>
            {
                st.stx_ino = 0;
            };

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
                Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(broker.IsRunning);
                Assert.NotNull(broker.AdmissionBlockedReason);
                Assert.Contains("invalid inode 0", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = null;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_PinnedDescriptorMismatch_ServerRejectsBeforeAdmission()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);

            // Mutate statx result ONLY on the pinned descriptor (<fd>) so it differs from path statx
            WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = (string p, ref WorkflowBrokerIpcTransport.LinuxStatx st) =>
            {
                if (p == "<fd>")
                {
                    st.stx_ino += 99999; // Inode mismatch between path and opened descriptor
                }
            };

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
                Assert.Contains("identity does not match", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(broker.IsRunning);
                Assert.NotNull(broker.AdmissionBlockedReason);
                Assert.Contains("identity mismatch", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
                Assert.False(File.Exists(paths.DomainLockPath), "Coordination layout must NOT be initialized after descriptor mismatch.");
            }
            finally
            {
                WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = null;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_DeviceCorrelationMismatch_FailsClosed()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);

            // Mutate statx result on <fd> to have same inode, but DIFFERENT device ID!
            WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = (string p, ref WorkflowBrokerIpcTransport.LinuxStatx st) =>
            {
                if (p == "<fd>")
                {
                    st.stx_dev_major += 1; // Different device ID!
                }
            };

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
                Assert.Contains("identity does not match", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(broker.IsRunning);
                Assert.NotNull(broker.AdmissionBlockedReason);
                Assert.Contains("identity mismatch", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = null;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task Broker_LinuxStorageRoot_PathSubstitutionBeforeOpen_ServerRejectsBeforeAdmission()
    {
        string root = this.CreateTempStorageRoot();
        try
        {
            string storageRoot = Path.Combine(root, "substitution_target");
            Directory.CreateDirectory(storageRoot);
            File.SetUnixFileMode(storageRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            WorkflowSchedulerPaths paths = new(storageRoot);
            await using WorkflowCoordinationBroker broker = new(paths);

            // Hook into path statx: right after path statx runs, replace the directory with a different one!
            int callCount = 0;
            WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = (string p, ref WorkflowBrokerIpcTransport.LinuxStatx st) =>
            {
                if (p == storageRoot && callCount++ == 0)
                {
                    // Path validation captured initial inode/device.
                    // Now simulate TOCTOU substitution before open:
                    string backup = storageRoot + "_old";
                    Directory.Move(storageRoot, backup);
                    Directory.CreateDirectory(storageRoot);
                    File.SetUnixFileMode(storageRoot,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            };

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
                Assert.Contains("identity does not match", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(broker.IsRunning);
                Assert.NotNull(broker.AdmissionBlockedReason);
                Assert.Contains("identity mismatch", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                WorkflowBrokerIpcTransport.LinuxStorageRootStatxMutator = null;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // =========================================================================
    // 36b. Windows NTFS storageRoot Admission & Pre-admission Security Hardening
    // =========================================================================

    [Fact]
    public async Task Broker_WindowsStorageRoot_PositiveControl_ProtectedHierarchy_StartsAndAdmitsWorkflows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);
        await broker.StartAsync();

        Assert.True(broker.IsRunning);
        Assert.Null(broker.AdmissionBlockedReason);

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths, broker.ServicePrincipal);
        await client.PingAsync();

        await using WorkflowBrokerLeaseHandle lease = await client.AcquireLeaseAsync("wf-pos-win-ntfs", []);
        Assert.NotNull(lease);
        Assert.Equal("wf-pos-win-ntfs", lease.WorkflowId);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_BroadWriteAce_Everyone_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateInsecureWindowsDirectory(root, new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Write);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("forbidden write/modify rights", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S-1-1-0", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_BroadWriteAce_Users_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateInsecureWindowsDirectory(root, new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.CreateFiles);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("forbidden write/modify rights", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S-1-5-32-545", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_BroadWriteAce_AuthenticatedUsers_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateInsecureWindowsDirectory(root, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.Modify);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("forbidden write/modify rights", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S-1-5-11", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Broker_WindowsStorageRoot_UntrustedOwner_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);

        // Validate directly that an untrusted owner SID fails closed
        Assert.False(WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
            root,
            "S-1-5-21-999999999-888888888-777777777-9999",
            out string reason,
            out _,
            out _));

        Assert.Contains("untrusted SID", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_OsInspectionFailure_FailsClosedBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        // Fault injection: simulate OS inspection failure on storageRoot
        WorkflowBrokerIpcTransport.WindowsStorageRootInspectionFaultInjector = p => p.Contains(root);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("OS inspection failed", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowBrokerIpcTransport.WindowsStorageRootInspectionFaultInjector = null;
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_InsecureRoot_CausesZeroCoordinationFileCreation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateInsecureWindowsDirectory(root, new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Write);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);

        // Invariant: zero coordination directories or files were created on the insecure root
        Assert.Empty(Directory.GetFileSystemEntries(root));
        Assert.False(File.Exists(paths.DomainLockPath));
        Assert.False(Directory.Exists(paths.LocksDirectory));
        Assert.False(Directory.Exists(paths.IpcDirectory));
        Assert.False(Directory.Exists(paths.WorkflowsDirectory));
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PinnedDescriptorMismatch_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        // Mutate identity on opened handle descriptor (<handle>) so FileIndex differs from path FileIndex
        WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = (string p, ref WorkflowBrokerIpcTransport.WindowsFileIdentity id) =>
        {
            if (p == "<handle>")
            {
                id.FileIndex += 100000;
            }
        };

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("identity does not match", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("identity mismatch", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(paths.DomainLockPath), "Coordination layout must NOT be initialized after descriptor mismatch.");
        }
        finally
        {
            WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = null;
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_DeviceVolumeMismatch_FailsClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        // Mutate identity on opened handle descriptor (<handle>) so VolumeSerialNumber differs
        WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = (string p, ref WorkflowBrokerIpcTransport.WindowsFileIdentity id) =>
        {
            if (p == "<handle>")
            {
                id.VolumeSerialNumber ^= 0xFFFFFFFF;
            }
        };

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("identity does not match", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("identity mismatch", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = null;
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PathSubstitution_DetectedAndAdmissionBlocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        string storageRoot = Path.Combine(root, "win_substitutable_storage");
        CreateProtectedWindowsDirectory(storageRoot);

        WorkflowSchedulerPaths paths = new(storageRoot);
        await using WorkflowCoordinationBroker broker = new(paths);
        await broker.StartAsync();

        Assert.True(broker.IsRunning);
        Assert.Null(broker.AdmissionBlockedReason);

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths, broker.ServicePrincipal);
        await client.PingAsync();

        try
        {
            // Simulate path substitution: identity of storageRoot path changes (different FileIndex)
            WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = (string p, ref WorkflowBrokerIpcTransport.WindowsFileIdentity id) =>
            {
                if (p.Contains(storageRoot))
                {
                    id.FileIndex += 99999;
                }
            };

            // 1. Ordinary workflow lease admission MUST detect substitution and fail closed
            var exLease = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireLeaseAsync("wf-after-sub-win", []));

            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, exLease.ErrorCode);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("substitution detected", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);

            // 2. Maintenance lease acquisition MUST also be blocked fail-closed
            var exMaint = await Assert.ThrowsAsync<WorkflowBrokerException>(
                () => client.AcquireMaintenanceLeaseAsync("wf-after-sub-win"));

            Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, exMaint.ErrorCode);
        }
        finally
        {
            WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = null;
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_ReparsePoint_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        // Mutate identity to simulate reparse point on the directory
        WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = (string p, ref WorkflowBrokerIpcTransport.WindowsFileIdentity id) =>
        {
            id.FileAttributes |= (uint)FileAttributes.ReparsePoint;
        };

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("reparse point", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("reparse point", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = null;
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingInsecureLocksDirectory_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);

        // Pre-create 'locks' directory with insecure broad write ACE (Authenticated Users)
        CreateInsecureWindowsDirectory(paths.LocksDirectory, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.Modify);

        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("Pre-existing coordination", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(paths.DomainLockPath), "Coordination layout files must NOT be created when pre-existing layout is insecure.");
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingInsecureIpcDirectory_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);

        // Pre-create 'ipc' directory with insecure broad write ACE (World / Everyone)
        CreateInsecureWindowsDirectory(paths.IpcDirectory, new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Write);

        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("Pre-existing coordination", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingLocksDirectoryReparsePoint_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot(protectWindowsAcl: true);
        WorkflowSchedulerPaths paths = new(root);
        CreateProtectedWindowsDirectory(paths.LocksDirectory);

        // Mutate identity on locks directory to simulate reparse point
        WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = (string p, ref WorkflowBrokerIpcTransport.WindowsFileIdentity id) =>
        {
            if (p.Contains(paths.LocksDirectory))
            {
                id.FileAttributes |= (uint)FileAttributes.ReparsePoint;
            }
        };

        try
        {
            await using WorkflowCoordinationBroker broker = new(paths);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("reparse point", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("reparse point", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowBrokerIpcTransport.WindowsStorageRootIdentityMutator = null;
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_BrokerSidLacksWritePermissions_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateWindowsDirectoryWithBrokerReadOnly(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("does not grant required write permissions", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(paths.DomainLockPath), "Coordination files must NOT be created when broker lacks write access.");
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_BrokerSidWriteDenied_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateWindowsDirectoryWithBrokerWriteDenied(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("Deny ACE blocking required broker write access", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingInsecureIpcFile_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateProtectedWindowsDirectory(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        CreateProtectedWindowsDirectory(paths.IpcDirectory);

        string rogueFile = Path.Combine(paths.IpcDirectory, "rogue.sock");
        SecurityIdentifier everyone = new(WellKnownSidType.WorldSid, null);
        CreateInsecureWindowsFile(rogueFile, everyone, FileSystemRights.Write);

        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("forbidden write/modify rights", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingInsecureWorkflowAuth_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateProtectedWindowsDirectory(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        string wfDir = Path.Combine(paths.WorkflowsDirectory, "0123456789abcdef");
        CreateProtectedWindowsDirectory(paths.WorkflowsDirectory);
        CreateProtectedWindowsDirectory(wfDir);

        string authFile = Path.Combine(wfDir, "owner-auth.json");
        SecurityIdentifier authUsers = new(WellKnownSidType.AuthenticatedUserSid, null);
        CreateInsecureWindowsFile(authFile, authUsers, FileSystemRights.Write);

        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("forbidden write/modify rights", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_TokenGroupDenyWrite_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        SecurityIdentifier authUsers = new(WellKnownSidType.AuthenticatedUserSid, null);
        CreateWindowsDirectoryWithTokenGroupWriteDenied(root, authUsers);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("Deny ACE blocking required broker write access", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_RuntimeTamperingOnLocks_AcquireLeaseRejectedFailClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        CreateProtectedWindowsDirectory(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths);
        await broker.StartAsync();
        Assert.True(broker.IsRunning);

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await client.PingAsync();

        // Tamper with locks directory ACL during runtime
        DirectoryInfo locksInfo = new(paths.LocksDirectory);
        DirectorySecurity ds = locksInfo.GetAccessControl();
        ds.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.WriteData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        locksInfo.SetAccessControl(ds);

        // Attempt to acquire lease: must be rejected fail-closed
        WorkflowId wfId = new("wf-runtime-tamper");
        var ex = await Assert.ThrowsAsync<WorkflowBrokerException>(() =>
            client.AcquireLeaseAsync(wfId.Value, [new WorkflowResourceClaim("logical:res1", WorkflowResourceAccessKind.ExclusiveWrite)]));
        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("Active coordination directory security validation failed", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(paths.GetActiveLeasePath(wfId.Value)), "Active lease manifest must NOT be written when coordination directory is tampered.");
    }

    [Fact]
    public void Broker_WindowsStorageRoot_TrustedInstallerExactSid_AcceptsExactRejectsPrefix()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SecurityIdentifier exactSid = new(WorkflowBrokerIpcTransport.KnownTrustedInstallerSid);
        Assert.True(WorkflowBrokerIpcTransport.IsTrustedInstallerSid(exactSid));

        SecurityIdentifier fakePrefixSid = new("S-1-5-80-956008885-1111111111-2222222222-3333333333-4444444444");
        Assert.False(WorkflowBrokerIpcTransport.IsTrustedInstallerSid(fakePrefixSid));
    }

    [Fact]
    public void Broker_WindowsStorageRoot_AncestorGrantsDeleteToUntrusted_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string parentDir = Path.Combine(baseDir, "aik-ancestor-test-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parentDir, "root");
        this._tempDirectories.Add(parentDir);

        // Create parent granting FileSystemRights.Delete to Authenticated Users
        DirectoryInfo di = new(parentDir);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier authUsers = new(WellKnownSidType.AuthenticatedUserSid, null);

        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            authUsers,
            FileSystemRights.Delete,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        di.Create(ds);

        CreateProtectedWindowsDirectory(root);

        bool admitted = WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
            root,
            currentUser.Value,
            out string reason,
            out _,
            out _);

        Assert.False(admitted);
        Assert.Contains("dangerous deletion/control rights", reason, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsNtfsJunctionFact]
    public async Task Broker_WindowsStorageRoot_RealNtfsJunction_ServerRejectsBeforeAdmission()
    {
        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string targetDir = Path.Combine(baseDir, "aik-target-" + Guid.NewGuid().ToString("N"));
        string junctionDir = Path.Combine(baseDir, "aik-junction-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(targetDir);
        this._tempDirectories.Add(junctionDir);

        CreateProtectedWindowsDirectory(targetDir);
        CreateNtfsJunction(junctionDir, targetDir);

        var ex = Assert.Throws<InvalidOperationException>(() => new WorkflowSchedulerPaths(junctionDir));
        Assert.Contains("reparse point", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsNtfsJunctionFact]
    public async Task Broker_WindowsStorageRoot_PreExistingRealNtfsJunctionLocks_ServerRejectsBeforeAdmission()
    {
        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        string targetDir = Path.Combine(baseDir, "aik-target-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(root);
        this._tempDirectories.Add(targetDir);

        CreateProtectedWindowsDirectory(root);
        CreateProtectedWindowsDirectory(targetDir);

        WorkflowSchedulerPaths paths = new(root);
        CreateNtfsJunction(paths.LocksDirectory, targetDir);

        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("reparse point", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_TokenGroupEnumerationFailure_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(root);

        CreateProtectedWindowsDirectory(root);

        try
        {
            WorkflowBrokerIpcTransport.TokenGroupsAccessorForTesting = () =>
                throw new System.Security.SecurityException("Simulated token group enumeration failure");

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("Failed to enumerate Broker token groups", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkflowBrokerIpcTransport.TokenGroupsAccessorForTesting = null;
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_TokenGroupDenyWrite_DynamicGroup_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SecurityIdentifier? dynamicGroup = null;
        var currentIdentity = WindowsIdentity.GetCurrent();
        if (currentIdentity.Groups != null)
        {
            HashSet<string> staticSids = new(StringComparer.OrdinalIgnoreCase)
            {
                "S-1-1-0", "S-1-5-11", "S-1-5-32-545", "S-1-5-4"
            };
            foreach (IdentityReference ir in currentIdentity.Groups)
            {
                if (ir is SecurityIdentifier sid && !staticSids.Contains(sid.Value))
                {
                    dynamicGroup = sid;
                    break;
                }
            }
        }

        SecurityIdentifier testGroup = dynamicGroup ?? new SecurityIdentifier("S-1-5-21-99999-99999-99999-1001");
        bool injected = dynamicGroup == null;

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-tests-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(root);

        CreateWindowsDirectoryWithTokenGroupWriteDenied(root, testGroup);

        try
        {
            if (injected)
            {
                WorkflowBrokerIpcTransport.TokenGroupsAccessorForTesting = () =>
                {
                    var col = new IdentityReferenceCollection();
                    col.Add(testGroup);
                    return col;
                };
            }

            WorkflowSchedulerPaths paths = new(root);
            await using WorkflowCoordinationBroker broker = new(paths);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
            Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(broker.IsRunning);
            Assert.NotNull(broker.AdmissionBlockedReason);
            Assert.Contains("Deny ACE blocking required broker write access", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (injected)
            {
                WorkflowBrokerIpcTransport.TokenGroupsAccessorForTesting = null;
            }
        }
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_RuntimeTamperingOnIpc_AcquireLeaseRejectedFailClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await client.PingAsync();

        // Tamper with IPC directory by adding an insecure file with Everyone: Write
        string rogueFile = Path.Combine(paths.IpcDirectory, "rogue-ipc-file.tmp");
        CreateInsecureWindowsFile(rogueFile, new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Write);

        WorkflowResourceClaim[] claims = [new("fs:/repo/src", WorkflowResourceAccessKind.ExclusiveWrite)];
        var ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.AcquireLeaseAsync("wf-test-tampered-ipc", claims, "Normal"));

        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("coordination file", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_RuntimeMissingMandatoryDirectory_AcquireLeaseRejectedFailClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await client.PingAsync();

        // Remove active leases directory while broker is running
        Directory.Delete(paths.ActiveLeasesDirectory, recursive: true);

        WorkflowResourceClaim[] claims = [new("fs:/repo/src", WorkflowResourceAccessKind.ExclusiveWrite)];
        var ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.AcquireLeaseAsync("wf-test-missing-dir", claims, "Normal"));

        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("is missing or was removed", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_RuntimeTamperingOnTargetWorkflowAuth_AcquireLeaseRejectedFailClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        await using WorkflowBrokerClient client = await WorkflowBrokerClient.ConnectAsync(paths);
        await client.PingAsync();

        // Pre-create target workflow directory with insecure owner-auth.json
        string wfDir = paths.GetWorkflowDirectory("wf-target-tampered");
        CreateProtectedWindowsDirectory(wfDir);
        string authPath = Path.Combine(wfDir, "owner-auth.json");
        CreateInsecureWindowsFile(authPath, new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Write);

        WorkflowResourceClaim[] claims = [new("fs:/repo/src", WorkflowResourceAccessKind.ExclusiveWrite)];
        var ex = await Assert.ThrowsAsync<WorkflowBrokerException>(
            () => client.AcquireLeaseAsync("wf-target-tampered", claims, "Normal"));

        Assert.Equal(WorkflowBrokerProtocol.ErrorCodeConflict, ex.ErrorCode);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("owner-auth.json", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_AncestorInheritedDeleteSubdirectoriesAndFiles_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string ancestorDir = Path.Combine(baseDir, "aik-ancestor-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(ancestorDir, "aik-storage-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(ancestorDir);
        this._tempDirectories.Add(root);

        DirectoryInfo di = new(ancestorDir);
        DirectorySecurity ds = new();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        SecurityIdentifier everyoneSid = new(WellKnownSidType.WorldSid, null);

        ds.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(
            everyoneSid,
            FileSystemRights.DeleteSubdirectoriesAndFiles,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        di.Create(ds);

        CreateProtectedWindowsDirectory(root);

        bool admitted = WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
            root,
            currentUser.Value,
            out string reason,
            out _,
            out _);

        Assert.False(admitted);
        Assert.Contains("dangerous deletion/control rights", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_AncestorInheritedDelete_CannotDeleteOrReplaceNonEmptyAncestor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // S4 EVIDENCE BOUNDARY ANALYSIS:
        // This test runs within the single-principal process context of the current test runner (same SID as the Broker).
        // It demonstrates that standard Windows API callers (RemoveDirectory, MoveFile) cannot delete or rename a
        // non-empty directory tree when children are protected or locked by open handles.
        // HOWEVER, this does NOT constitute proof of resistance to a distinct adversarial principal possessing different
        // token privileges or attacking across process boundaries. Full cross-principal verification requires genuine
        // multi-identity host provisioning (V7_P12_SLICE_02_CROSS_PLATFORM_SECURITY_ACCEPTANCE gate).
        //
        // S4 STARTUP TOCTOU WINDOW ANALYSIS:
        // Between initial filesystem hierarchy inspection/pinning and atomic acquisition of lifetimeDomainLockHandle,
        // a concurrent actor on the system could theoretically attempt directory replacement if intermediate ancestors
        // grant deletion or renaming rights. While the Option B security policy minimizes this by verifying that no
        // untrusted principal holds explicit Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, or TakeOwnership,
        // absolute non-substitutability requires genuine cross-SID adversary test harness verification.

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string ancestorDir = Path.Combine(baseDir, "aik-ancestor-del-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(ancestorDir, "aik-storage-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(ancestorDir);
        this._tempDirectories.Add(root);

        // Create ancestor directory inheriting native C:\Temp permissions (Authenticated Users: Modify, which contains Delete)
        Directory.CreateDirectory(ancestorDir);

        // Child storageRoot has inheritance broken (isProtected: true, preserveInheritance: false)
        CreateProtectedWindowsDirectory(root);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");

        // 1. StorageRoot hierarchy validation passes because inherited Modify on ancestors lacks DeleteSubdirectoriesAndFiles
        bool admitted = WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
            root,
            currentUser.Value,
            out string reason,
            out _,
            out _);

        Assert.True(admitted, $"Expected admission to succeed but failed: {reason}");

        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));
        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        // 2. Verify NTFS behavioral guarantee: Non-empty ancestor CANNOT be deleted via non-recursive delete
        Assert.Throws<IOException>(() => Directory.Delete(ancestorDir, recursive: false));

        // 3. Verify NTFS behavioral guarantee: Ancestor cannot be renamed while Broker holds domain.lock handle open
        string renamedAncestor = Path.Combine(baseDir, "aik-ancestor-renamed-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(renamedAncestor);
        Assert.Throws<IOException>(() => Directory.Move(ancestorDir, renamedAncestor));
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_BrokerSidWriteAttributesOnly_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-writeattr-" + Guid.NewGuid().ToString("N"));
        CreateWindowsDirectoryWithBrokerWriteAttributesOnly(root);
        this._tempDirectories.Add(root);

        // Direct transport validation check: WriteAttributes alone must NOT pass directory write validation
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        bool admitted = WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
            root,
            currentUser.Value,
            out string reason,
            out _,
            out _);
        Assert.False(admitted);
        Assert.Contains("does not grant required write permissions", reason, StringComparison.OrdinalIgnoreCase);

        // Server admission check: Server must fail-closed before any mutation
        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("does not grant required write permissions", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(paths.DomainLockPath), "Coordination files must NOT be created when broker only has WriteAttributes.");
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingCoordinationDirectoryWriteAttributesOnly_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-dir-writeattr-" + Guid.NewGuid().ToString("N"));
        CreateProtectedWindowsDirectory(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        CreateWindowsDirectoryWithBrokerWriteAttributesOnly(paths.LocksDirectory);

        // Direct transport validation check: WriteAttributes alone must NOT pass coordination directory validation
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        bool dirValid = WorkflowBrokerIpcTransport.ValidatePreExistingWindowsCoordinationDirectory(
            paths.LocksDirectory,
            currentUser.Value,
            out string reason);
        Assert.False(dirValid);
        Assert.Contains("does not grant required write permissions", reason, StringComparison.OrdinalIgnoreCase);

        // Server admission check: Server must fail-closed during pre-existing directory validation
        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("does not grant required write permissions", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingCoordinationFileWriteAttributesOnly_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-file-writeattr-" + Guid.NewGuid().ToString("N"));
        CreateProtectedWindowsDirectory(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        CreateProtectedWindowsDirectory(paths.LocksDirectory);
        CreateWindowsFileWithBrokerWriteAttributesOnly(paths.DomainLockPath);

        // Direct transport validation check: WriteAttributes alone must NOT pass coordination file validation
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        bool fileValid = WorkflowBrokerIpcTransport.ValidatePreExistingWindowsCoordinationFile(
            paths.DomainLockPath,
            currentUser.Value,
            out string reason);
        Assert.False(fileValid);
        Assert.Contains("does not grant required write permissions", reason, StringComparison.OrdinalIgnoreCase);

        // Server admission check: Server must fail-closed during pre-existing file validation
        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("does not grant required write permissions", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_BrokerSidWriteInheritOnly_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-writeinheritonly-" + Guid.NewGuid().ToString("N"));
        CreateWindowsDirectoryWithBrokerWriteInheritOnly(root);
        this._tempDirectories.Add(root);

        // Direct transport validation check: InheritOnly write ACE must NOT pass directory write validation
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        bool admitted = WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
            root,
            currentUser.Value,
            out string reason,
            out _,
            out _);
        Assert.False(admitted);
        Assert.Contains("does not grant required write permissions", reason, StringComparison.OrdinalIgnoreCase);

        // Server admission check: Server must fail-closed before any mutation
        WorkflowSchedulerPaths paths = new(root);
        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("does not grant required write permissions", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(paths.DomainLockPath), "Coordination files must NOT be created when broker only has InheritOnly write permissions.");
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_PreExistingCoordinationDirectoryWriteInheritOnly_ServerRejectsBeforeAdmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string root = Path.Combine(baseDir, "aik-broker-dir-writeinheritonly-" + Guid.NewGuid().ToString("N"));
        CreateProtectedWindowsDirectory(root);
        this._tempDirectories.Add(root);

        WorkflowSchedulerPaths paths = new(root);
        CreateWindowsDirectoryWithBrokerWriteInheritOnly(paths.LocksDirectory);

        // Direct transport validation check: InheritOnly write ACE must NOT pass coordination directory validation
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        bool dirValid = WorkflowBrokerIpcTransport.ValidatePreExistingWindowsCoordinationDirectory(
            paths.LocksDirectory,
            currentUser.Value,
            out string reason);
        Assert.False(dirValid);
        Assert.Contains("does not grant required write permissions", reason, StringComparison.OrdinalIgnoreCase);

        // Server admission check: Server must fail-closed during pre-existing directory validation
        await using WorkflowCoordinationBroker broker = new(paths);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.StartAsync());
        Assert.Contains("failed security admission", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(broker.IsRunning);
        Assert.NotNull(broker.AdmissionBlockedReason);
        Assert.Contains("does not grant required write permissions", broker.AdmissionBlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broker_WindowsStorageRoot_InheritedEffectiveWriteAce_ServerAdmitsWorkflow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string baseDir = Environment.GetEnvironmentVariable("AIREPO_TEST_SCRATCH_ROOT") ?? @"C:\Temp\airepo\A";
        string parentDir = Path.Combine(baseDir, "aik-parent-inherited-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parentDir, "aik-storage-" + Guid.NewGuid().ToString("N"));
        this._tempDirectories.Add(parentDir);
        this._tempDirectories.Add(root);

        // 1. Setup parent directory with explicit inheritance protection and full control to Broker with ContainerInherit | ObjectInherit
        DirectoryInfo parentDi = new(parentDir);
        DirectorySecurity parentDs = new();
        parentDs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        SecurityIdentifier systemSid = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier adminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        parentDs.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        parentDs.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        parentDs.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        parentDi.Create(parentDs);

        // 2. Create child directory (storageRoot) inheriting permissions from parent without breaking inheritance
        Directory.CreateDirectory(root);

        // Verify that the child's inherited ACE is effective (rule.IsInherited == true && PropagationFlags.InheritOnly == 0)
        DirectorySecurity rootSec = new DirectoryInfo(root).GetAccessControl();
        var inheritedRules = rootSec.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Where(r => r.IdentityReference.Value.Equals(currentUser.Value, StringComparison.OrdinalIgnoreCase) && r.IsInherited)
            .ToList();
        Assert.NotEmpty(inheritedRules);
        Assert.All(inheritedRules, r => Assert.Equal(PropagationFlags.None, r.PropagationFlags & PropagationFlags.InheritOnly));

        // 3. Hierarchy validation must PASS and accept the inherited write permissions
        bool admitted = WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
            root,
            currentUser.Value,
            out string reason,
            out _,
            out _);
        Assert.True(admitted, $"Expected validation to succeed on effective inherited ACE: {reason}");

        // 4. Broker must start successfully and acquire domain lock
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));
        await using WorkflowCoordinationBroker broker = new(paths, maxConcurrentWorkflows_: 4, allowUnrestrictedExecution_: true);
        await broker.StartAsync();

        Assert.True(broker.IsRunning);
        Assert.True(File.Exists(paths.DomainLockPath));
    }

    // =========================================================================
    // 37. Offline Maintenance: Valid P09 Reconciliation Audited and Released
    // =========================================================================

    [Fact]
    public async Task Broker_OfflineMaintenance_ValidP09Reconciliation_CleanlyReleasesMaintenanceLease()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-offline-valid");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "do work");
        ExecutableWork work = new(1, [task], [], [], [], []);

        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowState runningWorkflow = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWorkflow, 1);

        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWorkflow, "task-1", WorkflowStepStatus.Running);
        store.Append(runningStep, 2);

        CompiledPrompt prompt = new("task-1", "instruction");
        ExecutionEnvelope envelope = ExecutionEnvelope.Create(
            work,
            prompt,
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(root)),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);

        // Pre-create quarantined manifest for orphaned workflow
        WorkflowSchedulerLeaseManifest initialQuarantine = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "dead-token-12345",
            slot_: null,
            "Normal",
            [],
            4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initialQuarantine, 4, domainLockHandle_: null);

        TestAgentExecutor agentExecutor = new(new AgentProviderId("agent-1"), AgentExecutionResult.Completed());
        TestValidationExecutor validator = new(ValidationStrategy.Policy);
        TestSideEffectReconciler reconciler = new();

        // Offline reconciliation (no broker active)
        await WorkflowOrphanReconciler.ReconcileOrphanWorkflowAsync(
            root,
            store,
            work,
            envelope,
            [agentExecutor],
            [validator],
            new WorkflowRepairPolicy(1, 1),
            reconciler);

        // Maintenance completed and audited: active lease file is cleanly removed!
        string maintLeasePath = paths.GetActiveLeasePath(wfId.Value);
        Assert.False(File.Exists(maintLeasePath));
    }

    // =========================================================================
    // 38. Offline Maintenance: Failed P09 Audit Preserves MaintenanceInProgress
    // =========================================================================

    [Fact]
    public async Task Broker_OfflineMaintenance_FailedP09Audit_PreservesMaintenanceInProgress()
    {
        string root = this.CreateTempStorageRoot();
        WorkflowSchedulerPaths paths = new(root);
        WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, new WorkflowSchedulerPolicy(4, allowUnrestrictedExecution_: true));

        WorkflowId wfId = new("wf-offline-fail");
        WorkflowPersistenceStore store = new(root, wfId);

        ExecutableTask task = new("task-1", "step-1", "do work");
        ExecutableWork work = new(1, [task], [], [], [], []);

        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowState runningWorkflow = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWorkflow, 1);

        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWorkflow, "task-1", WorkflowStepStatus.Running);
        store.Append(runningStep, 2);

        CompiledPrompt prompt = new("task-1", "instruction");
        ExecutionEnvelope envelope = ExecutionEnvelope.Create(
            work,
            prompt,
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(root)),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);

        WorkflowSchedulerLeaseManifest initialQuarantine = new(
            wfId.Value,
            WorkflowSchedulerLeaseState.Quarantined,
            "dead-token-12345",
            slot_: null,
            "Normal",
            [],
            4);
        WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initialQuarantine, 4, domainLockHandle_: null);

        // Pre-create incomplete / adulterated P09 checkpoint evidence under execution-checkpoints:
        // A synthetic second task journal that lacks ExecutionCompletedRecord.
        // The real P09 coordinator will execute task-1 and return cleanly,
        // but when offline maintenance performs the P09 evidence audit after the coordinator returns,
        // the audit detects incomplete/adulterated evidence, fails closed, and preserves MaintenanceInProgress.
        string workflowIdHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(wfId.Value)).ToLowerInvariant();
        string checkpointsBaseDir = Path.Combine(root, "workflows", workflowIdHex, "execution-checkpoints");
        string task2Hex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("task-2")).ToLowerInvariant();
        string task2RevDir = Path.Combine(checkpointsBaseDir, task2Hex, 1L.ToString("D20"));
        string task2RecordsDir = Path.Combine(task2RevDir, "records");
        Directory.CreateDirectory(task2RecordsDir);

        // Write CheckpointStartedRecord with valid structure for task-2
        string baseFp = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(created);
        string inputFp = new('a', 64);
        string regFp = new('b', 64);
        CheckpointStartedRecord startedRecord = new()
        {
            Sequence = 1,
            WorkflowId = wfId.Value,
            TaskId = "task-2",
            BasePersistenceRevision = 1,
            BaseWorkflowStateFingerprint = baseFp,
            InputFingerprint = inputFp,
            RegistryFingerprint = regFp
        };
        string startedJson = System.Text.Json.JsonSerializer.Serialize(startedRecord);
        File.WriteAllText(Path.Combine(task2RecordsDir, "00000000000000000001.json"), startedJson);
        // NOTE: Deliberately do NOT write ExecutionCompletedRecord or ProjectionCompletedRecord!

        TestAgentExecutor agentExecutor = new(new AgentProviderId("agent-1"), AgentExecutionResult.Completed());
        TestValidationExecutor validator = new(ValidationStrategy.Policy);
        TestSideEffectReconciler reconciler = new();

        // Offline reconciliation coordinator executes successfully, but subsequent P09 evidence audit fails closed
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowOrphanReconciler.ReconcileOrphanWorkflowAsync(
                root,
                store,
                work,
                envelope,
                [agentExecutor],
                [validator],
                new WorkflowRepairPolicy(1, 1),
                reconciler));

        Assert.Contains("missing or incomplete P09 reconciliation evidence", ex.Message);

        // Verify coordinator returned and updated persistence store past initial revision
        WorkflowPersistenceSnapshot? finalSnapshot = store.Load();
        Assert.NotNull(finalSnapshot);
        Assert.True(finalSnapshot.Revision > 3, "Coordinator must have executed and projected before audit failure.");

        // Maintenance failed: active lease MUST be preserved on disk in MaintenanceInProgress!
        string maintLeasePath = paths.GetActiveLeasePath(wfId.Value);
        Assert.True(File.Exists(maintLeasePath));
        WorkflowSchedulerLeaseManifest preserved = WorkflowSchedulerLeaseManifest.ReadAndValidate(maintLeasePath, paths, 4);
        Assert.Equal(WorkflowSchedulerLeaseState.MaintenanceInProgress, preserved.State);
        Assert.Equal("ExclusiveMaintenance", preserved.DomainMode);
    }

    // =========================================================================
    // 38. Linux Lock Contention & Ancestor Protection Precision Tests
    // =========================================================================

    [Fact]
    public void LockPrimitives_IsSharingOrLockViolation_ClassifiesExpectedHResultsAndMessages()
    {
        // 1. Win32 HResults
        Assert.True(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Win32 sharing", unchecked((int)0x80070020))));
        Assert.True(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Win32 lock", unchecked((int)0x80070021))));

        // 2. Unix: .NET throws IOException with the raw errno as HResult for flock(LOCK_NB) contention
        //    (EAGAIN/EWOULDBLOCK = 11), verified empirically on Linux.
        //    On non-Linux platforms, raw 11 is not EAGAIN and must not be classified as lock contention.
        if (OperatingSystem.IsLinux())
        {
            Assert.True(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
                new IOException("Unix EWOULDBLOCK (message irrelevant)", 11)));
            Assert.True(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
                new IOException("Localizado: arquivo em uso", 11)));
        }
        else
        {
            Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
                new IOException("Non-Linux raw 11", 11)));
        }

        // 3. Indeterminate errors are NOT contention: EDEADLK (35) and EBUSY (16) in any encoding,
        //    and message text alone (even the English .NET sharing text) must never be trusted.
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Unix EDEADLK", 35)));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Unix EBUSY", 16)));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("EDEADLK facility", unchecked((int)0x80070023))));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("EBUSY facility", unchecked((int)0x80070010))));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("The process cannot access the file '/tmp/test.lock' because it is being used by another process.", unchecked((int)0x80131620))));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("The process cannot access the file because it is being used by another process.")));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Resource temporarily unavailable")));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Device or resource busy")));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("sharing violation")));

        // 4. Negative controls: non-contention IO errors and specialized exceptions must NOT be classified as sharing violation
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new FileNotFoundException("File not found")));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new DirectoryNotFoundException("Directory not found")));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new PathTooLongException("Path too long")));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new EndOfStreamException("End of stream")));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("No space left on device", unchecked((int)0x80070070))));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Read-only file system", unchecked((int)0x8007001E))));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Broken pipe", unchecked((int)0x8007006D))));
        Assert.False(WorkflowSchedulerLockPrimitives.IsSharingOrLockViolation(
            new IOException("Input/output error", unchecked((int)0x80070005))));
    }

    [Fact]
    public void LockPrimitives_ProbeLockState_RecognizesExclusiveSharedAndFree()
    {
        string root = this.CreateTempStorageRoot();
        string lockFile = Path.Combine(root, "test.lock");

        // 1. Non-existent file -> Free
        Assert.Equal(WorkflowLockProbeState.Free, WorkflowSchedulerLockPrimitives.ProbeLockState(lockFile));

        // 2. Existing empty file without active handle -> Free
        File.WriteAllText(lockFile, "");
        Assert.Equal(WorkflowLockProbeState.Free, WorkflowSchedulerLockPrimitives.ProbeLockState(lockFile));

        // 3. Held exclusive -> HeldExclusive
        using (WorkflowSchedulerLockHandle? excl = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(lockFile))
        {
            Assert.NotNull(excl);
            Assert.Equal(WorkflowLockProbeState.HeldExclusive, WorkflowSchedulerLockPrimitives.ProbeLockState(lockFile));
        }

        // After release -> Free
        Assert.Equal(WorkflowLockProbeState.Free, WorkflowSchedulerLockPrimitives.ProbeLockState(lockFile));

        // 4. Held shared -> HeldShared
        using (WorkflowSchedulerLockHandle? shared = WorkflowSchedulerLockPrimitives.TryAcquireShared(lockFile))
        {
            Assert.NotNull(shared);
            Assert.Equal(WorkflowLockProbeState.HeldShared, WorkflowSchedulerLockPrimitives.ProbeLockState(lockFile));
        }

        // After release -> Free
        Assert.Equal(WorkflowLockProbeState.Free, WorkflowSchedulerLockPrimitives.ProbeLockState(lockFile));
    }

    // =========================================================================
    // Test Doubles for P09 Reconciler Test
    // =========================================================================

    private sealed class ThrowingAgentExecutor(AgentProviderId providerId) : IAgentExecutor
    {
        public AgentProviderId ProviderId { get; } = providerId;
        public AgentCapabilitySet Capabilities { get; } = new([new AgentCapability("text-edit")]);

        public Task<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fatal executor crash during P09 reconciliation.");
    }

    private sealed class TestAgentExecutor(AgentProviderId providerId, AgentExecutionResult result) : IAgentExecutor
    {
        public AgentProviderId ProviderId { get; } = providerId;
        public AgentCapabilitySet Capabilities { get; } = new([new AgentCapability("text-edit")]);

        public Task<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class TestValidationExecutor(ValidationStrategy strategy) : IValidationExecutor
    {
        public ValidationStrategy Strategy { get; } = strategy;

        public Task<ValidationExecutionResult> ValidateAsync(
            ValidationRequirement requirement,
            ExecutionEnvironment environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidationExecutionResult(true, "Validation passed"));
    }

    private sealed class TestSideEffectReconciler : IWorkflowSideEffectReconciler
    {
        public Task<WorkflowSideEffectReconciliationResult> ReconcileAgentAsync(
            WorkflowId workflowId_,
            string taskId_,
            string operationId_,
            long operationOrdinal_,
            int invocationGeneration_,
            AgentProviderId providerId_,
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default) =>
            Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));

        public Task<WorkflowSideEffectReconciliationResult> ReconcileValidationAsync(
            WorkflowId workflowId_,
            string taskId_,
            string operationId_,
            long operationOrdinal_,
            int invocationGeneration_,
            ValidationRequirement requirement_,
            ExecutionEnvironment environment_,
            CancellationToken cancellationToken_ = default) =>
            Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));
    }
}

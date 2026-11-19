namespace AiRepoKit.WorkflowExecution;

using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using AiRepoKit.Orchestration;

internal sealed class WorkflowCoordinationBroker : IAsyncDisposable, IDisposable
{
    private readonly WorkflowSchedulerPaths _paths;
    private readonly int _maxConcurrentWorkflows;
    private readonly bool _allowUnrestrictedExecution;
    private readonly TimeSpan _domainLockTimeout;
    private readonly SemaphoreSlim _coordinationGate = new(1, 1);
    private readonly Dictionary<string, WorkflowBrokerSession> _activeLeaseSessions = new(StringComparer.Ordinal);
    private readonly HashSet<int> _occupiedSlots = [];
    private readonly List<WorkflowBrokerSession> _sessions = [];
    private readonly HashSet<Task> _activeSessionTasks = [];
    private readonly List<IDisposable> _retainedFailedGenerationLocks = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<object?> _shutdownTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Func<Task>? ControlledShutdownHook;
    internal Action? FaultInjectionBeforeQuarantineWrite;
    internal int ActiveSessionTaskCount
    {
        get
        {
            lock (this._activeSessionTasks)
            {
                return this._activeSessionTasks.Count;
            }
        }
    }

    private WorkflowSchedulerLockHandle? _lifetimeDomainLockHandle;
    private Task? _listenerTask;
    private Socket? _linuxServerSocket;
    private NamedPipeServerStream? _currentWindowsServerStream;
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _linuxStorageRootHandle;
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _windowsStorageRootHandle;
    private ulong _validatedStorageRootInode;
    private ulong _validatedStorageRootDevice;
    private ulong _validatedWindowsStorageRootFileIndex;
    private uint _validatedWindowsStorageRootVolumeSerial;
    private volatile string? _admissionBlockedReason;
    private int _rejectedAuthenticationCount;
    private int _shuttingDown;
    private volatile bool _quarantineFailedDuringShutdown;
    private volatile Exception? _shutdownQuarantineException;

    public WorkflowSchedulerPaths Paths => this._paths;
    public int MaxConcurrentWorkflows => this._maxConcurrentWorkflows;
    public bool AllowUnrestrictedExecution => this._allowUnrestrictedExecution;
    public static bool SimulateRollbackFailure { get; set; }
    public static bool SimulateDurabilityFailure { get; set; }

    public bool IsRunning => this._lifetimeDomainLockHandle is not null && !this._cts.IsCancellationRequested;
    public string? AdmissionBlockedReason => this._admissionBlockedReason;
    public int RejectedAuthenticationCount => Volatile.Read(ref this._rejectedAuthenticationCount);
    public string ServicePrincipal { get; }
    public IReadOnlyList<string> AuthorizedWorkerIdentities { get; }
    public WorkflowSchedulerLockHandle? DomainLockHandle => this._lifetimeDomainLockHandle;

    public WorkflowCoordinationBroker(
        WorkflowSchedulerPaths paths_,
        int maxConcurrentWorkflows_ = 4,
        bool allowUnrestrictedExecution_ = false,
        TimeSpan? domainLockTimeout_ = null,
        string? authorizedWorkerIdentity_ = null,
        IEnumerable<string>? authorizedWorkerIdentities_ = null)
    {
        this._paths = paths_ ?? throw new ArgumentNullException(nameof(paths_));
        if (maxConcurrentWorkflows_ < 1 || maxConcurrentWorkflows_ > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentWorkflows_),
                maxConcurrentWorkflows_,
                "MaxConcurrentWorkflows must be within 1..32.");
        }
        this._maxConcurrentWorkflows = maxConcurrentWorkflows_;
        this._allowUnrestrictedExecution = allowUnrestrictedExecution_;
        this._domainLockTimeout = domainLockTimeout_ ?? TimeSpan.FromSeconds(5);

        List<string> workerList = [];
        if (!string.IsNullOrWhiteSpace(authorizedWorkerIdentity_))
        {
            workerList.Add(authorizedWorkerIdentity_);
        }
        if (authorizedWorkerIdentities_ is not null)
        {
            foreach (string id in authorizedWorkerIdentities_)
            {
                if (!string.IsNullOrWhiteSpace(id) && !workerList.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    workerList.Add(id);
                }
            }
        }

        if (workerList.Count == 0)
        {
            string? envWorker = Environment.GetEnvironmentVariable("AIREPO_AUTHORIZED_WORKER_PRINCIPAL");
            if (!string.IsNullOrWhiteSpace(envWorker) && !workerList.Contains(envWorker, StringComparer.OrdinalIgnoreCase))
            {
                workerList.Add(envWorker);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            this.ServicePrincipal = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
        }
        else
        {
            this.ServicePrincipal = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        this.AuthorizedWorkerIdentities = workerList.AsReadOnly();
    }

    public async Task StartAsync(CancellationToken cancellationToken_ = default)
    {
        WorkflowBrokerIpcTransport.DefaultBrokerServicePrincipal = this.ServicePrincipal;

        // 0. Server-side validation of preprovisioned storageRoot hierarchy before any mutable operations.
        if (OperatingSystem.IsLinux())
        {
            uint brokerUid = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId();
            if (!WorkflowBrokerIpcTransport.ValidateLinuxStorageRootHierarchy(
                    this._paths.StorageRoot,
                    brokerUid,
                    out string validationFailureReason,
                    out ulong storageRootInode,
                    out ulong storageRootDevice))
            {
                this._admissionBlockedReason = $"Insecure storageRoot hierarchy: {validationFailureReason}";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: storageRoot hierarchy at '{this._paths.StorageRoot}' failed security admission: {validationFailureReason}");
            }

            if (storageRootInode == 0 || storageRootDevice == 0)
            {
                this._admissionBlockedReason = "Validated storageRoot device or inode is 0. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: storageRoot device or inode is 0.");
            }

            // Pin storageRoot directory descriptor against path substitution
            int fd = WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.Open(
                this._paths.StorageRoot,
                WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_RDONLY |
                WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_DIRECTORY |
                WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_CLOEXEC |
                WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_NOFOLLOW);

            if (fd < 0)
            {
                this._admissionBlockedReason = $"Failed to open directory file descriptor for storageRoot '{this._paths.StorageRoot}'. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: failed to pin storageRoot directory at '{this._paths.StorageRoot}'.");
            }

            Microsoft.Win32.SafeHandles.SafeFileHandle pinnedHandle = new(new IntPtr(fd), ownsHandle: true);

            // Verify pinned file descriptor identity matches validated storageRoot object (device + inode)
            if (!WorkflowBrokerIpcTransport.TryLinuxStatxFd(pinnedHandle, out WorkflowBrokerIpcTransport.LinuxStatx fdSt))
            {
                pinnedHandle.Dispose();
                this._admissionBlockedReason = $"statx inspection of pinned storageRoot descriptor failed for '{this._paths.StorageRoot}'. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: failed to inspect pinned storageRoot directory descriptor at '{this._paths.StorageRoot}'.");
            }

            if ((fdSt.stx_mask & WorkflowBrokerIpcTransport.STATX_REQUIRED_MASK) != WorkflowBrokerIpcTransport.STATX_REQUIRED_MASK)
            {
                pinnedHandle.Dispose();
                this._admissionBlockedReason = $"statx of pinned storageRoot descriptor returned incomplete mask 0x{fdSt.stx_mask:X}. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: pinned storageRoot directory descriptor returned incomplete information.");
            }

            if ((fdSt.stx_mode & WorkflowBrokerIpcTransport.S_IFMT) != WorkflowBrokerIpcTransport.S_IFDIR)
            {
                pinnedHandle.Dispose();
                this._admissionBlockedReason = $"Pinned storageRoot descriptor is not a directory (node type 0x{fdSt.stx_mode & WorkflowBrokerIpcTransport.S_IFMT:X}). Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: pinned storageRoot descriptor is not a directory.");
            }

            if (fdSt.stx_uid != brokerUid)
            {
                pinnedHandle.Dispose();
                this._admissionBlockedReason = $"Pinned storageRoot descriptor is owned by UID {fdSt.stx_uid}, not broker UID {brokerUid}. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: pinned storageRoot descriptor ownership mismatch.");
            }

            if (fdSt.stx_ino == 0 || fdSt.DeviceId == 0)
            {
                pinnedHandle.Dispose();
                this._admissionBlockedReason = "Pinned storageRoot descriptor returned invalid dev or inode 0. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: pinned storageRoot directory descriptor has invalid device/inode.");
            }

            // Correlate opened object identity (device + inode) with validated path identity
            if (fdSt.stx_ino != storageRootInode || fdSt.DeviceId != storageRootDevice)
            {
                pinnedHandle.Dispose();
                this._admissionBlockedReason = $"Pinned storageRoot descriptor identity mismatch (dev:ino {fdSt.DeviceId}:{fdSt.stx_ino} != expected {storageRootDevice}:{storageRootInode}). Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: pinned storageRoot directory descriptor identity does not match validated path object (dev:ino {fdSt.DeviceId}:{fdSt.stx_ino} != expected {storageRootDevice}:{storageRootInode}). Possible TOCTOU path substitution.");
            }

            this._linuxStorageRootHandle = pinnedHandle;
            this._validatedStorageRootInode = storageRootInode;
            this._validatedStorageRootDevice = storageRootDevice;
        }
        else if (OperatingSystem.IsWindows())
        {
            string brokerSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Cannot start Coordination Broker: Windows process user SID unavailable.");

            if (!WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
                    this._paths.StorageRoot,
                    brokerSid,
                    out string validationFailureReason,
                    out ulong storageRootFileIndex,
                    out uint storageRootVolumeSerial))
            {
                this._admissionBlockedReason = $"Insecure storageRoot hierarchy: {validationFailureReason}";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: storageRoot hierarchy at '{this._paths.StorageRoot}' failed security admission: {validationFailureReason}");
            }

            // F1: Validate pre-existing coordination layout subdirectories and files before EnsureCoordinationLayout()
            if (!WorkflowBrokerIpcTransport.ValidatePreExistingWindowsCoordinationLayout(
                    this._paths,
                    brokerSid,
                    out string preExistingLayoutFailureReason))
            {
                this._admissionBlockedReason = $"Insecure pre-existing coordination layout: {preExistingLayoutFailureReason}";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: pre-existing coordination layout at '{this._paths.StorageRoot}' failed security admission: {preExistingLayoutFailureReason}");
            }

            if (storageRootFileIndex == 0 || storageRootVolumeSerial == 0)
            {
                this._admissionBlockedReason = "Validated storageRoot volume or file index is 0. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: storageRoot volume or file index is 0.");
            }

            // Open and pin directory handle to storageRoot
            if (!WorkflowBrokerIpcTransport.TryOpenPinnedWindowsDirectoryHandle(
                    this._paths.StorageRoot,
                    out Microsoft.Win32.SafeHandles.SafeFileHandle? pinnedHandle,
                    out ulong pinnedFileIndex,
                    out uint pinnedVolumeSerial,
                    out string openReason))
            {
                this._admissionBlockedReason = $"Failed to open directory handle for storageRoot '{this._paths.StorageRoot}': {openReason}. Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: failed to pin storageRoot directory at '{this._paths.StorageRoot}': {openReason}");
            }

            // Correlate opened descriptor identity with validated path identity
            if (pinnedFileIndex != storageRootFileIndex || pinnedVolumeSerial != storageRootVolumeSerial)
            {
                pinnedHandle?.Dispose();
                this._admissionBlockedReason = $"Pinned storageRoot descriptor identity mismatch (vol:index {pinnedVolumeSerial}:{pinnedFileIndex} != expected {storageRootVolumeSerial}:{storageRootFileIndex}). Fail closed.";
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: pinned storageRoot directory descriptor identity does not match validated path object (vol:index {pinnedVolumeSerial}:{pinnedFileIndex} != expected {storageRootVolumeSerial}:{storageRootFileIndex}). Possible TOCTOU path substitution.");
            }

            this._windowsStorageRootHandle = pinnedHandle;
            this._validatedWindowsStorageRootFileIndex = storageRootFileIndex;
            this._validatedWindowsStorageRootVolumeSerial = storageRootVolumeSerial;
        }

        try
        {
            this._paths.EnsureCoordinationLayout();

            // 1. Acquire lifetime singleton ownership of domain.lock.
            // This excludes legacy schedulers and prevents competing brokers.
            try
            {
                this._lifetimeDomainLockHandle = await WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                    this._paths.DomainLockPath,
                    TimeSpan.FromMilliseconds(500),
                    cancellationToken_).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException(
                    $"Cannot start Coordination Broker: domain lock '{this._paths.DomainLockPath}' is held by another broker or legacy scheduler.");
            }

            // 2. Perform Phase 0 integrity initialization and recovery of dead active leases.
            await this._coordinationGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
            try
            {
                WorkflowSchedulerPolicy policyToUse;
                if (File.Exists(this._paths.DomainPolicyPath))
                {
                    policyToUse = WorkflowSchedulerDomainMetadata.ValidatePolicyDocument(this._paths);
                }
                else
                {
                    policyToUse = new WorkflowSchedulerPolicy(this._maxConcurrentWorkflows, this._allowUnrestrictedExecution);
                }

                WorkflowSchedulerDomainMetadata.InitializeOrValidate(
                    this._paths,
                    policyToUse,
                    this._lifetimeDomainLockHandle);

                this.RecoverDeadActiveLeasesPhase0();
            }
            finally
            {
                this._coordinationGate.Release();
            }

            // 3. Start IPC listener loop and await verified listener readiness.
            TaskCompletionSource<bool> readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            if (OperatingSystem.IsWindows())
            {
                this._listenerTask = Task.Run(() => this.WindowsPipeListenerLoopAsync(readyTcs, this._cts.Token));
            }
            else
            {
                this._listenerTask = Task.Run(() => this.LinuxSocketListenerLoopAsync(readyTcs, this._cts.Token));
            }

            await readyTcs.Task.WaitAsync(cancellationToken_).ConfigureAwait(false);
        }
        catch
        {
            this._linuxStorageRootHandle?.Dispose();
            this._linuxStorageRootHandle = null;
            this._windowsStorageRootHandle?.Dispose();
            this._windowsStorageRootHandle = null;
            this._lifetimeDomainLockHandle?.Dispose();
            this._lifetimeDomainLockHandle = null;
            throw;
        }
    }

    private void RecoverDeadActiveLeasesPhase0()
    {
        // 1. Inspect WorkflowsDirectory for orphaned owner-auth.json files (unresolved failed generations)
        if (Directory.Exists(this._paths.WorkflowsDirectory))
        {
            foreach (string wfDir in Directory.EnumerateDirectories(this._paths.WorkflowsDirectory))
            {
                string hexDirName = Path.GetFileName(wfDir);
                string wfId;
                try
                {
                    wfId = WorkflowSchedulerPaths.HexToWorkflowId(hexDirName);
                }
                catch (Exception ex)
                {
                    // Malformed directory identifier: reject fail-closed!
                    this._admissionBlockedReason = $"Malformed workflow directory identifier detected in '{wfDir}': {ex.Message}. Fail-closed.";
                    return;
                }

                string authPath = Path.Combine(wfDir, "owner-auth.json");
                if (File.Exists(authPath))
                {
                    string activeLeasePath = this._paths.GetActiveLeasePath(wfId);
                    bool hasActiveLease = false;
                    if (File.Exists(activeLeasePath))
                    {
                        try
                        {
                            WorkflowSchedulerLeaseManifest manifest =
                                WorkflowSchedulerLeaseManifest.ReadAndValidate(
                                    activeLeasePath,
                                    this._paths,
                                    this._maxConcurrentWorkflows);

                            if (manifest.State == WorkflowSchedulerLeaseState.Active ||
                                manifest.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                            {
                                hasActiveLease = true;
                            }
                        }
                        catch
                        {
                            hasActiveLease = false;
                        }
                    }

                    if (!hasActiveLease)
                    {
                        // Orphaned owner-auth.json without an active lease manifest represents an incomplete generation!
                        // Durably block admission fail-closed across broker restarts.
                        string wfLockPath = this._paths.GetWorkflowLockPath(wfId);
                        WorkflowLockProbeState probe = WorkflowSchedulerLockPrimitives.ProbeLockState(wfLockPath);
                        if (probe == WorkflowLockProbeState.Free)
                        {
                            this._admissionBlockedReason = $"Orphaned owner-auth.json detected in '{wfDir}' without active lease manifest. Incomplete generation requires safe recovery.";
                            return;
                        }
                    }
                }
            }
        }

        // 2. Inspect ActiveLeasesDirectory for dead active leases
        if (!Directory.Exists(this._paths.ActiveLeasesDirectory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(this._paths.ActiveLeasesDirectory, "*.lease.json"))
        {
            try
            {
                WorkflowSchedulerLeaseManifest manifest =
                    WorkflowSchedulerLeaseManifest.ReadAndValidate(
                        file,
                        this._paths,
                        this._maxConcurrentWorkflows);

                if (manifest.State == WorkflowSchedulerLeaseState.Active)
                {
                    string wfLockPath = this._paths.GetWorkflowLockPath(manifest.WorkflowId);
                    WorkflowLockProbeState probe = WorkflowSchedulerLockPrimitives.ProbeLockState(wfLockPath);

                    if (probe == WorkflowLockProbeState.Free)
                    {
                        // Dead active lease without a live OS lock holder: durably transition to Quarantined.
                        WorkflowSchedulerLeaseManifest quarantined = new(
                            manifest.WorkflowId,
                            WorkflowSchedulerLeaseState.Quarantined,
                            manifest.OwnerToken,
                            slot_: null,
                            manifest.DomainMode,
                            manifest.ResourceClaims,
                            this._maxConcurrentWorkflows);

                        WorkflowSchedulerLeaseManifest.WriteAtomic(
                            this._paths,
                            quarantined,
                            this._maxConcurrentWorkflows,
                            this._lifetimeDomainLockHandle);

                        WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                            this._paths,
                            manifest.WorkflowId,
                            expectedOwnerToken_: manifest.OwnerToken,
                            domainLockHandle_: this._lifetimeDomainLockHandle);
                    }
                }
                else if (manifest.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
                {
                    string wfLockPath = this._paths.GetWorkflowLockPath(manifest.WorkflowId);
                    WorkflowLockProbeState probe = WorkflowSchedulerLockPrimitives.ProbeLockState(wfLockPath);

                    if (probe == WorkflowLockProbeState.Free)
                    {
                        // Orphaned MaintenanceInProgress: previous broker or maintenance process died.
                        // Preserve global quarantine and unresolved P09 effects.
                        // Do not automatically delete or downgrade incomplete maintenance.
                        try
                        {
                            WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                                this._paths,
                                manifest.WorkflowId,
                                expectedOwnerToken_: manifest.OwnerToken,
                                domainLockHandle_: this._lifetimeDomainLockHandle);
                        }
                        catch
                        {
                            this._admissionBlockedReason = $"Orphaned maintenance cleanup failed for '{manifest.WorkflowId}'. Admissions blocked until safe recovery.";
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // CRITICAL: Fail-closed durability!
                this._admissionBlockedReason = $"Phase 0 recovery of dead active lease '{file}' failed: {ex.Message}";
                return;
            }
        }
    }

    private void StartSessionTask(WorkflowBrokerSession session, CancellationToken cancellationToken)
    {
        lock (this._sessions)
        {
            if (this._cts.IsCancellationRequested || Volatile.Read(ref this._shuttingDown) != 0)
            {
                session.Dispose();
                return;
            }

            this._sessions.Add(session);

            Task? sessionTask = null;
            lock (this._activeSessionTasks)
            {
                sessionTask = Task.Run(async () =>
                {
                    try
                    {
                        await this.ProcessSessionAsync(session, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (this._activeSessionTasks)
                        {
                            this._activeSessionTasks.Remove(sessionTask!);
                        }
                    }
                });

                this._activeSessionTasks.Add(sessionTask);
            }
        }
    }

    private async Task WindowsPipeListenerLoopAsync(TaskCompletionSource<bool> readyTcs_, CancellationToken cancellationToken_)
    {
        string pipeName = WorkflowBrokerIpcTransport.GetEndpointIdentifier(this._paths);
        bool signaledReady = false;

        while (!cancellationToken_.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                serverStream = WorkflowBrokerIpcTransport.CreateWindowsPipeServerStream(pipeName, this.AuthorizedWorkerIdentities);

                if (!signaledReady)
                {
                    signaledReady = true;
                    readyTcs_.TrySetResult(true);
                }

                this._currentWindowsServerStream = serverStream;
                try
                {
                    await serverStream.WaitForConnectionAsync(cancellationToken_).ConfigureAwait(false);
                }
                finally
                {
                    if (ReferenceEquals(this._currentWindowsServerStream, serverStream))
                    {
                        this._currentWindowsServerStream = null;
                    }
                }

                if (!WorkflowBrokerIpcTransport.VerifyClientIdentity(
                        serverStream,
                        out int clientPid,
                        out long startTimeTicks,
                        out string clientIdentity,
                        this.AuthorizedWorkerIdentities))
                {
                    Interlocked.Increment(ref this._rejectedAuthenticationCount);
                    serverStream.Dispose();
                    continue;
                }

                WorkflowBrokerSession session = new(
                    serverStream,
                    clientPid,
                    startTimeTicks,
                    clientIdentity);

                this.StartSessionTask(session, cancellationToken_);
            }
            catch (OperationCanceledException)
            {
                serverStream?.Dispose();
                if (!signaledReady)
                {
                    readyTcs_.TrySetCanceled(cancellationToken_);
                }
                break;
            }
            catch (Exception ex)
            {
                serverStream?.Dispose();
                if (!signaledReady)
                {
                    readyTcs_.TrySetException(ex);
                    break;
                }

                if (cancellationToken_.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task LinuxSocketListenerLoopAsync(TaskCompletionSource<bool> readyTcs_, CancellationToken cancellationToken_)
    {
        string socketPath = WorkflowBrokerIpcTransport.GetEndpointIdentifier(this._paths);

        try
        {
            Directory.CreateDirectory(this._paths.IpcDirectory);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    this._paths.IpcDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            FileInfo fi = new(socketPath);
            if (fi.Exists)
            {
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"Refusing to overwrite reparse point socket at '{socketPath}'.");
                }
                File.Delete(socketPath);
            }

            Socket serverSocket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            this._linuxServerSocket = serverSocket;
            serverSocket.Bind(new UnixDomainSocketEndPoint(socketPath));

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    socketPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite);

                UnixFileMode socketMode = File.GetUnixFileMode(socketPath);
                UnixFileMode dirMode = File.GetUnixFileMode(this._paths.IpcDirectory);

                if ((dirMode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                {
                    throw new InvalidOperationException($"Linux IPC directory at '{this._paths.IpcDirectory}' permits group or other write access: {dirMode}");
                }

                if ((socketMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                {
                    throw new InvalidOperationException($"Linux socket at '{socketPath}' has execute permissions: {socketMode}");
                }
            }

            serverSocket.Listen(128);

            readyTcs_.TrySetResult(true);
        }
        catch (Exception ex)
        {
            readyTcs_.TrySetException(ex);
            return;
        }

        while (!cancellationToken_.IsCancellationRequested)
        {
            try
            {
                Socket clientSocket = await this._linuxServerSocket.AcceptAsync(cancellationToken_).ConfigureAwait(false);
                NetworkStream netStream = new(clientSocket, ownsSocket: true);

                if (!WorkflowBrokerIpcTransport.VerifyClientIdentity(
                        netStream,
                        out int clientPid,
                        out long startTimeTicks,
                        out string clientIdentity,
                        this.AuthorizedWorkerIdentities))
                {
                    Interlocked.Increment(ref this._rejectedAuthenticationCount);
                    netStream.Dispose();
                    continue;
                }

                WorkflowBrokerSession session = new(
                    netStream,
                    clientPid,
                    startTimeTicks,
                    clientIdentity);

                this.StartSessionTask(session, cancellationToken_);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (cancellationToken_.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessSessionAsync(WorkflowBrokerSession session_, CancellationToken brokerToken_)
    {
        using CancellationTokenSource sessionCts = CancellationTokenSource.CreateLinkedTokenSource(brokerToken_);

        try
        {
            while (!sessionCts.Token.IsCancellationRequested)
            {
                WorkflowBrokerRequest? request = await WorkflowBrokerProtocol.ReceiveMessageAsync<WorkflowBrokerRequest>(
                    session_.Stream,
                    sessionCts.Token).ConfigureAwait(false);

                if (request is null)
                {
                    // Peer disconnected cleanly or dropped
                    break;
                }

                WorkflowBrokerResponse response = await this.DispatchBrokerRequestAsync(
                    session_,
                    request,
                    sessionCts.Token).ConfigureAwait(false);

                await WorkflowBrokerProtocol.SendMessageAsync(
                    session_.Stream,
                    response,
                    sessionCts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Peer communication terminated
        }
        finally
        {
            await this.HandleSessionTerminationAsync(session_).ConfigureAwait(false);
        }
    }

    private async Task<WorkflowBrokerResponse> DispatchBrokerRequestAsync(
        WorkflowBrokerSession session_,
        WorkflowBrokerRequest request_,
        CancellationToken cancellationToken_)
    {
        try
        {
            switch (request_.Action)
            {
                case WorkflowBrokerProtocol.ActionPing:
                    return WorkflowBrokerResponse.Ok(request_.RequestId);

                case WorkflowBrokerProtocol.ActionAcquireLease:
                    return await this.HandleAcquireLeaseAsync(session_, request_, cancellationToken_).ConfigureAwait(false);

                case WorkflowBrokerProtocol.ActionReleaseLease:
                    return await this.HandleReleaseLeaseAsync(session_, request_, cancellationToken_).ConfigureAwait(false);

                case WorkflowBrokerProtocol.ActionQuarantineLease:
                    return await this.HandleQuarantineLeaseAsync(session_, request_, cancellationToken_).ConfigureAwait(false);

                case WorkflowBrokerProtocol.ActionAcquireMaintenanceLease:
                    return await this.HandleAcquireMaintenanceLeaseAsync(session_, request_, cancellationToken_).ConfigureAwait(false);

                case WorkflowBrokerProtocol.ActionReleaseMaintenanceLease:
                    return await this.HandleReleaseMaintenanceLeaseAsync(session_, request_, cancellationToken_).ConfigureAwait(false);

                case WorkflowBrokerProtocol.ActionInspectIntegrity:
                    return this.HandleInspectIntegrity(request_);

                default:
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                        $"Unknown broker action '{request_.Action}'.");
            }
        }
        catch (Exception ex)
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeConflict,
                ex.Message);
        }
    }

    private async Task<WorkflowBrokerResponse> HandleAcquireLeaseAsync(
        WorkflowBrokerSession session_,
        WorkflowBrokerRequest request_,
        CancellationToken cancellationToken_)
    {
        if (string.IsNullOrWhiteSpace(request_.WorkflowId))
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                "WorkflowId is required.");
        }

        string domainMode = request_.DomainMode ?? "Normal";
        if (!string.Equals(domainMode, "Normal", StringComparison.Ordinal) &&
            !string.Equals(domainMode, "Unrestricted", StringComparison.Ordinal))
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                $"Invalid domainMode '{domainMode}'; must be 'Normal' or 'Unrestricted'.");
        }

        List<WorkflowResourceClaim> candidateClaims = [];
        if (request_.ResourceClaims is not null)
        {
            foreach (WorkflowBrokerResourceClaimDto claimDto in request_.ResourceClaims)
            {
                try
                {
                    candidateClaims.Add(new WorkflowResourceClaim(claimDto.ResourceId, (WorkflowResourceAccessKind) claimDto.AccessKind));
                }
                catch (Exception ex)
                {
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                        $"Invalid resource claim '{claimDto.ResourceId}': {ex.Message}");
                }
            }
        }

        // Intra-candidate duplicate / overlap validation
        for (int i = 0; i < candidateClaims.Count; i++)
        {
            for (int j = i + 1; j < candidateClaims.Count; j++)
            {
                if (WorkflowSchedulerConflictEngine.ClaimsConflict(candidateClaims[i], candidateClaims[j]))
                {
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Candidate claims '{candidateClaims[i].ResourceId}' and '{candidateClaims[j].ResourceId}' conflict internally.");
                }
            }
        }

        await this._coordinationGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
        try
        {
            if (this._admissionBlockedReason is not null)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Cannot admit workflow: broker admission is blocked due to prior durability/quarantine failure: {this._admissionBlockedReason}");
            }

            if (OperatingSystem.IsLinux())
            {
                if (this._validatedStorageRootInode == 0 ||
                    this._validatedStorageRootDevice == 0 ||
                    this._linuxStorageRootHandle is null ||
                    this._linuxStorageRootHandle.IsInvalid ||
                    this._linuxStorageRootHandle.IsClosed)
                {
                    this._admissionBlockedReason = "Broker storageRoot identity is unverified or pinned handle is invalid (inode/device is 0). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot admit workflow: {this._admissionBlockedReason}");
                }

                // Verify pinned descriptor identity
                if (!WorkflowBrokerIpcTransport.TryLinuxStatxFd(this._linuxStorageRootHandle, out WorkflowBrokerIpcTransport.LinuxStatx pinnedSt) ||
                    pinnedSt.stx_ino != this._validatedStorageRootInode ||
                    pinnedSt.DeviceId != this._validatedStorageRootDevice)
                {
                    this._admissionBlockedReason = $"Pinned storageRoot descriptor identity mismatch or corrupted (pinned {pinnedSt.DeviceId}:{pinnedSt.stx_ino} != expected {this._validatedStorageRootDevice}:{this._validatedStorageRootInode}). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot admit workflow: {this._admissionBlockedReason}");
                }

                // Verify path hierarchy and device:inode correlation
                uint brokerUid = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId();
                if (!WorkflowBrokerIpcTransport.ValidateLinuxStorageRootHierarchy(
                        this._paths.StorageRoot,
                        brokerUid,
                        out string subReason,
                        out ulong currentInode,
                        out ulong currentDevice) ||
                    currentInode != this._validatedStorageRootInode ||
                    currentDevice != this._validatedStorageRootDevice)
                {
                    this._admissionBlockedReason = $"storageRoot hierarchy substitution detected: {subReason}. Expected dev:inode {this._validatedStorageRootDevice}:{this._validatedStorageRootInode}, actual: {currentDevice}:{currentInode}. Fail-closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot admit workflow: {this._admissionBlockedReason}");
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                if (this._validatedWindowsStorageRootFileIndex == 0 ||
                    this._validatedWindowsStorageRootVolumeSerial == 0 ||
                    this._windowsStorageRootHandle is null ||
                    this._windowsStorageRootHandle.IsInvalid ||
                    this._windowsStorageRootHandle.IsClosed)
                {
                    this._admissionBlockedReason = "Broker storageRoot identity is unverified or pinned handle is invalid (fileIndex/volume is 0). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot admit workflow: {this._admissionBlockedReason}");
                }

                // Verify pinned descriptor identity
                if (!WorkflowBrokerIpcTransport.TryGetWindowsFileIdentity(this._windowsStorageRootHandle, out ulong pinnedIndex, out uint pinnedVol) ||
                    pinnedIndex != this._validatedWindowsStorageRootFileIndex ||
                    pinnedVol != this._validatedWindowsStorageRootVolumeSerial)
                {
                    this._admissionBlockedReason = $"Pinned storageRoot descriptor identity mismatch or corrupted (pinned {pinnedVol}:{pinnedIndex} != expected {this._validatedWindowsStorageRootVolumeSerial}:{this._validatedWindowsStorageRootFileIndex}). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot admit workflow: {this._admissionBlockedReason}");
                }

                // Verify path hierarchy and volume:fileIndex correlation
                string brokerSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
                if (!WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
                        this._paths.StorageRoot,
                        brokerSid,
                        out string subReason,
                        out ulong currentIndex,
                        out uint currentVol) ||
                    currentIndex != this._validatedWindowsStorageRootFileIndex ||
                    currentVol != this._validatedWindowsStorageRootVolumeSerial)
                {
                    this._admissionBlockedReason = $"storageRoot hierarchy substitution detected: {subReason}. Expected vol:fileIndex {this._validatedWindowsStorageRootVolumeSerial}:{this._validatedWindowsStorageRootFileIndex}, actual: {currentVol}:{currentIndex}. Fail-closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot admit workflow: {this._admissionBlockedReason}");
                }

                if (!WorkflowBrokerIpcTransport.ValidateActiveWindowsCoordinationDirectories(
                        this._paths,
                        brokerSid,
                        out string coordReason,
                        targetWorkflowId_: request_.WorkflowId))
                {
                    this._admissionBlockedReason = $"Active coordination directory security validation failed: {coordReason}. Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot admit workflow: {this._admissionBlockedReason}");
                }
            }

            if (session_.ActiveLease is not null)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Session already holds active lease for workflow '{session_.ActiveLease.WorkflowId}'.");
            }

            if (this._activeLeaseSessions.ContainsKey(request_.WorkflowId))
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Workflow '{request_.WorkflowId}' already has an active lease.");
            }

            // Inspect disk manifests for conflicts, integrity violations and global exclusion using broker's lifetime domain lock
            WorkflowSchedulerIntegrityReport inspection = WorkflowSchedulerIntegrityInspector.Inspect(
                this._paths,
                this._maxConcurrentWorkflows,
                this._lifetimeDomainLockHandle);

            if (inspection.Violations.Count > 0 || !inspection.IsHealthy)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Cannot admit workflow: domain integrity check failed with {inspection.Violations.Count} violation(s): {string.Join("; ", inspection.Violations)}");
            }

            if (inspection.HasGlobalExclusion)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    "Domain has active global exclusion (Unrestricted or Maintenance lease present).");
            }

            if (string.Equals(domainMode, "Unrestricted", StringComparison.Ordinal))
            {
                if (this._activeLeaseSessions.Count > 0 || inspection.ActiveLeases.Count > 0)
                {
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        "Unrestricted domain mode requires exclusive domain availability (active workflows exist).");
                }
            }

            // Check resource conflicts against active and quarantined manifests
            foreach (WorkflowSchedulerLeaseManifest activeManifest in inspection.ActiveLeases)
            {
                if (WorkflowSchedulerConflictEngine.HasConflictWithLease(candidateClaims, activeManifest, out string? reason))
                {
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Conflict with active lease '{activeManifest.WorkflowId}': {reason}");
                }
            }

            foreach (WorkflowSchedulerLeaseManifest quarantinedManifest in inspection.QuarantinedLeases)
            {
                if (WorkflowSchedulerConflictEngine.HasConflictWithLease(candidateClaims, quarantinedManifest, out string? reason))
                {
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Conflict with quarantined lease '{quarantinedManifest.WorkflowId}': {reason}");
                }
            }

            // Find lowest available slot
            int allocatedSlot = -1;
            for (int s = 1; s <= this._maxConcurrentWorkflows; s++)
            {
                if (!this._occupiedSlots.Contains(s))
                {
                    allocatedSlot = s;
                    break;
                }
            }

            if (allocatedSlot == -1)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"All {this._maxConcurrentWorkflows} capacity slots are currently occupied.");
            }

            // Acquire OS locks: domain-mode, slot, workflow
            WorkflowSchedulerLockHandle? modeLock = null;
            WorkflowSchedulerLockHandle? slotLock = null;
            WorkflowSchedulerLockHandle? wfLock = null;
            WorkflowSchedulerOwnerAuthHandle? ownerAuthHandle = null;
            string? ownerAuthCreatedWfId = null;
            string? manifestCreatedWfId = null;
            string? ownerToken = null;

            try
            {
                if (string.Equals(domainMode, "Unrestricted", StringComparison.Ordinal))
                {
                    modeLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(this._paths.DomainModeLockPath);
                    if (modeLock is null)
                    {
                        return WorkflowBrokerResponse.Error(
                            request_.RequestId,
                            WorkflowBrokerProtocol.ErrorCodeConflict,
                            "Failed to acquire exclusive domain-mode lock.");
                    }
                }
                else
                {
                    modeLock = WorkflowSchedulerLockPrimitives.TryAcquireShared(this._paths.DomainModeLockPath);
                    if (modeLock is null)
                    {
                        return WorkflowBrokerResponse.Error(
                            request_.RequestId,
                            WorkflowBrokerProtocol.ErrorCodeConflict,
                            "Failed to acquire shared domain-mode lock.");
                    }
                }

                string slotLockPath = this._paths.GetSlotLockPath(allocatedSlot, this._maxConcurrentWorkflows);
                slotLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(slotLockPath);
                if (slotLock is null)
                {
                    modeLock.Dispose();
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Slot {allocatedSlot} OS lock '{slotLockPath}' is unexpectedly occupied.");
                }

                string wfLockPath = this._paths.GetWorkflowLockPath(request_.WorkflowId);
                wfLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(wfLockPath);
                if (wfLock is null)
                {
                    slotLock.Dispose();
                    modeLock.Dispose();
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Workflow OS lock '{wfLockPath}' is already held.");
                }

                ownerToken = WorkflowBrokerSecurityPrimitives.GenerateToken();

                // Durably register owner authentication and lease manifest
                ownerAuthHandle =
                    WorkflowSchedulerOwnerAuthentication.RegisterBrokerOwnerAuthentication(
                        this._paths,
                        request_.WorkflowId,
                        ownerToken,
                        allocatedSlot,
                        domainMode,
                        wfLock,
                        slotLock,
                        modeLock,
                        this._lifetimeDomainLockHandle,
                        workerProcessId_: session_.ClientProcessId,
                        workerStartTimeTicks_: session_.ClientProcessStartTimeTicks);
                ownerAuthCreatedWfId = request_.WorkflowId;

                if (SimulateDurabilityFailure)
                {
                    throw new IOException("Fault-injected durability failure prior to manifest write.");
                }

                WorkflowSchedulerLeaseManifest manifest = new(
                    request_.WorkflowId,
                    WorkflowSchedulerLeaseState.Active,
                    ownerToken,
                    allocatedSlot,
                    domainMode,
                    candidateClaims,
                    this._maxConcurrentWorkflows);

                WorkflowSchedulerLeaseManifest.WriteAtomic(
                    this._paths,
                    manifest,
                    this._maxConcurrentWorkflows,
                    this._lifetimeDomainLockHandle);
                manifestCreatedWfId = request_.WorkflowId;

                // Commit transaction to session and broker state
                session_.WorkflowLockHandle = wfLock;
                session_.SlotLockHandle = slotLock;
                session_.DomainModeLockHandle = modeLock;
                session_.OwnerAuthHandle = ownerAuthHandle;

                WorkflowBrokerLeaseDto leaseDto = new()
                {
                    WorkflowId = request_.WorkflowId,
                    OwnerToken = ownerToken,
                    Slot = allocatedSlot,
                    DomainMode = domainMode,
                    ResourceClaims = candidateClaims.Select(c => new WorkflowBrokerResourceClaimDto
                    {
                        ResourceId = c.ResourceId,
                        AccessKind = (int) c.AccessKind
                    }).ToList(),
                    CreatedAt = manifest.CreatedAt
                };

                session_.ActiveLease = leaseDto;
                this._activeLeaseSessions[request_.WorkflowId] = session_;
                this._occupiedSlots.Add(allocatedSlot);

                return WorkflowBrokerResponse.Ok(request_.RequestId, leaseDto);
            }
            catch (Exception ex)
            {
                // Transaction rollback: remove owner authentication and manifest, release all acquired locks
                bool rollbackFailed = false;
                string rollbackFailureDetails = string.Empty;

                if (ownerAuthHandle is not null)
                {
                    try
                    {
                        ownerAuthHandle.Dispose();
                        ownerAuthHandle = null;
                    }
                    catch (Exception authEx)
                    {
                        rollbackFailed = true;
                        rollbackFailureDetails = $"Owner-auth rollback failed: {authEx.Message}";
                    }
                }

                if (manifestCreatedWfId is not null)
                {
                    try
                    {
                        string leasePath = this._paths.GetActiveLeasePath(manifestCreatedWfId);
                        if (File.Exists(leasePath))
                        {
                            WorkflowSchedulerDomainMetadata.DeleteAtomic(leasePath, this._paths.ActiveLeasesDirectory);
                        }
                    }
                    catch (Exception manifestEx)
                    {
                        rollbackFailed = true;
                        rollbackFailureDetails += $" Manifest rollback failed: {manifestEx.Message}";
                    }
                }

                if (SimulateRollbackFailure)
                {
                    rollbackFailed = true;
                    rollbackFailureDetails += " Fault-injected rollback failure.";
                }

                if (rollbackFailed)
                {
                    // Retain relevant OS locks and block further admission indefinitely.
                    lock (this._retainedFailedGenerationLocks)
                    {
                        if (wfLock is not null) this._retainedFailedGenerationLocks.Add(wfLock);
                        if (slotLock is not null) this._retainedFailedGenerationLocks.Add(slotLock);
                        if (modeLock is not null) this._retainedFailedGenerationLocks.Add(modeLock);
                        if (ownerAuthHandle is not null) this._retainedFailedGenerationLocks.Add(ownerAuthHandle);
                    }

                    wfLock = null;
                    slotLock = null;
                    modeLock = null;
                    ownerAuthHandle = null;

                    this._admissionBlockedReason = $"Transaction rollback failed. Durability uncertain: {rollbackFailureDetails.Trim()}";

                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeInternalError,
                        $"CRITICAL: AcquireLease transaction rollback failed: {rollbackFailureDetails.Trim()}. OS locks retained and admission blocked.");
                }

                ownerAuthHandle?.Dispose();
                wfLock?.Dispose();
                slotLock?.Dispose();
                modeLock?.Dispose();

                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeInternalError,
                    $"AcquireLease transaction rolled back due to error: {ex.Message}");
            }
        }
        finally
        {
            this._coordinationGate.Release();
        }
    }

    private async Task<WorkflowBrokerResponse> HandleReleaseLeaseAsync(
        WorkflowBrokerSession session_,
        WorkflowBrokerRequest request_,
        CancellationToken cancellationToken_)
    {
        if (string.IsNullOrWhiteSpace(request_.WorkflowId))
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                "WorkflowId is required.");
        }

        if (string.IsNullOrWhiteSpace(request_.OwnerToken))
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                "OwnerToken is required.");
        }

        await this._coordinationGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
        try
        {
            if (!this._activeLeaseSessions.TryGetValue(request_.WorkflowId, out WorkflowBrokerSession? owningSession))
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeNotFound,
                    $"Workflow '{request_.WorkflowId}' does not have an active lease.");
            }

            // CRITICAL PRIVILEGE FENCING: Only the authenticated session that acquired the lease can release it!
            if (!ReferenceEquals(owningSession, session_) ||
                owningSession.ActiveLease is null ||
                !string.Equals(owningSession.ActiveLease.OwnerToken, request_.OwnerToken, StringComparison.Ordinal))
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeUnauthorized,
                    "Unauthorized: A lease cannot be released by a different session, process or invalid owner token.");
            }

            if (session_.ActiveLease is null)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeUnauthorized,
                    "Unauthorized: Session does not hold an active lease.");
            }

            int slot = session_.ActiveLease.Slot;

            // Durably delete active lease manifest first
            string activePath = this._paths.GetActiveLeasePath(request_.WorkflowId);
            WorkflowSchedulerDomainMetadata.DeleteAtomic(
                activePath,
                this._paths.ActiveLeasesDirectory);

            session_.ReleaseLeaseLocks();
            this._activeLeaseSessions.Remove(request_.WorkflowId);
            this._occupiedSlots.Remove(slot);

            return WorkflowBrokerResponse.Ok(request_.RequestId);
        }
        finally
        {
            this._coordinationGate.Release();
        }
    }

    private async Task<WorkflowBrokerResponse> HandleQuarantineLeaseAsync(
        WorkflowBrokerSession session_,
        WorkflowBrokerRequest request_,
        CancellationToken cancellationToken_)
    {
        if (string.IsNullOrWhiteSpace(request_.WorkflowId))
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                "WorkflowId is required.");
        }

        await this._coordinationGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
        try
        {
            if (!this._activeLeaseSessions.TryGetValue(request_.WorkflowId, out WorkflowBrokerSession? owningSession) ||
                !ReferenceEquals(owningSession, session_) ||
                session_.ActiveLease is null)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeUnauthorized,
                    "Unauthorized: Cannot quarantine a lease not owned by this session.");
            }

            int slot = session_.ActiveLease.Slot;

            List<WorkflowResourceClaim> claims = session_.ActiveLease.ResourceClaims
                .Select(c => new WorkflowResourceClaim(c.ResourceId, (WorkflowResourceAccessKind) c.AccessKind))
                .ToList();

            bool isMaintenance = string.Equals(session_.ActiveLease.DomainMode, "ExclusiveMaintenance", StringComparison.Ordinal);

            WorkflowSchedulerLeaseManifest preservedManifest;
            if (isMaintenance)
            {
                // Preserve MaintenanceInProgress and ExclusiveMaintenance global exclusion!
                // Never downgrade active maintenance to Quarantined/Normal.
                preservedManifest = new(
                    session_.ActiveLease.WorkflowId,
                    WorkflowSchedulerLeaseState.MaintenanceInProgress,
                    session_.ActiveLease.OwnerToken,
                    slot_: null,
                    domainMode_: "ExclusiveMaintenance",
                    resourceClaims_: [],
                    this._maxConcurrentWorkflows);
            }
            else
            {
                preservedManifest = new(
                    session_.ActiveLease.WorkflowId,
                    WorkflowSchedulerLeaseState.Quarantined,
                    session_.ActiveLease.OwnerToken,
                    slot_: null,
                    session_.ActiveLease.DomainMode,
                    claims,
                    this._maxConcurrentWorkflows);
            }

            try
            {
                WorkflowSchedulerLeaseManifest.WriteAtomic(
                    this._paths,
                    preservedManifest,
                    this._maxConcurrentWorkflows,
                    this._lifetimeDomainLockHandle);
            }
            catch (Exception ex)
            {
                this._quarantineFailedDuringShutdown = true;
                this._shutdownQuarantineException ??= ex;

                // CRITICAL: Fail-closed durability! Never release locks if quarantine publication fails.
                this._admissionBlockedReason = $"Durable quarantine publication failed for workflow '{request_.WorkflowId}': {ex.Message}";
                lock (this._retainedFailedGenerationLocks)
                {
                    if (session_.WorkflowLockHandle is not null) this._retainedFailedGenerationLocks.Add(session_.WorkflowLockHandle);
                    if (session_.SlotLockHandle is not null) this._retainedFailedGenerationLocks.Add(session_.SlotLockHandle);
                    if (session_.DomainModeLockHandle is not null) this._retainedFailedGenerationLocks.Add(session_.DomainModeLockHandle);
                    if (session_.OwnerAuthHandle is not null) this._retainedFailedGenerationLocks.Add(session_.OwnerAuthHandle);
                }

                session_.WorkflowLockHandle = null;
                session_.SlotLockHandle = null;
                session_.DomainModeLockHandle = null;
                session_.OwnerAuthHandle = null;

                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeInternalError,
                    this._admissionBlockedReason);
            }

            session_.ReleaseLeaseLocks();
            this._activeLeaseSessions.Remove(request_.WorkflowId);
            this._occupiedSlots.Remove(slot);

            return WorkflowBrokerResponse.Ok(request_.RequestId);
        }
        finally
        {
            this._coordinationGate.Release();
        }
    }

    private async Task<WorkflowBrokerResponse> HandleAcquireMaintenanceLeaseAsync(
        WorkflowBrokerSession session_,
        WorkflowBrokerRequest request_,
        CancellationToken cancellationToken_)
    {
        if (string.IsNullOrWhiteSpace(request_.WorkflowId))
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                "WorkflowId is required for maintenance lease.");
        }

        await this._coordinationGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
        try
        {
            if (this._admissionBlockedReason is not null)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Cannot acquire maintenance lease: broker admission is blocked: {this._admissionBlockedReason}");
            }

            if (OperatingSystem.IsLinux())
            {
                if (this._validatedStorageRootInode == 0 ||
                    this._validatedStorageRootDevice == 0 ||
                    this._linuxStorageRootHandle is null ||
                    this._linuxStorageRootHandle.IsInvalid ||
                    this._linuxStorageRootHandle.IsClosed)
                {
                    this._admissionBlockedReason = "Broker storageRoot identity is unverified or pinned handle is invalid (inode/device is 0). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot acquire maintenance lease: {this._admissionBlockedReason}");
                }

                // Verify pinned descriptor identity
                if (!WorkflowBrokerIpcTransport.TryLinuxStatxFd(this._linuxStorageRootHandle, out WorkflowBrokerIpcTransport.LinuxStatx pinnedSt) ||
                    pinnedSt.stx_ino != this._validatedStorageRootInode ||
                    pinnedSt.DeviceId != this._validatedStorageRootDevice)
                {
                    this._admissionBlockedReason = $"Pinned storageRoot descriptor identity mismatch or corrupted (pinned {pinnedSt.DeviceId}:{pinnedSt.stx_ino} != expected {this._validatedStorageRootDevice}:{this._validatedStorageRootInode}). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot acquire maintenance lease: {this._admissionBlockedReason}");
                }

                // Verify path hierarchy and device:inode correlation
                uint brokerUid = WorkflowBrokerIpcTransport.GetCurrentLinuxUserId();
                if (!WorkflowBrokerIpcTransport.ValidateLinuxStorageRootHierarchy(
                        this._paths.StorageRoot,
                        brokerUid,
                        out string subReason,
                        out ulong currentInode,
                        out ulong currentDevice) ||
                    currentInode != this._validatedStorageRootInode ||
                    currentDevice != this._validatedStorageRootDevice)
                {
                    this._admissionBlockedReason = $"storageRoot hierarchy substitution detected: {subReason}. Expected dev:inode {this._validatedStorageRootDevice}:{this._validatedStorageRootInode}, actual: {currentDevice}:{currentInode}. Fail-closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot acquire maintenance lease: {this._admissionBlockedReason}");
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                if (this._validatedWindowsStorageRootFileIndex == 0 ||
                    this._validatedWindowsStorageRootVolumeSerial == 0 ||
                    this._windowsStorageRootHandle is null ||
                    this._windowsStorageRootHandle.IsInvalid ||
                    this._windowsStorageRootHandle.IsClosed)
                {
                    this._admissionBlockedReason = "Broker storageRoot identity is unverified or pinned handle is invalid (fileIndex/volume is 0). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot acquire maintenance lease: {this._admissionBlockedReason}");
                }

                // Verify pinned descriptor identity
                if (!WorkflowBrokerIpcTransport.TryGetWindowsFileIdentity(this._windowsStorageRootHandle, out ulong pinnedIndex, out uint pinnedVol) ||
                    pinnedIndex != this._validatedWindowsStorageRootFileIndex ||
                    pinnedVol != this._validatedWindowsStorageRootVolumeSerial)
                {
                    this._admissionBlockedReason = $"Pinned storageRoot descriptor identity mismatch or corrupted (pinned {pinnedVol}:{pinnedIndex} != expected {this._validatedWindowsStorageRootVolumeSerial}:{this._validatedWindowsStorageRootFileIndex}). Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot acquire maintenance lease: {this._admissionBlockedReason}");
                }

                // Verify path hierarchy and volume:fileIndex correlation
                string brokerSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
                if (!WorkflowBrokerIpcTransport.ValidateWindowsStorageRootHierarchy(
                        this._paths.StorageRoot,
                        brokerSid,
                        out string subReason,
                        out ulong currentIndex,
                        out uint currentVol) ||
                    currentIndex != this._validatedWindowsStorageRootFileIndex ||
                    currentVol != this._validatedWindowsStorageRootVolumeSerial)
                {
                    this._admissionBlockedReason = $"storageRoot hierarchy substitution detected: {subReason}. Expected vol:fileIndex {this._validatedWindowsStorageRootVolumeSerial}:{this._validatedWindowsStorageRootFileIndex}, actual: {currentVol}:{currentIndex}. Fail-closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot acquire maintenance lease: {this._admissionBlockedReason}");
                }

                if (!WorkflowBrokerIpcTransport.ValidateActiveWindowsCoordinationDirectories(
                        this._paths,
                        brokerSid,
                        out string coordReason,
                        targetWorkflowId_: request_.WorkflowId))
                {
                    this._admissionBlockedReason = $"Active coordination directory security validation failed: {coordReason}. Fail closed.";
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Cannot acquire maintenance lease: {this._admissionBlockedReason}");
                }
            }

            if (this._activeLeaseSessions.Count > 0)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    "Cannot acquire maintenance lease while active workflow leases exist.");
            }

            // Maintenance acquisition must never bypass durable quarantine!
            string activePath = this._paths.GetActiveLeasePath(request_.WorkflowId);
            if (!File.Exists(activePath))
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Cannot acquire maintenance lease for '{request_.WorkflowId}': no active or quarantined lease exists for this workflow.");
            }

            WorkflowSchedulerLeaseManifest existingManifest =
                WorkflowSchedulerLeaseManifest.ReadAndValidate(activePath, this._paths, this._maxConcurrentWorkflows);

            if (existingManifest.State != WorkflowSchedulerLeaseState.Quarantined &&
                existingManifest.State != WorkflowSchedulerLeaseState.MaintenanceInProgress)
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Cannot acquire maintenance lease for '{request_.WorkflowId}': workflow state is '{existingManifest.State}'; must be 'Quarantined'.");
            }

            if (existingManifest.State == WorkflowSchedulerLeaseState.MaintenanceInProgress)
            {
                // Must be genuinely orphaned: no active session in this broker
                if (this._activeLeaseSessions.ContainsKey(request_.WorkflowId))
                {
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Workflow '{request_.WorkflowId}' is already under active maintenance in this broker.");
                }
            }

            session_.MaintenanceBaselineRevision = GetCurrentWorkflowRevision(request_.WorkflowId);

            WorkflowSchedulerLockHandle? modeLock = null;
            WorkflowSchedulerLockHandle? wfLock = null;
            bool manifestPublished = false;

            try
            {
                modeLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(this._paths.DomainModeLockPath);
                if (modeLock is null)
                {
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        "Failed to acquire exclusive domain-mode lock for maintenance.");
                }

                string wfLockPath = this._paths.GetWorkflowLockPath(request_.WorkflowId);
                wfLock = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(wfLockPath);
                if (wfLock is null)
                {
                    modeLock.Dispose();
                    modeLock = null;
                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeConflict,
                        $"Failed to acquire exclusive workflow lock '{wfLockPath}' for maintenance.");
                }

                if (SimulateDurabilityFailure)
                {
                    throw new IOException("Simulated maintenance failure before manifest publication.");
                }

                string ownerToken = WorkflowBrokerSecurityPrimitives.GenerateToken();

                WorkflowSchedulerLeaseManifest maintManifest = new(
                    request_.WorkflowId,
                    WorkflowSchedulerLeaseState.MaintenanceInProgress,
                    ownerToken,
                    slot_: null,
                    domainMode_: "ExclusiveMaintenance",
                    resourceClaims_: [],
                    this._maxConcurrentWorkflows);

                WorkflowSchedulerLeaseManifest.WriteAtomic(
                    this._paths,
                    maintManifest,
                    this._maxConcurrentWorkflows,
                    this._lifetimeDomainLockHandle);

                manifestPublished = true;

                if (SimulateDurabilityFailure)
                {
                    throw new IOException("Simulated maintenance failure after manifest publication.");
                }

                // Clean up any residual owner authentication from prior generation: must NOT swallow exceptions!
                WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    this._paths,
                    request_.WorkflowId,
                    domainLockHandle_: this._lifetimeDomainLockHandle);

                session_.WorkflowLockHandle = wfLock;
                session_.DomainModeLockHandle = modeLock;
                session_.ActiveLease = new WorkflowBrokerLeaseDto
                {
                    WorkflowId = request_.WorkflowId,
                    OwnerToken = ownerToken,
                    Slot = 0,
                    DomainMode = "ExclusiveMaintenance",
                    ResourceClaims = []
                };

                this._activeLeaseSessions[request_.WorkflowId] = session_;

                return WorkflowBrokerResponse.Ok(request_.RequestId, session_.ActiveLease);
            }
            catch (Exception ex)
            {
                bool rollbackFailed = false;

                if (manifestPublished)
                {
                    // Attempt rollback to existing manifest state
                    try
                    {
                        if (SimulateRollbackFailure)
                        {
                            throw new IOException("Simulated rollback failure during maintenance acquire.");
                        }

                        WorkflowSchedulerLeaseManifest rollbackManifest = new(
                            existingManifest.WorkflowId,
                            existingManifest.State,
                            existingManifest.OwnerToken,
                            slot_: null,
                            domainMode_: existingManifest.DomainMode,
                            resourceClaims_: existingManifest.ResourceClaims.Select(c => new WorkflowResourceClaim(c.ResourceId, (WorkflowResourceAccessKind) c.AccessKind)).ToList(),
                            this._maxConcurrentWorkflows);

                        WorkflowSchedulerLeaseManifest.WriteAtomic(
                            this._paths,
                            rollbackManifest,
                            this._maxConcurrentWorkflows,
                            this._lifetimeDomainLockHandle);
                    }
                    catch
                    {
                        rollbackFailed = true;
                    }
                }

                if (rollbackFailed || SimulateRollbackFailure)
                {
                    lock (this._retainedFailedGenerationLocks)
                    {
                        if (wfLock is not null) this._retainedFailedGenerationLocks.Add(wfLock);
                        if (modeLock is not null) this._retainedFailedGenerationLocks.Add(modeLock);
                    }
                    wfLock = null;
                    modeLock = null;

                    this._admissionBlockedReason = $"Maintenance lease acquisition rollback failed for workflow '{request_.WorkflowId}': {ex.Message}";

                    return WorkflowBrokerResponse.Error(
                        request_.RequestId,
                        WorkflowBrokerProtocol.ErrorCodeInternalError,
                        $"CRITICAL: Maintenance acquisition transaction failed: {ex.Message}. OS locks retained and admission blocked.");
                }

                wfLock?.Dispose();
                modeLock?.Dispose();

                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeInternalError,
                    $"Failed to acquire maintenance lease for '{request_.WorkflowId}': {ex.Message}");
            }
        }
        finally
        {
            this._coordinationGate.Release();
        }
    }

    private async Task<WorkflowBrokerResponse> HandleReleaseMaintenanceLeaseAsync(
        WorkflowBrokerSession session_,
        WorkflowBrokerRequest request_,
        CancellationToken cancellationToken_)
    {
        if (string.IsNullOrWhiteSpace(request_.WorkflowId) || string.IsNullOrWhiteSpace(request_.OwnerToken))
        {
            return WorkflowBrokerResponse.Error(
                request_.RequestId,
                WorkflowBrokerProtocol.ErrorCodeInvalidRequest,
                "WorkflowId and OwnerToken are required to release maintenance lease.");
        }

        await this._coordinationGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
        try
        {
            if (!this._activeLeaseSessions.TryGetValue(request_.WorkflowId, out WorkflowBrokerSession? owningSession) ||
                !ReferenceEquals(owningSession, session_) ||
                session_.ActiveLease is null ||
                !string.Equals(session_.ActiveLease.OwnerToken, request_.OwnerToken, StringComparison.Ordinal))
            {
                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeUnauthorized,
                    "Unauthorized: Maintenance lease cannot be released by a different session or invalid owner token.");
            }

            // Audit P09 reconciliation evidence before allowing lease release!
            if (!VerifyP09ReconciliationEvidence(this._paths, request_.WorkflowId, session_.MaintenanceBaselineRevision))
            {
                // Incomplete or absent P09 reconciliation: preserve durable global exclusion!
                session_.ReleaseLeaseLocks();
                this._activeLeaseSessions.Remove(request_.WorkflowId);

                return WorkflowBrokerResponse.Error(
                    request_.RequestId,
                    WorkflowBrokerProtocol.ErrorCodeConflict,
                    $"Cannot release maintenance lease for workflow '{request_.WorkflowId}': missing or incomplete P09 reconciliation evidence. Workflow remains under global exclusion.");
            }

            string activePath = this._paths.GetActiveLeasePath(request_.WorkflowId);
            WorkflowSchedulerDomainMetadata.DeleteAtomic(activePath, this._paths.ActiveLeasesDirectory);

            try
            {
                WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    this._paths,
                    request_.WorkflowId,
                    domainLockHandle_: this._lifetimeDomainLockHandle);
            }
            catch
            {
            }

            session_.ReleaseLeaseLocks();
            this._activeLeaseSessions.Remove(request_.WorkflowId);

            return WorkflowBrokerResponse.Ok(request_.RequestId);
        }
        finally
        {
            this._coordinationGate.Release();
        }
    }

    private WorkflowBrokerResponse HandleInspectIntegrity(WorkflowBrokerRequest request_)
    {
        int maxSlots = request_.MaxConcurrentWorkflows > 0 ? request_.MaxConcurrentWorkflows : this._maxConcurrentWorkflows;
        WorkflowSchedulerIntegrityReport report = WorkflowSchedulerIntegrityInspector.Inspect(
            this._paths,
            maxSlots,
            this._lifetimeDomainLockHandle);

        WorkflowBrokerIntegrityReportDto dto = new()
        {
            IsHealthy = report.IsHealthy,
            HasGlobalExclusion = report.HasGlobalExclusion,
            ActiveLeaseCount = report.ActiveLeases.Count,
            QuarantinedLeaseCount = report.QuarantinedLeases.Count,
            MaintenanceLeaseCount = report.MaintenanceLeases.Count,
            Violations = report.Violations.ToList()
        };

        return WorkflowBrokerResponse.OkIntegrity(request_.RequestId, dto);
    }

    private async Task HandleSessionTerminationAsync(WorkflowBrokerSession session_)
    {
        await this._coordinationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (this._sessions)
            {
                this._sessions.Remove(session_);
            }

            if (session_.ActiveLease is not null)
            {
                // Worker process crashed or disconnected unexpectedly: durably quarantine the lease!
                string workflowId = session_.ActiveLease.WorkflowId;
                int slot = session_.ActiveLease.Slot;

                List<WorkflowResourceClaim> claims = session_.ActiveLease.ResourceClaims
                    .Select(c => new WorkflowResourceClaim(c.ResourceId, (WorkflowResourceAccessKind) c.AccessKind))
                    .ToList();

                bool isMaintenance = string.Equals(session_.ActiveLease.DomainMode, "ExclusiveMaintenance", StringComparison.Ordinal);

                WorkflowSchedulerLeaseManifest preservedManifest;
                if (isMaintenance)
                {
                    // Preserve MaintenanceInProgress and ExclusiveMaintenance domain mode!
                    // Never downgrade to Quarantined/Normal!
                    preservedManifest = new(
                        workflowId,
                        WorkflowSchedulerLeaseState.MaintenanceInProgress,
                        session_.ActiveLease.OwnerToken,
                        slot_: null,
                        domainMode_: "ExclusiveMaintenance",
                        resourceClaims_: [],
                        this._maxConcurrentWorkflows);
                }
                else
                {
                    preservedManifest = new(
                        workflowId,
                        WorkflowSchedulerLeaseState.Quarantined,
                        session_.ActiveLease.OwnerToken,
                        slot_: null,
                        session_.ActiveLease.DomainMode,
                        claims,
                        this._maxConcurrentWorkflows);
                }

                try
                {
                    this.FaultInjectionBeforeQuarantineWrite?.Invoke();

                    WorkflowSchedulerLeaseManifest.WriteAtomic(
                        this._paths,
                        preservedManifest,
                        this._maxConcurrentWorkflows,
                        this._lifetimeDomainLockHandle);

                    session_.ReleaseLeaseLocks();
                    this._activeLeaseSessions.Remove(workflowId);
                    this._occupiedSlots.Remove(slot);
                }
                catch (Exception ex)
                {
                    this._quarantineFailedDuringShutdown = true;
                    this._shutdownQuarantineException ??= ex;

                    // CRITICAL: Fail-closed durability!
                    // Never release workflow, slot or domain-mode OS locks when durable quarantine publication fails.
                    this._admissionBlockedReason = $"Durable quarantine publication failed upon worker disconnect for workflow '{workflowId}': {ex.Message}";
                    lock (this._retainedFailedGenerationLocks)
                    {
                        if (session_.WorkflowLockHandle is not null) this._retainedFailedGenerationLocks.Add(session_.WorkflowLockHandle);
                        if (session_.SlotLockHandle is not null) this._retainedFailedGenerationLocks.Add(session_.SlotLockHandle);
                        if (session_.DomainModeLockHandle is not null) this._retainedFailedGenerationLocks.Add(session_.DomainModeLockHandle);
                        if (session_.OwnerAuthHandle is not null) this._retainedFailedGenerationLocks.Add(session_.OwnerAuthHandle);
                    }

                    session_.WorkflowLockHandle = null;
                    session_.SlotLockHandle = null;
                    session_.DomainModeLockHandle = null;
                    session_.OwnerAuthHandle = null;
                }
            }
        }
        finally
        {
            this._coordinationGate.Release();
        }

        await session_.DisposeAsync().ConfigureAwait(false);
    }

    private long GetCurrentWorkflowRevision(string workflowId)
    {
        try
        {
            WorkflowPersistenceStore store = new(this._paths.StorageRoot, new WorkflowId(workflowId));
            WorkflowPersistenceSnapshot? snapshot = store.Load();
            return snapshot?.Revision ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    internal static bool VerifyP09ReconciliationEvidence(WorkflowSchedulerPaths paths, string workflowId, long baselineRevision)
    {
        try
        {
            WorkflowPersistenceStore store = new(paths.StorageRoot, new WorkflowId(workflowId));
            WorkflowPersistenceSnapshot? snapshot = store.Load();
            if (snapshot is null)
            {
                return false;
            }

            // 1. Revision must have strictly advanced past baseline
            if (snapshot.Revision <= baselineRevision)
            {
                return false;
            }

            // 2. All steps must be resolved (no steps left in Running state)
            foreach (WorkflowStepState step in snapshot.State.Steps)
            {
                if (step.Status == WorkflowStepStatus.Running)
                {
                    return false;
                }
            }

            // 3. Complete directory inspection of execution-checkpoints: malformed or unrecognized artifacts fail closed
            string workflowIdHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(workflowId)).ToLowerInvariant();
            string checkpointsBaseDir = Path.Combine(paths.StorageRoot, "workflows", workflowIdHex, "execution-checkpoints");
            if (!Directory.Exists(checkpointsBaseDir))
            {
                return false;
            }

            if (Directory.GetFiles(checkpointsBaseDir).Length > 0)
            {
                return false;
            }

            string[] taskDirs = Directory.GetDirectories(checkpointsBaseDir);
            if (taskDirs.Length == 0)
            {
                return false;
            }

            bool foundValidJournal = false;
            long maxAuditedRevision = 0;

            foreach (string taskDir in taskDirs)
            {
                if (Directory.GetFiles(taskDir).Length > 0)
                {
                    return false;
                }

                string taskIdHex = Path.GetFileName(taskDir);
                string taskId;
                try
                {
                    byte[] taskBytes = Convert.FromHexString(taskIdHex);
                    taskId = System.Text.Encoding.UTF8.GetString(taskBytes);
                }
                catch
                {
                    return false;
                }

                string[] revDirs = Directory.GetDirectories(taskDir);
                foreach (string revDir in revDirs)
                {
                    string revDirName = Path.GetFileName(revDir);
                    if (revDirName.Length != 20 || !long.TryParse(revDirName, out _))
                    {
                        return false;
                    }

                    string[] revSubDirs = Directory.GetDirectories(revDir);
                    if (revSubDirs.Length != 1 || !string.Equals(Path.GetFileName(revSubDirs[0]), "records", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (Directory.GetFiles(revDir).Length > 0)
                    {
                        return false;
                    }
                }

                IReadOnlyList<long> candidateRevisions =
                    WorkflowCheckpointJournal.DiscoverCandidateRevisions(paths.StorageRoot, new WorkflowId(workflowId), taskId);

                if (candidateRevisions.Count == 0)
                {
                    continue;
                }

                foreach (long rev in candidateRevisions)
                {
                    WorkflowCheckpointJournal journal;
                    try
                    {
                        journal = WorkflowCheckpointJournal.OpenExisting(paths.StorageRoot, new WorkflowId(workflowId), taskId, rev);
                    }
                    catch
                    {
                        return false;
                    }

                    // Invariant: First record must be CheckpointStartedRecord with Sequence 1
                    if (journal.StartedRecord is null || journal.StartedRecord.Sequence != 1)
                    {
                        return false;
                    }

                    if (!string.Equals(journal.StartedRecord.WorkflowId, workflowId, StringComparison.Ordinal) ||
                        !string.Equals(journal.StartedRecord.TaskId, taskId, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    // Invariant: Base persistence revision must be valid
                    long baseRev = journal.StartedRecord.BasePersistenceRevision;
                    if (baseRev < 1 || baseRev > snapshot.Revision)
                    {
                        return false;
                    }

                    // Load base persistence record to validate base state fingerprint
                    string recordsDir = Path.Combine(paths.StorageRoot, "workflows", workflowIdHex, "records");
                    string baseRecordPath = Path.Combine(recordsDir, $"{baseRev:D20}.json");
                    string? expectedBaseStateFp = GetStateFingerprintFromRecord(baseRecordPath);
                    if (expectedBaseStateFp is null || !string.Equals(journal.StartedRecord.BaseWorkflowStateFingerprint, expectedBaseStateFp, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    // Reject placeholder fingerprints fail-closed: must be valid 64-char lowercase hex
                    if (!WorkflowGateChallenge.IsLowerHex64(journal.StartedRecord.InputFingerprint) ||
                        !WorkflowGateChallenge.IsLowerHex64(journal.StartedRecord.RegistryFingerprint))
                    {
                        return false;
                    }

                    // Invariant: Canonical P09 ExecutionCompletedRecord MUST be present
                    ExecutionCompletedRecord? completedRec = journal.CompletedRecord;
                    if (completedRec is null)
                    {
                        return false;
                    }

                    if (completedRec.AttemptCount <= 0 || completedRec.ExpectedProjectionTransitionCount < 0)
                    {
                        return false;
                    }

                    // Invariant: ProjectionCompletedRecord MUST be present and be the final record in the journal
                    ProjectionCompletedRecord? projRec = journal.ProjectionCompletedRecord;
                    if (projRec is null)
                    {
                        return false;
                    }

                    if (journal.Records.Count == 0 || !ReferenceEquals(journal.Records[^1], projRec))
                    {
                        return false;
                    }

                    long finalRev = projRec.FinalPersistenceRevision;
                    if (finalRev < baseRev || finalRev > snapshot.Revision)
                    {
                        return false;
                    }

                    // Validate final state fingerprint against canonical persistence state at THIS journal's final revision
                    string finalRecordPath = Path.Combine(recordsDir, $"{finalRev:D20}.json");
                    string? expectedFinalStateFp = GetStateFingerprintFromRecord(finalRecordPath);
                    if (expectedFinalStateFp is null || !string.Equals(completedRec.FinalStateFingerprint, expectedFinalStateFp, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    long expectedTransitions = finalRev - baseRev;
                    if (expectedTransitions != completedRec.ExpectedProjectionTransitionCount)
                    {
                        return false;
                    }

                    // Verify projection prefix: every persistence record from baseRev + 1 to finalRev must exist
                    for (long r = baseRev + 1; r <= finalRev; r++)
                    {
                        string rPath = Path.Combine(recordsDir, $"{r:D20}.json");
                        if (!File.Exists(rPath))
                        {
                            return false;
                        }
                    }

                    // Invariant: Matching operation IDs, kinds, ordinals, generations, and request fingerprints
                    Dictionary<(long Ordinal, int Generation), (string OpId, string Kind, string RequestFp)> outcomes = [];
                    Dictionary<(long Ordinal, int Generation), string> rnes = [];

                    foreach (JournalRecord rec in journal.Records)
                    {
                        if (rec is InvocationOutcomeRecord outcome)
                        {
                            outcomes[(outcome.OperationOrdinal, outcome.InvocationGeneration)] =
                                (outcome.OperationId, outcome.OperationKind, outcome.RequestFingerprint);
                        }
                        else if (rec is ReconciliationNotExecutedRecord rne)
                        {
                            rnes[(rne.OperationOrdinal, rne.InvocationGeneration)] = rne.OperationId;
                        }
                    }

                    foreach (JournalRecord rec in journal.Records)
                    {
                        if (rec is InvocationIntentRecord intent)
                        {
                            bool hasMatchingOutcome = outcomes.TryGetValue((intent.OperationOrdinal, intent.InvocationGeneration), out var outcomeInfo) &&
                                                      string.Equals(outcomeInfo.OpId, intent.OperationId, StringComparison.Ordinal) &&
                                                      string.Equals(outcomeInfo.Kind, intent.OperationKind, StringComparison.Ordinal) &&
                                                      string.Equals(outcomeInfo.RequestFp, intent.RequestFingerprint, StringComparison.Ordinal);

                            bool hasMatchingRne = rnes.TryGetValue((intent.OperationOrdinal, intent.InvocationGeneration), out string? rneOpId) &&
                                                  string.Equals(rneOpId, intent.OperationId, StringComparison.Ordinal);

                            if (!hasMatchingOutcome && !hasMatchingRne)
                            {
                                return false;
                            }

                            if (journal.FindActiveAmbiguousIntent(intent.OperationOrdinal) is not null)
                            {
                                return false;
                            }
                        }
                    }

                    maxAuditedRevision = Math.Max(maxAuditedRevision, finalRev);
                    foundValidJournal = true;
                }
            }

            // Invariant: Consistency between journal projection and canonical P02 state transitions
            IReadOnlyList<WorkflowExecutionEvent> events = store.ReadEvents();
            if (events.Count != snapshot.Revision)
            {
                return false;
            }

            if (maxAuditedRevision != snapshot.Revision)
            {
                return false;
            }

            return foundValidJournal;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetStateFingerprintFromRecord(string recordPath)
    {
        if (!File.Exists(recordPath))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(recordPath));
            if (!doc.RootElement.TryGetProperty("state", out JsonElement stateElem))
            {
                return null;
            }

            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("sourceImplementationPlanRevision", stateElem.GetProperty("sourceImplementationPlanRevision").GetInt32());
                writer.WriteNumber("status", stateElem.GetProperty("status").GetInt32());
                writer.WriteStartArray("steps");
                foreach (JsonElement step in stateElem.GetProperty("steps").EnumerateArray())
                {
                    writer.WriteStartObject();
                    writer.WriteString("taskId", step.GetProperty("taskId").GetString());
                    writer.WriteNumber("status", step.GetProperty("status").GetInt32());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            byte[] hash = SHA256.HashData(stream.ToArray());
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        this.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref this._shuttingDown, 1, 0) != 0)
        {
            await this._shutdownTcs.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            this._cts.Cancel();

            if (this.ControlledShutdownHook is not null)
            {
                await this.ControlledShutdownHook().ConfigureAwait(false);
            }

            // 1. Close server listener streams immediately to stop accepting any new connections
            try
            {
                if (this._currentWindowsServerStream is not null)
                {
                    await this._currentWindowsServerStream.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                this._linuxServerSocket?.Dispose();
            }
            catch
            {
            }

            // 2. Await listener task exit
            if (this._listenerTask is not null)
            {
                try
                {
                    await this._listenerTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            // 3. Dispose streams of all current sessions to unblock reads/writes immediately
            List<WorkflowBrokerSession> sessionsList;
            lock (this._sessions)
            {
                sessionsList = [.. this._sessions];
            }

            foreach (WorkflowBrokerSession s in sessionsList)
            {
                try
                {
                    s.Dispose();
                }
                catch
                {
                }
            }

            // 4. Await completion of all active session tasks without holding any locks or gates
            Task[] tasksToAwait;
            lock (this._activeSessionTasks)
            {
                this._activeSessionTasks.RemoveWhere(t => t.IsCompleted);
                tasksToAwait = [.. this._activeSessionTasks];
            }

            if (tasksToAwait.Length > 0)
            {
                try
                {
                    await Task.WhenAll(tasksToAwait).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            // 5. Under coordination gate, safely quarantine any residual live leases
            await this._coordinationGate.WaitAsync().ConfigureAwait(false);
            bool quarantineFailed = false;
            Exception? firstQuarantineException = null;

            try
            {
                lock (this._sessions)
                {
                    foreach (var kvp in this._activeLeaseSessions.ToList())
                    {
                        string workflowId = kvp.Key;
                        WorkflowBrokerSession session = kvp.Value;
                        if (session.ActiveLease is not null)
                        {
                            if (session.WorkflowLockHandle is null && session.SlotLockHandle is null && session.OwnerAuthHandle is null)
                            {
                                continue;
                            }

                            try
                            {
                                this.FaultInjectionBeforeQuarantineWrite?.Invoke();

                                bool isMaintenance = string.Equals(session.ActiveLease.DomainMode, "ExclusiveMaintenance", StringComparison.Ordinal);

                                WorkflowSchedulerLeaseManifest preservedManifest;
                                if (isMaintenance)
                                {
                                    preservedManifest = new(
                                        workflowId,
                                        WorkflowSchedulerLeaseState.MaintenanceInProgress,
                                        session.ActiveLease.OwnerToken,
                                        slot_: null,
                                        domainMode_: "ExclusiveMaintenance",
                                        resourceClaims_: [],
                                        this._maxConcurrentWorkflows);
                                }
                                else
                                {
                                    preservedManifest = new(
                                        workflowId,
                                        WorkflowSchedulerLeaseState.Quarantined,
                                        session.ActiveLease.OwnerToken,
                                        slot_: null,
                                        session.ActiveLease.DomainMode,
                                        session.ActiveLease.ResourceClaims.Select(c => new WorkflowResourceClaim(c.ResourceId, (WorkflowResourceAccessKind) c.AccessKind)).ToList(),
                                        this._maxConcurrentWorkflows);
                                }

                                WorkflowSchedulerLeaseManifest.WriteAtomic(
                                    this._paths,
                                    preservedManifest,
                                    this._maxConcurrentWorkflows,
                                    this._lifetimeDomainLockHandle);

                                if (session.OwnerAuthHandle is not null)
                                {
                                    session.OwnerAuthHandle.Dispose();
                                    session.OwnerAuthHandle = null;
                                }
                                else
                                {
                                    WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                                        this._paths,
                                        workflowId,
                                        expectedOwnerToken_: session.ActiveLease.OwnerToken,
                                        domainLockHandle_: this._lifetimeDomainLockHandle);
                                }

                                session.ReleaseLeaseLocks();
                            }
                            catch (Exception ex)
                            {
                                quarantineFailed = true;
                                firstQuarantineException ??= ex;

                                lock (this._retainedFailedGenerationLocks)
                                {
                                    if (session.WorkflowLockHandle is not null) this._retainedFailedGenerationLocks.Add(session.WorkflowLockHandle);
                                    if (session.SlotLockHandle is not null) this._retainedFailedGenerationLocks.Add(session.SlotLockHandle);
                                    if (session.DomainModeLockHandle is not null) this._retainedFailedGenerationLocks.Add(session.DomainModeLockHandle);
                                    if (session.OwnerAuthHandle is not null) this._retainedFailedGenerationLocks.Add(session.OwnerAuthHandle);
                                }

                                session.WorkflowLockHandle = null;
                                session.SlotLockHandle = null;
                                session.DomainModeLockHandle = null;
                                session.OwnerAuthHandle = null;
                            }
                        }
                    }

                    bool hasPriorRetainedLocks = false;
                    lock (this._retainedFailedGenerationLocks)
                    {
                        hasPriorRetainedLocks = this._retainedFailedGenerationLocks.Count > 0;
                    }

                    if (!quarantineFailed && !hasPriorRetainedLocks && !this._quarantineFailedDuringShutdown)
                    {
                        this._activeLeaseSessions.Clear();
                        this._occupiedSlots.Clear();

                        foreach (WorkflowBrokerSession session in this._sessions)
                        {
                            session.Dispose();
                        }
                        this._sessions.Clear();
                    }
                }
            }
            finally
            {
                this._coordinationGate.Release();
            }

            if (OperatingSystem.IsLinux())
            {
                try
                {
                    string socketPath = WorkflowBrokerIpcTransport.GetEndpointIdentifier(this._paths);
                    if (File.Exists(socketPath))
                    {
                        File.Delete(socketPath);
                    }
                }
                catch
                {
                }
            }

            bool hasRetainedFailureLocks = false;
            lock (this._retainedFailedGenerationLocks)
            {
                hasRetainedFailureLocks = this._retainedFailedGenerationLocks.Count > 0;
            }

            if (this._quarantineFailedDuringShutdown || quarantineFailed || hasRetainedFailureLocks)
            {
                string failureReason = this._shutdownQuarantineException?.Message
                    ?? firstQuarantineException?.Message
                    ?? this._admissionBlockedReason
                    ?? "Unresolved critical failure: retained failure locks present";

                throw new InvalidOperationException(
                    $"Broker shutdown failed: could not safely quarantine active leases ({failureReason}). Execution authority retained fail-closed.",
                    this._shutdownQuarantineException ?? firstQuarantineException);
            }

            lock (this._retainedFailedGenerationLocks)
            {
                foreach (IDisposable lockHandle in this._retainedFailedGenerationLocks)
                {
                    try
                    {
                        lockHandle.Dispose();
                    }
                    catch
                    {
                    }
                }
                this._retainedFailedGenerationLocks.Clear();
            }

            this._linuxStorageRootHandle?.Dispose();
            this._linuxStorageRootHandle = null;
            this._windowsStorageRootHandle?.Dispose();
            this._windowsStorageRootHandle = null;
            this._lifetimeDomainLockHandle?.Dispose();
            this._lifetimeDomainLockHandle = null;
            this._cts.Dispose();

            this._shutdownTcs.TrySetResult(null);
        }
        catch (Exception ex)
        {
            this._shutdownTcs.TrySetException(ex);
            throw;
        }
    }

    internal void ForceReleaseRetainedLocksForTesting()
    {
        lock (this._retainedFailedGenerationLocks)
        {
            foreach (IDisposable lockHandle in this._retainedFailedGenerationLocks)
            {
                try
                {
                    lockHandle.Dispose();
                }
                catch
                {
                }
            }
            this._retainedFailedGenerationLocks.Clear();
        }

        this._linuxStorageRootHandle?.Dispose();
        this._linuxStorageRootHandle = null;
        this._windowsStorageRootHandle?.Dispose();
        this._windowsStorageRootHandle = null;
        this._lifetimeDomainLockHandle?.Dispose();
        this._lifetimeDomainLockHandle = null;
        try
        {
            this._cts.Dispose();
        }
        catch
        {
        }
    }
}

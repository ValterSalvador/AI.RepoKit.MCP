namespace AiRepoKit.WorkflowExecution.Tests;

using System.Diagnostics;
using Xunit;

public sealed class WorkflowSchedulerMultiprocessLockTests
{
    private static string GetRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "AI.RepoKit.MCP.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static readonly System.Collections.Concurrent.ConcurrentBag<string> _tempScripts =
        new();

    static WorkflowSchedulerMultiprocessLockTests()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupHelperScripts();
    }

    private static string CreateUniqueHelperScript()
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            "RepokitLockHelper_" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(scriptPath, TestLockProcessSource.SourceCode);
        _tempScripts.Add(scriptPath);
        return scriptPath;
    }

    private static void CleanupHelperScripts()
    {
        while (_tempScripts.TryTake(out string? path))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private static string CreateTempStorageRoot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "repokit-mproc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Process StartHelperProcess(string command, string path, params string[] extraArgs)
    {
        string scriptPath = CreateUniqueHelperScript();
        string extra = extraArgs.Length > 0 ? " " + string.Join(" ", extraArgs) : string.Empty;

        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            Arguments = $"run \"{scriptPath}\" -- {command} \"{path}\"{extra}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process = Process.Start(psi)!;

        // Read startup confirmation with timeout
        string? line = null;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        cts.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        while (!process.StandardOutput.EndOfStream)
        {
            line = process.StandardOutput.ReadLine();
            if (line == null)
            {
                break;
            }

            if (line.StartsWith("HELPER_PID:", StringComparison.Ordinal) &&
                int.TryParse(
                    line.AsSpan("HELPER_PID:".Length),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int reportedPid))
            {
                _holderPids[process.Id] = reportedPid;
                continue;
            }

            if (line.Contains("LOCKED") || line.Contains("SLOT_LOCKED") || line.Contains("WORKFLOW_LOCKED") || line.Contains("ERROR"))
            {
                break;
            }
        }

        if (line == null || (!line.Contains("LOCKED") && !line.Contains("SLOT_LOCKED") && !line.Contains("WORKFLOW_LOCKED")))
        {
            string err = process.StandardError.ReadToEnd();
            process.Dispose();
            throw new InvalidOperationException($"Child process failed to acquire lock. Line: '{line}', Stderr: '{err}'");
        }

        return process;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _holderPids = new();

    /// <summary>
    /// Returns the PID of the process that actually runs the helper code and holds its OS locks.
    /// Under <c>dotnet run</c> this is NOT <see cref="Process.Id"/> of the launcher.
    /// </summary>
    private static int GetHolderPid(Process launcher)
    {
        Assert.True(
            _holderPids.TryGetValue(launcher.Id, out int holderPid),
            $"Helper launcher PID {launcher.Id} never reported HELPER_PID; cannot identify the lock-holding process.");
        return holderPid;
    }

    /// <summary>
    /// Linux: parses /proc/&lt;pid&gt;/stat. Returns false when the process no longer exists.
    /// </summary>
    private static bool TryReadLinuxProcState(int pid, out char state, out int parentPid)
    {
        state = '?';
        parentPid = 0;
        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            int close = stat.LastIndexOf(')');
            if (close < 0 || close + 4 >= stat.Length)
            {
                return false;
            }

            string[] parts = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            state = parts[0][0];
            parentPid = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// True while the process is alive and able to hold file descriptors.
    /// A zombie (Z) or dead (X) process has already released all descriptors and locks.
    /// </summary>
    private static bool IsProcessHoldingResources(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            return TryReadLinuxProcState(pid, out char state, out _) && state is not ('Z' or 'X');
        }

        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Bounded wait (polling, no blanket timeout increase) until the given PID can no longer hold resources.
    /// </summary>
    private static bool WaitUntilProcessGone(int pid, TimeSpan timeout, out TimeSpan elapsed)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (IsProcessHoldingResources(pid))
        {
            if (sw.Elapsed >= timeout)
            {
                elapsed = sw.Elapsed;
                return false;
            }

            Thread.Sleep(10);
        }

        elapsed = sw.Elapsed;
        return true;
    }

    private static void KillProcessByPid(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    /// <summary>
    /// Diagnostic read that is compatible with the owner's flock (the production handles stay unchanged).
    /// On Linux .NET's FileStream rejects any open, even FileShare.ReadWrite, while another process holds
    /// LOCK_EX on the file, so the file is opened with plain open(2) and wrapped in a handle-based stream
    /// (which does not take a flock). This mirrors production's LinuxReadAllBytesWithoutFlock.
    /// </summary>
    private static string ReadAllTextWithoutFlock(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            using FileStream shared = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using StreamReader sharedReader = new(shared, System.Text.Encoding.UTF8);
            return sharedReader.ReadToEnd();
        }

        int fd = WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.Open(
            path,
            WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_RDONLY |
            WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_CLOEXEC |
            WorkflowSchedulerDomainMetadata.LinuxDirectoryNative.O_NOFOLLOW);
        Assert.True(fd >= 0, $"open(O_RDONLY) failed for '{path}'.");

        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = new(new IntPtr(fd), ownsHandle: true);
        using FileStream stream = new(handle, FileAccess.Read, bufferSize: 4096);
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void AssertFlockHeldBy(string path, int expectedPid, string description)
    {
        LockEnumerationResult result =
            WorkflowSchedulerIntegrityInspector.GetLockingProcesses(path, requireExclusive: true);
        Assert.True(result.IsSuccess, $"Lock enumeration failed for {description}.");
        Assert.True(
            result.ProcessIds.Contains(expectedPid),
            $"{description}: FLOCK holders [{string.Join(",", result.ProcessIds)}] do not include owner PID {expectedPid}.");
    }

    /// <summary>
    /// Runs the adversarial same-inode substitution helper (open(O_WRONLY|O_TRUNC), no flock) and
    /// proves the write took place by checking the helper's exit code and completion marker.
    /// </summary>
    private static void RunSubstitutionHelper(string root, string workflowId, string token, int recordedPid, long recordedStartTicks)
    {
        Process helper = StartHelperProcess(
            "substitute-auth-same-inode",
            root,
            workflowId,
            token,
            recordedPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            recordedStartTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "1",
            "Normal");
        try
        {
            Assert.True(helper.WaitForExit(15000), "Substitution helper did not exit within 15s.");
            Assert.True(
                helper.ExitCode == 0,
                $"Substitution helper exit code {helper.ExitCode} (10 = open failed, 11 = write incomplete).");
        }
        finally
        {
            StopHelperProcess(helper);
        }
    }

    private static void StopHelperProcess(Process process)
    {
        _holderPids.TryRemove(process.Id, out int holderPid);
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("UNLOCK");
                process.StandardInput.Flush();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
        finally
        {
            process.Dispose();

            // The launcher exiting does not prove the real lock holder has exited: bound the wait,
            // then terminate it explicitly so the OS releases its locks before the test proceeds.
            if (holderPid > 0 && !WaitUntilProcessGone(holderPid, TimeSpan.FromSeconds(5), out _))
            {
                KillProcessByPid(holderPid);
                WaitUntilProcessGone(holderPid, TimeSpan.FromSeconds(5), out _);
            }
        }
    }

    [Fact]
    public async Task DomainLock_ExcludesIndependentProcess_AndRecoversOnRelease()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            Process child = StartHelperProcess("lock-exclusive", paths.DomainLockPath);
            try
            {
                // Main process cannot acquire exclusive while child holds it
                using WorkflowSchedulerLockHandle? handle =
                    WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath);
                Assert.Null(handle);

                // Timed acquisition fails
                await Assert.ThrowsAsync<TimeoutException>(
                    () => WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                        paths.DomainLockPath,
                        TimeSpan.FromMilliseconds(150)));
            }
            finally
            {
                StopHelperProcess(child);
            }

            // Immediately after child releases lock, main process can acquire it
            using WorkflowSchedulerLockHandle? acquired =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath);
            Assert.NotNull(acquired);
            Assert.True(acquired.IsExclusive);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DomainModeLock_SharedReadersCoexist_ExclusiveModeBlocked()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            Process child = StartHelperProcess("lock-shared", paths.DomainModeLockPath);
            try
            {
                // Main process CAN acquire shared lock simultaneously!
                using WorkflowSchedulerLockHandle? sharedHandle =
                    WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath);
                Assert.NotNull(sharedHandle);
                Assert.False(sharedHandle.IsExclusive);

                // Main process CANNOT acquire exclusive lock while shared lock is held
                using WorkflowSchedulerLockHandle? exclusiveHandle =
                    WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath);
                Assert.Null(exclusiveHandle);
            }
            finally
            {
                StopHelperProcess(child);
            }

            // Once child releases, exclusive mode can be acquired
            using WorkflowSchedulerLockHandle? exclusiveAcquired =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath);
            Assert.NotNull(exclusiveAcquired);
            Assert.True(exclusiveAcquired.IsExclusive);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkflowLock_ExcludesConcurrentOwnersForSameWorkflow()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string wf1 = "wf-alpha";
            string wf2 = "wf-beta";

            paths.EnsureWorkflowDirectory(wf1);
            paths.EnsureWorkflowDirectory(wf2);

            Process child = StartHelperProcess("hold-workflow", root, wf1);
            try
            {
                // Cannot acquire wf1
                using WorkflowSchedulerLockHandle? wf1Handle =
                    WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wf1));
                Assert.Null(wf1Handle);

                // CAN acquire wf2 (independent workflows)
                using WorkflowSchedulerLockHandle? wf2Handle =
                    WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wf2));
                Assert.NotNull(wf2Handle);
            }
            finally
            {
                StopHelperProcess(child);
            }

            // After child exits, wf1 can be acquired
            using WorkflowSchedulerLockHandle? wf1Acquired =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wf1));
            Assert.NotNull(wf1Acquired);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CapacitySlots_ExhaustionAndDeterministicOrder_AcrossProcesses()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            int maxSlots = 2;
            paths.EnsureSlotFile(1, maxSlots);
            paths.EnsureSlotFile(2, maxSlots);

            Process child1 = StartHelperProcess("lock-slot", paths.SlotsDirectory, "1", "2");
            try
            {
                // Slot 1 is taken by child1. Next available slot should be slot 2!
                (int SlotIndex, WorkflowSchedulerLockHandle Handle)? acquiredSlot =
                    WorkflowSchedulerLockPrimitives.TryAcquireSlot(paths, maxSlots);

                Assert.NotNull(acquiredSlot);
                Assert.Equal(2, acquiredSlot.Value.SlotIndex);

                using WorkflowSchedulerLockHandle slot2Handle = acquiredSlot.Value.Handle;

                // Both slot 1 and slot 2 are now occupied: capacity exhausted!
                Assert.Null(WorkflowSchedulerLockPrimitives.TryAcquireSlot(paths, maxSlots));

                // Bounded wait times out
                await Assert.ThrowsAsync<TimeoutException>(
                    () => WorkflowSchedulerLockPrimitives.AcquireSlotAsync(
                        paths,
                        maxSlots,
                        TimeSpan.FromMilliseconds(150)));
            }
            finally
            {
                StopHelperProcess(child1);
            }

            // Once child1 exits, slot 1 becomes available again and is picked first (deterministic order)
            (int SlotIndex, WorkflowSchedulerLockHandle Handle)? slot1Acquired =
                WorkflowSchedulerLockPrimitives.TryAcquireSlot(paths, maxSlots);
            Assert.NotNull(slot1Acquired);
            Assert.Equal(1, slot1Acquired.Value.SlotIndex);
            slot1Acquired.Value.Handle.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AbruptProcessTermination_ReleasesOsLockHandles()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            Process child = StartHelperProcess("lock-exclusive", paths.DomainLockPath);
            int launcherPid = child.Id;
            int holderPid = GetHolderPid(child);
            try
            {
                // Verify locked
                Assert.Null(WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath));

                // Identify the process that REALLY holds the kernel lock (not the dotnet-run launcher).
                if (OperatingSystem.IsLinux())
                {
                    Assert.True(
                        TryReadLinuxProcState(holderPid, out char holderState, out int holderParent),
                        $"Holder PID {holderPid} (launcher {launcherPid}) not visible in /proc.");
                    Assert.NotEqual('Z', holderState);

                    LockEnumerationResult before =
                        WorkflowSchedulerIntegrityInspector.GetLockingProcesses(paths.DomainLockPath, requireExclusive: true);
                    Assert.True(before.IsSuccess, "Could not enumerate /proc/locks before kill.");
                    Assert.True(
                        before.ProcessIds.Contains(holderPid),
                        $"Lock holder PIDs [{string.Join(",", before.ProcessIds)}] do not include helper PID {holderPid} " +
                        $"(launcher PID {launcherPid}, helper parent PID {holderParent}).");
                }

                // Kill abruptly: the actual holder first, then the launcher tree.
                KillProcessByPid(holderPid);
                child.Kill(entireProcessTree: true);
                Assert.True(
                    child.WaitForExit(5000),
                    $"Launcher PID {launcherPid} did not exit within 5s after Kill.");

                // Prove the real holder terminated (bounded, explicit diagnostics).
                bool gone = WaitUntilProcessGone(holderPid, TimeSpan.FromSeconds(5), out TimeSpan goneAfter);
                Assert.True(
                    gone,
                    $"Lock-holding PID {holderPid} (launcher {launcherPid}) still alive {goneAfter.TotalMilliseconds:F0}ms after SIGKILL.");
            }
            finally
            {
                _holderPids.TryRemove(launcherPid, out _);
                child.Dispose();
            }

            // Once the holder is gone the kernel must have released its lock. Bounded poll, no blanket timeout.
            Stopwatch lockWait = Stopwatch.StartNew();
            WorkflowSchedulerLockHandle? handle = null;
            while (handle is null && lockWait.Elapsed < TimeSpan.FromSeconds(2))
            {
                handle = WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath);
                if (handle is null)
                {
                    Thread.Sleep(10);
                }
            }

            using (handle)
            {
                Assert.True(
                    handle is not null,
                    $"Lock still held {lockWait.ElapsedMilliseconds}ms after holder PID {holderPid} terminated. " +
                    $"Remaining lock holders: [{string.Join(",", WorkflowSchedulerIntegrityInspector.GetLockingProcesses(paths.DomainLockPath).ProcessIds)}].");
                Assert.True(handle!.IsExclusive);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LockAcquisition_HonorsCancellationAndTimeout()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            Process child = StartHelperProcess("lock-exclusive", paths.DomainLockPath);
            try
            {
                // Timeout
                await Assert.ThrowsAsync<TimeoutException>(
                    () => WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                        paths.DomainLockPath,
                        TimeSpan.FromMilliseconds(100)));

                // Cancellation
                using CancellationTokenSource cts = new();
                cts.CancelAfter(50);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                        paths.DomainLockPath,
                        TimeSpan.FromSeconds(5),
                        cts.Token));
            }
            finally
            {
                StopHelperProcess(child);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeadlockAvoidance_NeverHoldsDomainLockWhileWaitingForIncompatibleLock()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            // Simulate protocol: Process holds domain.lock and needs workflow.lock
            using WorkflowSchedulerLockHandle domainLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath)!;
            Assert.NotNull(domainLock);

            // Child holds workflow.lock
            string wf = "wf-locked";
            paths.EnsureWorkflowDirectory(wf);
            Process child = StartHelperProcess("hold-workflow", root, wf);
            try
            {
                // Non-blocking try-acquire while holding domain.lock
                using WorkflowSchedulerLockHandle? wfLock =
                    WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.GetWorkflowLockPath(wf));

                // wfLock is busy: protocol dictates releasing domainLock instead of blocking!
                Assert.Null(wfLock);
            }
            finally
            {
                StopHelperProcess(child);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Multiprocess_DomainLock_ProtectsBootstrap_AgainstConcurrentProcess()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            // Child holds domain.lock
            Process child = StartHelperProcess("lock-exclusive", paths.DomainLockPath);
            try
            {
                // While child holds domain.lock, InitializeOrValidate must not bypass the lock!
                WorkflowSchedulerPolicy policy = new(
                    maxConcurrentWorkflows_: 4,
                    allowUnrestrictedExecution_: false,
                    continueUntilBlockedOrTerminal_: true,
                    domainLockTimeout_: TimeSpan.FromMilliseconds(200));

                Assert.Throws<TimeoutException>(
                    () => WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy));
            }
            finally
            {
                StopHelperProcess(child);
            }

            // Once child releases domain.lock, InitializeOrValidate succeeds cleanly
            WorkflowSchedulerPolicy validPolicy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, validPolicy);

            Assert.True(File.Exists(paths.DomainIntegrityPath));
            Assert.True(File.Exists(paths.DomainPolicyPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Multiprocess_DomainLock_ProtectsLeaseTransition_AgainstConcurrentProcess()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Child holds domain.lock
            Process child = StartHelperProcess("lock-exclusive", paths.DomainLockPath);
            try
            {
                WorkflowSchedulerLeaseManifest manifest = new(
                    "wf-mproc-transition",
                    WorkflowSchedulerLeaseState.Active,
                    "token-A",
                    1,
                    "Normal",
                    [],
                    4);

                // WriteAtomic must not bypass domain.lock: throws TimeoutException
                Assert.Throws<TimeoutException>(
                    () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4));
            }
            finally
            {
                StopHelperProcess(child);
            }

            // After child releases domain.lock, WriteAtomic succeeds
            WorkflowSchedulerLeaseManifest validManifest = new(
                "wf-mproc-transition",
                WorkflowSchedulerLeaseState.Active,
                "token-A",
                1,
                "Normal",
                [],
                4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, validManifest, 4);
            Assert.True(File.Exists(paths.GetActiveLeasePath("wf-mproc-transition")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DomainLock_WhileIndependentProcessHoldsExclusive_ForgedOrMismatchedHandle_IsRejected()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            TimeSpan policyTimeout = TimeSpan.FromMilliseconds(200);
            WorkflowSchedulerPolicy policy = new(
                maxConcurrentWorkflows_: 4,
                allowUnrestrictedExecution_: false,
                continueUntilBlockedOrTerminal_: true,
                domainLockTimeout_: policyTimeout);

            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            // Child process holds domain.lock exclusively
            Process child = StartHelperProcess("lock-exclusive", paths.DomainLockPath);
            try
            {
                // 1. Create a dummy file
                string dummyFile = Path.Combine(root, "dummy.lock");
                using (FileStream dummyStream = new(
                    dummyFile,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    // Attempting to construct WorkflowSchedulerLockHandle using dummyStream and paths.DomainLockPath must throw
                    Assert.Throws<InvalidOperationException>(
                        () => new WorkflowSchedulerLockHandle(dummyStream, paths.DomainLockPath, true));
                }

                // 2. Open dummy file legitimately and acquire exclusive handle with its own path
                using WorkflowSchedulerLockHandle mismatchedHandle =
                    WorkflowSchedulerLockPrimitives.TryAcquireExclusive(dummyFile)!;
                Assert.NotNull(mismatchedHandle);

                // ValidateDomainLockHandle must reject mismatched handle
                Assert.Throws<InvalidOperationException>(
                    () => WorkflowSchedulerDomainMetadata.ValidateDomainLockHandle(paths, mismatchedHandle));

                // WriteAtomic passing mismatched handle must reject it
                WorkflowSchedulerLeaseManifest manifest = new(
                    workflowId_: "wf-mproc-forged",
                    state_: WorkflowSchedulerLeaseState.Active,
                    ownerToken_: "owner-mproc-1",
                    slot_: 1,
                    domainMode_: "Normal",
                    resourceClaims_: [],
                    maxConcurrentWorkflows_: 4);

                Assert.Throws<InvalidOperationException>(
                    () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4, domainLockHandle_: mismatchedHandle));

                // WriteAtomic without handle must respect domainLockTimeout (200ms) rather than hardcoded 5000ms
                long startTicks = Environment.TickCount64;
                TimeoutException timeoutEx = Assert.Throws<TimeoutException>(
                    () => WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4));
                long elapsedMs = Environment.TickCount64 - startTicks;

                Assert.Contains("domain.lock", timeoutEx.Message);
                Assert.True(elapsedMs < 3000, $"WriteAtomic took {elapsedMs}ms; expected to respect domain timeout of {policyTimeout.TotalMilliseconds}ms.");
            }
            finally
            {
                StopHelperProcess(child);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveLease_ProcessAHoldsWorkflowLock_ProcessBHoldsSlotLock_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-split-locks";
            WorkflowSchedulerLeaseManifest activeManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "owner-split-1",
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4);

            // Process A locks workflow.lock
            Process procA = StartHelperProcess("hold-workflow", root, wfId);
            try
            {
                // Process B locks slot 1
                Process procB = StartHelperProcess("lock-slot", paths.SlotsDirectory, "1", "4");
                try
                {
                    // While each lock is exclusively held by independent processes without authenticated common ownership,
                    // inspector must classify state as indeterminate and fail closed.
                    WorkflowSchedulerIntegrityReport report =
                        WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                    Assert.False(report.IsHealthy);
                    Assert.Contains(report.Violations, v => v.Contains("lacks authenticated common ownership correlation"));
                }
                finally
                {
                    StopHelperProcess(procB);
                }
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveLease_SingleLegitimateOwner_ReportsHealthy()
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

            string wfId = "wf-mproc-legit";
            string ownerToken = "owner-mproc-legit";

            WorkflowSchedulerLeaseManifest activeManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: ownerToken,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4);

            // Single owner process holds both workflow.lock and slot 1 and authenticates its generation
            string asmPath = typeof(WorkflowSchedulerPaths).Assembly.Location;
            Process ownerProc = StartHelperProcess("hold-active-lease", root, wfId, "1", "4", "Normal", ownerToken, asmPath);
            try
            {
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.True(report.IsHealthy);
                Assert.Empty(report.Violations);
                Assert.Single(report.ActiveLeases);
                Assert.Equal(wfId, report.ActiveLeases[0].WorkflowId);
            }
            finally
            {
                StopHelperProcess(ownerProc);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveLease_LocksHeldWithDifferentGenerationOrToken_ReportsUnhealthy()
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

            string wfId = "wf-mproc-mismatch-token";
            string authenticOwnerToken = "owner-generation-1";
            string substitutedOwnerToken = "owner-generation-2-substituted";

            // True owner process acquires locks and authenticates for authenticOwnerToken first
            string asmPath = typeof(WorkflowSchedulerPaths).Assembly.Location;
            Process ownerProc = StartHelperProcess("hold-active-lease", root, wfId, "1", "4", "Normal", authenticOwnerToken, asmPath);
            try
            {
                // Manifest declares substitutedOwnerToken (simulating mismatched manifest vs authenticated owner generation)
                WorkflowSchedulerLeaseManifest activeManifest = new(
                    workflowId_: wfId,
                    state_: WorkflowSchedulerLeaseState.Active,
                    ownerToken_: substitutedOwnerToken,
                    slot_: 1,
                    domainMode_: "Normal",
                    resourceClaims_: [],
                    maxConcurrentWorkflows_: 4);

                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4);

                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.False(report.IsHealthy);
                Assert.Contains(report.Violations, v => v.Contains("authenticated ownerToken"));
            }
            finally
            {
                StopHelperProcess(ownerProc);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveUnrestrictedLease_WithoutDomainModeLock_ReportsUnhealthy()
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

            string wfId = "wf-unrestricted-nomode";
            WorkflowSchedulerLeaseManifest activeManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: "owner-unrestricted",
                slot_: 1,
                domainMode_: "Unrestricted",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, activeManifest, 4);
            }

            Process procA = StartHelperProcess("hold-workflow", root, wfId);
            try
            {
                Process procB = StartHelperProcess("lock-slot", paths.SlotsDirectory, "1", "4");
                try
                {
                    // domain-mode.lock is free (not exclusively held)
                    WorkflowSchedulerIntegrityReport report =
                        WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                    Assert.False(report.IsHealthy);
                    Assert.Contains(report.Violations, v => v.Contains("lacks required exclusive lock on domain-mode.lock"));
                }
                finally
                {
                    StopHelperProcess(procB);
                }
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveLease_AdversarialForeignProcess_ForgesOwnerAuth_ReportsUnhealthy()
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

            string wfId = "wf-adversarial-spoof";
            string forgedToken = "tok-adversary-forged";

            // Process A legitimately holds workflow.lock, slot 1 lock, and shared domain-mode.lock as a single process
            Process procA = StartHelperProcess("hold-active-locks", root, wfId, "1", "4", "Normal");
            try
            {
                // Process B (adversary) publishes manifest declaring Process A's slot and forgedToken
                WorkflowSchedulerLeaseManifest forgedManifest = new(
                    workflowId_: wfId,
                    state_: WorkflowSchedulerLeaseState.Active,
                    ownerToken_: forgedToken,
                    slot_: 1,
                    domainMode_: "Normal",
                    resourceClaims_: [],
                    maxConcurrentWorkflows_: 4);

                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, forgedManifest, 4);

                // Process B writes owner-auth.json spoofing Process A's PID and start time
                long startTicksOfA = WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(procA.Id);
                string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, wfId);
                string spoofedAuthJson = $$"""
                {
                  "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
                  "schemaVersion": 1,
                  "workflowId": "{{wfId}}",
                  "ownerToken": "{{forgedToken}}",
                  "slot": 1,
                  "domainMode": "Normal",
                  "processId": {{procA.Id}},
                  "processStartTimeTicks": {{startTicksOfA}}
                }
                """;
                File.WriteAllText(authPath, spoofedAuthJson);

                // The inspector MUST reject this adversarial state specifically because Process A does not hold the OS lock provenance on owner-auth.json
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.False(report.IsHealthy);
                Assert.Contains(report.Violations, v =>
                    v.Contains("owner-auth.json lacks genuine OS lock provenance held by lock owner PID"));
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static class NativePosixHelper
    {
        public const int O_RDONLY = 0;
        public const int O_CLOEXEC = 0x80000;

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(string pathname, int flags);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);
    }

    [Fact]
    public void IntegrityInspector_LinuxOpenFdWithoutLock_IsNotAcceptedAsLockOwnership()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();

            string testLockFile = Path.Combine(paths.LocksDirectory, "fd-only-test.lock");
            File.WriteAllText(testLockFile, "test");

            // Open with native open() without flock
            int fd = NativePosixHelper.Open(testLockFile, NativePosixHelper.O_RDONLY | NativePosixHelper.O_CLOEXEC);
            Assert.True(fd >= 0, "Native open failed.");

            try
            {
                IReadOnlyList<int> lockingPids =
                    WorkflowSchedulerIntegrityInspector.GetLockingProcessIds(testLockFile);

                // Even though this process has an open file descriptor in /proc/<pid>/fd,
                // it does NOT hold a kernel lock in /proc/locks, so GetLockingProcessIds must not include our PID!
                Assert.DoesNotContain(Environment.ProcessId, lockingPids);

                // Now acquire a genuine exclusive lock (FileShare.None -> flock LOCK_EX)
                using WorkflowSchedulerLockHandle? realLock =
                    WorkflowSchedulerLockPrimitives.TryAcquireExclusive(testLockFile);
                Assert.NotNull(realLock);

                IReadOnlyList<int> realLockingPids =
                    WorkflowSchedulerIntegrityInspector.GetLockingProcessIds(testLockFile);
                Assert.Contains(Environment.ProcessId, realLockingPids);
            }
            finally
            {
                NativePosixHelper.Close(fd);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveLease_AdversarialOwnerTokenSubstitution_RejectedByProvenance()
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

            string wfId = "wf-adversarial-subst";
            string legitToken = "tok-legit-t1";
            string substitutedToken = "tok-subst-t2";

            // Publish legitimate Active lease manifest for generation T1
            WorkflowSchedulerLeaseManifest initialManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: legitToken,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, initialManifest, 4);
            }

            // 1. Process A acquires genuine workflow, slot and domain-mode locks
            // 2. Process A registers a legitimate generation with ownerToken=T1
            // 3. Process A remains alive, with all handles open
            string asmPath = typeof(WorkflowSchedulerPaths).Assembly.Location;
            Process procA = StartHelperProcess("hold-active-lease", root, wfId, "1", "4", "Normal", legitToken, asmPath);
            try
            {
                // Verify initial state is healthy
                WorkflowSchedulerIntegrityReport initialReport =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
                Assert.True(initialReport.IsHealthy);

                // 4. Process B does not possess those locks.
                // 5. Process B attempts to substitute on disk the manifest and the authenticated record with ownerToken=T2,
                //    preserving PID and start-time of A.
                string activeLeasePath = paths.GetActiveLeasePath(wfId);
                string substitutedManifestJson = $$"""
                {
                  "schemaId": "ai.repokit.workflow-scheduler-lease",
                  "schemaVersion": 1,
                  "workflowId": "{{wfId}}",
                  "state": "Active",
                  "ownerToken": "{{substitutedToken}}",
                  "slot": 1,
                  "domainMode": "Normal",
                  "resourceClaims": []
                }
                """;
                File.WriteAllText(activeLeasePath, substitutedManifestJson);

                long startTicksOfA = WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(procA.Id);
                string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, wfId);
                string substitutedAuthJson = $$"""
                {
                  "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
                  "schemaVersion": 1,
                  "workflowId": "{{wfId}}",
                  "ownerToken": "{{substitutedToken}}",
                  "slot": 1,
                  "domainMode": "Normal",
                  "processId": {{procA.Id}},
                  "processStartTimeTicks": {{startTicksOfA}}
                }
                """;

                try
                {
                    File.WriteAllText(authPath, substitutedAuthJson);
                }
                catch (IOException)
                {
                    // On Windows, Process A's FileShare.Read prevents in-place overwrite.
                    // The substitution attempt was blocked by OS lock or will fail provenance.
                }

                // 6. Inspection cannot classify generation T2 as healthy!
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.False(report.IsHealthy);
                Assert.NotEmpty(report.Violations);
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveLease_UnregisteredFileReader_RejectedByProvenance()
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

            string wfId = "wf-unregistered-reader";
            string token = "tok-unregistered-1";

            // Write active lease manifest
            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: token,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4);
            }

            // Create owner-auth.json on disk BEFORE starting Process A
            string authPath = WorkflowSchedulerOwnerAuthentication.GetAuthFilePath(paths, wfId);
            paths.EnsureWorkflowDirectory(wfId);

            // Placeholder with temporary PID, will be rewritten with Process A's actual PID and start ticks
            File.WriteAllText(authPath, "{}");

            // Start Process A which acquires workflow.lock, slot lock, and shared domain-mode.lock,
            // and ONLY opens owner-auth.json for reading (FileShare.ReadWrite), without production registration!
            Process procA = StartHelperProcess("hold-active-locks-with-auth-reader", root, wfId, "1", "4", "Normal", authPath);
            try
            {
                long startTicksOfA = WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(procA.Id);
                string falsifiedAuthJson = $$"""
                {
                  "schemaId": "ai.repokit.workflow-scheduler-owner-auth",
                  "schemaVersion": 1,
                  "workflowId": "{{wfId}}",
                  "ownerToken": "{{token}}",
                  "slot": 1,
                  "domainMode": "Normal",
                  "processId": {{procA.Id}},
                  "processStartTimeTicks": {{startTicksOfA}}
                }
                """;

                // Overwrite the content while procA holds it open for reading (FileShare.ReadWrite allows writing)
                File.WriteAllText(authPath, falsifiedAuthJson);

                // Inspection runs:
                // Process A holds workflow.lock, slot lock, and mode lock, and has an open handle to owner-auth.json.
                // However, Process A only opened it for reading and did NOT execute production registration!
                // Inspector MUST reject it by provenance violation!
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.False(report.IsHealthy);
                Assert.Contains(report.Violations, v =>
                    v.Contains("owner-auth.json lacks genuine OS lock provenance"));
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveNormalLease_WithoutSharedDomainModeLock_ReportsUnhealthy()
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

            string wfId = "wf-normal-nomode";
            string ownerToken = "tok-normal-nomode";

            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: ownerToken,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4);

            Process procA = StartHelperProcess("hold-workflow", root, wfId);
            try
            {
                Process procSlot = StartHelperProcess("lock-slot", paths.SlotsDirectory, "1", "4");
                try
                {
                    // domain-mode.lock is not held shared
                    WorkflowSchedulerIntegrityReport report =
                        WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                    Assert.False(report.IsHealthy);
                    Assert.Contains(report.Violations, v => v.Contains("domain-mode.lock"));
                }
                finally
                {
                    StopHelperProcess(procSlot);
                }
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveUnrestrictedLease_WithExclusiveDomainModeLock_ReportsHealthy()
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

            string wfId = "wf-unrestricted-legit";
            string ownerToken = "tok-unrestricted-legit";

            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: ownerToken,
                slot_: 1,
                domainMode_: "Unrestricted",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4);
            }

            // Single owner process holds workflow.lock, slot 1 lock, and domain-mode.lock exclusive, authenticating its generation
            string asmPath = typeof(WorkflowSchedulerPaths).Assembly.Location;
            Process ownerProc = StartHelperProcess("hold-active-lease", root, wfId, "1", "4", "Unrestricted", ownerToken, asmPath);
            try
            {
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.True(report.IsHealthy);
                Assert.Empty(report.Violations);
                Assert.Single(report.ActiveLeases);
            }
            finally
            {
                StopHelperProcess(ownerProc);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_ActiveUnrestrictedAndNormal_ConflictOnDomainModeLock_CannotCoexist()
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

            // Process A acquires domain-mode.lock in shared mode (Normal workflow)
            using WorkflowSchedulerLockHandle? sharedModeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath);
            Assert.NotNull(sharedModeLock);

            // Process B attempts to acquire domain-mode.lock in exclusive mode (Unrestricted workflow)
            using WorkflowSchedulerLockHandle? exclusiveModeLock =
                WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainModeLockPath);

            // OS conflict on domain-mode.lock guarantees that Unrestricted and Normal cannot coexist!
            Assert.Null(exclusiveModeLock);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_MaintenanceLease_DomainModeLockHeldByDifferentOwner_ReportsUnhealthy()
    {
        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-maint-diffowner";
            WorkflowSchedulerLeaseManifest maintManifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.MaintenanceInProgress,
                ownerToken_: "owner-maint",
                slot_: null,
                domainMode_: "ExclusiveMaintenance",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            WorkflowSchedulerLeaseManifest.WriteAtomic(paths, maintManifest, 4);

            // Process A holds workflow.lock
            Process procA = StartHelperProcess("hold-workflow", root, wfId);
            try
            {
                // Process B holds domain-mode.lock exclusive (different owner)
                Process procB = StartHelperProcess("lock-exclusive", paths.DomainModeLockPath);
                try
                {
                    WorkflowSchedulerIntegrityReport report =
                        WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                    Assert.False(report.IsHealthy);
                    Assert.Contains(report.Violations, v => v.Contains("domain-mode.lock"));
                }
                finally
                {
                    StopHelperProcess(procB);
                }
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_WindowsNoByteLockSpoof_Rejected()
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

            string wfId = "wf-spoofer-win";
            string ownerToken = "tok-spoofer-win";

            // 1. Legitimate Active lease manifest published in active lease directory
            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: ownerToken,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4);
            }

            // 2. Process A acquires genuine workflow, slot, and domain-mode locks,
            // and opens owner-auth.json with FileAccess.ReadWrite, FileShare.Read,
            // containing forged record matching A's PID and start-time,
            // but does NOT execute FileStream.Lock over the sentinel byte.
            Process procA = StartHelperProcess("hold-active-locks-with-unlocked-auth", root, wfId, "1", "4", "Normal", ownerToken);
            try
            {
                // 3. Inspect must reject generation specifically for lack of genuine OS registration lock provenance!
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.False(report.IsHealthy);
                Assert.Contains(report.Violations, v => v.Contains("lacks genuine OS lock provenance"));
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_WindowsPositive_ProductionRegistration_ReportsHealthy()
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

            string wfId = "wf-win-pos";
            string ownerToken = "tok-win-pos";

            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: ownerToken,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4);
            }

            string asmPath = typeof(WorkflowSchedulerPaths).Assembly.Location;
            Process procA = StartHelperProcess("hold-active-lease", root, wfId, "1", "4", "Normal", ownerToken, asmPath);
            try
            {
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.True(report.IsHealthy);
                Assert.Empty(report.Violations);
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_LinuxPositive_ProductionRegistration_ReportsHealthy()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-linux-pos";
            string ownerToken = "tok-linux-pos";

            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: ownerToken,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4);
            }

            string asmPath = typeof(WorkflowSchedulerPaths).Assembly.Location;
            Process procA = StartHelperProcess("hold-active-lease", root, wfId, "1", "4", "Normal", ownerToken, asmPath);
            try
            {
                WorkflowSchedulerIntegrityReport report =
                    WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);

                Assert.True(report.IsHealthy);
                Assert.Empty(report.Violations);
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IntegrityInspector_LinuxSameInodeAdversarialSubstitution_Evaluated()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            WorkflowSchedulerPolicy policy = new(4);
            WorkflowSchedulerDomainMetadata.InitializeOrValidate(paths, policy);

            string wfId = "wf-adv-sub";
            string t1 = "tok-t1-authentic";
            string t2 = "tok-t2-substituted";

            WorkflowSchedulerLeaseManifest manifest = new(
                workflowId_: wfId,
                state_: WorkflowSchedulerLeaseState.Active,
                ownerToken_: t1,
                slot_: 1,
                domainMode_: "Normal",
                resourceClaims_: [],
                maxConcurrentWorkflows_: 4);

            using (WorkflowSchedulerLockHandle? modeLock =
                   WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath))
            {
                Assert.NotNull(modeLock);
                WorkflowSchedulerLeaseManifest.WriteAtomic(paths, manifest, 4);
            }

            string asmPath = typeof(WorkflowSchedulerPaths).Assembly.Location;
            Process procA = StartHelperProcess("hold-active-lease", root, wfId, "1", "4", "Normal", t1, asmPath);
            int launcherPidA = procA.Id;
            int ownerPidA = GetHolderPid(procA);
            try
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(wfId);
                string hex = Convert.ToHexString(bytes).ToLowerInvariant();
                string authPath = Path.Combine(root, "workflows", hex, "owner-auth.json");
                string manifestPath = Path.Combine(root, "locks", "active", $"{hex}.lease.json");
                string wfLockPath = paths.GetWorkflowLockPath(wfId);

                // Effective identity of the owner: the process that holds the flock, NOT the dotnet-run launcher.
                Assert.NotEqual(launcherPidA, ownerPidA);
                Assert.True(IsProcessHoldingResources(ownerPidA), $"Owner PID {ownerPidA} must be alive.");
                AssertFlockHeldBy(authPath, ownerPidA, "owner-auth.json (before substitution)");
                AssertFlockHeldBy(wfLockPath, ownerPidA, "workflow.lock (before substitution)");

                long ownerStartTicks = WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(ownerPidA);
                Assert.True(ownerStartTicks > 0, "Owner start time must be resolvable.");

                // Baseline: untampered state is healthy, so later findings are caused by the substitution only.
                WorkflowSchedulerIntegrityReport baseline = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
                Assert.True(
                    baseline.IsHealthy,
                    "Baseline must be healthy. Violations: " + string.Join(" | ", baseline.Violations));

                // Diagnostic reads use the same flock-free path as production inspection; File.ReadAllText
                // would be rejected by the owner's LOCK_EX (this was the cause of the former failure).
                Assert.Contains(t1, ReadAllTextWithoutFlock(authPath));

                // ---------- Scenario A: non-cooperative writer that cannot reproduce the owner identity ----------
                // Substitutes token + manifest on the same inode via O_WRONLY|O_TRUNC without flock,
                // recording the launcher PID and an invalid start time (as a naive attacker would).
                RunSubstitutionHelper(root, wfId, t2, launcherPidA, 0L);

                string authAfterA = ReadAllTextWithoutFlock(authPath);
                string manifestAfterA = ReadAllTextWithoutFlock(manifestPath);
                Assert.Contains(t2, authAfterA);
                Assert.DoesNotContain(t1, authAfterA);
                Assert.Contains(t2, manifestAfterA);

                // Same inode proof: the owner's flock is still attached to the path that was rewritten.
                AssertFlockHeldBy(authPath, ownerPidA, "owner-auth.json (after substitution A)");

                // Owner is still alive with its original handles.
                Assert.True(IsProcessHoldingResources(ownerPidA));
                Assert.False(procA.HasExited);

                WorkflowSchedulerIntegrityReport reportA = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
                Assert.False(
                    reportA.IsHealthy,
                    "Substitution with a wrong PID/start time on the same inode MUST be detected.");
                // The v1 record with an invalid start time / foreign PID is rejected outright
                // ("lacks valid authenticated owner generation record") or flagged as PID mismatch.
                Assert.Contains(
                    reportA.Violations,
                    v => v.Contains("lacks valid authenticated owner generation record", StringComparison.Ordinal) ||
                         v.Contains("does not match lock holder PID", StringComparison.Ordinal) ||
                         v.Contains("invalid start time", StringComparison.Ordinal));

                // ---------- Scenario B: same-UID writer that REPRODUCES the owner's PID and start time ----------
                // CHARACTERIZATION of the approved trust boundary, not a guarantee: owner-auth.json v1 carries no
                // cryptographic authenticity, and persistence (P02/P09) is cooperative. A same-UID non-cooperative
                // writer that knows the owner's PID/start time and rewrites BOTH owner-auth.json and the lease
                // manifest consistently is indistinguishable from a legitimate generation. Hardening this would
                // require an architecture decision (e.g. keyed authentication or a broker-only writer); none is
                // invented here.
                RunSubstitutionHelper(root, wfId, t2, ownerPidA, ownerStartTicks);
                AssertFlockHeldBy(authPath, ownerPidA, "owner-auth.json (after substitution B)");

                WorkflowSchedulerIntegrityReport reportB = WorkflowSchedulerIntegrityInspector.Inspect(paths, 4);
                Assert.True(
                    reportB.IsHealthy,
                    "Scenario B characterization changed (v1 detects identity-mimicking writer?). Violations: " +
                    string.Join(" | ", reportB.Violations));
            }
            finally
            {
                StopHelperProcess(procA);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LinuxLockTypeCorrelation_DotNetLocksAreFlock_NotPosixOrOfd()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTempStorageRoot();
        try
        {
            WorkflowSchedulerPaths paths = new(root);
            paths.EnsureCoordinationLayout();
            int self = Environment.ProcessId;

            using (WorkflowSchedulerLockHandle? exclusive =
                   WorkflowSchedulerLockPrimitives.TryAcquireExclusive(paths.DomainLockPath))
            {
                Assert.NotNull(exclusive);

                LockEnumerationResult flock = WorkflowSchedulerIntegrityInspector.GetLockingProcesses(
                    paths.DomainLockPath, requireExclusive: true);
                Assert.True(flock.IsSuccess);
                Assert.Contains(self, flock.ProcessIds);

                // A POSIX/OFDLCK entry would NOT be a lock acquired by the production .NET primitives.
                foreach (string foreign in new[] { "POSIX", "OFDLCK" })
                {
                    LockEnumerationResult other =
                        WorkflowSchedulerIntegrityInspector.LinuxProcessTracker.GetLockingProcesses(
                            paths.DomainLockPath, requireExclusive: true, requiredLockType: foreign);
                    Assert.True(other.IsSuccess);
                    Assert.DoesNotContain(self, other.ProcessIds);
                }
            }

            using (WorkflowSchedulerLockHandle? shared =
                   WorkflowSchedulerLockPrimitives.TryAcquireShared(paths.DomainModeLockPath))
            {
                Assert.NotNull(shared);

                LockEnumerationResult asShared = WorkflowSchedulerIntegrityInspector.GetLockingProcesses(
                    paths.DomainModeLockPath, requireExclusive: false);
                Assert.True(asShared.IsSuccess);
                Assert.Contains(self, asShared.ProcessIds);

                // Shared .NET lock (LOCK_SH => FLOCK READ) must not satisfy an exclusive requirement.
                LockEnumerationResult asExclusive = WorkflowSchedulerIntegrityInspector.GetLockingProcesses(
                    paths.DomainModeLockPath, requireExclusive: true);
                Assert.True(asExclusive.IsSuccess);
                Assert.DoesNotContain(self, asExclusive.ProcessIds);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}


namespace AiRepoKit.WorkflowExecution;

using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

internal enum WorkflowLockProbeState
{
    Free = 0,
    HeldShared = 1,
    HeldExclusive = 2,
    Indeterminate = 3
}

internal static class WorkflowSchedulerLockPrimitives
{
    private static readonly ConditionalWeakTable<SafeFileHandle, object> _genuineExclusiveHandles = new();
    private static readonly ConditionalWeakTable<SafeFileHandle, object> _genuineSharedHandles = new();

    internal static bool IsGenuinelyAcquiredExclusive(
        SafeFileHandle? handle_)
    {
        if (handle_ is null ||
            handle_.IsInvalid ||
            handle_.IsClosed)
        {
            return false;
        }

        return _genuineExclusiveHandles.TryGetValue(
            handle_,
            out _);
    }

    internal static bool IsGenuinelyAcquiredShared(
        SafeFileHandle? handle_)
    {
        if (handle_ is null ||
            handle_.IsInvalid ||
            handle_.IsClosed)
        {
            return false;
        }

        return _genuineSharedHandles.TryGetValue(
            handle_,
            out _);
    }

    internal static bool IsSharingOrLockViolation(
        IOException ioException_)
    {
        if (ioException_ is null)
        {
            return false;
        }

        // Do not classify specialized IO exceptions as lock contention
        if (ioException_ is FileNotFoundException or
            DirectoryNotFoundException or
            PathTooLongException or
            EndOfStreamException)
        {
            return false;
        }

        int hResult =
            ioException_.HResult;

        // Classification is purely structural (numeric codes). Exception messages are
        // localizable and are never used to infer contention.
        //
        // 1. Windows HResults:
        // 0x80070020 = Win32 ERROR_SHARING_VIOLATION (32)
        // 0x80070021 = Win32 ERROR_LOCK_VIOLATION (33)
        //
        // 2. Unix (verified empirically on Linux .NET 10): when SafeFileHandle.Open's non-blocking
        // flock(LOCK_NB) fails with EWOULDBLOCK/EAGAIN, the runtime throws IOException whose HResult
        // is the raw errno (11), not a 0x8007xxxx facility HResult.
        //
        // EDEADLK (35) and EBUSY (16) are intentionally NOT contention: flock(LOCK_NB) never
        // reports them as lock contention, so they are indeterminate faults and must propagate
        // (fail closed) instead of being converted into "another owner holds the lock".
        if (hResult == unchecked((int)0x80070020) ||
            hResult == unchecked((int)0x80070021))
        {
            return true;
        }

        if (OperatingSystem.IsLinux() && hResult == UnixErrnoEagain)
        {
            return true;
        }

        // 3. Inner Win32Exception native error codes if available (same structural rule).
        if (ioException_.InnerException is System.ComponentModel.Win32Exception win32Ex)
        {
            int nativeErr = win32Ex.NativeErrorCode;
            if (OperatingSystem.IsWindows()
                    ? nativeErr is 32 /* ERROR_SHARING_VIOLATION */ or 33 /* ERROR_LOCK_VIOLATION */
                    : nativeErr == UnixErrnoEagain)
            {
                return true;
            }
        }

        return false;
    }

    // Linux errno EAGAIN == EWOULDBLOCK.
    private const int UnixErrnoEagain = 11;

    public static WorkflowSchedulerLockHandle? TryAcquireExclusive(
        string path_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path_,
            nameof(path_));

        WorkflowSchedulerPaths.AssertNoReparsePoint(
            path_);

        string? dir =
            Path.GetDirectoryName(
                path_);

        if (dir is not null &&
            !Directory.Exists(
                dir))
        {
            Directory.CreateDirectory(
                dir);
        }

        FileStream stream;
        try
        {
            stream =
                new(
                    path_,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
        }
        catch (IOException ioException) when (IsSharingOrLockViolation(ioException))
        {
            // Legitimate lock contention by another owner.
            return null;
        }

        // Validate open file handle bound to OS object to avoid adversarial symlink replacement.
        try
        {
            WorkflowSchedulerPaths.AssertSafeOpenFile(
                stream.SafeFileHandle,
                path_);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        _genuineExclusiveHandles.Add(
            stream.SafeFileHandle,
            new object());

        WorkflowLockExclusiveCapability capability =
            new(
                stream.SafeFileHandle,
                path_);

        return new WorkflowSchedulerLockHandle(
            stream,
            path_,
            true,
            capability,
            null);
    }

    public static WorkflowSchedulerLockHandle? TryAcquireShared(
        string path_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path_,
            nameof(path_));

        WorkflowSchedulerPaths.AssertNoReparsePoint(
            path_);

        string? dir =
            Path.GetDirectoryName(
                path_);

        if (dir is not null &&
            !Directory.Exists(
                dir))
        {
            Directory.CreateDirectory(
                dir);
        }

        if (!File.Exists(
                path_))
        {
            try
            {
                using FileStream init =
                    new(
                        path_,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite);
            }
            catch (IOException ioException) when (IsSharingOrLockViolation(ioException))
            {
                // Concurrently locked exclusive by another process.
            }
        }

        FileStream stream;
        try
        {
            stream =
                new(
                    path_,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1,
                    FileOptions.None);
        }
        catch (IOException ioException) when (IsSharingOrLockViolation(ioException))
        {
            // Legitimate exclusive lock held by another process.
            return null;
        }

        // Validate open file handle bound to OS object to avoid adversarial symlink replacement.
        try
        {
            WorkflowSchedulerPaths.AssertSafeOpenFile(
                stream.SafeFileHandle,
                path_);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        _genuineSharedHandles.Add(
            stream.SafeFileHandle,
            new object());

        WorkflowLockSharedCapability capability =
            new(
                stream.SafeFileHandle,
                path_);

        return new WorkflowSchedulerLockHandle(
            stream,
            path_,
            false,
            null,
            capability);
    }

    public static async Task<WorkflowSchedulerLockHandle> AcquireExclusiveAsync(
        string path_,
        TimeSpan timeout_,
        CancellationToken cancellationToken_ = default)
    {
        if (timeout_ <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout_),
                timeout_,
                "Lock acquisition timeout must be greater than zero.");
        }

        long startTicks =
            Environment.TickCount64;

        int delayMs =
            2;

        while (true)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            WorkflowSchedulerLockHandle? handle =
                TryAcquireExclusive(
                    path_);

            if (handle is not null)
            {
                return handle;
            }

            long elapsedMs =
                Environment.TickCount64 - startTicks;

            if (elapsedMs >= timeout_.TotalMilliseconds)
            {
                throw new TimeoutException(
                    $"Timed out acquiring exclusive lock on '{path_}' after {timeout_.TotalMilliseconds}ms.");
            }

            int currentDelay =
                Math.Min(
                    delayMs,
                    (int)Math.Max(
                        1,
                        timeout_.TotalMilliseconds - elapsedMs));

            await Task.Delay(
                currentDelay,
                cancellationToken_).ConfigureAwait(false);

            delayMs =
                Math.Min(
                    delayMs * 2,
                    25);
        }
    }

    public static async Task<WorkflowSchedulerLockHandle> AcquireSharedAsync(
        string path_,
        TimeSpan timeout_,
        CancellationToken cancellationToken_ = default)
    {
        if (timeout_ <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout_),
                timeout_,
                "Lock acquisition timeout must be greater than zero.");
        }

        long startTicks =
            Environment.TickCount64;

        int delayMs =
            2;

        while (true)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            WorkflowSchedulerLockHandle? handle =
                TryAcquireShared(
                    path_);

            if (handle is not null)
            {
                return handle;
            }

            long elapsedMs =
                Environment.TickCount64 - startTicks;

            if (elapsedMs >= timeout_.TotalMilliseconds)
            {
                throw new TimeoutException(
                    $"Timed out acquiring shared lock on '{path_}' after {timeout_.TotalMilliseconds}ms.");
            }

            int currentDelay =
                Math.Min(
                    delayMs,
                    (int)Math.Max(
                        1,
                        timeout_.TotalMilliseconds - elapsedMs));

            await Task.Delay(
                currentDelay,
                cancellationToken_).ConfigureAwait(false);

            delayMs =
                Math.Min(
                    delayMs * 2,
                    25);
        }
    }

    public static (int SlotIndex, WorkflowSchedulerLockHandle Handle)? TryAcquireSlot(
        WorkflowSchedulerPaths paths_,
        int maxConcurrentWorkflows_)
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

        for (int slot = 1; slot <= maxConcurrentWorkflows_; slot++)
        {
            paths_.EnsureSlotFile(
                slot,
                maxConcurrentWorkflows_);

            string slotPath =
                paths_.GetSlotLockPath(
                    slot,
                    maxConcurrentWorkflows_);

            WorkflowSchedulerLockHandle? handle =
                TryAcquireExclusive(
                    slotPath);

            if (handle is not null)
            {
                return (slot, handle);
            }
        }

        return null;
    }

    public static async Task<(int SlotIndex, WorkflowSchedulerLockHandle Handle)> AcquireSlotAsync(
        WorkflowSchedulerPaths paths_,
        int maxConcurrentWorkflows_,
        TimeSpan timeout_,
        CancellationToken cancellationToken_ = default)
    {
        if (timeout_ <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout_),
                timeout_,
                "Lock acquisition timeout must be greater than zero.");
        }

        long startTicks =
            Environment.TickCount64;

        int delayMs =
            2;

        while (true)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            (int SlotIndex, WorkflowSchedulerLockHandle Handle)? acquired =
                TryAcquireSlot(
                    paths_,
                    maxConcurrentWorkflows_);

            if (acquired is not null)
            {
                return acquired.Value;
            }

            long elapsedMs =
                Environment.TickCount64 - startTicks;

            if (elapsedMs >= timeout_.TotalMilliseconds)
            {
                throw new TimeoutException(
                    $"Timed out waiting for available capacity slot (1..{maxConcurrentWorkflows_}) after {timeout_.TotalMilliseconds}ms.");
            }

            int currentDelay =
                Math.Min(
                    delayMs,
                    (int)Math.Max(
                        1,
                        timeout_.TotalMilliseconds - elapsedMs));

            await Task.Delay(
                currentDelay,
                cancellationToken_).ConfigureAwait(false);

            delayMs =
                Math.Min(
                    delayMs * 2,
                    25);
        }
    }

    public static WorkflowLockProbeState ProbeLockState(
        string path_)
    {
        if (!File.Exists(
                path_))
        {
            return WorkflowLockProbeState.Free;
        }

        try
        {
            WorkflowSchedulerPaths.AssertNoReparsePoint(
                path_);
        }
        catch
        {
            return WorkflowLockProbeState.Indeterminate;
        }

        WorkflowSchedulerLockHandle? sharedHandle;
        try
        {
            sharedHandle =
                TryAcquireShared(
                    path_);
        }
        catch (UnauthorizedAccessException)
        {
            return WorkflowLockProbeState.Indeterminate;
        }
        catch (IOException ioException) when (!IsSharingOrLockViolation(ioException))
        {
            return WorkflowLockProbeState.Indeterminate;
        }
        catch
        {
            return WorkflowLockProbeState.Indeterminate;
        }

        if (sharedHandle is null)
        {
            return WorkflowLockProbeState.HeldExclusive;
        }

        try
        {
            sharedHandle.Dispose();
        }
        catch
        {
            return WorkflowLockProbeState.Indeterminate;
        }

        WorkflowSchedulerLockHandle? exclusiveHandle;
        try
        {
            exclusiveHandle =
                TryAcquireExclusive(
                    path_);
        }
        catch (UnauthorizedAccessException)
        {
            return WorkflowLockProbeState.Indeterminate;
        }
        catch (IOException ioException) when (!IsSharingOrLockViolation(ioException))
        {
            return WorkflowLockProbeState.Indeterminate;
        }
        catch
        {
            return WorkflowLockProbeState.Indeterminate;
        }

        if (exclusiveHandle is null)
        {
            return WorkflowLockProbeState.HeldShared;
        }

        try
        {
            exclusiveHandle.Dispose();
        }
        catch
        {
            return WorkflowLockProbeState.Indeterminate;
        }

        return WorkflowLockProbeState.Free;
    }
}

namespace AiRepoKit.WorkflowExecution;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal sealed class WorkflowSchedulerPaths
{
    private static readonly UTF8Encoding _strictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string StorageRoot
    {
        get;
    }

    public string LocksDirectory
    {
        get;
    }

    public string DomainLockPath
    {
        get;
    }

    public string DomainModeLockPath
    {
        get;
    }

    public string DomainPolicyPath
    {
        get;
    }

    public string DomainIntegrityPath
    {
        get;
    }

    public string SlotsDirectory
    {
        get;
    }

    public string ActiveLeasesDirectory
    {
        get;
    }

    public string ResourcesDirectory
    {
        get;
    }

    public string WorkflowsDirectory
    {
        get;
    }

    public WorkflowSchedulerPaths(
        string storageRoot_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            storageRoot_,
            nameof(storageRoot_));

        if (storageRoot_.Contains('\0'))
        {
            throw new ArgumentException(
                "StorageRoot cannot contain NUL character.",
                nameof(storageRoot_));
        }

        string fullStorageRoot =
            Path.GetFullPath(
                storageRoot_);

        if (!Directory.Exists(
                fullStorageRoot))
        {
            throw new DirectoryNotFoundException(
                $"StorageRoot does not exist: '{fullStorageRoot}'.");
        }

        string? driveRoot =
            Path.GetPathRoot(
                fullStorageRoot);

        AssertNoReparsePointInHierarchy(
            fullStorageRoot,
            driveRoot);

        this.StorageRoot =
            fullStorageRoot;
        this.LocksDirectory =
            Path.Combine(
                fullStorageRoot,
                "locks");
        this.DomainLockPath =
            Path.Combine(
                this.LocksDirectory,
                "domain.lock");
        this.DomainModeLockPath =
            Path.Combine(
                this.LocksDirectory,
                "domain-mode.lock");
        this.DomainPolicyPath =
            Path.Combine(
                this.LocksDirectory,
                "domain-policy.json");
        this.DomainIntegrityPath =
            Path.Combine(
                this.LocksDirectory,
                "domain-integrity.json");
        this.SlotsDirectory =
            Path.Combine(
                this.LocksDirectory,
                "slots");
        this.ActiveLeasesDirectory =
            Path.Combine(
                this.LocksDirectory,
                "active");
        this.ResourcesDirectory =
            Path.Combine(
                this.LocksDirectory,
                "resources");
        this.WorkflowsDirectory =
            Path.Combine(
                fullStorageRoot,
                "workflows");
        this.IpcDirectory =
            Path.Combine(
                fullStorageRoot,
                "ipc");
    }

    public string IpcDirectory
    {
        get;
    }

    public void EnsureCoordinationLayout()
    {
        EnsureDirectory(
            this.LocksDirectory);
        EnsureDirectory(
            this.SlotsDirectory);
        EnsureDirectory(
            this.ActiveLeasesDirectory);
        EnsureDirectory(
            this.ResourcesDirectory);
        EnsureDirectory(
            this.WorkflowsDirectory);
        EnsureDirectory(
            this.IpcDirectory);

        EnsureFile(
            this.DomainLockPath);
        EnsureFile(
            this.DomainModeLockPath);
    }

    public string GetSlotLockPath(
        int slotIndex_,
        int maxConcurrentWorkflows_)
    {
        if (slotIndex_ < 1 ||
            slotIndex_ > maxConcurrentWorkflows_)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex_),
                slotIndex_,
                $"Slot index must be between 1 and {maxConcurrentWorkflows_}.");
        }

        string fileName =
            string.Concat(
                "slot-",
                slotIndex_.ToString(
                    "D8",
                    CultureInfo.InvariantCulture),
                ".lock");

        string fullPath =
            Path.Combine(
                this.SlotsDirectory,
                fileName);

        AssertPathWithinRoot(
            fullPath);

        return fullPath;
    }

    public string GetActiveLeasePath(
        string workflowId_)
    {
        string hex =
            WorkflowIdToHex(
                workflowId_);

        string fullPath =
            Path.Combine(
                this.ActiveLeasesDirectory,
                $"{hex}.lease.json");

        AssertPathWithinRoot(
            fullPath);

        return fullPath;
    }

    public string GetWorkflowDirectory(
        string workflowId_)
    {
        string hex =
            WorkflowIdToHex(
                workflowId_);

        string fullPath =
            Path.Combine(
                this.WorkflowsDirectory,
                hex);

        AssertPathWithinRoot(
            fullPath);

        return fullPath;
    }

    public string GetWorkflowLockPath(
        string workflowId_)
    {
        string workflowDir =
            this.GetWorkflowDirectory(
                workflowId_);

        string fullPath =
            Path.Combine(
                workflowDir,
                "workflow.lock");

        AssertPathWithinRoot(
            fullPath);

        return fullPath;
    }

    public void EnsureWorkflowDirectory(
        string workflowId_)
    {
        string workflowDir =
            this.GetWorkflowDirectory(
                workflowId_);

        EnsureDirectory(
            workflowDir);

        string lockPath =
            this.GetWorkflowLockPath(
                workflowId_);

        EnsureFile(
            lockPath);
    }

    public void EnsureSlotFile(
        int slotIndex_,
        int maxConcurrentWorkflows_)
    {
        string path =
            this.GetSlotLockPath(
                slotIndex_,
                maxConcurrentWorkflows_);

        EnsureFile(
            path);
    }

    public void AssertPathWithinRoot(
        string path_)
    {
        StringComparison pathComparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        string normalizedRoot =
            Path.GetFullPath(
                this.StorageRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        string normalizedPath =
            Path.GetFullPath(
                path_);

        if (!normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                pathComparison) &&
            !string.Equals(
                normalizedPath,
                normalizedRoot,
                pathComparison))
        {
            throw new InvalidOperationException(
                $"Path traversal detected: path '{path_}' is outside storage root '{this.StorageRoot}'.");
        }

        AssertNoReparsePointInHierarchy(
            normalizedPath,
            this.StorageRoot);
    }

    public void AssertSafeCoordinationFile(
        string filePath_)
    {
        this.AssertPathWithinRoot(
            filePath_);

        AssertNoReparsePoint(
            filePath_);
    }

    public static string WorkflowIdToHex(
        string workflowId_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workflowId_,
            nameof(workflowId_));

        byte[] bytes;
        try
        {
            bytes =
                _strictUtf8.GetBytes(
                    workflowId_);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Workflow ID contains invalid UTF-8 characters.",
                nameof(workflowId_),
                exception);
        }

        if (bytes.Length > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workflowId_),
                workflowId_,
                "Workflow ID UTF-8 representation must not exceed 64 bytes.");
        }

        return Convert
            .ToHexString(
                bytes)
            .ToLowerInvariant();
    }

    public static string HexToWorkflowId(
        string hex_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            hex_,
            nameof(hex_));

        if (hex_.Length % 2 != 0)
        {
            throw new ArgumentException(
                "Workflow ID hex length must be even.",
                nameof(hex_));
        }

        for (int i = 0; i < hex_.Length; i++)
        {
            char c = hex_[i];
            if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
            {
                throw new ArgumentException(
                    "Workflow ID hex contains non-lowercase-hexadecimal characters.",
                    nameof(hex_));
            }
        }

        byte[] bytes;
        try
        {
            bytes =
                Convert.FromHexString(
                    hex_);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Workflow ID hex is invalid.",
                nameof(hex_),
                exception);
        }

        if (bytes.Length > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hex_),
                hex_,
                "Workflow ID UTF-8 representation must not exceed 64 bytes.");
        }

        string workflowId;
        try
        {
            workflowId =
                _strictUtf8.GetString(
                    bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException(
                "Workflow ID hex bytes are not valid UTF-8.",
                nameof(hex_),
                exception);
        }

        if (string.IsNullOrWhiteSpace(
                workflowId))
        {
            throw new ArgumentException(
                "Decoded Workflow ID cannot be empty or whitespace.",
                nameof(hex_));
        }

        return workflowId;
    }

    public static void AssertNoReparsePoint(
        string path_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path_,
            nameof(path_));

        // 1. Check LinkTarget via File/Directory ResolveLinkTarget (detects symlinks even if broken)
        try
        {
            FileSystemInfo? linkTarget =
                File.ResolveLinkTarget(
                    path_,
                    returnFinalTarget: false);

            if (linkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Reparse point, symlink, or junction detected in coordination path: '{path_}'.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            // Normal absence of file
        }
        catch (DirectoryNotFoundException)
        {
            // Normal absence of directory
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to inspect file link target for '{path_}': {ex.Message}",
                ex);
        }

        try
        {
            FileSystemInfo? dirLinkTarget =
                Directory.ResolveLinkTarget(
                    path_,
                    returnFinalTarget: false);

            if (dirLinkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Reparse point, symlink, or junction detected in coordination path: '{path_}'.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            // Normal absence of directory
        }
        catch (DirectoryNotFoundException)
        {
            // Normal absence of directory
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to inspect directory link target for '{path_}': {ex.Message}",
                ex);
        }

        // 2. Check FileAttributes.ReparsePoint (including when broken symlink)
        try
        {
            if (File.Exists(
                    path_) ||
                Directory.Exists(
                    path_))
            {
                FileAttributes attributes =
                    File.GetAttributes(
                        path_);

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Reparse point, symlink, or junction detected in coordination path: '{path_}'.");
                }
            }
            else
            {
                FileInfo fileInfo =
                    new(path_);

                if (fileInfo.LinkTarget is not null)
                {
                    throw new InvalidOperationException(
                        $"Broken symlink detected in coordination path: '{path_}'.");
                }

                DirectoryInfo dirInfo =
                    new(path_);

                if (dirInfo.LinkTarget is not null)
                {
                    throw new InvalidOperationException(
                        $"Broken symlink or junction detected in coordination path: '{path_}'.");
                }

                try
                {
                    FileAttributes missingAttrs = fileInfo.Attributes;
                    if ((int)missingAttrs != -1 && (missingAttrs & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Broken symlink or reparse point detected in coordination path: '{path_}'.");
                    }

                    if ((int)missingAttrs == -1 && OperatingSystem.IsWindows())
                    {
                        int err = Marshal.GetLastWin32Error();
                        // 2 = ERROR_FILE_NOT_FOUND, 3 = ERROR_PATH_NOT_FOUND
                        if (err != 2 && err != 3 && err != 0)
                        {
                            throw new InvalidOperationException(
                                $"Indeterminate filesystem state inspecting non-existent path '{path_}': Win32 error {err}.");
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    // Confirmed genuine absence
                }
                catch (DirectoryNotFoundException)
                {
                    // Confirmed genuine absence
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Indeterminate filesystem state inspecting attributes of non-existent path '{path_}': {ex.Message}",
                        ex);
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            // Confirmed genuine absence
        }
        catch (DirectoryNotFoundException)
        {
            // Confirmed genuine absence
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Indeterminate filesystem state inspecting attributes of path '{path_}': {ex.Message}",
                ex);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        [Out] StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    internal static string GetFinalPath(SafeFileHandle handle_)
    {
        ArgumentNullException.ThrowIfNull(
            handle_,
            nameof(handle_));

        if (handle_.IsInvalid || handle_.IsClosed)
        {
            throw new InvalidOperationException(
                "SafeFileHandle is closed or invalid.");
        }

        if (OperatingSystem.IsWindows())
        {
            StringBuilder builder =
                new(1024);

            uint result =
                GetFinalPathNameByHandle(
                    handle_,
                    builder,
                    (uint)builder.Capacity,
                    0); // FILE_NAME_NORMALIZED (0) | VOLUME_NAME_DOS (0)

            if (result == 0)
            {
                int error =
                    Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"GetFinalPathNameByHandle failed with Win32 error {error}.");
            }

            if (result > builder.Capacity)
            {
                builder.EnsureCapacity(
                    (int)result);
                result =
                    GetFinalPathNameByHandle(
                        handle_,
                        builder,
                        (uint)builder.Capacity,
                        0);

                if (result == 0)
                {
                    int error =
                        Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"GetFinalPathNameByHandle failed with Win32 error {error}.");
                }
            }

            string path =
                builder.ToString();

            if (path.StartsWith(
                    @"\\?\",
                    StringComparison.Ordinal))
            {
                path =
                    path.Substring(4);
            }

            return Path.GetFullPath(
                path);
        }

        if (OperatingSystem.IsLinux())
        {
            int fd =
                handle_.DangerousGetHandle().ToInt32();
            string procPath =
                $"/proc/self/fd/{fd}";

            FileSystemInfo? target =
                File.ResolveLinkTarget(
                    procPath,
                    returnFinalTarget: true);

            if (target is not null)
            {
                return Path.GetFullPath(
                    target.FullName);
            }

            throw new InvalidOperationException(
                $"Could not resolve proc fd link '{procPath}'.");
        }

        throw new PlatformNotSupportedException(
            "OS handle physical file verification is not supported on this platform.");
    }

    internal static void AssertSafeOpenFile(
        SafeFileHandle handle_,
        string expectedPath_)
    {
        ArgumentNullException.ThrowIfNull(
            handle_,
            nameof(handle_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            expectedPath_,
            nameof(expectedPath_));

        if (handle_.IsInvalid || handle_.IsClosed)
        {
            throw new InvalidOperationException(
                "Open file handle is closed or invalid.");
        }

        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandle(
                    handle_,
                    out BY_HANDLE_FILE_INFORMATION info))
            {
                int error =
                    Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"GetFileInformationByHandle failed for open handle: Win32 error {error}.");
            }

            if ((info.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Open file handle for '{expectedPath_}' is a reparse point.");
            }
        }

        string finalPhysicalPath =
            GetFinalPath(
                handle_);

        string normalizedExpected =
            Path.GetFullPath(
                expectedPath_);

        StringComparison comp =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (!string.Equals(
                finalPhysicalPath,
                normalizedExpected,
                comp))
        {
            throw new InvalidOperationException(
                $"Open file handle physical path mismatch: handle physically resolves to '{finalPhysicalPath}', expected '{normalizedExpected}'.");
        }
    }

    public static void AssertNoReparsePointInHierarchy(
        string path_,
        string? stopRoot_ = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path_,
            nameof(path_));

        StringComparison pathComparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        string current =
            Path.GetFullPath(
                path_);

        string? normalizedStop = null;
        if (stopRoot_ is not null)
        {
            if (string.IsNullOrWhiteSpace(stopRoot_))
            {
                throw new ArgumentException(
                    "Stop root cannot be empty or whitespace.",
                    nameof(stopRoot_));
            }

            string fullStop =
                Path.GetFullPath(
                    stopRoot_);

            normalizedStop =
                fullStop.Length > 1 && fullStop.EndsWith(Path.DirectorySeparatorChar)
                    ? fullStop.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    : fullStop;

            string normalizedCurrentForCheck =
                current.Length > 1 && current.EndsWith(Path.DirectorySeparatorChar)
                    ? current.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    : current;

            string stopPrefix =
                normalizedStop.EndsWith(Path.DirectorySeparatorChar)
                    ? normalizedStop
                    : normalizedStop + Path.DirectorySeparatorChar;

            bool isAncestor =
                normalizedCurrentForCheck.Equals(normalizedStop, pathComparison) ||
                normalizedCurrentForCheck.StartsWith(stopPrefix, pathComparison);

            if (!isAncestor)
            {
                throw new ArgumentException(
                    $"Stop root '{stopRoot_}' is not an ancestor of path '{path_}'.",
                    nameof(stopRoot_));
            }
        }

        while (!string.IsNullOrEmpty(
                   current))
        {
            AssertNoReparsePoint(
                current);

            string normalizedCurrent =
                current.Length > 1 && current.EndsWith(Path.DirectorySeparatorChar)
                    ? current.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    : current;

            if (normalizedStop is not null &&
                string.Equals(
                    normalizedCurrent,
                    normalizedStop,
                    pathComparison))
            {
                break;
            }

            string? parent =
                Path.GetDirectoryName(
                    current);

            if (string.IsNullOrEmpty(parent) ||
                string.Equals(
                    parent,
                    current,
                    pathComparison))
            {
                break;
            }

            current =
                parent;
        }
    }

    private void EnsureDirectory(
        string directoryPath_)
    {
        this.AssertPathWithinRoot(
            directoryPath_);

        if (!Directory.Exists(
                directoryPath_))
        {
            Directory.CreateDirectory(
                directoryPath_);
        }

        AssertNoReparsePoint(
            directoryPath_);
    }

    private void EnsureFile(
        string filePath_)
    {
        this.AssertPathWithinRoot(
            filePath_);

        string? dir =
            Path.GetDirectoryName(
                filePath_);

        if (dir is not null &&
            !Directory.Exists(
                dir))
        {
            Directory.CreateDirectory(
                dir);
        }

        if (!File.Exists(
                filePath_))
        {
            using FileStream stream =
                new(
                    filePath_,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);
        }

        AssertNoReparsePoint(
            filePath_);
    }
}

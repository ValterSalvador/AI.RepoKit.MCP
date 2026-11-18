namespace AiRepoKit.WorkflowExecution;

using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class WorkflowBrokerIpcTransport
{
    private const int SOL_SOCKET = 1;
    private const int SO_PEERCRED = 17;

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LinuxPeerCred
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
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

    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint READ_CONTROL = 0x00020000;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint FILE_SHARE_DELETE = 4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    internal struct WindowsFileIdentity
    {
        public uint VolumeSerialNumber;
        public ulong FileIndex;
        public uint FileAttributes;
    }

    internal delegate void WindowsIdentityMutator(string path, ref WindowsFileIdentity identity);
    internal static WindowsIdentityMutator? WindowsStorageRootIdentityMutator { get; set; }
    internal static Func<string, bool>? WindowsStorageRootInspectionFaultInjector { get; set; }

    [DllImport("libc", SetLastError = true)]
    private static extern int getsockopt(int sockfd, int level, int optname, out LinuxPeerCred optval, ref int optlen);

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();

    private const int AT_FDCWD = -100;
    private const int AT_SYMLINK_NOFOLLOW = 0x100;
    private const int AT_EMPTY_PATH = 0x1000;
    private const uint STATX_BASIC_STATS = 0x07ff;
    private const uint STATX_TYPE = 0x0001;
    private const uint STATX_MODE = 0x0002;
    private const uint STATX_UID = 0x0008;
    private const uint STATX_INO = 0x0100;
    internal const uint STATX_REQUIRED_MASK = STATX_TYPE | STATX_MODE | STATX_UID | STATX_INO;
    internal const ushort S_IFMT = 0xF000;
    internal const ushort S_IFSOCK = 0xC000;
    internal const ushort S_IFDIR = 0x4000;
    internal const ushort S_IFLNK = 0xA000;
    internal const ushort S_IRWXU = 0x01C0;
    internal const ushort S_IWOTH = 0x0002;
    internal const ushort S_IWGRP = 0x0010;
    internal const ushort S_ISVTX = 0x0200; // sticky bit (01000 octal)

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct LinuxStatx
    {
        [FieldOffset(0)]
        public uint stx_mask;
        [FieldOffset(16)]
        public uint stx_nlink;
        [FieldOffset(20)]
        public uint stx_uid;
        [FieldOffset(24)]
        public uint stx_gid;
        [FieldOffset(28)]
        public ushort stx_mode;
        [FieldOffset(32)]
        public ulong stx_ino;
        [FieldOffset(40)]
        public ulong stx_size;
        [FieldOffset(136)]
        public uint stx_dev_major;
        [FieldOffset(140)]
        public uint stx_dev_minor;

        public ulong DeviceId => ((ulong)this.stx_dev_major << 32) | this.stx_dev_minor;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int dirfd, string pathname, int flags, uint mask, out LinuxStatx statxbuf);

    internal delegate void LinuxStatxMutator(string path, ref LinuxStatx st);

    /// <summary>
    /// Test-only delegate to mutate statx results for testing failure modes (incomplete mask, inode 0, device mismatch).
    /// </summary>
    internal static LinuxStatxMutator? LinuxStorageRootStatxMutator { get; set; }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static bool TryLinuxStatx(string path, bool followSymlink, out LinuxStatx st)
    {
        st = default;
        try
        {
            int flags = followSymlink ? 0 : AT_SYMLINK_NOFOLLOW;
            int res = statx(AT_FDCWD, path, flags, STATX_BASIC_STATS, out st);
            if (res == 0 && LinuxStorageRootStatxMutator is not null)
            {
                LinuxStorageRootStatxMutator(path, ref st);
            }
            return res == 0;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    internal static bool TryLinuxStatxFd(Microsoft.Win32.SafeHandles.SafeFileHandle handle_, out LinuxStatx st_)
    {
        st_ = default;
        if (handle_ is null || handle_.IsInvalid || handle_.IsClosed)
        {
            return false;
        }

        try
        {
            int fd = handle_.DangerousGetHandle().ToInt32();
            int res = statx(fd, string.Empty, AT_EMPTY_PATH, STATX_BASIC_STATS, out st_);
            if (res == 0 && LinuxStorageRootStatxMutator is not null)
            {
                LinuxStorageRootStatxMutator("<fd>", ref st_);
            }
            return res == 0;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool TryTranslateToSidValue(string identityStr, out string sidValue)
    {
        sidValue = string.Empty;
        try
        {
            NTAccount account = new(identityStr);
            SecurityIdentifier sid = (SecurityIdentifier) account.Translate(typeof(SecurityIdentifier));
            sidValue = sid.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetEndpointIdentifier(WorkflowSchedulerPaths paths_)
    {
        ArgumentNullException.ThrowIfNull(paths_, nameof(paths_));
        if (OperatingSystem.IsWindows())
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(paths_.StorageRoot.ToLowerInvariant()));
            return "AiRepoKit_Broker_" + Convert.ToHexString(hash);
        }
        else
        {
            return Path.Combine(paths_.IpcDirectory, "broker.sock");
        }
    }

    public static string? DefaultBrokerServicePrincipal { get; set; } =
        Environment.GetEnvironmentVariable("AIREPO_BROKER_SERVICE_PRINCIPAL");

    public static NamedPipeServerStream CreateWindowsPipeServerStream(
        string pipeName_,
        string? authorizedClientIdentity_) =>
        CreateWindowsPipeServerStream(
            pipeName_,
            authorizedClientIdentity_ is not null ? [authorizedClientIdentity_] : null);

    public static NamedPipeServerStream CreateWindowsPipeServerStream(
        string pipeName_,
        IEnumerable<string>? authorizedClientIdentities_ = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName_, nameof(pipeName_));

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Named pipe server streams are only supported on Windows.");
        }

        PipeSecurity pipeSecurity = new();
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows user identity is unavailable.");

        // Broker service principal has server-only creation rights (FullControl / CreateNewInstance)
        pipeSecurity.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));

        // If authorized worker principals are configured and distinct from current user:
        if (authorizedClientIdentities_ is not null)
        {
            foreach (string identityStr in authorizedClientIdentities_)
            {
                if (string.IsNullOrWhiteSpace(identityStr))
                {
                    continue;
                }

                try
                {
                    SecurityIdentifier clientSid;
                    if (identityStr.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
                    {
                        clientSid = new(identityStr);
                    }
                    else
                    {
                        NTAccount account = new(identityStr);
                        clientSid = (SecurityIdentifier) account.Translate(typeof(SecurityIdentifier));
                    }

                    if (!clientSid.Equals(currentUser))
                    {
                        // Grant authorized workers ONLY client access (ReadWrite) - NO server instance creation rights
                        pipeSecurity.AddAccessRule(new PipeAccessRule(clientSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    }
                }
                catch
                {
                }
            }
        }

        // Deny Network and World / Anonymous
        SecurityIdentifier networkSid = new(WellKnownSidType.NetworkSid, null);
        SecurityIdentifier anonymousSid = new(WellKnownSidType.AnonymousSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(networkSid, PipeAccessRights.FullControl, AccessControlType.Deny));
        pipeSecurity.AddAccessRule(new PipeAccessRule(anonymousSid, PipeAccessRights.FullControl, AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            pipeName_,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }

    public static bool TryGetWindowsPipeClientProcessId(SafePipeHandle handle_, out int processId_)
    {
        if (GetNamedPipeClientProcessId(handle_, out uint pid))
        {
            processId_ = (int) pid;
            return true;
        }

        processId_ = 0;
        return false;
    }

    public static bool TryGetWindowsPipeServerProcessId(SafePipeHandle handle_, out int processId_)
    {
        if (GetNamedPipeServerProcessId(handle_, out uint pid))
        {
            processId_ = (int) pid;
            return true;
        }

        processId_ = 0;
        return false;
    }

    public static bool TryGetProcessTokenSids(
        int processId_,
        out string userSid_,
        out List<string> serviceSids_,
        out List<string> groupSids_)
    {
        userSid_ = string.Empty;
        serviceSids_ = [];
        groupSids_ = [];
        if (!OperatingSystem.IsWindows() || processId_ <= 0)
        {
            return false;
        }

        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId_);
        if (hProcess == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out IntPtr hToken))
            {
                return false;
            }

            try
            {
                using WindowsIdentity identity = new(hToken);
                userSid_ = identity.User?.Value ?? string.Empty;
                if (identity.Groups is not null)
                {
                    foreach (IdentityReference group in identity.Groups)
                    {
                        if (group is SecurityIdentifier sid)
                        {
                            string sidVal = sid.Value;
                            if (sidVal.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(sidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(sidVal, "S-1-5-19", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(sidVal, "S-1-5-20", StringComparison.OrdinalIgnoreCase))
                            {
                                serviceSids_.Add(sidVal);
                            }
                            else
                            {
                                groupSids_.Add(sidVal);
                            }
                        }
                    }
                }
                return !string.IsNullOrEmpty(userSid_);
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    public static bool TryGetProcessTokenSids(int processId_, out string userSid_, out List<string> groupAndServiceSids_)
    {
        if (TryGetProcessTokenSids(processId_, out userSid_, out List<string> serviceSids, out List<string> groupSids))
        {
            groupAndServiceSids_ = [..serviceSids, ..groupSids];
            return true;
        }

        groupAndServiceSids_ = [];
        return false;
    }

    public static bool TryGetProcessUserSid(int processId_, out string sid_)
    {
        if (TryGetProcessTokenSids(processId_, out string userSid, out _))
        {
            sid_ = userSid;
            return true;
        }

        sid_ = string.Empty;
        return false;
    }

    public static uint GetCurrentLinuxUserId()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                return geteuid();
            }
            catch
            {
                return uint.MaxValue;
            }
        }

        return 0;
    }

    public static bool TryGetLinuxSocketPeerCredentials(Socket socket_, out int processId_, out uint userId_)
    {
        processId_ = 0;
        userId_ = 0;

        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            LinuxPeerCred cred = default;
            int len = Marshal.SizeOf<LinuxPeerCred>();
            int result = getsockopt(socket_.Handle.ToInt32(), SOL_SOCKET, SO_PEERCRED, out cred, ref len);
            if (result == 0)
            {
                processId_ = cred.Pid;
                userId_ = cred.Uid;
                return true;
            }
        }
        catch
        {
            // Fail closed on platform error
        }

        return false;
    }

    public static bool VerifyClientIdentity(
        Stream stream_,
        out int verifiedPid_,
        out long processStartTimeTicks_,
        out string verifiedIdentity_,
        string? expectedClientIdentity_) =>
        VerifyClientIdentity(
            stream_,
            out verifiedPid_,
            out processStartTimeTicks_,
            out verifiedIdentity_,
            expectedClientIdentity_ is not null ? [expectedClientIdentity_] : null);

    public static bool VerifyClientIdentity(
        Stream stream_,
        out int verifiedPid_,
        out long processStartTimeTicks_,
        out string verifiedIdentity_,
        IEnumerable<string>? authorizedClientIdentities_ = null)
    {
        verifiedPid_ = 0;
        processStartTimeTicks_ = 0;
        verifiedIdentity_ = string.Empty;

        if (authorizedClientIdentities_ is null)
        {
            // Fail closed: No same-user fallback!
            return false;
        }

        List<string> authorized = authorizedClientIdentities_
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (authorized.Count == 0)
        {
            // Fail closed: No same-user fallback!
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            if (stream_ is NamedPipeServerStream pipeServer)
            {
                if (!TryGetWindowsPipeClientProcessId(pipeServer.SafePipeHandle, out verifiedPid_))
                {
                    return false;
                }

                if (verifiedPid_ <= 0)
                {
                    return false;
                }

                processStartTimeTicks_ = WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(verifiedPid_);
                if (processStartTimeTicks_ <= 0)
                {
                    return false;
                }

                // Distinguish process user SID from service SID in the token
                List<string> serviceSids = [];
                List<string> groupSids = [];
                if (TryGetProcessTokenSids(verifiedPid_, out string clientSid, out serviceSids, out groupSids))
                {
                    verifiedIdentity_ = clientSid;
                }
                else
                {
                    string capturedIdentity = string.Empty;
                    try
                    {
                        pipeServer.RunAsClient(() =>
                        {
                            if (OperatingSystem.IsWindows())
                            {
                                capturedIdentity = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
                            }
                        });
                        verifiedIdentity_ = capturedIdentity;
                    }
                    catch
                    {
                        verifiedIdentity_ = string.Empty;
                    }
                }

                if (string.IsNullOrEmpty(verifiedIdentity_))
                {
                    return false;
                }

                foreach (string authPrincipal in authorized)
                {
                    bool isServicePrincipal = authPrincipal.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(authPrincipal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(authPrincipal, "S-1-5-19", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(authPrincipal, "S-1-5-20", StringComparison.OrdinalIgnoreCase);

                    if (isServicePrincipal)
                    {
                        if (serviceSids.Any(s => string.Equals(s, authPrincipal, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        // User principal: must match client process user SID
                        if (string.Equals(verifiedIdentity_, authPrincipal, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    if (TryTranslateToSidValue(authPrincipal, out string sidVal))
                    {
                        bool isTranslatedService = sidVal.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(sidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(sidVal, "S-1-5-19", StringComparison.OrdinalIgnoreCase) ||
                                                   string.Equals(sidVal, "S-1-5-20", StringComparison.OrdinalIgnoreCase);

                        if (isTranslatedService)
                        {
                            if (serviceSids.Any(s => string.Equals(s, sidVal, StringComparison.OrdinalIgnoreCase)))
                            {
                                return true;
                            }
                        }
                        else
                        {
                            if (string.Equals(verifiedIdentity_, sidVal, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            return false;
        }
        else if (OperatingSystem.IsLinux())
        {
            if (stream_ is NetworkStream netStream)
            {
                if (!TryGetLinuxSocketPeerCredentials(netStream.Socket, out verifiedPid_, out uint uid))
                {
                    return false;
                }

                if (verifiedPid_ <= 0)
                {
                    return false;
                }

                processStartTimeTicks_ = WorkflowSchedulerIntegrityInspector.GetProcessStartTimeTicks(verifiedPid_);
                if (processStartTimeTicks_ <= 0)
                {
                    return false;
                }

                verifiedIdentity_ = uid.ToString(System.Globalization.CultureInfo.InvariantCulture);

                foreach (string authPrincipal in authorized)
                {
                    if (uint.TryParse(authPrincipal, out uint parsedUid) && uid == parsedUid)
                    {
                        return true;
                    }

                    if (string.Equals(verifiedIdentity_, authPrincipal, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        return false;
    }

    public static bool VerifyServerIdentity(
        Stream stream_,
        WorkflowSchedulerPaths paths_,
        string? expectedServerIdentity_ = null)
    {
        ArgumentNullException.ThrowIfNull(stream_, nameof(stream_));
        ArgumentNullException.ThrowIfNull(paths_, nameof(paths_));

        string? expectedSid = expectedServerIdentity_ ?? DefaultBrokerServicePrincipal;
        if (string.IsNullOrWhiteSpace(expectedSid))
        {
            // Fail closed: No expected broker service principal configured.
            // Fallback to worker's own SID is strictly forbidden.
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            if (stream_ is NamedPipeClientStream pipeClient)
            {
                if (!TryGetWindowsPipeServerProcessId(pipeClient.SafePipeHandle, out int serverPid))
                {
                    return false;
                }

                if (serverPid <= 0)
                {
                    return false;
                }

                if (!WorkflowSchedulerIntegrityInspector.IsProcessAlive(serverPid))
                {
                    return false;
                }

                // Verify server process user SID and service SID match expected service identity
                List<string> serverServiceSids = [];
                List<string> serverGroupSids = [];
                if (!TryGetProcessTokenSids(serverPid, out string serverSid, out serverServiceSids, out serverGroupSids))
                {
                    return false;
                }

                bool isExpectedService = expectedSid.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(expectedSid, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(expectedSid, "S-1-5-19", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(expectedSid, "S-1-5-20", StringComparison.OrdinalIgnoreCase);

                if (isExpectedService)
                {
                    if (serverServiceSids.Any(s => string.Equals(s, expectedSid, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
                else
                {
                    if (string.Equals(serverSid, expectedSid, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                if (TryTranslateToSidValue(expectedSid, out string sidVal))
                {
                    bool isTranslatedService = sidVal.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(sidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(sidVal, "S-1-5-19", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(sidVal, "S-1-5-20", StringComparison.OrdinalIgnoreCase);

                    if (isTranslatedService)
                    {
                        if (serverServiceSids.Any(s => string.Equals(s, sidVal, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        if (string.Equals(serverSid, sidVal, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                return false; // Rogue server under different OS identity rejected!
            }

            return false;
        }
        else if (OperatingSystem.IsLinux())
        {
            if (stream_ is NetworkStream netStream)
            {
                if (!TryGetLinuxSocketPeerCredentials(netStream.Socket, out int serverPid, out uint serverUid))
                {
                    return false;
                }

                if (serverPid <= 0)
                {
                    return false;
                }

                if (!WorkflowSchedulerIntegrityInspector.IsProcessAlive(serverPid))
                {
                    return false;
                }

                if (!uint.TryParse(expectedSid, out uint expectedUid))
                {
                    // Fail closed: No valid expected broker UID configured.
                    // Fallback to worker's own UID is strictly forbidden.
                    return false;
                }

                if (serverUid != expectedUid)
                {
                    return false; // Rogue server under different UID rejected!
                }

                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Test-only fault injection for OS inspection (statx) of a given path. When it returns true the
    /// inspection of that path is treated as failed. Production code never sets it.
    /// </summary>
    internal static Func<string, bool>? LinuxStorageRootInspectionFaultInjector { get; set; }

    /// <summary>
    /// Server-side (Broker) admission check of the PREPROVISIONED storageRoot hierarchy on Linux.
    /// It runs before any mutable coordination state is created and never modifies permissions,
    /// ownership or the hierarchy itself (no silent repair, no privileged provisioning).
    /// Option B: the storageRoot must already exist, be a real directory owned by the Broker's
    /// effective UID, and be non-writable by group/other; every ancestor must be owned by root or
    /// the Broker UID and must not be writable by unauthorized principals (a root-owned sticky
    /// directory such as /tmp is the only permitted exception).
    /// All inspection is by statx(AT_SYMLINK_NOFOLLOW) and fails closed on missing information.
    /// Residual (documented, not claimed): POSIX ACLs, mount namespaces/bind mounts and
    /// same-UID/root adversaries are outside this mode-bit based proof.
    /// </summary>
    internal static bool ValidateLinuxStorageRootHierarchy(
        string storageRoot_,
        uint brokerUid_,
        out string reason_,
        out ulong storageRootInode_)
    {
        return ValidateLinuxStorageRootHierarchy(
            storageRoot_,
            brokerUid_,
            out reason_,
            out storageRootInode_,
            out _);
    }

    internal static bool ValidateLinuxStorageRootHierarchy(
        string storageRoot_,
        uint brokerUid_,
        out string reason_,
        out ulong storageRootInode_,
        out ulong storageRootDevice_)
    {
        reason_ = string.Empty;
        storageRootInode_ = 0;
        storageRootDevice_ = 0;

        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(storageRoot_))
        {
            reason_ = "storageRoot is empty.";
            return false;
        }

        if (brokerUid_ == uint.MaxValue)
        {
            reason_ = "Broker effective UID could not be determined.";
            return false;
        }

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(storageRoot_).TrimEnd('/');
            if (fullRoot.Length == 0)
            {
                reason_ = "storageRoot must not be the filesystem root.";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason_ = $"storageRoot path is invalid: {ex.GetType().Name}.";
            return false;
        }

        string? current = fullRoot;
        bool isStorageRoot = true;

        while (current is not null)
        {
            string display = current.Length == 0 ? "/" : current;
            string probePath = current.Length == 0 ? "/" : current;

            Func<string, bool>? injector = LinuxStorageRootInspectionFaultInjector;
            if (injector is not null && injector(probePath))
            {
                reason_ = $"OS inspection failed for '{display}' (fail closed).";
                return false;
            }

            if (!TryLinuxStatx(probePath, followSymlink: false, out LinuxStatx st))
            {
                reason_ = $"OS inspection (statx) failed for '{display}' (fail closed).";
                return false;
            }

            if ((st.stx_mask & STATX_REQUIRED_MASK) != STATX_REQUIRED_MASK)
            {
                reason_ = $"OS inspection of '{display}' returned incomplete information (mask 0x{st.stx_mask:X}).";
                return false;
            }

            if (st.stx_ino == 0)
            {
                reason_ = $"'{display}' returned invalid inode 0 (fail closed).";
                return false;
            }

            if (st.DeviceId == 0)
            {
                reason_ = $"'{display}' returned invalid device 0 (fail closed).";
                return false;
            }

            ushort type = (ushort)(st.stx_mode & S_IFMT);
            if (type == S_IFLNK)
            {
                reason_ = $"'{display}' is a symbolic link.";
                return false;
            }

            if (type != S_IFDIR)
            {
                reason_ = $"'{display}' is not a directory (node type 0x{type:X}).";
                return false;
            }

            bool otherWrite = (st.stx_mode & S_IWOTH) != 0;
            bool groupWrite = (st.stx_mode & S_IWGRP) != 0;

            if (isStorageRoot)
            {
                if (st.stx_uid != brokerUid_)
                {
                    reason_ = $"storageRoot '{display}' is owned by UID {st.stx_uid}, not the Broker UID {brokerUid_}.";
                    return false;
                }

                if (otherWrite || groupWrite)
                {
                    reason_ = $"storageRoot '{display}' is writable by group/other (mode 0{Convert.ToString(st.stx_mode & 0xFFF, 8)}).";
                    return false;
                }

                if ((st.stx_mode & S_IRWXU) != S_IRWXU)
                {
                    reason_ = $"storageRoot '{display}' does not grant the owner rwx (mode 0{Convert.ToString(st.stx_mode & 0xFFF, 8)}).";
                    return false;
                }

                storageRootInode_ = st.stx_ino;
                storageRootDevice_ = st.DeviceId;
            }
            else
            {
                if (st.stx_uid != 0 && st.stx_uid != brokerUid_)
                {
                    reason_ = $"Ancestor '{display}' is owned by UID {st.stx_uid} (must be root or the Broker UID).";
                    return false;
                }

                if (otherWrite || groupWrite)
                {
                    bool stickyRootOwned = (st.stx_mode & S_ISVTX) != 0 && st.stx_uid == 0;
                    if (!stickyRootOwned)
                    {
                        reason_ = $"Ancestor '{display}' is writable by group/other without a root-owned sticky bit (mode 0{Convert.ToString(st.stx_mode & 0xFFF, 8)}).";
                        return false;
                    }
                }
            }

            isStorageRoot = false;

            if (current.Length == 0)
            {
                break;
            }

            int slash = current.LastIndexOf('/');
            current = slash < 0 ? null : current[..slash];
        }

        return true;
    }

    internal static bool TryGetWindowsFileIdentity(
        SafeFileHandle handle_,
        out ulong fileIndex_,
        out uint volumeSerialNumber_)
    {
        fileIndex_ = 0;
        volumeSerialNumber_ = 0;
        if (handle_ is null || handle_.IsInvalid || handle_.IsClosed)
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!GetFileInformationByHandle(handle_, out BY_HANDLE_FILE_INFORMATION info))
        {
            return false;
        }

        WindowsFileIdentity identity = new()
        {
            VolumeSerialNumber = info.dwVolumeSerialNumber,
            FileIndex = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow,
            FileAttributes = info.dwFileAttributes
        };

        if (WindowsStorageRootIdentityMutator is not null)
        {
            WindowsStorageRootIdentityMutator("<handle>", ref identity);
        }

        fileIndex_ = identity.FileIndex;
        volumeSerialNumber_ = identity.VolumeSerialNumber;
        return fileIndex_ != 0 && volumeSerialNumber_ != 0;
    }

    internal static bool TryOpenPinnedWindowsDirectoryHandle(
        string directoryPath_,
        out SafeFileHandle? handle_,
        out ulong fileIndex_,
        out uint volumeSerialNumber_,
        out string reason_)
    {
        handle_ = null;
        fileIndex_ = 0;
        volumeSerialNumber_ = 0;
        reason_ = string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            reason_ = "Windows-only operation.";
            return false;
        }

        try
        {
            SafeFileHandle h = CreateFileW(
                directoryPath_,
                FILE_READ_ATTRIBUTES | READ_CONTROL | SYNCHRONIZE,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            if (h.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                reason_ = $"CreateFileW failed with error {err}";
                h.Dispose();
                return false;
            }

            if (!GetFileInformationByHandle(h, out BY_HANDLE_FILE_INFORMATION info))
            {
                int err = Marshal.GetLastWin32Error();
                reason_ = $"GetFileInformationByHandle failed with error {err}";
                h.Dispose();
                return false;
            }

            WindowsFileIdentity identity = new()
            {
                VolumeSerialNumber = info.dwVolumeSerialNumber,
                FileIndex = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow,
                FileAttributes = info.dwFileAttributes
            };

            if (WindowsStorageRootIdentityMutator is not null)
            {
                WindowsStorageRootIdentityMutator("<handle>", ref identity);
            }

            if ((identity.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                reason_ = "Pinned handle is a reparse point";
                h.Dispose();
                return false;
            }

            if ((identity.FileAttributes & (uint)FileAttributes.Directory) == 0)
            {
                reason_ = "Pinned handle is not a directory";
                h.Dispose();
                return false;
            }

            if (identity.FileIndex == 0 || identity.VolumeSerialNumber == 0)
            {
                reason_ = "Invalid volume serial or file index 0";
                h.Dispose();
                return false;
            }

            handle_ = h;
            fileIndex_ = identity.FileIndex;
            volumeSerialNumber_ = identity.VolumeSerialNumber;
            return true;
        }
        catch (Exception ex)
        {
            reason_ = ex.Message;
            return false;
        }
    }

    internal static bool ValidateWindowsStorageRootHierarchy(
        string storageRoot_,
        string expectedBrokerSid_,
        out string reason_,
        out ulong storageRootFileIndex_,
        out uint storageRootVolumeSerial_)
    {
        reason_ = string.Empty;
        storageRootFileIndex_ = 0;
        storageRootVolumeSerial_ = 0;

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(storageRoot_))
        {
            reason_ = "storageRoot is empty.";
            return false;
        }

        if (storageRoot_.Contains('\0'))
        {
            reason_ = "storageRoot contains NUL character.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedBrokerSid_))
        {
            reason_ = "Broker Windows SID is empty or unavailable.";
            return false;
        }

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(storageRoot_).TrimEnd('\\', '/');
            string? driveRoot = Path.GetPathRoot(fullRoot);
            if (string.IsNullOrEmpty(driveRoot) ||
                string.Equals(fullRoot, driveRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                reason_ = "storageRoot must not be the volume root.";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason_ = $"storageRoot path is invalid: {ex.GetType().Name}.";
            return false;
        }

        string? current = fullRoot;
        bool isStorageRoot = true;

        if (!TryGetApplicableBrokerSids(expectedBrokerSid_, out HashSet<string> applicableBrokerSids, out string? tokenGroupReason))
        {
            reason_ = tokenGroupReason ?? "Failed to enumerate Broker token groups. Fail closed.";
            return false;
        }

        HashSet<string> broadSids = new(StringComparer.OrdinalIgnoreCase)
        {
            "S-1-1-0",      // Everyone
            "S-1-5-32-545", // Builtin Users
            "S-1-5-11",     // Authenticated Users
            "S-1-5-7",      // Anonymous
            "S-1-5-2",      // Network
            "S-1-5-4"       // Interactive
        };

        const FileSystemRights ForbiddenStorageRootWriteRights =
            FileSystemRights.Write |
            FileSystemRights.WriteData |
            FileSystemRights.CreateFiles |
            FileSystemRights.CreateDirectories |
            FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;

        while (!string.IsNullOrEmpty(current))
        {
            string display = current;

            // 1. Fault injector check
            if (WindowsStorageRootInspectionFaultInjector is not null &&
                WindowsStorageRootInspectionFaultInjector(display))
            {
                reason_ = $"OS inspection failed for '{display}' (fail closed).";
                return false;
            }

            // 2. Existence check
            if (!Directory.Exists(current))
            {
                reason_ = $"Path '{display}' does not exist.";
                return false;
            }

            // 3. Fast reparse point check via DirectoryInfo
            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new(current);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reason_ = $"'{display}' is a symbolic link or junction reparse point.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason_ = $"Failed to inspect directory attributes for '{display}': {ex.Message}";
                return false;
            }

            // 4. Open handle and retrieve low-level NT object identity
            try
            {
                using SafeFileHandle dirHandle = CreateFileW(
                    current,
                    FILE_READ_ATTRIBUTES | READ_CONTROL | SYNCHRONIZE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                    IntPtr.Zero);

                if (dirHandle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    reason_ = $"OS inspection (CreateFileW) failed for '{display}': Win32 error {error} (fail closed).";
                    return false;
                }

                if (!GetFileInformationByHandle(dirHandle, out BY_HANDLE_FILE_INFORMATION info))
                {
                    int error = Marshal.GetLastWin32Error();
                    reason_ = $"OS inspection (GetFileInformationByHandle) failed for '{display}': Win32 error {error} (fail closed).";
                    return false;
                }

                WindowsFileIdentity identity = new()
                {
                    VolumeSerialNumber = info.dwVolumeSerialNumber,
                    FileIndex = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow,
                    FileAttributes = info.dwFileAttributes
                };

                if (WindowsStorageRootIdentityMutator is not null)
                {
                    WindowsStorageRootIdentityMutator(current, ref identity);
                }

                if ((identity.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
                {
                    reason_ = $"'{display}' is a reparse point (flagged on open handle).";
                    return false;
                }

                if ((identity.FileAttributes & (uint)FileAttributes.Directory) == 0)
                {
                    reason_ = $"'{display}' is not a directory.";
                    return false;
                }

                if (identity.FileIndex == 0)
                {
                    reason_ = $"'{display}' returned invalid file index 0 (fail closed).";
                    return false;
                }

                if (identity.VolumeSerialNumber == 0)
                {
                    reason_ = $"'{display}' returned invalid volume serial number 0 (fail closed).";
                    return false;
                }

                if (isStorageRoot)
                {
                    storageRootFileIndex_ = identity.FileIndex;
                    storageRootVolumeSerial_ = identity.VolumeSerialNumber;
                }
            }
            catch (Exception ex)
            {
                reason_ = $"OS inspection of '{display}' encountered error: {ex.Message}";
                return false;
            }

            // 5. Inspect NTFS Security Descriptor (Owner and DACL)
            DirectorySecurity sec;
            try
            {
                sec = dirInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            }
            catch (Exception ex)
            {
                reason_ = $"Failed to read Security Descriptor (ACL) for '{display}': {ex.Message} (fail closed).";
                return false;
            }

            if (sec is null)
            {
                reason_ = $"Security descriptor for '{display}' is null (fail closed).";
                return false;
            }

            // Owner check
            SecurityIdentifier? ownerSid;
            try
            {
                ownerSid = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            }
            catch (Exception ex)
            {
                reason_ = $"Failed to read owner SID for '{display}': {ex.Message} (fail closed).";
                return false;
            }

            if (ownerSid is null)
            {
                reason_ = $"Owner SID for '{display}' is null (fail closed).";
                return false;
            }

            string ownerSidVal = ownerSid.Value;
            if (isStorageRoot)
            {
                bool isAllowedStorageOwner =
                    string.Equals(ownerSidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ownerSidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ownerSidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase);

                if (!isAllowedStorageOwner)
                {
                    reason_ = $"storageRoot '{display}' is owned by untrusted SID '{ownerSidVal}' (expected Broker SID '{expectedBrokerSid_}', SYSTEM, or Administrators).";
                    return false;
                }
            }
            else
            {
                bool isAllowedAncestorOwner =
                    string.Equals(ownerSidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ownerSidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ownerSidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase) ||
                    IsTrustedInstallerSid(ownerSid);

                if (!isAllowedAncestorOwner)
                {
                    reason_ = $"Ancestor '{display}' is owned by untrusted SID '{ownerSidVal}' (expected Broker, SYSTEM, Administrators, or TrustedInstaller).";
                    return false;
                }
            }

            // DACL rules evaluation
            AuthorizationRuleCollection rules;
            try
            {
                rules = sec.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier));
            }
            catch (Exception ex)
            {
                reason_ = $"Failed to get access rules for '{display}': {ex.Message} (fail closed).";
                return false;
            }

            if (rules is null || rules.Count == 0)
            {
                reason_ = $"DACL for '{display}' is empty or unreadable (fail closed).";
                return false;
            }

            FileSystemRights brokerAllowedRights = 0;

            foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
            {
                if (rule.IdentityReference is not SecurityIdentifier ruleSid)
                {
                    continue;
                }

                string sidVal = ruleSid.Value;

                if (rule.AccessControlType == AccessControlType.Allow)
                {
                    if (isStorageRoot)
                    {
                        if (broadSids.Contains(sidVal))
                        {
                            if ((rule.FileSystemRights & ForbiddenStorageRootWriteRights) != 0)
                            {
                                reason_ = $"storageRoot '{display}' grants forbidden write/modify rights ({rule.FileSystemRights}) to broad group '{sidVal}'.";
                                return false;
                            }
                        }
                        else if (!string.Equals(sidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(sidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(sidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase))
                        {
                            if ((rule.FileSystemRights & ForbiddenStorageRootWriteRights) != 0)
                            {
                                reason_ = $"storageRoot '{display}' grants forbidden write/modify rights ({rule.FileSystemRights}) to unauthorized principal '{sidVal}'.";
                                return false;
                            }
                        }

                        if (string.Equals(sidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase))
                        {
                            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0)
                            {
                                brokerAllowedRights |= rule.FileSystemRights;
                            }
                        }
                    }
                    else
                    {
                        string? pathRoot = Path.GetPathRoot(fullRoot);
                        bool isPathRoot = !string.IsNullOrEmpty(pathRoot) &&
                            string.Equals(current.TrimEnd('\\', '/'), pathRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

                        if (!isPathRoot)
                        {
                            if (broadSids.Contains(sidVal) ||
                                (!string.Equals(sidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(sidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(sidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase) &&
                                 !IsTrustedInstallerSid((SecurityIdentifier)rule.IdentityReference)))
                            {
                                // Policy on Ancestor Rights:
                                // In the Windows NTFS access control model:
                                // 1. FILE_DELETE_CHILD (DeleteSubdirectoriesAndFiles, 0x40): grants permission to delete
                                //    subdirectories or files within the directory, even if the child lacks Delete permission.
                                //    Any grant of DeleteSubdirectoriesAndFiles (inherited OR explicit) to an untrusted
                                //    identity represents an immediate hierarchy substitution threat and is strictly rejected.
                                // 2. DELETE (0x10000): grants permission to delete the object itself. Under NTFS semantics:
                                //    - RemoveDirectory/NtSetInformationFile(FileDispositionInformation) strictly requires the
                                //      target directory to be EMPTY. A non-empty directory CANNOT be deleted (STATUS_DIRECTORY_NOT_EMPTY).
                                //    - Because the storageRoot exists inside the ancestor with inheritance blocked and exclusive
                                //      Broker/SYSTEM/Administrator permissions, an untrusted identity cannot delete the storageRoot
                                //      nor empty the ancestor.
                                //    - Furthermore, active Broker execution holds locked handles (domain.lock without FileShare.Delete),
                                //      preventing deletion or renaming of any ancestor in the path.
                                //    - Standard Windows environments (e.g. C:\Temp created with Authenticated Users: Modify)
                                //      natively inherit Modify (0x301BF = ReadAndExecute | Write | Delete) without DeleteSubdirectoriesAndFiles.
                                //    - Therefore, inherited Delete on ancestors is tolerated IF and ONLY IF DeleteSubdirectoriesAndFiles,
                                //      ChangePermissions, and TakeOwnership are NOT granted.
                                //    - In contrast, any EXPLICIT grant of Delete to an untrusted principal on an intermediate ancestor
                                //      represents deliberate ACL tampering or insecure provisioning, and is rejected fail-closed.
                                const FileSystemRights DangerousAncestorInheritedRights =
                                    FileSystemRights.DeleteSubdirectoriesAndFiles |
                                    FileSystemRights.ChangePermissions |
                                    FileSystemRights.TakeOwnership;

                                const FileSystemRights DangerousAncestorExplicitRights =
                                    FileSystemRights.Delete |
                                    FileSystemRights.DeleteSubdirectoriesAndFiles |
                                    FileSystemRights.ChangePermissions |
                                    FileSystemRights.TakeOwnership;

                                bool isDangerous = rule.IsInherited
                                    ? (rule.FileSystemRights & DangerousAncestorInheritedRights) != 0
                                    : (rule.FileSystemRights & DangerousAncestorExplicitRights) != 0;

                                if (isDangerous)
                                {
                                    if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0)
                                    {
                                        reason_ = $"Ancestor '{display}' grants dangerous deletion/control rights ({rule.FileSystemRights}) to untrusted principal '{sidVal}'.";
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (rule.AccessControlType == AccessControlType.Deny)
                {
                    if (applicableBrokerSids.Contains(sidVal))
                    {
                        const FileSystemRights RequiredDirectoryWriteRights =
                            FileSystemRights.CreateFiles |
                            FileSystemRights.CreateDirectories;

                        if ((rule.FileSystemRights & RequiredDirectoryWriteRights) != 0 ||
                            (rule.FileSystemRights & FileSystemRights.Write) == FileSystemRights.Write ||
                            (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify ||
                            (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                        {
                            reason_ = $"'{display}' contains Deny ACE blocking required broker write access for '{sidVal}'.";
                            return false;
                        }
                    }
                }
            }

            if (isStorageRoot)
            {
                const FileSystemRights RequiredDirectoryWriteRights =
                    FileSystemRights.CreateFiles |
                    FileSystemRights.CreateDirectories;

                bool hasRequiredWrite =
                    (brokerAllowedRights & RequiredDirectoryWriteRights) == RequiredDirectoryWriteRights ||
                    (brokerAllowedRights & FileSystemRights.Modify) == FileSystemRights.Modify ||
                    (brokerAllowedRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;

                if (!hasRequiredWrite)
                {
                    reason_ = $"storageRoot '{display}' does not grant required write permissions to effective Broker SID '{expectedBrokerSid_}'.";
                    return false;
                }
            }

            isStorageRoot = false;

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) ||
                string.Equals(parent.TrimEnd('\\', '/'), current.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return true;
    }

    internal const string KnownTrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    internal static Func<IdentityReferenceCollection?>? TokenGroupsAccessorForTesting;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool TryGetApplicableBrokerSids(
        string expectedBrokerSid_,
        out HashSet<string> sids,
        out string? failureReason)
    {
        sids = new(StringComparer.OrdinalIgnoreCase)
        {
            expectedBrokerSid_,
            "S-1-1-0",      // Everyone
            "S-1-5-11",     // Authenticated Users
            "S-1-5-32-545", // Builtin Users
            "S-1-5-4"       // Interactive
        };
        failureReason = null;

        try
        {
            IdentityReferenceCollection? groups = TokenGroupsAccessorForTesting != null
                ? TokenGroupsAccessorForTesting()
                : WindowsIdentity.GetCurrent().Groups;

            if (groups is null)
            {
                failureReason = "Failed to enumerate Broker token groups: identity groups collection was null. Fail closed.";
                return false;
            }

            foreach (IdentityReference grp in groups)
            {
                if (grp is SecurityIdentifier sid)
                {
                    sids.Add(sid.Value);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"Failed to enumerate Broker token groups: {ex.Message} (fail closed).";
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static HashSet<string> GetApplicableBrokerSids(string expectedBrokerSid_)
    {
        if (!TryGetApplicableBrokerSids(expectedBrokerSid_, out HashSet<string> sids, out string? failureReason))
        {
            throw new InvalidOperationException(failureReason);
        }

        return sids;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool IsTrustedInstallerSid(SecurityIdentifier sid)
    {
        if (string.Equals(sid.Value, KnownTrustedInstallerSid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            IdentityReference nt = sid.Translate(typeof(NTAccount));
            return string.Equals(nt.Value, @"NT SERVICE\TrustedInstaller", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool ValidatePreExistingWindowsCoordinationDirectory(
        string directoryPath_,
        string expectedBrokerSid_,
        out string reason_)
    {
        reason_ = string.Empty;
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(directoryPath_))
        {
            return true;
        }

        if (!Directory.Exists(directoryPath_))
        {
            return true;
        }

        if (WindowsStorageRootInspectionFaultInjector is not null &&
            WindowsStorageRootInspectionFaultInjector(directoryPath_))
        {
            reason_ = $"OS inspection failed for pre-existing coordination directory '{directoryPath_}' (fail closed).";
            return false;
        }

        DirectoryInfo dirInfo;
        try
        {
            dirInfo = new(directoryPath_);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                reason_ = $"Pre-existing coordination directory '{directoryPath_}' is a symbolic link or junction reparse point.";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to inspect directory attributes for pre-existing coordination directory '{directoryPath_}': {ex.Message}";
            return false;
        }

        try
        {
            using SafeFileHandle dirHandle = CreateFileW(
                directoryPath_,
                FILE_READ_ATTRIBUTES | READ_CONTROL | SYNCHRONIZE,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            if (dirHandle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                reason_ = $"OS inspection (CreateFileW) failed for pre-existing coordination directory '{directoryPath_}': Win32 error {error} (fail closed).";
                return false;
            }

            if (!GetFileInformationByHandle(dirHandle, out BY_HANDLE_FILE_INFORMATION info))
            {
                int error = Marshal.GetLastWin32Error();
                reason_ = $"OS inspection (GetFileInformationByHandle) failed for pre-existing coordination directory '{directoryPath_}': Win32 error {error} (fail closed).";
                return false;
            }

            WindowsFileIdentity identity = new()
            {
                VolumeSerialNumber = info.dwVolumeSerialNumber,
                FileIndex = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow,
                FileAttributes = info.dwFileAttributes
            };

            if (WindowsStorageRootIdentityMutator is not null)
            {
                WindowsStorageRootIdentityMutator(directoryPath_, ref identity);
            }

            if ((identity.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                reason_ = $"Pre-existing coordination directory '{directoryPath_}' is a reparse point (flagged on open handle).";
                return false;
            }

            if ((identity.FileAttributes & (uint)FileAttributes.Directory) == 0)
            {
                reason_ = $"Pre-existing coordination path '{directoryPath_}' is not a directory.";
                return false;
            }

            if (identity.FileIndex == 0 || identity.VolumeSerialNumber == 0)
            {
                reason_ = $"Pre-existing coordination directory '{directoryPath_}' returned invalid file index or volume serial 0 (fail closed).";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason_ = $"OS inspection of pre-existing coordination directory '{directoryPath_}' encountered error: {ex.Message}";
            return false;
        }

        DirectorySecurity sec;
        try
        {
            sec = dirInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to read Security Descriptor (ACL) for pre-existing coordination directory '{directoryPath_}': {ex.Message} (fail closed).";
            return false;
        }

        if (sec is null)
        {
            reason_ = $"Security Descriptor for pre-existing coordination directory '{directoryPath_}' is null (fail closed).";
            return false;
        }

        SecurityIdentifier? ownerSid;
        try
        {
            ownerSid = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to read owner SID for pre-existing coordination directory '{directoryPath_}': {ex.Message} (fail closed).";
            return false;
        }

        if (ownerSid is null)
        {
            reason_ = $"Owner SID for pre-existing coordination directory '{directoryPath_}' is null (fail closed).";
            return false;
        }

        string ownerSidVal = ownerSid.Value;
        bool isAllowedOwner =
            string.Equals(ownerSidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ownerSidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ownerSidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedOwner)
        {
            reason_ = $"Pre-existing coordination directory '{directoryPath_}' is owned by untrusted SID '{ownerSidVal}' (expected Broker SID '{expectedBrokerSid_}', SYSTEM, or Administrators).";
            return false;
        }

        AuthorizationRuleCollection rules;
        try
        {
            rules = sec.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier));
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to read DACL access rules for pre-existing coordination directory '{directoryPath_}': {ex.Message} (fail closed).";
            return false;
        }

        if (rules is null || rules.Count == 0)
        {
            reason_ = $"DACL for pre-existing coordination directory '{directoryPath_}' is empty or unreadable (fail closed).";
            return false;
        }

        HashSet<string> broadSids = ["S-1-1-0", "S-1-5-32-545", "S-1-5-11", "S-1-5-2", "S-1-5-4"];

        const FileSystemRights ForbiddenWriteRights =
            FileSystemRights.Write |
            FileSystemRights.WriteData |
            FileSystemRights.CreateFiles |
            FileSystemRights.CreateDirectories |
            FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;

        const FileSystemRights RequiredDirectoryWriteRights =
            FileSystemRights.CreateFiles |
            FileSystemRights.CreateDirectories;

        if (!TryGetApplicableBrokerSids(expectedBrokerSid_, out HashSet<string> applicableBrokerSids, out string? tokenGroupReason))
        {
            reason_ = tokenGroupReason ?? "Failed to enumerate Broker token groups. Fail closed.";
            return false;
        }
        FileSystemRights brokerAllowedRights = 0;

        foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
        {
            if (rule.IdentityReference is not SecurityIdentifier ruleSid)
            {
                continue;
            }

            string sidVal = ruleSid.Value;

            if (rule.AccessControlType == AccessControlType.Allow)
            {
                if (broadSids.Contains(sidVal))
                {
                    if ((rule.FileSystemRights & ForbiddenWriteRights) != 0)
                    {
                        reason_ = $"Pre-existing coordination directory '{directoryPath_}' grants forbidden write/modify rights ({rule.FileSystemRights}) to broad group '{sidVal}'.";
                        return false;
                    }
                }
                else if (!string.Equals(sidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(sidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(sidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase))
                {
                    if ((rule.FileSystemRights & ForbiddenWriteRights) != 0)
                    {
                        reason_ = $"Pre-existing coordination directory '{directoryPath_}' grants forbidden write/modify rights ({rule.FileSystemRights}) to unauthorized principal '{sidVal}'.";
                        return false;
                    }
                }

                if (string.Equals(sidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase))
                {
                    if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0)
                    {
                        brokerAllowedRights |= rule.FileSystemRights;
                    }
                }
            }
            else if (rule.AccessControlType == AccessControlType.Deny)
            {
                if (applicableBrokerSids.Contains(sidVal))
                {
                    if ((rule.FileSystemRights & RequiredDirectoryWriteRights) != 0 ||
                        (rule.FileSystemRights & FileSystemRights.Write) == FileSystemRights.Write ||
                        (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify ||
                        (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                    {
                        reason_ = $"Pre-existing coordination directory '{directoryPath_}' contains Deny ACE blocking required broker write access for '{sidVal}'.";
                        return false;
                    }
                }
            }
        }

        bool hasRequiredWrite =
            (brokerAllowedRights & RequiredDirectoryWriteRights) == RequiredDirectoryWriteRights ||
            (brokerAllowedRights & FileSystemRights.Modify) == FileSystemRights.Modify ||
            (brokerAllowedRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;

        if (!hasRequiredWrite)
        {
            reason_ = $"Pre-existing coordination directory '{directoryPath_}' does not grant required write permissions to effective Broker SID '{expectedBrokerSid_}'.";
            return false;
        }

        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool ValidatePreExistingWindowsCoordinationFile(
        string filePath_,
        string expectedBrokerSid_,
        out string reason_)
    {
        reason_ = string.Empty;
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath_))
        {
            return true;
        }

        if (!File.Exists(filePath_))
        {
            return true;
        }

        if (WindowsStorageRootInspectionFaultInjector is not null &&
            WindowsStorageRootInspectionFaultInjector(filePath_))
        {
            reason_ = $"OS inspection failed for pre-existing coordination file '{filePath_}' (fail closed).";
            return false;
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new(filePath_);
            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                reason_ = $"Pre-existing coordination file '{filePath_}' is a symbolic link or junction reparse point.";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to inspect file attributes for pre-existing coordination file '{filePath_}': {ex.Message}";
            return false;
        }

        FileSecurity sec;
        try
        {
            sec = fileInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to read Security Descriptor (ACL) for pre-existing coordination file '{filePath_}': {ex.Message} (fail closed).";
            return false;
        }

        if (sec is null)
        {
            reason_ = $"Security Descriptor for pre-existing coordination file '{filePath_}' is null (fail closed).";
            return false;
        }

        SecurityIdentifier? ownerSid;
        try
        {
            ownerSid = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to read owner SID for pre-existing coordination file '{filePath_}': {ex.Message} (fail closed).";
            return false;
        }

        if (ownerSid is null)
        {
            reason_ = $"Owner SID for pre-existing coordination file '{filePath_}' is null (fail closed).";
            return false;
        }

        string ownerSidVal = ownerSid.Value;
        bool isAllowedOwner =
            string.Equals(ownerSidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ownerSidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ownerSidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedOwner)
        {
            reason_ = $"Pre-existing coordination file '{filePath_}' is owned by untrusted SID '{ownerSidVal}' (expected Broker, SYSTEM, or Administrators).";
            return false;
        }

        AuthorizationRuleCollection rules;
        try
        {
            rules = sec.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier));
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to read DACL rules for pre-existing coordination file '{filePath_}': {ex.Message} (fail closed).";
            return false;
        }

        if (rules is null || rules.Count == 0)
        {
            reason_ = $"DACL for pre-existing coordination file '{filePath_}' is empty or unreadable (fail closed).";
            return false;
        }

        HashSet<string> broadSids = ["S-1-1-0", "S-1-5-32-545", "S-1-5-11", "S-1-5-2", "S-1-5-4"];
        const FileSystemRights ForbiddenWriteRights =
            FileSystemRights.Write |
            FileSystemRights.WriteData |
            FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.Delete |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;

        const FileSystemRights RequiredFileWriteRights =
            FileSystemRights.WriteData;

        if (!TryGetApplicableBrokerSids(expectedBrokerSid_, out HashSet<string> applicableBrokerSids, out string? tokenGroupReason))
        {
            reason_ = tokenGroupReason ?? "Failed to enumerate Broker token groups. Fail closed.";
            return false;
        }
        FileSystemRights brokerAllowedRights = 0;

        foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
        {
            if (rule.IdentityReference is not SecurityIdentifier ruleSid)
            {
                continue;
            }

            string sidVal = ruleSid.Value;

            if (rule.AccessControlType == AccessControlType.Allow)
            {
                if (broadSids.Contains(sidVal))
                {
                    if ((rule.FileSystemRights & ForbiddenWriteRights) != 0)
                    {
                        reason_ = $"Pre-existing coordination file '{filePath_}' grants forbidden write/modify rights ({rule.FileSystemRights}) to broad group '{sidVal}'.";
                        return false;
                    }
                }
                else if (!string.Equals(sidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(sidVal, "S-1-5-18", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(sidVal, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase))
                {
                    if ((rule.FileSystemRights & ForbiddenWriteRights) != 0)
                    {
                        reason_ = $"Pre-existing coordination file '{filePath_}' grants forbidden write/modify rights ({rule.FileSystemRights}) to unauthorized principal '{sidVal}'.";
                        return false;
                    }
                }

                if (string.Equals(sidVal, expectedBrokerSid_, StringComparison.OrdinalIgnoreCase))
                {
                    if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0)
                    {
                        brokerAllowedRights |= rule.FileSystemRights;
                    }
                }
            }
            else if (rule.AccessControlType == AccessControlType.Deny)
            {
                if (applicableBrokerSids.Contains(sidVal))
                {
                    if ((rule.FileSystemRights & RequiredFileWriteRights) != 0 ||
                        (rule.FileSystemRights & FileSystemRights.Write) == FileSystemRights.Write ||
                        (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify ||
                        (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                    {
                        reason_ = $"Pre-existing coordination file '{filePath_}' contains Deny ACE blocking required broker write access for '{sidVal}'.";
                        return false;
                    }
                }
            }
        }

        bool hasRequiredWrite =
            (brokerAllowedRights & RequiredFileWriteRights) == RequiredFileWriteRights ||
            (brokerAllowedRights & FileSystemRights.Modify) == FileSystemRights.Modify ||
            (brokerAllowedRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;

        if (!hasRequiredWrite)
        {
            reason_ = $"Pre-existing coordination file '{filePath_}' does not grant required write permissions to effective Broker SID '{expectedBrokerSid_}'.";
            return false;
        }

        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool ValidatePreExistingWindowsCoordinationLayout(
        WorkflowSchedulerPaths paths_,
        string expectedBrokerSid_,
        out string reason_)
    {
        reason_ = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        if (File.Exists(paths_.DomainLockPath))
        {
            if (!ValidatePreExistingWindowsCoordinationFile(paths_.DomainLockPath, expectedBrokerSid_, out reason_))
            {
                return false;
            }
        }

        string[] candidateDirs =
        [
            paths_.LocksDirectory,
            paths_.SlotsDirectory,
            paths_.ActiveLeasesDirectory,
            paths_.ResourcesDirectory,
            paths_.WorkflowsDirectory,
            paths_.IpcDirectory
        ];

        foreach (string dir in candidateDirs)
        {
            if (Directory.Exists(dir))
            {
                if (!ValidatePreExistingWindowsCoordinationDirectory(dir, expectedBrokerSid_, out reason_))
                {
                    return false;
                }
            }
        }

        if (Directory.Exists(paths_.LocksDirectory))
        {
            try
            {
                foreach (string subDir in Directory.GetDirectories(paths_.LocksDirectory, "*", SearchOption.AllDirectories))
                {
                    if (!ValidatePreExistingWindowsCoordinationDirectory(subDir, expectedBrokerSid_, out reason_))
                    {
                        return false;
                    }
                }

                foreach (string file in Directory.GetFiles(paths_.LocksDirectory, "*", SearchOption.AllDirectories))
                {
                    if (!ValidatePreExistingWindowsCoordinationFile(file, expectedBrokerSid_, out reason_))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                reason_ = $"Failed to enumerate existing entries under '{paths_.LocksDirectory}': {ex.Message}";
                return false;
            }
        }

        if (Directory.Exists(paths_.IpcDirectory))
        {
            try
            {
                foreach (string subDir in Directory.GetDirectories(paths_.IpcDirectory, "*", SearchOption.AllDirectories))
                {
                    if (!ValidatePreExistingWindowsCoordinationDirectory(subDir, expectedBrokerSid_, out reason_))
                    {
                        return false;
                    }
                }

                foreach (string file in Directory.GetFiles(paths_.IpcDirectory, "*", SearchOption.AllDirectories))
                {
                    if (!ValidatePreExistingWindowsCoordinationFile(file, expectedBrokerSid_, out reason_))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                reason_ = $"Failed to enumerate existing entries under '{paths_.IpcDirectory}': {ex.Message}";
                return false;
            }
        }

        if (Directory.Exists(paths_.WorkflowsDirectory))
        {
            try
            {
                foreach (string subDir in Directory.GetDirectories(paths_.WorkflowsDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!ValidatePreExistingWindowsCoordinationDirectory(subDir, expectedBrokerSid_, out reason_))
                    {
                        return false;
                    }

                    string authFile = Path.Combine(subDir, "owner-auth.json");
                    if (File.Exists(authFile))
                    {
                        if (!ValidatePreExistingWindowsCoordinationFile(authFile, expectedBrokerSid_, out reason_))
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                reason_ = $"Failed to enumerate existing entries under '{paths_.WorkflowsDirectory}': {ex.Message}";
                return false;
            }
        }

        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool ValidateActiveWindowsCoordinationDirectories(
        WorkflowSchedulerPaths paths_,
        string expectedBrokerSid_,
        out string reason_,
        string? targetWorkflowId_ = null)
    {
        reason_ = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        string[] mandatoryDirs =
        [
            paths_.LocksDirectory,
            paths_.SlotsDirectory,
            paths_.ActiveLeasesDirectory,
            paths_.ResourcesDirectory,
            paths_.WorkflowsDirectory,
            paths_.IpcDirectory
        ];

        foreach (string dir in mandatoryDirs)
        {
            if (!Directory.Exists(dir))
            {
                reason_ = $"Mandatory coordination directory '{dir}' is missing or was removed during execution. Fail closed.";
                return false;
            }

            if (!ValidatePreExistingWindowsCoordinationDirectory(dir, expectedBrokerSid_, out reason_))
            {
                return false;
            }
        }

        // Validate domain.lock if present
        if (File.Exists(paths_.DomainLockPath))
        {
            if (!ValidatePreExistingWindowsCoordinationFile(paths_.DomainLockPath, expectedBrokerSid_, out reason_))
            {
                return false;
            }
        }

        // Validate all files in IpcDirectory
        try
        {
            if (Directory.Exists(paths_.IpcDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(paths_.IpcDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!ValidatePreExistingWindowsCoordinationFile(file, expectedBrokerSid_, out reason_))
                    {
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            reason_ = $"Failed to inspect active IPC directory files in '{paths_.IpcDirectory}': {ex.Message}. Fail closed.";
            return false;
        }

        // If targetWorkflowId_ is specified, validate Broker-owned coordination metadata in its directory
        // respecting cooperative P02/P09 persistence (only Broker metadata owner-auth.json, avoiding worker payload scans)
        if (!string.IsNullOrWhiteSpace(targetWorkflowId_))
        {
            string wfDir = paths_.GetWorkflowDirectory(targetWorkflowId_);
            if (Directory.Exists(wfDir))
            {
                if (!ValidatePreExistingWindowsCoordinationDirectory(wfDir, expectedBrokerSid_, out reason_))
                {
                    return false;
                }

                string authFile = Path.Combine(wfDir, "owner-auth.json");
                if (File.Exists(authFile))
                {
                    if (!ValidatePreExistingWindowsCoordinationFile(authFile, expectedBrokerSid_, out reason_))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public static bool ValidateLinuxSocketSecurity(
        string socketPath_,
        string ipcDirectory_,
        uint? expectedUid_ = null)
    {
        if (string.IsNullOrWhiteSpace(socketPath_) || string.IsNullOrWhiteSpace(ipcDirectory_))
        {
            return false;
        }

        // Hierarchy and canonical path validation: socket must reside directly within ipcDirectory_
        try
        {
            string fullSocketPath = Path.GetFullPath(socketPath_);
            string fullIpcDir = Path.GetFullPath(ipcDirectory_);
            string? socketParentDir = Path.GetDirectoryName(fullSocketPath);
            if (socketParentDir is null ||
                !string.Equals(socketParentDir.TrimEnd('/', '\\'), fullIpcDir.TrimEnd('/', '\\'), StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        try
        {
            if (!File.Exists(socketPath_) || !Directory.Exists(ipcDirectory_))
            {
                return false;
            }

            FileInfo fi = new(socketPath_);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            DirectoryInfo di = new(ipcDirectory_);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            if (!expectedUid_.HasValue)
            {
                // Worker UID must never be used as an implicit trusted broker UID. Fail closed.
                return false;
            }

            uint trustedUid = expectedUid_.Value;

            // Verify ancestor directory hierarchy:
            // Ensure no reparse points exist in ancestor chain, audit statx fail-closed,
            // verify ownership (root or trusted UID), and reject insecure group/other write permissions
            DirectoryInfo? ancestor = di.Parent;
            while (ancestor != null)
            {
                if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                // Inspection is mandatory: failures must trigger fail-closed rejection
                if (!TryLinuxStatx(ancestor.FullName, followSymlink: false, out LinuxStatx ancestorSt))
                {
                    return false;
                }

                if ((ancestorSt.stx_mask & STATX_REQUIRED_MASK) != STATX_REQUIRED_MASK)
                {
                    return false;
                }

                if ((ancestorSt.stx_mode & S_IFMT) != S_IFDIR)
                {
                    return false;
                }

                // Ancestor must be owned by root (UID 0) or the trusted principal
                if (ancestorSt.stx_uid != 0 && ancestorSt.stx_uid != trustedUid)
                {
                    return false;
                }

                bool hasOtherWrite = (ancestorSt.stx_mode & S_IWOTH) != 0;
                bool hasGroupWrite = (ancestorSt.stx_mode & S_IWGRP) != 0;
                bool hasStickyBit = (ancestorSt.stx_mode & S_ISVTX) != 0;

                // Other-writable directory is ONLY permissible if it has sticky bit AND is root-owned (e.g. /tmp)
                if (hasOtherWrite && (!hasStickyBit || ancestorSt.stx_uid != 0))
                {
                    return false;
                }

                // Group-writable directory without sticky bit allows group members to tamper with / substitute hierarchy
                if (hasGroupWrite && !hasStickyBit)
                {
                    return false;
                }

                ancestor = ancestor.Parent;
            }

            // 1. Audit IPC directory node type, ownership and permissions via statx
            if (!TryLinuxStatx(ipcDirectory_, followSymlink: false, out LinuxStatx dirSt))
            {
                return false;
            }

            if ((dirSt.stx_mask & STATX_REQUIRED_MASK) != STATX_REQUIRED_MASK)
            {
                return false;
            }

            if ((dirSt.stx_mode & S_IFMT) != S_IFDIR)
            {
                return false; // Must be a genuine directory
            }

            if (dirSt.stx_uid != trustedUid)
            {
                return false; // Must be owned by trusted principal
            }

            UnixFileMode dirMode = File.GetUnixFileMode(ipcDirectory_);

            // Directory must NOT permit group or other write access (protects against socket substitution / tampering)
            if ((dirMode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                return false;
            }

            // Directory must have UserRead | UserWrite | UserExecute (broker authority)
            if ((dirMode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) !=
                (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            {
                return false;
            }

            // 2. Audit socket node type, ownership and permissions via statx
            if (!TryLinuxStatx(socketPath_, followSymlink: false, out LinuxStatx sockSt))
            {
                return false;
            }

            if ((sockSt.stx_mask & STATX_REQUIRED_MASK) != STATX_REQUIRED_MASK)
            {
                return false;
            }

            // Explicit node type verification: must be a genuine socket (S_IFSOCK = 0xC000)
            // Rejects regular files (0x8000), symlinks (0xA000), FIFOs, devices, etc.
            if ((sockSt.stx_mode & S_IFMT) != S_IFSOCK)
            {
                return false;
            }

            // Explicit ownership verification: socket must be owned by trusted principal
            if (sockSt.stx_uid != trustedUid)
            {
                return false;
            }

            UnixFileMode socketMode = File.GetUnixFileMode(socketPath_);

            // Socket must have UserRead | UserWrite
            if ((socketMode & (UnixFileMode.UserRead | UnixFileMode.UserWrite)) !=
                (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            {
                return false;
            }

            // Socket must NOT have execute permissions
            if ((socketMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

namespace AiRepoKit.WorkflowExecution.Tests;

internal static class TestLockProcessSource
{
    public const string SourceCode = """
#pragma warning disable
using System;
using System.IO;
using System.Text;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: <command> <path> [extra...]");
            return 1;
        }

        string command = args[0];
        string path = args[1];

        // Report the identity of the process that actually runs this code (and therefore holds the
        // OS locks). Under `dotnet run` the launcher PID seen by the test differs from this PID.
        Console.WriteLine($"HELPER_PID:{Environment.ProcessId}");
        Console.Out.Flush();

        try
        {
            switch (command)
            {
                case "lock-exclusive":
                {
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using FileStream fs = new(
                        path,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    Console.WriteLine("LOCKED");
                    Console.Out.Flush();

                    _ = Console.ReadLine();
                    return 0;
                }

                case "lock-shared":
                {
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (!File.Exists(path))
                    {
                        using FileStream init = new(
                            path,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.ReadWrite);
                    }

                    using FileStream fs = new(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 1,
                        FileOptions.None);

                    Console.WriteLine("LOCKED");
                    Console.Out.Flush();

                    _ = Console.ReadLine();
                    return 0;
                }

                case "lock-slot":
                {
                    int slotIndex = int.Parse(args[2]);
                    int maxSlots = int.Parse(args[3]);

                    string slotFileName = $"slot-{slotIndex:D8}.lock";
                    string slotPath = Path.Combine(path, slotFileName);

                    string? dir = Path.GetDirectoryName(slotPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using FileStream fs = new(
                        slotPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    Console.WriteLine($"SLOT_LOCKED:{slotIndex}");
                    Console.Out.Flush();

                    _ = Console.ReadLine();
                    return 0;
                }

                case "hold-workflow":
                {
                    string workflowId = args[2];
                    byte[] bytes = Encoding.UTF8.GetBytes(workflowId);
                    string hex = Convert.ToHexString(bytes).ToLowerInvariant();

                    string wfDir = Path.Combine(path, "workflows", hex);
                    if (!Directory.Exists(wfDir))
                    {
                        Directory.CreateDirectory(wfDir);
                    }

                    string lockPath = Path.Combine(wfDir, "workflow.lock");
                    using FileStream fs = new(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    Console.WriteLine("WORKFLOW_LOCKED");
                    Console.Out.Flush();

                    _ = Console.ReadLine();
                    return 0;
                }

                case "hold-active-locks":
                {
                    string workflowId = args[2];
                    int slotIndex = int.Parse(args[3]);
                    int maxSlots = int.Parse(args[4]);
                    string domainMode = args.Length > 5 ? args[5] : "Normal";

                    byte[] bytes = Encoding.UTF8.GetBytes(workflowId);
                    string hex = Convert.ToHexString(bytes).ToLowerInvariant();

                    string wfDir = Path.Combine(path, "workflows", hex);
                    if (!Directory.Exists(wfDir))
                    {
                        Directory.CreateDirectory(wfDir);
                    }

                    string lockPath = Path.Combine(wfDir, "workflow.lock");
                    using FileStream fsWf = new(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    string slotFileName = $"slot-{slotIndex:D8}.lock";
                    string slotPath = Path.Combine(path, "locks", "slots", slotFileName);
                    string? slotDir = Path.GetDirectoryName(slotPath);
                    if (!string.IsNullOrEmpty(slotDir) && !Directory.Exists(slotDir))
                    {
                        Directory.CreateDirectory(slotDir);
                    }

                    using FileStream fsSlot = new(
                        slotPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    FileStream? fsMode = null;
                    string modePath = Path.Combine(path, "locks", "domain-mode.lock");
                    string? modeDir = Path.GetDirectoryName(modePath);
                    if (!string.IsNullOrEmpty(modeDir) && !Directory.Exists(modeDir))
                    {
                        Directory.CreateDirectory(modeDir);
                    }

                    if (string.Equals(domainMode, "Unrestricted", StringComparison.OrdinalIgnoreCase))
                    {
                        fsMode = new FileStream(
                            modePath,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.None,
                            bufferSize: 1,
                            FileOptions.None);
                    }
                    else
                    {
                        if (!File.Exists(modePath))
                        {
                            try
                            {
                                using var initStream = new FileStream(modePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                            }
                            catch
                            {
                            }
                        }

                        fsMode = new FileStream(
                            modePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 1,
                            FileOptions.None);
                    }

                    try
                    {
                        Console.WriteLine("ACTIVE_LOCKS_LOCKED");
                        Console.Out.Flush();

                        _ = Console.ReadLine();
                    }
                    finally
                    {
                        fsMode?.Dispose();
                        fsSlot?.Dispose();
                        fsWf?.Dispose();
                    }

                    return 0;
                }

                case "hold-active-locks-with-auth-reader":
                {
                    string workflowId = args[2];
                    int slotIndex = int.Parse(args[3]);
                    int maxSlots = int.Parse(args[4]);
                    string domainMode = args.Length > 5 ? args[5] : "Normal";
                    string authPath = args[6];

                    byte[] bytes = Encoding.UTF8.GetBytes(workflowId);
                    string hex = Convert.ToHexString(bytes).ToLowerInvariant();

                    string wfDir = Path.Combine(path, "workflows", hex);
                    if (!Directory.Exists(wfDir))
                    {
                        Directory.CreateDirectory(wfDir);
                    }

                    string lockPath = Path.Combine(wfDir, "workflow.lock");
                    using FileStream fsWf = new(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    string slotFileName = $"slot-{slotIndex:D8}.lock";
                    string slotPath = Path.Combine(path, "locks", "slots", slotFileName);
                    string? slotDir = Path.GetDirectoryName(slotPath);
                    if (!string.IsNullOrEmpty(slotDir) && !Directory.Exists(slotDir))
                    {
                        Directory.CreateDirectory(slotDir);
                    }

                    using FileStream fsSlot = new(
                        slotPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    string modePath = Path.Combine(path, "locks", "domain-mode.lock");
                    string? modeDir = Path.GetDirectoryName(modePath);
                    if (!string.IsNullOrEmpty(modeDir) && !Directory.Exists(modeDir))
                    {
                        Directory.CreateDirectory(modeDir);
                    }

                    FileStream? fsMode = null;
                    if (string.Equals(domainMode, "Unrestricted", StringComparison.OrdinalIgnoreCase))
                    {
                        fsMode = new FileStream(
                            modePath,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.None,
                            bufferSize: 1,
                            FileOptions.None);
                    }
                    else
                    {
                        if (!File.Exists(modePath))
                        {
                            try
                            {
                                using var initStream = new FileStream(modePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                            }
                            catch
                            {
                            }
                        }

                        fsMode = new FileStream(
                            modePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 1,
                            FileOptions.None);
                    }

                    FileStream? fsAuthReader = null;
                    if (File.Exists(authPath))
                    {
                        fsAuthReader = new FileStream(
                            authPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite,
                            bufferSize: 1,
                            FileOptions.None);
                    }

                    try
                    {
                        Console.WriteLine("ACTIVE_READER_LOCKED");
                        Console.Out.Flush();

                        _ = Console.ReadLine();
                    }
                    finally
                    {
                        fsAuthReader?.Dispose();
                        fsMode?.Dispose();
                        fsSlot?.Dispose();
                        fsWf?.Dispose();
                    }

                    return 0;
                }

                case "hold-active-locks-with-unlocked-auth":
                {
                    string workflowId = args[2];
                    int slotIndex = int.Parse(args[3]);
                    int maxSlots = int.Parse(args[4]);
                    string domainMode = args.Length > 5 ? args[5] : "Normal";
                    string ownerToken = args[6];

                    byte[] bytes = Encoding.UTF8.GetBytes(workflowId);
                    string hex = Convert.ToHexString(bytes).ToLowerInvariant();

                    string wfDir = Path.Combine(path, "workflows", hex);
                    if (!Directory.Exists(wfDir))
                    {
                        Directory.CreateDirectory(wfDir);
                    }

                    string lockPath = Path.Combine(wfDir, "workflow.lock");
                    using FileStream fsWf = new(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    string slotFileName = $"slot-{slotIndex:D8}.lock";
                    string slotPath = Path.Combine(path, "locks", "slots", slotFileName);
                    string? slotDir = Path.GetDirectoryName(slotPath);
                    if (!string.IsNullOrEmpty(slotDir) && !Directory.Exists(slotDir))
                    {
                        Directory.CreateDirectory(slotDir);
                    }

                    using FileStream fsSlot = new(
                        slotPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);

                    string modePath = Path.Combine(path, "locks", "domain-mode.lock");
                    string? modeDir = Path.GetDirectoryName(modePath);
                    if (!string.IsNullOrEmpty(modeDir) && !Directory.Exists(modeDir))
                    {
                        Directory.CreateDirectory(modeDir);
                    }

                    FileStream? fsMode = null;
                    if (string.Equals(domainMode, "Unrestricted", StringComparison.OrdinalIgnoreCase))
                    {
                        fsMode = new FileStream(
                            modePath,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.None,
                            bufferSize: 1,
                            FileOptions.None);
                    }
                    else
                    {
                        if (!File.Exists(modePath))
                        {
                            try
                            {
                                using var initStream = new FileStream(modePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                            }
                            catch
                            {
                            }
                        }

                        fsMode = new FileStream(
                            modePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 1,
                            FileOptions.None);
                    }

                    string authPath = Path.Combine(wfDir, "owner-auth.json");
                    long startTicks = GetCurrentProcessStartTimeTicks();
                    int pid = Environment.ProcessId;

                    string json = $"{{\"schemaId\":\"ai.repokit.workflow-scheduler-owner-auth\",\"schemaVersion\":1,\"workflowId\":\"{workflowId}\",\"ownerToken\":\"{ownerToken}\",\"slot\":{slotIndex},\"domainMode\":\"{domainMode}\",\"processId\":{pid},\"processStartTimeTicks\":{startTicks}}}";
                    File.WriteAllText(authPath, json);

                    FileStream? fsAuth = new FileStream(
                        authPath,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        bufferSize: 1,
                        FileOptions.None);

                    try
                    {
                        Console.WriteLine("ACTIVE_UNLOCKED_AUTH_LOCKED");
                        Console.Out.Flush();

                        _ = Console.ReadLine();
                    }
                    finally
                    {
                        fsAuth?.Dispose();
                        fsMode?.Dispose();
                        fsSlot?.Dispose();
                        fsWf?.Dispose();
                    }

                    return 0;
                }

                case "substitute-auth-same-inode":
                {
                    string workflowId = args[2];
                    string substitutedToken = args[3];
                    int ownerPid = int.Parse(args[4]);
                    long ownerStartTicks = long.Parse(args[5]);
                    int slotIndex = args.Length > 6 ? int.Parse(args[6]) : 1;
                    string domainMode = args.Length > 7 ? args[7] : "Normal";

                    byte[] bytes = Encoding.UTF8.GetBytes(workflowId);
                    string hex = Convert.ToHexString(bytes).ToLowerInvariant();

                    string authPath = Path.Combine(path, "workflows", hex, "owner-auth.json");
                    string substitutedAuthJson = $"{{\"schemaId\":\"ai.repokit.workflow-scheduler-owner-auth\",\"schemaVersion\":1,\"workflowId\":\"{workflowId}\",\"ownerToken\":\"{substitutedToken}\",\"slot\":{slotIndex},\"domainMode\":\"{domainMode}\",\"processId\":{ownerPid},\"processStartTimeTicks\":{ownerStartTicks}}}";

                    if (OperatingSystem.IsLinux())
                    {
                        byte[] authBytes = Encoding.UTF8.GetBytes(substitutedAuthJson);
                        int fd = NativePosix.Open(authPath, 1 /* O_WRONLY */ | 0x200 /* O_TRUNC */);
                        if (fd < 0)
                        {
                            Console.Error.WriteLine($"POSIX open failed on '{authPath}'.");
                            return 10;
                        }
                        try
                        {
                            IntPtr written = NativePosix.Write(fd, authBytes, (IntPtr)authBytes.Length);
                            if (written.ToInt64() != authBytes.Length)
                            {
                                Console.Error.WriteLine("POSIX write incomplete.");
                                return 11;
                            }
                        }
                        finally
                        {
                            NativePosix.Close(fd);
                        }
                    }
                    else
                    {
                        File.WriteAllText(authPath, substitutedAuthJson);
                    }

                    string manifestPath = Path.Combine(path, "locks", "active", $"{hex}.lease.json");
                    string substitutedManifest = $"{{\"schemaId\":\"ai.repokit.workflow-scheduler-lease\",\"schemaVersion\":1,\"workflowId\":\"{workflowId}\",\"state\":\"Active\",\"ownerToken\":\"{substitutedToken}\",\"slot\":{slotIndex},\"domainMode\":\"{domainMode}\",\"resourceClaims\":[]}}";
                    File.WriteAllText(manifestPath, substitutedManifest);

                    Console.WriteLine("SUBSTITUTED_LOCKED");
                    Console.Out.Flush();
                    return 0;
                }

                case "hold-active-lease":
                {
                    string workflowId = args[2];
                    int slotIndex = int.Parse(args[3]);
                    int maxSlots = int.Parse(args[4]);
                    string domainMode = args.Length > 5 ? args[5] : "Normal";
                    string? ownerToken = args.Length > 6 ? args[6] : null;
                    string asmPath = args.Length > 7 ? args[7] : FindAssembly(AppContext.BaseDirectory);

                    System.Reflection.Assembly asm = System.Reflection.Assembly.LoadFrom(asmPath);

                    Type pathsType = asm.GetType("AiRepoKit.WorkflowExecution.WorkflowSchedulerPaths")!;
                    object paths = Activator.CreateInstance(pathsType, path)!;

                    Type lockPrimitivesType = asm.GetType("AiRepoKit.WorkflowExecution.WorkflowSchedulerLockPrimitives")!;
                    var tryAcquireExclusive = lockPrimitivesType.GetMethod("TryAcquireExclusive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, [typeof(string)])!;
                    var tryAcquireShared = lockPrimitivesType.GetMethod("TryAcquireShared", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, [typeof(string)])!;

                    var getWfLockPath = pathsType.GetMethod("GetWorkflowLockPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [typeof(string)])!;
                    string wfLockPath = (string)getWfLockPath.Invoke(paths, [workflowId])!;

                    var getSlotLockPath = pathsType.GetMethod("GetSlotLockPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [typeof(int), typeof(int)])!;
                    string slotLockPath = (string)getSlotLockPath.Invoke(paths, [slotIndex, maxSlots])!;

                    var modeLockPathProp = pathsType.GetProperty("DomainModeLockPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    string modeLockPath = (string)modeLockPathProp.GetValue(paths)!;

                    IDisposable wfLockHandle = (IDisposable)tryAcquireExclusive.Invoke(null, [wfLockPath])!;
                    IDisposable slotLockHandle = (IDisposable)tryAcquireExclusive.Invoke(null, [slotLockPath])!;
                    IDisposable modeLockHandle = string.Equals(domainMode, "Unrestricted", StringComparison.OrdinalIgnoreCase)
                        ? (IDisposable)tryAcquireExclusive.Invoke(null, [modeLockPath])!
                        : (IDisposable)tryAcquireShared.Invoke(null, [modeLockPath])!;

                    IDisposable? authHandle = null;
                    if (!string.IsNullOrWhiteSpace(ownerToken))
                    {
                        Type authType = asm.GetType("AiRepoKit.WorkflowExecution.WorkflowSchedulerOwnerAuthentication")!;
                        var registerMethod = authType.GetMethod("RegisterOwnerAuthentication", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

                        authHandle = (IDisposable)registerMethod.Invoke(null, [
                            paths,
                            workflowId,
                            ownerToken,
                            slotIndex,
                            domainMode,
                            wfLockHandle,
                            slotLockHandle,
                            modeLockHandle,
                            null
                        ])!;
                    }

                    try
                    {
                        Console.WriteLine("ACTIVE_LEASE_LOCKED");
                        Console.Out.Flush();

                        _ = Console.ReadLine();
                    }
                    finally
                    {
                        authHandle?.Dispose();
                        modeLockHandle?.Dispose();
                        slotLockHandle?.Dispose();
                        wfLockHandle?.Dispose();
                    }

                    return 0;
                }

                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Exception actual = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Console.Error.WriteLine($"ERROR: {actual.GetType().Name}: {actual.Message}");
            return 3;
        }
    }

    private static long GetCurrentProcessStartTimeTicks()
    {
        if (OperatingSystem.IsWindows())
        {
            return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                string content = File.ReadAllText("/proc/self/stat");
                int lastClose = content.LastIndexOf(')');
                if (lastClose >= 0 && lastClose + 2 < content.Length)
                {
                    string remainder = content.Substring(lastClose + 2);
                    string[] parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 19 && long.TryParse(parts[19], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long jiffies))
                    {
                        return jiffies;
                    }
                }
            }
            catch
            {
            }
        }

        return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    }

    private static class NativePosix
    {
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(string pathname, int flags);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "write", SetLastError = true)]
        public static extern IntPtr Write(int fd, byte[] buf, IntPtr count);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);
    }

    private static string FindAssembly(string startDir)
    {
        string? dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, "AiRepoKit.WorkflowExecution.dll");
            if (File.Exists(candidate)) return candidate;
            string srcCandidate = Path.Combine(dir, "src", "AiRepoKit.WorkflowExecution", "bin", "Debug", "net10.0", "AiRepoKit.WorkflowExecution.dll");
            if (File.Exists(srcCandidate)) return srcCandidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not locate AiRepoKit.WorkflowExecution.dll");
    }
}
""";
}

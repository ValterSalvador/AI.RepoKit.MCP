using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace AiRepoKit.Cli.Services;

public sealed class WindowsMcpHostProcessAccessor : IMcpHostProcessAccessor
{
    public IReadOnlyList<McpHostProcessInfo> GetProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            using ManagementObjectSearcher searcher =
                new(
                    "SELECT ProcessId, Name, CommandLine " +
                    "FROM Win32_Process");

            List<McpHostProcessInfo> processes = [];

            foreach (ManagementBaseObject process in searcher.Get())
            {
                if (!TryReadProcessId(
                    process["ProcessId"],
                    out int processId))
                {
                    continue;
                }

                string name =
                    Convert.ToString(
                        process["Name"],
                        CultureInfo.InvariantCulture)
                    ?? string.Empty;

                string commandLine =
                    Convert.ToString(
                        process["CommandLine"],
                        CultureInfo.InvariantCulture)
                    ?? string.Empty;

                processes.Add(
                    new McpHostProcessInfo(
                        processId,
                        name,
                        commandLine));
            }

            return processes;
        }
        catch
        {
            // Preserve the historical best-effort discovery contract.
            // Discovery failure behaves as an empty process snapshot.
            return [];
        }
    }

    public bool TryStopProcess(int processId_)
    {
        if (!OperatingSystem.IsWindows() ||
            processId_ <= 0)
        {
            return false;
        }

        try
        {
            using Process process =
                Process.GetProcessById(
                    processId_);

            process.Kill();

            _ = process.WaitForExit(
                5000);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadProcessId(
        object? value_,
        out int processId_)
    {
        processId_ = 0;

        if (value_ is null)
        {
            return false;
        }

        try
        {
            processId_ =
                Convert.ToInt32(
                    value_,
                    CultureInfo.InvariantCulture);

            return processId_ > 0;
        }
        catch
        {
            processId_ = 0;
            return false;
        }
    }
}

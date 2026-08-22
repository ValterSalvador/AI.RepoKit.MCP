using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Services.McpLaunch;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Real MCP stdio session backed by a process with redirected stdio.
/// Matches the process setup pattern used in McpSmokeTestService:
/// ArgumentList-based argument passing, UTF-8 encoding, no shell.
/// </summary>
internal sealed class McpStdioSession : IMcpSession
{
    private readonly Process _process;
    private readonly List<string> _stdoutLines = [];
    private readonly List<string> _stderrLines = [];
    private bool _stdoutHadNonJsonLine;
    private bool _disposed;

    private McpStdioSession(Process process)
    {
        _process = process;
    }

    /// <summary>
    /// Builds a ProcessStartInfo from the launch spec so tests can verify the
    /// exact process launch contract without starting a real process.
    /// </summary>
    internal static ProcessStartInfo CreateProcessStartInfo(McpServerLaunchSpec launchSpec_)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = launchSpec_.FileName,
            WorkingDirectory = launchSpec_.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in launchSpec_.Arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        return processStartInfo;
    }

    /// <summary>
    /// Starts a new MCP process session using the given launch spec.
    /// Uses ArgumentList (no shell string building) and UTF-8 stdio encoding.
    /// </summary>
    public static McpStdioSession Start(McpServerLaunchSpec launchSpec_)
    {
        Process process = new();
        process.StartInfo = CreateProcessStartInfo(launchSpec_);

        McpStdioSession session = new(process);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (session._stdoutLines)
                {
                    session._stdoutLines.Add(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (session._stderrLines)
                {
                    session._stderrLines.Add(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return session;
    }

    public bool StdoutHadNonJsonLine => _stdoutHadNonJsonLine;

    public int StdoutLineCount
    {
        get { lock (_stdoutLines) { return _stdoutLines.Count; } }
    }

    public int StderrLineCount
    {
        get { lock (_stderrLines) { return _stderrLines.Count; } }
    }

    public void SendJson(string text)
    {
        _process.StandardInput.WriteLine(text);
        _process.StandardInput.Flush();
    }

    public (string Raw, JsonDocument Document) WaitForResponse(int id, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        int index = 0;

        while (DateTime.UtcNow < deadline)
        {
            List<string> snapshot;
            lock (_stdoutLines)
            {
                snapshot = [.. _stdoutLines];
            }

            while (index < snapshot.Count)
            {
                string line = snapshot[index++];
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch
                {
                    _stdoutHadNonJsonLine = true;
                    continue;
                }

                if (document.RootElement.TryGetProperty("id", out JsonElement idEl) &&
                    idEl.ValueKind == JsonValueKind.Number &&
                    idEl.GetInt32() == id)
                {
                    return (line, document);
                }

                document.Dispose();
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for JSON-RPC response id {id}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.Close(); } catch { }

                if (!_process.WaitForExit(2000))
                {
                    try { _process.Kill(); } catch { }
                    _process.WaitForExit(5000);
                }
            }
        }
        catch { }
        finally
        {
            _process.Dispose();
        }
    }
}

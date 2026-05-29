using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models.McpDiagnostics;

namespace AiRepoKit.Cli.Services;

public sealed class McpSmokeTestService
{
    private static readonly string[] ExpectedTools =
    [
        "get_repo_brief",
        "get_health",
        "get_policy",
        "get_context",
        "search_context"
    ];

    public McpSmokeTestResult Run(string repoPath_, string dllPath_, bool verbose_)
    {
        if (!File.Exists(dllPath_))
        {
            return new McpSmokeTestResult("Failed", "MCP Release DLL is missing.", [], []);
        }

        string repoPath = Path.GetFullPath(repoPath_);
        List<string> stdoutLines = [];
        List<string> stderrLines = [];

        using Process process = new();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = repoPath;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.ArgumentList.Add(dllPath_);
        process.StartInfo.ArgumentList.Add("--repo");
        process.StartInfo.ArgumentList.Add(repoPath);

        try
        {
            process.OutputDataReceived += (_, eventArgs_) =>
            {
                if (eventArgs_.Data is not null)
                {
                    lock (stdoutLines)
                    {
                        stdoutLines.Add(ProcessRunner.Redact(eventArgs_.Data));
                    }
                }
            };
            process.ErrorDataReceived += (_, eventArgs_) =>
            {
                if (eventArgs_.Data is not null)
                {
                    lock (stderrLines)
                    {
                        stderrLines.Add(ProcessRunner.Redact(eventArgs_.Data));
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "airepo-mcp-diagnose",
                        version = "1.0.0"
                    }
                }
            });

            using JsonDocument initialize = WaitForResponse(stdoutLines, 1, TimeSpan.FromSeconds(20));
            if (initialize.RootElement.TryGetProperty("error", out _))
            {
                return new McpSmokeTestResult("Failed", "MCP initialize returned a JSON-RPC error.", GetSmokeDetails(stdoutLines, stderrLines, verbose_), []);
            }

            WriteJson(process, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            });

            WriteJson(process, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            });

            using JsonDocument tools = WaitForResponse(stdoutLines, 2, TimeSpan.FromSeconds(20));
            if (tools.RootElement.TryGetProperty("error", out _))
            {
                return new McpSmokeTestResult("Failed", "MCP tools/list returned a JSON-RPC error.", GetSmokeDetails(stdoutLines, stderrLines, verbose_), []);
            }

            IReadOnlyList<string> toolNames = GetToolNames(tools.RootElement);
            string[] missing = ExpectedTools.Where(tool_ => !toolNames.Contains(tool_, StringComparer.Ordinal)).ToArray();
            List<string> optionalWarnings = [];
            if (missing.Length == 0)
            {
                AddOptionalContextCall(process, stdoutLines, stderrLines, optionalWarnings, 3, "context-packs");
                AddOptionalContextCall(process, stdoutLines, stderrLines, optionalWarnings, 4, "changed-files");
                AddOptionalContextCall(process, stdoutLines, stderrLines, optionalWarnings, 5, "graph");
            }

            process.StandardInput.Close();
            process.WaitForExit(2000);

            if (missing.Length > 0)
            {
                return new McpSmokeTestResult("Failed", "MCP smoke test did not list expected tools: " + string.Join(", ", missing) + ".", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames), toolNames);
            }

            string message = "MCP initialize and tools/list passed. Expected tools listed: " + string.Join(", ", ExpectedTools) + ".";
            if (optionalWarnings.Count > 0)
            {
                return new McpSmokeTestResult("Warning", message + " Optional context-kind checks returned warnings: " + string.Join("; ", optionalWarnings) + ".", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames), toolNames);
            }

            if (stderrLines.Count > 0)
            {
                return new McpSmokeTestResult("Warning", message + $" stderr contained {stderrLines.Count} log line(s), but stdout was valid JSON-RPC.", GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames), toolNames);
            }

            return new McpSmokeTestResult("Passed", message, GetSmokeDetails(stdoutLines, stderrLines, verbose_, toolNames), toolNames);
        }
        catch (Exception exception)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Close();
                    process.WaitForExit(2000);
                }
            }
            catch
            {
            }

            return new McpSmokeTestResult("Failed", ProcessRunner.Redact(exception.Message), GetSmokeDetails(stdoutLines, stderrLines, verbose_), []);
        }
    }

    private static void WriteJson(Process process_, object value_)
    {
        process_.StandardInput.WriteLine(JsonSerializer.Serialize(value_));
        process_.StandardInput.Flush();
    }

    private static void AddOptionalContextCall(Process process_, List<string> stdoutLines_, List<string> stderrLines_, List<string> warnings_, int id_, string kind_)
    {
        try
        {
            WriteJson(process_, new
            {
                jsonrpc = "2.0",
                id = id_,
                method = "tools/call",
                @params = new
                {
                    name = "get_context",
                    arguments = new
                    {
                        kind = kind_,
                        detail = "brief",
                        limit = 5
                    }
                }
            });
            using JsonDocument response = WaitForResponse(stdoutLines_, id_, TimeSpan.FromSeconds(20));
            if (response.RootElement.TryGetProperty("error", out _))
            {
                warnings_.Add($"get_context kind={kind_} returned a JSON-RPC error");
            }
        }
        catch (Exception exception)
        {
            warnings_.Add($"get_context kind={kind_}: {ProcessRunner.Redact(exception.Message)}");
            lock (stderrLines_)
            {
                stderrLines_.Add($"optional context smoke warning for {kind_}");
            }
        }
    }

    private static JsonDocument WaitForResponse(List<string> stdoutLines_, int id_, TimeSpan timeout_)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout_);
        int index = 0;
        while (DateTime.UtcNow < deadline)
        {
            List<string> snapshot;
            lock (stdoutLines_)
            {
                snapshot = stdoutLines_.ToList();
            }

            while (index < snapshot.Count)
            {
                string line = snapshot[index++];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch
                {
                    continue;
                }

                if (document.RootElement.TryGetProperty("id", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id_)
                {
                    return document;
                }

                document.Dispose();
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for JSON-RPC response id {id_}.");
    }

    private static IReadOnlyList<string> GetToolNames(JsonElement root_)
    {
        JsonElement current = root_;
        if (current.TryGetProperty("result", out JsonElement result))
        {
            current = result;
        }

        if (!current.TryGetProperty("tools", out JsonElement tools) || tools.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> names = [];
        foreach (JsonElement tool in tools.EnumerateArray())
        {
            if (tool.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
            {
                names.Add(name.GetString() ?? string.Empty);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> GetSmokeDetails(List<string> stdoutLines_, List<string> stderrLines_, bool verbose_, IReadOnlyList<string>? tools_ = null)
    {
        List<string> details = [];
        if (tools_ is not null)
        {
            details.Add("Tools: " + string.Join(", ", tools_));
        }

        details.Add($"stdout JSON-RPC line count: {stdoutLines_.Count}");
        details.Add($"stderr line count: {stderrLines_.Count}");
        if (verbose_)
        {
            details.AddRange(stderrLines_.TakeLast(5).Select(line_ => "stderr: " + line_));
        }

        return details;
    }
}

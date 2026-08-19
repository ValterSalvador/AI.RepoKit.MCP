using System.Text.Json;

namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Represents a single live MCP stdio session. Provides JSON-RPC send/receive
/// and tracks stdout line statistics needed for budget evaluation.
/// </summary>
internal interface IMcpSession : IDisposable
{
    /// <summary>Sends a JSON-RPC line to the MCP process stdin.</summary>
    void SendJson(string text);

    /// <summary>
    /// Blocks until a JSON-RPC response with the given id arrives on stdout,
    /// or until the timeout elapses. Throws TimeoutException on timeout.
    /// Returns the raw stdout line and the parsed document (caller must dispose).
    /// </summary>
    (string Raw, JsonDocument Document) WaitForResponse(int id, TimeSpan timeout);

    /// <summary>True if any non-JSON-parseable line was received on stdout.</summary>
    bool StdoutHadNonJsonLine { get; }

    /// <summary>Total lines received on stdout so far.</summary>
    int StdoutLineCount { get; }

    /// <summary>Total lines received on stderr so far.</summary>
    int StderrLineCount { get; }
}

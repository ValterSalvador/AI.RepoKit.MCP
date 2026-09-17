using System.Runtime.CompilerServices;
using System.Text.Json;
using AiRepoKit.Agents.Runtime;

[assembly: InternalsVisibleTo("AiRepoKit.Agents.Antigravity.Tests")]

namespace AiRepoKit.Agents.Antigravity;

public sealed class AntigravityCliClientAdapter : IAgentExecutor
{
    private static readonly AgentProviderId _providerId =
        new("antigravity");

    private static readonly AgentCapabilitySet _capabilities =
        new([new AgentCapability("structured-output")]);

    private const string ReadOnlyPolicyDiagnostic =
        "Current Antigravity headless execution cannot represent the requested read-only policy.";

    private readonly IProcessExecutionRuntime _processRunner;

    public AgentProviderId ProviderId
    {
        get
        {
            return _providerId;
        }
    }

    public AgentCapabilitySet Capabilities
    {
        get
        {
            return _capabilities;
        }
    }

    public AntigravityCliClientAdapter()
        : this(new SystemProcessExecutionRuntime())
    {
    }

    internal AntigravityCliClientAdapter(
        IProcessExecutionRuntime processRunner_)
    {
        ArgumentNullException.ThrowIfNull(
            processRunner_,
            nameof(processRunner_));

        this._processRunner =
            processRunner_;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        cancellationToken_.ThrowIfCancellationRequested();

        if (request_.Permission == ExecutionPermission.ReadOnly)
        {
            return AgentExecutionResult.Blocked(
                diagnosticText_: ReadOnlyPolicyDiagnostic,
                sessionReference_: null,
                outputText_: null);
        }

        ProcessExecutionRequest invocation =
            BuildInvocation(
                request_);

        ProcessExecutionResult processResult;
        try
        {
            processResult = await this._processRunner.ExecuteAsync(
                invocation,
                cancellationToken_).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken_.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return AgentExecutionResult.Failed(
                diagnosticText_: $"Failed to start or execute Antigravity process: {exception.Message}");
        }

        return ParseExecutionResult(
            processResult.ExitCode,
            processResult.StandardOutput,
            processResult.StandardError);
    }

    internal static ProcessExecutionRequest BuildInvocation(
        AgentExecutionRequest request_)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        if (request_.Permission == ExecutionPermission.ReadOnly)
        {
            throw new InvalidOperationException(
                "Cannot build process invocation for ReadOnly permission.");
        }

        List<string> arguments =
        [
            "-p",
            request_.Instruction,
            "--output-format",
            "json"
        ];

        switch (request_.Permission)
        {
            case ExecutionPermission.WorkspaceWrite:
                arguments.Add("--sandbox");
                break;

            case ExecutionPermission.Unrestricted:
                arguments.Add("--dangerously-skip-permissions");
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request_),
                    request_.Permission,
                    "Unsupported execution permission.");
        }

        if (request_.SessionReference is not null)
        {
            arguments.Add("--conversation");
            arguments.Add(request_.SessionReference.Value);
        }

        if (request_.StructuredOutput is not null)
        {
            arguments.Add("--json-schema");
            arguments.Add(request_.StructuredOutput.JsonSchema);
        }

        return new ProcessExecutionRequest(
            "agy",
            arguments.AsReadOnly(),
            request_.Environment.WorkingDirectory);
    }

    internal static AgentExecutionResult ParseExecutionResult(
        int exitCode_,
        string? stdout_,
        string? stderr_)
    {
        if (string.IsNullOrWhiteSpace(
                stdout_))
        {
            return AgentExecutionResult.Failed(
                diagnosticText_: CombineDiagnostics(
                    "Antigravity process returned empty stdout.",
                    stderr_));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                stdout_);
        }
        catch (JsonException exception)
        {
            return AgentExecutionResult.Failed(
                diagnosticText_: CombineDiagnostics(
                    $"Failed to parse Antigravity response: malformed JSON output ({exception.Message}).",
                    stderr_));
        }

        using (document)
        {
            JsonElement root =
                document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return AgentExecutionResult.Failed(
                    diagnosticText_: CombineDiagnostics(
                        "Antigravity response envelope must be a JSON object.",
                        stderr_));
            }

            string? rawStatus =
                null;

            if (root.TryGetProperty("status", out JsonElement statusElement) &&
                statusElement.ValueKind == JsonValueKind.String)
            {
                rawStatus =
                    statusElement.GetString();
            }

            if (string.IsNullOrEmpty(
                    rawStatus))
            {
                return AgentExecutionResult.Failed(
                    diagnosticText_: CombineDiagnostics(
                        "Antigravity response missing status field.",
                        stderr_));
            }

            string? responseText =
                null;

            if (root.TryGetProperty("response", out JsonElement responseElement))
            {
                responseText = responseElement.ValueKind == JsonValueKind.String
                    ? responseElement.GetString()
                    : responseElement.GetRawText();
            }

            string? errorText =
                null;

            if (root.TryGetProperty("error", out JsonElement errorElement))
            {
                errorText = errorElement.ValueKind == JsonValueKind.String
                    ? errorElement.GetString()
                    : errorElement.GetRawText();
            }

            AgentSessionReference? sessionReference =
                null;

            if (root.TryGetProperty("conversation_id", out JsonElement conversationElement) &&
                conversationElement.ValueKind == JsonValueKind.String)
            {
                string? conversationId =
                    conversationElement.GetString();

                if (!string.IsNullOrWhiteSpace(
                        conversationId))
                {
                    sessionReference =
                        new AgentSessionReference(conversationId);
                }
            }

            switch (rawStatus)
            {
                case "SUCCESS":
                {
                    if (exitCode_ != 0)
                    {
                        return AgentExecutionResult.Failed(
                            diagnosticText_: CombineDiagnostics(
                                $"Antigravity reported SUCCESS but process exited with code {exitCode_}.",
                                CombineDiagnostics(errorText, stderr_)),
                            sessionReference_: sessionReference,
                            outputText_: responseText);
                    }

                    return AgentExecutionResult.Completed(
                        outputText_: responseText,
                        sessionReference_: sessionReference,
                        diagnosticText_: CombineDiagnostics(errorText, stderr_));
                }

                case "WAITING":
                {
                    return AgentExecutionResult.NeedsInput(
                        diagnosticText_: CombineDiagnostics(errorText, stderr_),
                        sessionReference_: sessionReference,
                        outputText_: responseText);
                }

                case "ERROR":
                case "INVALID":
                case "CANCELED":
                case "INTERRUPTED":
                case "RUNNING":
                {
                    string? diagnostic =
                        CombineDiagnostics(
                            errorText,
                            stderr_);

                    if (string.IsNullOrWhiteSpace(
                            diagnostic))
                    {
                        diagnostic =
                            $"Antigravity reported {rawStatus}.";
                    }

                    return AgentExecutionResult.Failed(
                        diagnosticText_: diagnostic,
                        sessionReference_: sessionReference,
                        outputText_: responseText);
                }

                default:
                {
                    return AgentExecutionResult.Failed(
                        diagnosticText_: CombineDiagnostics(
                            $"Antigravity returned unrecognized status '{rawStatus}'.",
                            CombineDiagnostics(errorText, stderr_)),
                        sessionReference_: sessionReference,
                        outputText_: responseText);
                }
            }
        }
    }

    private static string? CombineDiagnostics(
        string? primary_,
        string? secondary_)
    {
        bool hasPrimary =
            !string.IsNullOrWhiteSpace(primary_);
        bool hasSecondary =
            !string.IsNullOrWhiteSpace(secondary_);

        if (hasPrimary && hasSecondary)
        {
            return $"{primary_}\n{secondary_}";
        }

        if (hasPrimary)
        {
            return primary_;
        }

        if (hasSecondary)
        {
            return secondary_;
        }

        return null;
    }
}

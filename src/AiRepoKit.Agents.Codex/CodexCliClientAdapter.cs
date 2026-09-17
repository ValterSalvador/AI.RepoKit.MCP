using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("AiRepoKit.Agents.Codex.Tests")]

namespace AiRepoKit.Agents.Codex;

public sealed class CodexCliClientAdapter : IAgentExecutor
{
    private static readonly AgentProviderId _providerId =
        new("codex");

    private static readonly AgentCapabilitySet _capabilities =
        new([new AgentCapability("structured-output")]);

    private readonly ICodexProcessRunner _processRunner;

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

    public CodexCliClientAdapter()
        : this(new CodexProcessRunner())
    {
    }

    internal CodexCliClientAdapter(
        ICodexProcessRunner processRunner_)
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

        string? schemaFilePath = null;
        try
        {
            if (request_.StructuredOutput is not null)
            {
                schemaFilePath = CodexTempSchemaFile.Create(
                    request_.StructuredOutput.JsonSchema);
            }

            CodexProcessInvocation invocation =
                BuildInvocation(
                    request_,
                    schemaFilePath);

            CodexProcessResult processResult;
            try
            {
                processResult = await this._processRunner.RunAsync(
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
                    diagnosticText_: $"Failed to start or execute Codex process: {exception.Message}");
            }

            return ParseExecutionResult(
                processResult.ExitCode,
                processResult.StandardOutput,
                processResult.StandardError);
        }
        finally
        {
            CodexTempSchemaFile.Delete(schemaFilePath);
        }
    }

    internal static CodexProcessInvocation BuildInvocation(
        AgentExecutionRequest request_,
        string? schemaFilePath_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        List<string> arguments =
        [
            "exec",
            "--json",
            "--color",
            "never",
            "--skip-git-repo-check",
            "-C",
            request_.Environment.WorkingDirectory
        ];

        switch (request_.Permission)
        {
            case ExecutionPermission.ReadOnly:
                arguments.Add("--sandbox");
                arguments.Add("read-only");
                break;

            case ExecutionPermission.WorkspaceWrite:
                arguments.Add("--sandbox");
                arguments.Add("workspace-write");
                break;

            case ExecutionPermission.Unrestricted:
                arguments.Add("--dangerously-bypass-approvals-and-sandbox");
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request_),
                    request_.Permission,
                    "Unsupported execution permission.");
        }

        if (request_.StructuredOutput is not null)
        {
            arguments.Add("--output-schema");
            arguments.Add(schemaFilePath_ ?? string.Empty);
        }

        if (request_.SessionReference is not null)
        {
            arguments.Add("resume");
            arguments.Add(request_.SessionReference.Value);
        }

        arguments.Add(request_.Instruction);

        return new CodexProcessInvocation(
            "codex",
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
                    "Codex process returned empty stdout.",
                    stderr_));
        }

        AgentSessionReference? latestSessionReference = null;
        string? latestAgentMessage = null;
        bool hasTurnCompleted = false;
        bool hasTurnFailed = false;
        bool hasTopLevelError = false;
        List<string> providerDiagnostics = [];

        using StringReader reader = new(stdout_);
        string? line;
        bool hasAnyNonEmptyLine = false;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            hasAnyNonEmptyLine = true;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException exception)
            {
                return AgentExecutionResult.Failed(
                    diagnosticText_: CombineDiagnostics(
                        $"Failed to parse Codex JSON Lines stdout: malformed JSON ({exception.Message}).",
                        stderr_),
                    sessionReference_: latestSessionReference,
                    outputText_: latestAgentMessage);
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return AgentExecutionResult.Failed(
                        diagnosticText_: CombineDiagnostics(
                            "Codex JSON Lines event envelope must be a JSON object.",
                            stderr_),
                        sessionReference_: latestSessionReference,
                        outputText_: latestAgentMessage);
                }

                if (!root.TryGetProperty("type", out JsonElement typeElement) ||
                    typeElement.ValueKind != JsonValueKind.String)
                {
                    return AgentExecutionResult.Failed(
                        diagnosticText_: CombineDiagnostics(
                            "Codex JSON Lines event missing string 'type' property.",
                            stderr_),
                        sessionReference_: latestSessionReference,
                        outputText_: latestAgentMessage);
                }

                string eventType = typeElement.GetString()!;

                switch (eventType)
                {
                    case "thread.started":
                    {
                        if (root.TryGetProperty("thread_id", out JsonElement threadIdElement) &&
                            threadIdElement.ValueKind == JsonValueKind.String)
                        {
                            string? threadId = threadIdElement.GetString();
                            if (!string.IsNullOrWhiteSpace(threadId))
                            {
                                latestSessionReference = new AgentSessionReference(threadId);
                            }
                        }
                        break;
                    }

                    case "item.completed":
                    {
                        if (root.TryGetProperty("item", out JsonElement itemElement) &&
                            itemElement.ValueKind == JsonValueKind.Object)
                        {
                            if (itemElement.TryGetProperty("type", out JsonElement itemTypeElement) &&
                                itemTypeElement.ValueKind == JsonValueKind.String &&
                                string.Equals(itemTypeElement.GetString(), "agent_message", StringComparison.Ordinal))
                            {
                                if (itemElement.TryGetProperty("text", out JsonElement textElement) &&
                                    textElement.ValueKind == JsonValueKind.String)
                                {
                                    latestAgentMessage = textElement.GetString();
                                }
                            }
                        }
                        break;
                    }

                    case "turn.completed":
                    {
                        hasTurnCompleted = true;
                        break;
                    }

                    case "turn.failed":
                    {
                        hasTurnFailed = true;

                        if (root.TryGetProperty("error", out JsonElement errorElement))
                        {
                            if (errorElement.ValueKind == JsonValueKind.Object &&
                                errorElement.TryGetProperty("message", out JsonElement messageElement) &&
                                messageElement.ValueKind == JsonValueKind.String)
                            {
                                string? msg = messageElement.GetString();
                                if (!string.IsNullOrWhiteSpace(msg))
                                {
                                    providerDiagnostics.Add(msg);
                                }
                            }
                            else if (errorElement.ValueKind == JsonValueKind.String)
                            {
                                string? msg = errorElement.GetString();
                                if (!string.IsNullOrWhiteSpace(msg))
                                {
                                    providerDiagnostics.Add(msg);
                                }
                            }
                        }
                        break;
                    }

                    case "error":
                    {
                        hasTopLevelError = true;

                        if (root.TryGetProperty("message", out JsonElement messageElement) &&
                            messageElement.ValueKind == JsonValueKind.String)
                        {
                            string? msg = messageElement.GetString();
                            if (!string.IsNullOrWhiteSpace(msg))
                            {
                                providerDiagnostics.Add(msg);
                            }
                        }
                        break;
                    }

                    default:
                        // Syntactically valid unknown top-level event: ignore for forward compatibility.
                        break;
                }
            }
        }

        if (!hasAnyNonEmptyLine)
        {
            return AgentExecutionResult.Failed(
                diagnosticText_: CombineDiagnostics(
                    "Codex process returned empty stdout.",
                    stderr_),
                sessionReference_: latestSessionReference,
                outputText_: latestAgentMessage);
        }

        string? providerText = providerDiagnostics.Count > 0
            ? string.Join("\n", providerDiagnostics)
            : null;

        if (hasTurnFailed || hasTopLevelError)
        {
            string fallbackError = hasTurnFailed
                ? "Codex reported turn.failed."
                : "Codex reported error.";

            string? failureDiagnostic = CombineDiagnostics(
                providerText ?? fallbackError,
                stderr_);

            return AgentExecutionResult.Failed(
                diagnosticText_: failureDiagnostic,
                sessionReference_: latestSessionReference,
                outputText_: latestAgentMessage);
        }

        if (exitCode_ != 0)
        {
            string exitDiagnostic = hasTurnCompleted
                ? $"Codex reported turn.completed but process exited with code {exitCode_}."
                : $"Codex process exited with code {exitCode_}.";

            string? failureDiagnostic = CombineDiagnostics(
                providerText ?? exitDiagnostic,
                stderr_);

            return AgentExecutionResult.Failed(
                diagnosticText_: failureDiagnostic,
                sessionReference_: latestSessionReference,
                outputText_: latestAgentMessage);
        }

        if (!hasTurnCompleted)
        {
            string? failureDiagnostic = CombineDiagnostics(
                providerText ?? "Codex execution did not emit a terminal turn.completed event.",
                stderr_);

            return AgentExecutionResult.Failed(
                diagnosticText_: failureDiagnostic,
                sessionReference_: latestSessionReference,
                outputText_: latestAgentMessage);
        }

        return AgentExecutionResult.Completed(
            outputText_: latestAgentMessage,
            sessionReference_: latestSessionReference,
            diagnosticText_: string.IsNullOrWhiteSpace(stderr_) ? null : stderr_);
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

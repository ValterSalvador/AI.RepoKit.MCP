using System.Diagnostics;
using System.Text;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Codex;
using AiRepoKit.Agents.Runtime;
using Xunit;

namespace AiRepoKit.Agents.Codex.Tests;

public sealed class CodexCliClientAdapterTests
{
    private static ExecutionEnvironment CreateEnvironment()
    {
        return new ExecutionEnvironment(
            Path.GetFullPath(AppContext.BaseDirectory));
    }

    private static AgentExecutionRequest CreateRequest(
        string instruction_ = "Analyze repo",
        ExecutionPermission permission_ = ExecutionPermission.WorkspaceWrite,
        AgentSessionReference? sessionReference_ = null,
        StructuredOutputContract? structuredOutput_ = null)
    {
        return new AgentExecutionRequest(
            instruction_,
            permission_,
            CreateEnvironment(),
            sessionReference_,
            structuredOutput_);
    }

    // ---------------------------------------------------------
    // Provider identity and metadata (18-21)
    // ---------------------------------------------------------

    [Fact]
    public void ProviderId_HasExactValueCodex()
    {
        CodexCliClientAdapter adapter =
            new(new FakeCodexProcessRunner());

        Assert.Equal(
            "codex",
            adapter.ProviderId.Value);
    }

    [Fact]
    public void Capabilities_ContainsExactlyOneCapability()
    {
        CodexCliClientAdapter adapter =
            new(new FakeCodexProcessRunner());

        Assert.Single(
            adapter.Capabilities);
    }

    [Fact]
    public void Capabilities_ContainsExactCapabilityStructuredOutput()
    {
        CodexCliClientAdapter adapter =
            new(new FakeCodexProcessRunner());

        AgentCapability capability =
            new("structured-output");

        Assert.True(
            adapter.Capabilities.Supports(capability));
    }

    [Fact]
    public void Capabilities_EnumerationIsDeterministic()
    {
        CodexCliClientAdapter adapter =
            new(new FakeCodexProcessRunner());

        List<string> capabilityValues =
            adapter.Capabilities
                .Select(c_ => c_.Value)
                .ToList();

        Assert.Single(capabilityValues);
        Assert.Equal(
            "structured-output",
            capabilityValues[0]);
    }

    [Fact]
    public void PublicParameterlessConstructor_InstantiatesAdapter()
    {
        CodexCliClientAdapter adapter =
            new();

        Assert.Equal(
            "codex",
            adapter.ProviderId.Value);
        Assert.Single(
            adapter.Capabilities);
    }

    // ---------------------------------------------------------
    // Base invocation (22-35)
    // ---------------------------------------------------------

    [Fact]
    public void Invocation_ExecutableIsExactlyCodex()
    {
        AgentExecutionRequest request =
            CreateRequest();

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            "codex",
            invocation.Executable);
    }

    [Fact]
    public void Invocation_WorkingDirectoryIsExactEnvironmentValue()
    {
        ExecutionEnvironment env =
            CreateEnvironment();
        AgentExecutionRequest request =
            new(
                "Do task",
                ExecutionPermission.WorkspaceWrite,
                env);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            env.WorkingDirectory,
            invocation.WorkingDirectory);
    }

    [Fact]
    public void Invocation_CommonRootArguments_BeginWithExactSequence()
    {
        ExecutionEnvironment env =
            CreateEnvironment();
        AgentExecutionRequest request =
            new(
                "Run tests now",
                ExecutionPermission.WorkspaceWrite,
                env);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.True(
            invocation.Arguments.Count >= 7);
        Assert.Equal(
            "exec",
            invocation.Arguments[0]);
        Assert.Equal(
            "--json",
            invocation.Arguments[1]);
        Assert.Equal(
            "--color",
            invocation.Arguments[2]);
        Assert.Equal(
            "never",
            invocation.Arguments[3]);
        Assert.Equal(
            "--skip-git-repo-check",
            invocation.Arguments[4]);
        Assert.Equal(
            "-C",
            invocation.Arguments[5]);
        Assert.Equal(
            env.WorkingDirectory,
            invocation.Arguments[6]);
    }

    [Fact]
    public void Invocation_InstructionWithSpaces_RemainsOneArgument()
    {
        string instruction =
            "Execute task with spaces in the middle and trailing words";

        AgentExecutionRequest request =
            CreateRequest(instruction);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            instruction,
            invocation.Arguments.Last());
    }

    [Fact]
    public void Invocation_InstructionWithQuotes_RemainsOneArgument()
    {
        string instruction =
            "Analyze \"quotes\" and 'single' and `backticks`";

        AgentExecutionRequest request =
            CreateRequest(instruction);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            instruction,
            invocation.Arguments.Last());
    }

    [Fact]
    public void Invocation_InstructionWithNewlines_RemainsOneArgument()
    {
        string instruction =
            "Line 1\r\nLine 2\nLine 3\n\nEnd";

        AgentExecutionRequest request =
            CreateRequest(instruction);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            instruction,
            invocation.Arguments.Last());
    }

    [Fact]
    public void RealInvocationBuilder_ProducesValidProcessStartInfo()
    {
        AgentExecutionRequest request =
            CreateRequest("Verify build info");

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        ProcessStartInfo startInfo =
            SystemProcessExecutionRuntime.CreateProcessStartInfo(invocation);

        Assert.Equal(
            "codex",
            startInfo.FileName);
        Assert.Equal(
            request.Environment.WorkingDirectory,
            startInfo.WorkingDirectory);
        Assert.False(
            startInfo.UseShellExecute);
        Assert.True(
            startInfo.RedirectStandardOutput);
        Assert.True(
            startInfo.RedirectStandardError);
        Assert.True(
            startInfo.CreateNoWindow);

        Assert.Equal(
            invocation.Arguments.Count,
            startInfo.ArgumentList.Count);

        for (int i = 0; i < invocation.Arguments.Count; i++)
        {
            Assert.Equal(
                invocation.Arguments[i],
                startInfo.ArgumentList[i]);
        }
    }

    // ---------------------------------------------------------
    // Permission mapping (36-41)
    // ---------------------------------------------------------

    [Fact]
    public void ReadOnly_AddsSandboxReadOnly_AndDoesNotEmitBypass()
    {
        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.ReadOnly);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        int sandboxIndex =
            invocation.Arguments.ToList().IndexOf("--sandbox");

        Assert.True(sandboxIndex >= 0);
        Assert.Equal(
            "read-only",
            invocation.Arguments[sandboxIndex + 1]);
        Assert.DoesNotContain(
            "--dangerously-bypass-approvals-and-sandbox",
            invocation.Arguments);
    }

    [Fact]
    public void WorkspaceWrite_AddsSandboxWorkspaceWrite_AndDoesNotEmitBypass()
    {
        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.WorkspaceWrite);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        int sandboxIndex =
            invocation.Arguments.ToList().IndexOf("--sandbox");

        Assert.True(sandboxIndex >= 0);
        Assert.Equal(
            "workspace-write",
            invocation.Arguments[sandboxIndex + 1]);
        Assert.DoesNotContain(
            "--dangerously-bypass-approvals-and-sandbox",
            invocation.Arguments);
    }

    [Fact]
    public void Unrestricted_AddsDangerouslyBypass_AndDoesNotEmitSandbox()
    {
        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.Unrestricted);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.Contains(
            "--dangerously-bypass-approvals-and-sandbox",
            invocation.Arguments);
        Assert.DoesNotContain(
            "--sandbox",
            invocation.Arguments);
    }

    // ---------------------------------------------------------
    // New-session vs Resume-session mapping (42-50)
    // ---------------------------------------------------------

    [Fact]
    public void NewSession_DoesNotEmitResume_AndInstructionIsFinalPositionalArgument()
    {
        string instruction =
            "Start new task prompt";
        AgentExecutionRequest request =
            CreateRequest(
                instruction_: instruction,
                sessionReference_: null);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        Assert.DoesNotContain(
            "resume",
            invocation.Arguments);
        Assert.Equal(
            instruction,
            invocation.Arguments.Last());
    }

    [Fact]
    public void ResumeSession_EmitsResume_FollowedByExactOpaqueSession_AndInstruction()
    {
        string sessionId =
            "session-abc-123";
        string instruction =
            "Continue previous work";
        AgentSessionReference session =
            new(sessionId);

        AgentExecutionRequest request =
            CreateRequest(
                instruction_: instruction,
                sessionReference_: session);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        int resumeIndex =
            invocation.Arguments.ToList().IndexOf("resume");

        Assert.True(resumeIndex >= 0);
        Assert.Equal(
            sessionId,
            invocation.Arguments[resumeIndex + 1]);
        Assert.Equal(
            instruction,
            invocation.Arguments[resumeIndex + 2]);
        Assert.Equal(
            instruction,
            invocation.Arguments.Last());
    }

    [Fact]
    public void ResumeSession_SessionNotTrimmedOrNormalized()
    {
        string opaqueSession =
            "  opaque session with spaces  ";
        AgentSessionReference session =
            new(opaqueSession);

        AgentExecutionRequest request =
            CreateRequest(
                sessionReference_: session);

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        int resumeIndex =
            invocation.Arguments.ToList().IndexOf("resume");

        Assert.Equal(
            opaqueSession,
            invocation.Arguments[resumeIndex + 1]);
    }

    [Fact]
    public void ResumeSession_AllRootOptionsPrecedeResume()
    {
        AgentSessionReference session =
            new("sess-1");
        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.WorkspaceWrite,
                sessionReference_: session,
                structuredOutput_: new StructuredOutputContract("{\"type\":\"object\"}"));

        string schemaPath = "C:\\temp\\schema.json";
        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request, schemaPath);

        int resumeIndex =
            invocation.Arguments.ToList().IndexOf("resume");
        int sandboxIndex =
            invocation.Arguments.ToList().IndexOf("--sandbox");
        int schemaIndex =
            invocation.Arguments.ToList().IndexOf("--output-schema");

        Assert.True(sandboxIndex < resumeIndex);
        Assert.True(schemaIndex < resumeIndex);
    }

    // ---------------------------------------------------------
    // Structured output & temp file lifecycle (51-61)
    // ---------------------------------------------------------

    [Fact]
    public async Task StructuredOutput_Null_EmitsNoOutputSchemaFlag()
    {
        FakeCodexProcessRunner fakeRunner =
            new();
        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest(structuredOutput_: null);

        await adapter.ExecuteAsync(request);

        Assert.NotNull(fakeRunner.LastInvocation);
        Assert.DoesNotContain(
            "--output-schema",
            fakeRunner.LastInvocation.Arguments);
    }

    [Fact]
    public async Task StructuredOutput_CreatesTempFile_ExistsDuringRunnerExecution_WithExactContents()
    {
        string originalSchema =
            "{\n  \"type\": \"object\",\n  \"properties\": {\n    \"count\": { \"type\": \"integer\" }\n  }\n}";

        string? capturedFilePath = null;
        string? capturedFileContent = null;
        bool fileExistedDuringRun = false;

        FakeCodexProcessRunner fakeRunner =
            new()
            {
                CustomHandler = (inv_, token_) =>
                {
                    int flagIndex = inv_.Arguments.ToList().IndexOf("--output-schema");
                    if (flagIndex >= 0)
                    {
                        capturedFilePath = inv_.Arguments[flagIndex + 1];
                        fileExistedDuringRun = File.Exists(capturedFilePath);
                        if (fileExistedDuringRun)
                        {
                            capturedFileContent = File.ReadAllText(capturedFilePath, Encoding.UTF8);
                        }
                    }

                    return Task.FromResult(new ProcessExecutionResult(
                        0,
                        "{\"type\":\"turn.completed\"}",
                        string.Empty));
                }
            };

        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest(
                structuredOutput_: new StructuredOutputContract(originalSchema));

        AgentExecutionResult result =
            await adapter.ExecuteAsync(request);

        Assert.Equal(AgentExecutionStatus.Completed, result.Status);
        Assert.True(fileExistedDuringRun, "Temp schema file must exist during process runner execution.");
        Assert.Equal(originalSchema, capturedFileContent);

        Assert.NotNull(capturedFilePath);
        Assert.False(
            File.Exists(capturedFilePath),
            "Temp schema file must be deleted after successful execution.");
    }

    [Fact]
    public async Task StructuredOutput_TempFileDeleted_AfterProcessFailure()
    {
        string? capturedFilePath = null;

        FakeCodexProcessRunner fakeRunner =
            new()
            {
                CustomHandler = (inv_, token_) =>
                {
                    int flagIndex = inv_.Arguments.ToList().IndexOf("--output-schema");
                    if (flagIndex >= 0)
                    {
                        capturedFilePath = inv_.Arguments[flagIndex + 1];
                    }

                    return Task.FromResult(new ProcessExecutionResult(
                        1,
                        "{\"type\":\"turn.failed\",\"error\":{\"message\":\"Execution failed\"}}",
                        string.Empty));
                }
            };

        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest(
                structuredOutput_: new StructuredOutputContract("{\"type\":\"object\"}"));

        AgentExecutionResult result =
            await adapter.ExecuteAsync(request);

        Assert.Equal(AgentExecutionStatus.Failed, result.Status);
        Assert.NotNull(capturedFilePath);
        Assert.False(
            File.Exists(capturedFilePath),
            "Temp schema file must be deleted after process failure.");
    }

    [Fact]
    public async Task StructuredOutput_TempFileDeleted_AfterCallerCancellation()
    {
        using CancellationTokenSource cts =
            new();

        string? capturedFilePath = null;

        FakeCodexProcessRunner fakeRunner =
            new()
            {
                CustomHandler = (inv_, token_) =>
                {
                    int flagIndex = inv_.Arguments.ToList().IndexOf("--output-schema");
                    if (flagIndex >= 0)
                    {
                        capturedFilePath = inv_.Arguments[flagIndex + 1];
                    }

                    cts.Cancel();
                    token_.ThrowIfCancellationRequested();
                    return Task.FromResult(new ProcessExecutionResult(0, "{}", string.Empty));
                }
            };

        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest(
                structuredOutput_: new StructuredOutputContract("{\"type\":\"object\"}"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(request, cts.Token));

        Assert.NotNull(capturedFilePath);
        Assert.False(
            File.Exists(capturedFilePath),
            "Temp schema file must be deleted before caller cancellation propagation finishes.");
    }

    [Fact]
    public void TempSchemaFile_WrittenWithoutUtf8Bom()
    {
        string schema = "{\"type\":\"object\"}";
        string tempFile = CodexTempSchemaFile.Create(schema);

        try
        {
            byte[] bytes = File.ReadAllBytes(tempFile);
            // UTF-8 BOM is 0xEF, 0xBB, 0xBF.
            if (bytes.Length >= 3)
            {
                bool hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                Assert.False(hasBom, "Schema file must be written without UTF-8 BOM.");
            }
        }
        finally
        {
            CodexTempSchemaFile.Delete(tempFile);
        }
    }

    // ---------------------------------------------------------
    // JSONL stdout protocol & session handling (62-66)
    // ---------------------------------------------------------

    [Fact]
    public void Result_ThreadStarted_ValidThreadId_ProducesAgentSessionReference()
    {
        string stdout =
            "{\"type\":\"thread.started\",\"thread_id\":\"th-abc-456\"}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.NotNull(result.SessionReference);
        Assert.Equal(
            "th-abc-456",
            result.SessionReference.Value);
    }

    [Fact]
    public void Result_ThreadStarted_ExactThreadIdPreserved_NotTrimmed()
    {
        string stdout =
            "{\"type\":\"thread.started\",\"thread_id\":\"  th-untrimmed-id  \"}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.NotNull(result.SessionReference);
        Assert.Equal(
            "  th-untrimmed-id  ",
            result.SessionReference.Value);
    }

    [Fact]
    public void Result_ThreadStarted_WhitespaceThreadIdIgnored()
    {
        string stdout =
            "{\"type\":\"thread.started\",\"thread_id\":\"   \"}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Null(result.SessionReference);
    }

    [Fact]
    public void Result_AbsentThreadStarted_ProducesNullSessionReference()
    {
        string stdout =
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Null(result.SessionReference);
    }

    [Fact]
    public void Result_UnknownTopLevelEvent_IgnoredForForwardCompatibility()
    {
        string stdout =
            "{\"type\":\"future.unrecognized.event\",\"some_key\":\"value\"}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Completed,
            result.Status);
    }

    // ---------------------------------------------------------
    // Agent message & OutputText mapping (67-70)
    // ---------------------------------------------------------

    [Fact]
    public void Result_ItemCompleted_AgentMessage_MapsToOutputText()
    {
        string expectedMessage =
            "Completed task successfully with answer.";
        string stdout =
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"" + expectedMessage + "\"}}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            expectedMessage,
            result.OutputText);
    }

    [Fact]
    public void Result_MultipleAgentMessages_LatestWins()
    {
        string stdout =
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"First message\"}}\n" +
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"Final message\"}}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            "Final message",
            result.OutputText);
    }

    [Fact]
    public void Result_OutputTextWhitespacePreserved()
    {
        string multilineWithWhitespace =
            "  Leading spaces\nLine 2 with \ttabs\n\nEnd  ";
        string stdout =
            $"{{\"type\":\"item.completed\",\"item\":{{\"type\":\"agent_message\",\"text\":{System.Text.Json.JsonSerializer.Serialize(multilineWithWhitespace)}}}}}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            multilineWithWhitespace,
            result.OutputText);
    }

    [Fact]
    public void Result_NonAgentItemTypes_DoNotReplaceOutputText()
    {
        string stdout =
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"Real answer\"}}\n" +
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"text\":\"Thinking steps\"}}\n" +
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"command\":\"ls\"}}\n" +
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"file_change\",\"path\":\"a.txt\"}}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            "Real answer",
            result.OutputText);
    }

    // ---------------------------------------------------------
    // Completion & Failure semantics (71-82)
    // ---------------------------------------------------------

    [Fact]
    public void Result_Exit0AndTurnCompleted_ReturnsCompleted()
    {
        string stdout =
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Completed,
            result.Status);
    }

    [Fact]
    public void Result_Completed_DoesNotRequireAgentMessage()
    {
        string stdout =
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Completed,
            result.Status);
        Assert.Null(result.OutputText);
    }

    [Fact]
    public void Result_NonzeroExitAndTurnCompleted_ReturnsFailed()
    {
        string stdout =
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(1, stdout, "Crash trace");

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "code 1",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_MissingTurnCompleted_ReturnsFailed()
    {
        string stdout =
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"incomplete\"}}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "turn.completed",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_TurnFailed_ReturnsFailed()
    {
        string stdout =
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"Rate limit exceeded\"}}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "Rate limit exceeded",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_TopLevelError_ReturnsFailed()
    {
        string stdout =
            "{\"type\":\"error\",\"message\":\"Authentication failed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "Authentication failed",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_TurnFailedFollowedByTurnCompleted_RemainsFailed()
    {
        string stdout =
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"Initial error\"}}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "Initial error",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_ErrorFollowedByTurnCompleted_RemainsFailed()
    {
        string stdout =
            "{\"type\":\"error\",\"message\":\"Early fatal error\"}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "Early fatal error",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_MalformedNonEmptyJsonLine_ReturnsFailed()
    {
        string stdout =
            "{ this is not valid JSON }\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "malformed JSON",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_EmptyStdout_ReturnsFailed()
    {
        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, string.Empty, "stderr text");

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "empty stdout",
            result.DiagnosticText);
        Assert.Contains(
            "stderr text",
            result.DiagnosticText);
    }

    [Fact]
    public void Result_NeverReturnsNeedsInput()
    {
        string stdout =
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"Please approve\"}}\n" +
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.NotEqual(
            AgentExecutionStatus.NeedsInput,
            result.Status);
    }

    // ---------------------------------------------------------
    // Diagnostics mapping (83-88)
    // ---------------------------------------------------------

    [Fact]
    public void Diagnostics_TurnFailedErrorMessage_Retained()
    {
        string stdout =
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"Specific turn error\"}}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(1, stdout, string.Empty);

        Assert.Equal(
            "Specific turn error",
            result.DiagnosticText);
    }

    [Fact]
    public void Diagnostics_TopLevelErrorMessage_Retained()
    {
        string stdout =
            "{\"type\":\"error\",\"message\":\"Specific top error\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(1, stdout, string.Empty);

        Assert.Equal(
            "Specific top error",
            result.DiagnosticText);
    }

    [Fact]
    public void Diagnostics_StderrRetainedOnFailure()
    {
        string stdout =
            "{\"type\":\"turn.failed\"}";
        string stderr =
            "Process crash details in stderr";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(1, stdout, stderr);

        Assert.Contains(
            stderr,
            result.DiagnosticText);
    }

    [Fact]
    public void Diagnostics_ProviderDiagnosticAndStderr_CombinedDeterministically()
    {
        string stdout =
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"Provider fault\"}}";
        string stderr =
            "Native stderr output";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(1, stdout, stderr);

        Assert.Equal(
            "Provider fault\nNative stderr output",
            result.DiagnosticText);
    }

    [Fact]
    public void Diagnostics_NonzeroExit_DeterministicWhenProviderDiagnosticAbsent()
    {
        string stdout =
            "{\"type\":\"turn.completed\"}";

        AgentExecutionResult result =
            CodexCliClientAdapter.ParseExecutionResult(42, stdout, string.Empty);

        Assert.Equal(
            "Codex reported turn.completed but process exited with code 42.",
            result.DiagnosticText);
    }

    // ---------------------------------------------------------
    // Caller cancellation (89-95)
    // ---------------------------------------------------------

    [Fact]
    public async Task Cancellation_PreCanceledToken_PropagatesOperationCanceledException()
    {
        FakeCodexProcessRunner fakeRunner =
            new();
        CodexCliClientAdapter adapter =
            new(fakeRunner);

        using CancellationTokenSource cts =
            new();
        cts.Cancel();

        AgentExecutionRequest request =
            CreateRequest();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(request, cts.Token));
    }

    [Fact]
    public async Task Cancellation_PreCanceledToken_DoesNotInvokeProcessSeam()
    {
        FakeCodexProcessRunner fakeRunner =
            new();
        CodexCliClientAdapter adapter =
            new(fakeRunner);

        using CancellationTokenSource cts =
            new();
        cts.Cancel();

        AgentExecutionRequest request =
            CreateRequest();

        try
        {
            await adapter.ExecuteAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(
            0,
            fakeRunner.InvocationCount);
    }

    [Fact]
    public async Task Cancellation_InFlightCallerCancellation_PropagatesOperationCanceledException()
    {
        using CancellationTokenSource cts =
            new();

        FakeCodexProcessRunner fakeRunner =
            new()
            {
                CustomHandler = async (inv_, token_) =>
                {
                    cts.Cancel();
                    token_.ThrowIfCancellationRequested();
                    await Task.Yield();
                    return new ProcessExecutionResult(0, "{}", string.Empty);
                }
            };

        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(request, cts.Token));
    }

    [Fact]
    public async Task Cancellation_InFlightCancellation_InvokesProcessTerminationInFakeSeam()
    {
        using CancellationTokenSource cts =
            new();

        FakeCodexProcessRunner fakeRunner =
            new()
            {
                CustomHandler = async (inv_, token_) =>
                {
                    cts.Cancel();
                    token_.ThrowIfCancellationRequested();
                    await Task.Yield();
                    return new ProcessExecutionResult(0, "{}", string.Empty);
                }
            };

        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        try
        {
            await adapter.ExecuteAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(
            fakeRunner.TerminationInvoked,
            "Cancellation registration must invoke process termination.");
    }

    [Fact]
    public async Task Cancellation_CallerCancellationNeverReturnsFailed()
    {
        using CancellationTokenSource cts =
            new();

        FakeCodexProcessRunner fakeRunner =
            new()
            {
                CustomHandler = async (inv_, token_) =>
                {
                    cts.Cancel();
                    token_.ThrowIfCancellationRequested();
                    await Task.Yield();
                    return new ProcessExecutionResult(0, "{}", string.Empty);
                }
            };

        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        OperationCanceledException ex =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => adapter.ExecuteAsync(request, cts.Token));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task ProcessStartFailure_MapsToFailedWithDiagnostic()
    {
        FakeCodexProcessRunner fakeRunner =
            new()
            {
                ExceptionToThrow =
                    new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified")
            };

        CodexCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        AgentExecutionResult result =
            await adapter.ExecuteAsync(request);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "Failed to start or execute Codex process",
            result.DiagnosticText);
        Assert.Contains(
            "The system cannot find the file specified",
            result.DiagnosticText);
    }
}
